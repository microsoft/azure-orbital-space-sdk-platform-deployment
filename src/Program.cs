namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public class Program {
    private static void Test() {
        MessageFormats.PlatformServices.Deployment.DeployRequest _request = new MessageFormats.PlatformServices.Deployment.DeployRequest();
        _request.StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
        _request.MaxDuration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0));
        _request.AppContextFile = new MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContextFile() {
            FileName = "test1.jpg",
            Required = false
        };

        _request.GpuRequirement = MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions.Nvidia;
        _request.DeployAction = MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create;




        Google.Protobuf.JsonFormatter formatter = new Google.Protobuf.JsonFormatter(Google.Protobuf.JsonFormatter.Settings.Default);
        string jsonString = formatter.Format(_request);

        Console.WriteLine(jsonString);
        Console.WriteLine("Woohoo!");

    }
    public static void Main(string[] args) {
        //Test();
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddJsonFile("/workspaces/platform-deployment-config/appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("/workspaces/platform-deployment/src/appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("/workspaces/platform-deployment/src/appsettings.{env:DOTNET_ENVIRONMENT}.json", optional: true, reloadOnChange: true).Build();

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
            services.AddHostedService<Services.ScheduleProcessor>(p => p.GetRequiredService<Services.ScheduleProcessor>());

        }).ConfigureLogging((logging) => {
            logging.AddProvider(new Microsoft.Extensions.Logging.SpaceFX.Logger.HostSvcLoggerProvider());
            logging.AddConsole();
        });

        var app = builder.Build();

        app.UseRouting();
        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<Microsoft.Azure.SpaceFx.Core.Services.MessageReceiver>();
            endpoints.MapGet("/", async context => {
                await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
            });
        });
        app.Run();
    }
}