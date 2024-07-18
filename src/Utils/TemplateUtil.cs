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

            Dictionary<string, string> annotations = GenerateAnnotations();
            Dictionary<string, string> annotationsWithDapr = GenerateAnnotations(enableDapr: true);
            Models.KubernetesObjects.ResourceDefinition resourceLimits = GenerateResourceLimits();

            // Loop through and add annotations to the deployment and the Deployment Spec
            foreach (KeyValuePair<string, string> kvp in annotations) {
                if (!k8sDeployment.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Metadata.Annotations.Add(kvp.Key, kvp.Value);
                if (!k8sDeployment.Spec.Template.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Spec.Template.Metadata.Annotations.Add(kvp.Key, kvp.Value);
            }

            // if(k8sDeployment.Spec.Template.Spec.Containers.Count == 0) {
            //     throw new NullReferenceException("Spec.Template.Spec.Containers.Count is 0");
            // }

            int containerInjectionTargetX = k8sDeployment.Spec.Template.Spec.Containers.IndexOf(k8sDeployment.Spec.Template.Spec.Containers.FirstOrDefault(_container => _container.Name == origDeploymentItem.DeployRequest.ContainerInjectionTarget));
            if (containerInjectionTargetX == -1) containerInjectionTargetX = 0;

            // Loop through and update the containers
            for (int x = 0; x < k8sDeployment.Spec.Template.Spec.Containers.Count; x++) {
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources ??= new V1ResourceRequirements();
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits ??= new Dictionary<string, ResourceQuantity>();
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests ??= new Dictionary<string, ResourceQuantity>();

                // Add the default value if it's missing
                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Any(limit => string.Equals(limit.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Add("memory", new ResourceQuantity(resourceLimits.Resources.Limits.Memory));
                }

                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Any(limit => string.Equals(limit.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Add("cpu", new ResourceQuantity(resourceLimits.Resources.Limits.Cpu));
                }

                // Add the default value if it's missing
                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Any(request => string.Equals(request.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Add("memory", new ResourceQuantity(resourceLimits.Resources.Requests.Memory));
                }

                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Any(request => string.Equals(request.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Add("cpu", new ResourceQuantity(resourceLimits.Resources.Requests.Cpu));
                }


                // if (deployRequest.GpuRequirement == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions.Nvidia) {
                //     if (!k8sDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Any(limit => string.Equals(limit.Key, "nvidia.com/gpu", StringComparison.CurrentCultureIgnoreCase))) {
                //         k8sDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Add("nvidia.com/gpu", new ResourceQuantity("1"));
                //     }
                // }

            }



            // Loop through and add annotations to the deployment
            // foreach (KeyValuePair<string, string> kvp in GenerateAnnotations(deploymentItem: origDeploymentItem, enableDapr: true)) {

            //     if (!k8sDeployment.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Metadata.Annotations.Add(kvp.Key, kvp.Value);
            // }

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

            _logger.LogDebug("Helm Template Command: '{app} {arguments}'", startInfo.FileName, startInfo.Arguments);

            using (Process process = new Process { StartInfo = startInfo }) {
                process.Start();
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                returnCode = process.ExitCode;
            }


            if (_appConfig.ENABLE_YAML_DEBUG) {
                string outputFile = Path.Combine(_deploymentOutputDir, $"templateOutput_{DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")}{(returnCode == 0 ? "" : ".error")}.yaml");
                _logger.LogDebug($"ENABLE_YAML_DEBUG = 'true'.  Outputting helm generated values to '{outputFile}'");
                File.WriteAllText(outputFile, output);
            }


            if (returnCode > 0) {
                throw new ApplicationException($"Helm failed to generate requested template.  Output: {output}.  Error: {error}  Return Code: {returnCode}");
            }


            _logger.LogDebug("Successfully generated template");
            return output;
        }

        public Dictionary<string, string> GenerateAnnotations(bool enableDapr = false) {
            var templateRequest = new Dictionary<string, string> {
                { "services.payloadapp.payloadappTemplate.enabled", "true" },
                { "services.payloadapp.payloadappTemplate.annotations.enabled", "true" }
            };

            if (enableDapr) templateRequest.Add("services.payloadapp.payloadappTemplate.annotations.daprEnabled", "true");

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            string templateYaml = GenerateTemplate(templateRequest);
            Dictionary<string, string> returnDictionary = deserializer.Deserialize<Dictionary<string, string>>(templateYaml);

            returnDictionary = returnDictionary.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString() ?? ""
            );

            return returnDictionary;
        }

        public Models.KubernetesObjects.ResourceDefinition GenerateResourceLimits() {
            var templateRequest = new Dictionary<string, string> {
                { "services.payloadapp.payloadappTemplate.enabled", "true" },
                { "services.payloadapp.payloadappTemplate.resources.enabled", "true" }
            };

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.LowerCaseNamingConvention.Instance) // Kubernetes YAML typically uses camelCase
                .Build();


            string templateYaml = GenerateTemplate(templateRequest);
            Models.KubernetesObjects.ResourceDefinition returnValue = deserializer.Deserialize<Models.KubernetesObjects.ResourceDefinition>(templateYaml);

            return returnValue;
        }
    }
}
