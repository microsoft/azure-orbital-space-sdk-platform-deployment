namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public class Program {
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        // Load the configuration being supplicated by the cluster first
        builder.Configuration.AddJsonFile(Path.Combine("{env:SPACEFX_CONFIG_DIR}", "config", "appsettings.json"), optional: true, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.{env:DOTNET_ENVIRONMENT}.json"), optional: true, reloadOnChange: false);

        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(50051, o => o.Protocols = HttpProtocols.Http2))
        .ConfigureServices((services) => {
            services.AddAzureOrbitalFramework();
            services.AddSingleton<PluginDelegates>();

            services.AddSingleton<Services.DeployRequestProcessor>();
            services.AddHostedService<Services.DeployRequestProcessor>(p => p.GetRequiredService<Services.DeployRequestProcessor>());
            services.AddSingleton<Services.ScheduleProcessor>();
            services.AddSingleton<Utils.K8sClient>();
            services.AddSingleton<Utils.DownlinkUtil>();
            services.AddSingleton<Utils.TimeUtils>();
            services.AddSingleton<Utils.TemplateUtil>();
            services.AddHostedService<Services.ScheduleProcessor>(p => p.GetRequiredService<Services.ScheduleProcessor>());

            services.AddSingleton<Core.IMessageHandler<MessageFormats.HostServices.Link.LinkResponse>, MessageHandler<MessageFormats.HostServices.Link.LinkResponse>>();
            services.AddSingleton<Core.IMessageHandler<MessageFormats.Common.LogMessageResponse>, MessageHandler<MessageFormats.Common.LogMessageResponse>>();


        }).ConfigureLogging((logging) => {
            logging.AddProvider(new Microsoft.Extensions.Logging.SpaceFX.Logger.HostSvcLoggerProvider());
            logging.AddConsole();
        });

        var app = builder.Build();

        app.UseRouting();
        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<Microsoft.Azure.SpaceFx.Core.Services.MessageReceiver>();
            endpoints.MapGrpcHealthChecksService();
            endpoints.MapGet("/", async context => {
                await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
            });
        });

        // Add a middleware to catch exceptions and stop the host gracefully
        app.Use(async (context, next) => {
            try {
                await next.Invoke();
            } catch (Exception ex) {
                Console.Error.WriteLine($"Exception caught in middleware: {ex.Message}");

                // Stop the host gracefully so it triggers the pod to error
                var lifetime = context.RequestServices.GetService<IHostApplicationLifetime>();
                lifetime?.StopApplication();
            }
        });

        app.Run();
    }
}
