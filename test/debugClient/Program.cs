namespace PayloadApp.DebugClient;

public class Program {
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        // Load the configuration being supplicated by the cluster first
        builder.Configuration.AddJsonFile(Path.Combine("{env:SPACEFX_CONFIG_DIR}", "config", "appsettings.json"), optional: true, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.{env:DOTNET_ENVIRONMENT}.json"), optional: true, reloadOnChange: false);

        // Build the configuration
        build.Configuration.Build();

        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(50051, o => o.Protocols = HttpProtocols.Http2))
        .ConfigureServices((services) => {
            services.AddAzureOrbitalFramework();
            services.AddHostedService<MessageSender>();
            services.AddSingleton<Core.IMessageHandler<Microsoft.Azure.SpaceFx.MessageFormats.HostServices.Link.LinkRequest>, MessageHandler<Microsoft.Azure.SpaceFx.MessageFormats.HostServices.Link.LinkRequest>>();
            services.AddSingleton<Core.IMessageHandler<Microsoft.Azure.SpaceFx.MessageFormats.HostServices.Link.LinkResponse>, MessageHandler<Microsoft.Azure.SpaceFx.MessageFormats.HostServices.Link.LinkResponse>>();

            Core.APP_CONFIG appConfig = new() {
                HEARTBEAT_PULSE_TIMING_MS = 3000,
                HEARTBEAT_RECEIVED_TOLERANCE_MS = 10000
            };

            services.AddSingleton(appConfig);

        }).ConfigureLogging((logging) => {
            logging.AddProvider(new Microsoft.Extensions.Logging.SpaceFX.Logger.HostSvcLoggerProvider());
            logging.AddConsole();
        });

        var app = builder.Build();

        app.UseRouting();
        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<Core.Services.MessageReceiver>();
            endpoints.MapGrpcHealthChecksService();
            endpoints.MapGet("/", async context => {
                await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
            });
        });
        app.Run();
    }
}