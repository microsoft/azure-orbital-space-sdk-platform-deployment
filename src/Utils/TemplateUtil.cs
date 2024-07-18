namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {
    /// <summary>
    /// Utility class to create template from the SpaceFx Chart
    /// </summary>
    public class TemplateUtil {
        private readonly ILogger<TemplateUtil> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Core.Client _client;
        private readonly Models.APP_CONFIG _appConfig;
        private string _helmApp;
        private string _spacefxChart;
        private readonly string _deploymentOutputDir;

        public TemplateUtil(ILogger<TemplateUtil> logger, IServiceProvider serviceProvider, Core.Client client, IOptions<Models.APP_CONFIG> appConfig) {
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

            _deploymentOutputDir = Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, "deployments");

            Directory.CreateDirectory(_deploymentOutputDir);

            // if (_appConfig.ENABLE_YAML_DEBUG) {
            //         _logger.LogDebug("ENABLE_YAML_DEBUG = 'true'.  Outputting original yaml contents to '{yamlDestination}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
            //         File.WriteAllText(Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.YamlFileContents);
            //     }
        }

        internal List<IKubernetesObject> GenerateKubernetesObjectsFromDeployment(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            List<IKubernetesObject> returnList = new();

            // Update for any deployment objects
            KubernetesYaml.LoadAllFromString(deploymentItem.DeployRequest.YamlFileContents)
                .OfType<V1Deployment>() // Filter for V1Deployment objects using LINQ
                .ToList() // Convert to a list
                .ForEach(k8sDeployment => returnList.Add(UpdateDeployment(origDeploymentItem: deploymentItem, k8sDeployment: k8sDeployment)));

            return returnList;
        }

        internal V1Deployment UpdateDeployment(MessageFormats.PlatformServices.Deployment.DeployResponse origDeploymentItem, V1Deployment k8sDeployment) {
            k8sDeployment.EnsureMetadata();
            k8sDeployment.EnsureMetadata();
            k8sDeployment.Metadata.EnsureAnnotations();
            k8sDeployment.Metadata.EnsureLabels();
            k8sDeployment.Spec.Template.EnsureMetadata();
            k8sDeployment.Spec.Template.Metadata.EnsureAnnotations();
            k8sDeployment.Spec.Template.Metadata.EnsureLabels();

            if (string.IsNullOrWhiteSpace(k8sDeployment.Metadata.Name)) {
                throw new NullReferenceException("Metadata.Name is null or empty");
            }

            // Loop through and add annotations
            foreach (KeyValuePair<string, string> kvp in GenerateAnnotations(origDeploymentItem)) {
                if (!k8sDeployment.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Metadata.Annotations.Add(kvp.Key, kvp.Value);
                if (!k8sDeployment.Spec.Template.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Spec.Template.Metadata.Annotations.Add(kvp.Key, kvp.Value);
            }

            return k8sDeployment;
        }

        public string GenerateTemplate(Dictionary<string, string> helmValuesToSet) {
            _logger.LogDebug("Generating template for SpaceFx Chart '{spacefxChart}' with values '{helmValuesToSet}'", _spacefxChart, helmValuesToSet);
            string output = "", error = "";
            int returnCode = 0;

            StringBuilder helmValues = new();

            foreach (string key in helmValuesToSet.Keys) {
                helmValues.Append($" --set {key}={helmValuesToSet[key]}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = _helmApp,
                Arguments = $"template {_spacefxChart} {helmValues}",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _logger.LogDebug("Starting process: '{app} {arguments}'", startInfo.FileName, startInfo.Arguments);

            using (Process process = new Process { StartInfo = startInfo }) {
                process.Start();
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                returnCode = process.ExitCode;
            }

            if (returnCode > 0) {
                throw new ApplicationException($"Helm failed to generate requested template.  Output: {output}.  Error: {error}  Return Code: {returnCode}");
            }


            _logger.LogDebug("Successfully generated template");
            return output;
        }

        public Dictionary<string, string> GenerateAnnotations(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            var templateRequest = new Dictionary<string, string> {
                { "services.payloadapp.payloadapp.enabled", "true" },
                { "services.payloadapp.payloadapp.annotations.enabled", "true" }
            };

            var templateOutput = GenerateTemplate(templateRequest);
            return string.IsNullOrEmpty(templateOutput) ? new Dictionary<string, string>() :
                templateOutput
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split(new[] { ':' }, 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Replace("\"", ""));
        }
    }
}
