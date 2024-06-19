namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {

    /// <summary>
    /// Downlinks a deployment response
    /// </summary>
    public class DownlinkUtil {

        private readonly ILogger<DownlinkUtil> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Core.Client _client;
        private string _outboxDirectory;

        public DownlinkUtil(ILogger<DownlinkUtil> logger, IServiceProvider serviceProvider, Core.Client client) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _client = client;
            _outboxDirectory = _client.GetXFerDirectories().Result.outbox_directory;
        }

        internal void DownlinkDeploymentResponse(MessageFormats.PlatformServices.Deployment.DeployResponse response) {
            var jsonFormatter = new Google.Protobuf.JsonFormatter(Google.Protobuf.JsonFormatter.Settings.Default);

            string outbox_directory = Core.GetXFerDirectories().Result.outbox_directory;
            string output_fileName = string.Format($"{response.DeployRequest.AppName}-{response.ResponseHeader.TrackingId}-{DateTime.UtcNow:dd-MM-yy-hh.mm.ss.fff}.deployResponse");

            // Serialize the response to a file so we can send it to MTS
            File.WriteAllText(Path.Combine(outbox_directory, output_fileName), jsonFormatter.Format(response));

            DownlinkFile(Path.Combine(outbox_directory, output_fileName));
        }

        internal void DownlinkFile(string fileName) {
            MessageFormats.HostServices.Link.LinkRequest linkRequest = new() {
                DestinationAppId = $"platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}",
                ExpirationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddHours(12)),
                LeaveSourceFile = false,
                LinkType = MessageFormats.HostServices.Link.LinkRequest.Types.LinkType.Downlink,
                Priority = MessageFormats.Common.Priority.Medium,
                RequestHeader = new() {
                    TrackingId = Guid.NewGuid().ToString(),
                }
            };

            if (!File.Exists(fileName)) {
                throw new FileNotFoundException($"File '{fileName}' not found.  Check path");
            }

            var (inbox_directory, outbox_directory, root_directory) = Core.GetXFerDirectories().Result;

            if (!fileName.StartsWith(outbox_directory)) {
                System.IO.File.Move(fileName, Path.Combine(outbox_directory, System.IO.Path.GetFileName(fileName)), overwrite: true);
            }

            linkRequest.FileName = System.IO.Path.GetFileName(fileName);
            linkRequest.RequestHeader.CorrelationId = linkRequest.RequestHeader.TrackingId;

            _client.DirectToApp(appId: $"hostsvc-{nameof(MessageFormats.Common.HostServices.Link).ToLower()}", message: linkRequest);
        }


    }
}
