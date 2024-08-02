namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment.IntegrationTests;

/// <summary>
/// We only get one opportunity to build our client per deployment
/// This class allows us to instantiate and share the build context across
/// multiple test runs
/// </summary>
public class TestSharedContext : IDisposable {
    internal static string TARGET_SVC_APP_ID = "platform-deployment";
    private static TestSharedContext TextContext { get; set; } = null!;
    private static WebApplication _grpcHost { get; set; } = null!;
    internal static bool IS_ONLINE = false;
    internal static string APP_ID = "";
    internal static k8s.Kubernetes K8S_CLIENT = null!;
    internal static Core.Client SPACEFX_CLIENT = null!;
    internal static bool HOST_SVC_ONLINE = false;
    internal static TimeSpan MAX_TIMESPAN_TO_WAIT_FOR_MSG = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Setup the SpaceFx Core to be shared across tests
    /// </summary>
    public TestSharedContext() {
        if (_grpcHost != null) return;

        k8s.KubernetesClientConfiguration config = k8s.KubernetesClientConfiguration.BuildDefaultConfig();
        K8S_CLIENT = new k8s.Kubernetes(config);

        var builder = WebApplication.CreateBuilder();
        string _secretDir = Environment.GetEnvironmentVariable("SPACEFX_SECRET_DIR") ?? throw new Exception("SPACEFX_SECRET_DIR environment variable not set");
        // Load the configuration being supplicated by the cluster first
        builder.Configuration.AddJsonFile(Path.Combine($"{_secretDir}", "config", "appsettings.json"), optional: false, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false);

        // Load any local appsettings incase they're overriding the cluster values
        string? dotnet_env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(dotnet_env))
            builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{dotnet_env}.json"), optional: true, reloadOnChange: false);

        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(50051, o => o.Protocols = HttpProtocols.Http2))
        .ConfigureServices((services) => {
            services.AddAzureOrbitalFramework();
            services.AddSingleton<Core.IMessageHandler<MessageFormats.HostServices.Link.LinkResponse>, MessageHandler<MessageFormats.HostServices.Link.LinkResponse>>();
            services.AddHostedService<ServiceCallback>();

            // Translate the custom app config to core config
            Core.APP_CONFIG coreConfig = new() { };

            services.AddSingleton(coreConfig);
        }).ConfigureLogging((logging) => {
            logging.AddProvider(new Microsoft.Extensions.Logging.SpaceFX.Logger.HostSvcLoggerProvider());
        });

        _grpcHost = builder.Build();

        _grpcHost.UseRouting();
        _grpcHost.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<Core.Services.MessageReceiver>();
            endpoints.MapGrpcHealthChecksService();
            endpoints.MapGet("/", async context => {
                await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
            });
        });

        _grpcHost.StartAsync();

        // Waiting for the _grpcHost to spin up
        while (TestSharedContext.IS_ONLINE == false) {
            Thread.Sleep(250);
        }
    }

    public static void WritePropertyLineToScreen(string testName, string propertyName) {
        Console.WriteLine($"[{testName}] testing property '{propertyName}'");
    }

    public void Dispose() {
        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition(nameof(TestSharedContext))]
public class TestSharedContextCollection : ICollectionFixture<TestSharedContext> {
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}


public class MessageHandler<T> : Microsoft.Azure.SpaceFx.Core.IMessageHandler<T> where T : notnull {
    private readonly ILogger<MessageHandler<T>> _logger;
    private readonly IServiceProvider _serviceProvider;
    public static EventHandler<T>? MessageReceivedEvent;
    public MessageHandler(ILogger<MessageHandler<T>> logger, IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public void MessageReceived(T message, MessageFormats.Common.DirectToApp fullMessage) {
        using (var scope = _serviceProvider.CreateScope()) {
            _logger.LogInformation($"Received message type '{typeof(T).Name}'");

            if (MessageReceivedEvent != null) {
                foreach (Delegate handler in MessageReceivedEvent.GetInvocationList()) {
                    Task.Factory.StartNew(
                        () => handler.DynamicInvoke(fullMessage.ResponseHeader.AppId, message));
                }
            }
        }
    }
}


public class ServiceCallback : BackgroundService {
    private readonly ILogger<ServiceCallback> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Microsoft.Azure.SpaceFx.Core.Client _client;
    private readonly string _appId;

    public ServiceCallback(ILogger<ServiceCallback> logger, IServiceProvider serviceProvider, Core.Client client) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _client = client;
        _appId = _client.GetAppID().Result;


    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) {
        return Task.Run(async () => {
            using (var scope = _serviceProvider.CreateScope()) {
                while (!_client.IS_ONLINE) {
                    Thread.Sleep(250);
                }
                TestSharedContext.IS_ONLINE = _client.IS_ONLINE;
                TestSharedContext.APP_ID = _appId;
                TestSharedContext.SPACEFX_CLIENT = _client;
            }
        });

    }
}