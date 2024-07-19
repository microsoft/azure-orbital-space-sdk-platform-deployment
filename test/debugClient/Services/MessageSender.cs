namespace PayloadApp.DebugClient;

public class MessageSender : BackgroundService {
    private readonly ILogger<MessageSender> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Microsoft.Azure.SpaceFx.Core.Client _client;

    private readonly string SCHEDULE_DROPOFF = "schedule";
    private readonly string OUTBOX;

    private readonly string _appId;
    private readonly string _hostSvcAppId;

    public MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _client = _serviceProvider.GetService<Microsoft.Azure.SpaceFx.Core.Client>() ?? throw new NullReferenceException($"{nameof(Microsoft.Azure.SpaceFx.Core.Client)} is null");
        _appId = _client.GetAppID().Result;
        _hostSvcAppId = _appId.Replace("-client", "");
        OUTBOX = Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, SCHEDULE_DROPOFF);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using (var scope = _serviceProvider.CreateScope()) {
            _logger.LogInformation("MessageSender running at: {time}", DateTimeOffset.Now);

            System.IO.Directory.CreateDirectory(OUTBOX);

            // System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/busybox.json", string.Format($"{OUTBOX}/busybox.json"), overwrite: true);
            // System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/busybox.yaml", string.Format($"{OUTBOX}/busybox.yaml"), overwrite: true);

            System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/integration-test-deployment.yaml", string.Format($"{OUTBOX}/integration-test-deployment.yaml"), overwrite: true);
            System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/integration-test-schedule.json", string.Format($"{OUTBOX}/integration-test-schedule.json"), overwrite: true);
            System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/pubsub-csharp-subscriber.tar", string.Format($"{OUTBOX}/pubsub-csharp-subscriber.tar"), overwrite: true);
            System.IO.File.Copy("/workspace/platform-deployment/test/sampleSchedules/astronaut.jpg", string.Format($"{OUTBOX}/astronaut.jpg"), overwrite: true);

            await MoveScheduleArtifacts();
        }

    }
    private async Task MoveScheduleArtifacts() {
        _logger.LogInformation("{functionName}: Sending artifacts for deployment....)", nameof(MoveScheduleArtifacts));

        LinkRequest linkRequest = new();

        if (CheckForYaml(OUTBOX)) {

            string[] files = Directory.GetFiles(OUTBOX);
            try {
                foreach (string file in files) {
                    string fileName = Path.GetFileName(file);

                    // Need this deployment does not delete yaml
                    linkRequest = new() {
                        // todo: common return 'D' instead of 'd' hence, hardcoded
                        // DestinationAppId = $"platform-{nameof(Microsoft.Azure.SpaceFx.MessageFormats.Common.PlatformServices.Deployment)}",
                        DestinationAppId = "platform-deployment",
                        ExpirationTime = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(6)),
                        FileName = fileName,
                        Subdirectory = SCHEDULE_DROPOFF,
                        LeaveSourceFile = false,
                        Overwrite = true,
                        LinkType = LinkRequest.Types.LinkType.App2App,
                        Priority = Priority.Critical,
                        RequestHeader = new() {
                            AppId = _appId,
                            TrackingId = Guid.NewGuid().ToString(),
                            CorrelationId = Guid.NewGuid().ToString(), // Tasking request ID -> Job.something
                        }
                    };
                    await _client.DirectToApp(appId: $"hostsvc-{nameof(Microsoft.Azure.SpaceFx.MessageFormats.Common.HostServices.Link)}", message: linkRequest);


                    _logger.LogInformation("{functionName}: Sent artifacts for deployment!", nameof(MoveScheduleArtifacts));
                }

            } catch (Exception e) {
                string message = "Unable to queue file for deployment...";
                _logger.LogDebug("{functionName}: {message}", nameof(MoveScheduleArtifacts), message);
                _logger.LogError("{functionName}: {Exception}", nameof(MoveScheduleArtifacts), e);
                throw e;
            }
        }
    }

    private bool CheckForYaml(string dirPath) {
        // checks to ensure all expected schedule artifacts are present
        return Directory.GetFiles(dirPath, "*.yaml").Length > 0;
    }
}
