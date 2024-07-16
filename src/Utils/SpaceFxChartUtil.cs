namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {
    /// <summary>
    /// Utility class to create template from the SpaceFx Chart
    /// </summary>
    public class SpaceFxChartUtil {
        private readonly ILogger<SpaceFxChartUtil> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Core.Client _client;
        private readonly Models.APP_CONFIG _appConfig;
        private string _helmApp;
        private string _spacefxChart;

        public SpaceFxChartUtil(ILogger<SpaceFxChartUtil> logger, IServiceProvider serviceProvider, Core.Client client, IOptions<Models.APP_CONFIG> appConfig) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _client = client;
            _appConfig = appConfig.Value;
            _helmApp = Path.Combine(_client.GetXFerDirectories().Result.root_directory, "tmp", "helm", "helm");
            _spacefxChart = Path.Combine(_client.GetXFerDirectories().Result.root_directory, "tmp", "chart", Core.GetConfigSetting("spacefx_version").Result);

            if (!Directory.Exists(_spacefxChart)) {
                _logger.LogWarning("SpaceFx chart not found at '{spacefxChart}' and is required for Platform-Deployment.  Please check chart is in the right place and restart Platform-Deployment", _spacefxChart);
                throw new DirectoryNotFoundException($"SpaceFx chart not found at '{_spacefxChart}' and is required for Platform-Deployment.  Please check chart is in the right place and restart Platform-Deployment");
            }

            if (!File.Exists(_helmApp)) {
                _logger.LogWarning("helm not found at '{helmApp}' and is required for Platform-Deployment.  Please check helm is in the right place and restart Platform-Deployment", _helmApp);
                throw new FileNotFoundException("helm not found at '{helmApp}' and is required for Platform-Deployment.  Please check helm is in the right place and restart Platform-Deployment", _helmApp);
            }

            // if (_appConfig.ENABLE_YAML_DEBUG) {
            //         _logger.LogDebug("ENABLE_YAML_DEBUG = 'true'.  Outputting original yaml contents to '{yamlDestination}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
            //         File.WriteAllText(Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.YamlFileContents);
            //     }
        }
    }
}
