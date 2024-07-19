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

            // Add the appsettings to the deployment
            returnList.Add(GenerateAppSettings(deploymentItem));
            GeneratePersistentVolumes(deploymentItem).ForEach(pv => returnList.Add(pv));
            GeneratePersistentVolumeClaims(deploymentItem).ForEach(pvc => returnList.Add(pvc));


            // Update for any deployment objects
            KubernetesYaml.LoadAllFromString(deploymentItem.DeployRequest.YamlFileContents)
                .OfType<V1Deployment>() // Filter for V1Deployment objects using LINQ
                .ToList() // Convert to a list
                .ForEach(k8sDeployment => returnList.Add(UpdateDeployment(deploymentItem: deploymentItem, k8sDeployment: k8sDeployment)));

            return returnList;
        }

        internal V1Deployment UpdateDeployment(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem, V1Deployment k8sDeployment) {
            k8sDeployment.EnsureMetadata();
            k8sDeployment.Metadata.EnsureAnnotations();
            k8sDeployment.Metadata.EnsureLabels();
            k8sDeployment.Spec.Template.EnsureMetadata();
            k8sDeployment.Spec.Template.Metadata.EnsureAnnotations();
            k8sDeployment.Spec.Template.Metadata.EnsureLabels();

            if (string.IsNullOrWhiteSpace(k8sDeployment.Metadata.Name)) {
                throw new NullReferenceException("Metadata.Name is null or empty");
            }

            k8sDeployment.Metadata.Name = deploymentItem.DeployRequest.AppName;
            k8sDeployment.Metadata.SetNamespace(deploymentItem.DeployRequest.NameSpace);

            Dictionary<string, string> template_annotations = GenerateAnnotations(deploymentItem);
            Dictionary<string, string> template_annotationsWithDapr = GenerateAnnotations(deploymentItem, enableDapr: true);
            Dictionary<string, string> template_labels = GenerateLabels(deploymentItem);
            Dictionary<string, string> template_environmentVariables = GenerateEnvironmentVariables(deploymentItem);
            Models.KubernetesObjects.ResourceDefinition template_resourceLimits = GenerateResourceLimits(deploymentItem);
            List<V1Volume> template_volumes = GenerateVolumes(deploymentItem);
            List<V1VolumeMount> template_volumeMounts = GenerateVolumeMounts(deploymentItem);



            // Loop through and add annotations to the deployment and the Deployment Spec
            foreach (KeyValuePair<string, string> kvp in template_annotationsWithDapr) {
                if (!k8sDeployment.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Metadata.Annotations.Add(kvp.Key, kvp.Value);
                if (!k8sDeployment.Spec.Template.Metadata.Annotations.ContainsKey(kvp.Key)) k8sDeployment.Spec.Template.Metadata.Annotations.Add(kvp.Key, kvp.Value);
            }

            foreach (KeyValuePair<string, string> kvp in template_labels) {
                if (!k8sDeployment.Metadata.Labels.ContainsKey(kvp.Key)) k8sDeployment.Metadata.Labels.Add(kvp.Key, kvp.Value);
                if (!k8sDeployment.Spec.Template.Metadata.Labels.ContainsKey(kvp.Key)) k8sDeployment.Spec.Template.Metadata.Labels.Add(kvp.Key, kvp.Value);
            }

            if (k8sDeployment.Spec.Template.Spec.Containers.Count == 0) {
                throw new NullReferenceException("Spec.Template.Spec.Containers.Count is 0");
            }

            // Update the requested container with the environment variables
            int containerInjectionTargetX = k8sDeployment.Spec.Template.Spec.Containers.IndexOf(k8sDeployment.Spec.Template.Spec.Containers.FirstOrDefault(_container => _container.Name == deploymentItem.DeployRequest.ContainerInjectionTarget));
            if (containerInjectionTargetX == -1) containerInjectionTargetX = 0;

            // Add the volumes from the template to the deployment
            k8sDeployment.Spec.Template.Spec.Volumes ??= new List<V1Volume>();
            foreach (V1Volume volume in template_volumes) {
                if (!k8sDeployment.Spec.Template.Spec.Volumes.Any(yamlVolume => yamlVolume.Name == volume.Name)) {
                    k8sDeployment.Spec.Template.Spec.Volumes.Add(volume);
                }
            }

            // Add the volume mounts from the template to the target container
            k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts ??= new List<V1VolumeMount>();
            foreach (V1VolumeMount volumeMount in template_volumeMounts) {
                if (!k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts.Any(yamlVolumeMount => yamlVolumeMount.Name == volumeMount.Name)) {
                    try {
                        _logger.LogDebug("Adding volume mount to container: {volumeMount}", volumeMount.Name);
                        k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts.Add(volumeMount);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error adding volume mount to container");
                        throw;
                    }
                }
            }

            k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env ??= new List<V1EnvVar>();

            foreach (KeyValuePair<string, string> kvp in template_environmentVariables) {
                if (!k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Any(yamlEnvVar => yamlEnvVar.Name == kvp.Key)) {
                    k8sDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Add(new V1EnvVar() { Name = kvp.Key, Value = kvp.Value });
                }
            }

            // Loop through and update the containers with the annotations, labels, and specs
            for (int x = 0; x < k8sDeployment.Spec.Template.Spec.Containers.Count; x++) {
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources ??= new V1ResourceRequirements();
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits ??= new Dictionary<string, ResourceQuantity>();
                k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests ??= new Dictionary<string, ResourceQuantity>();

                // Add the default value if it's missing
                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Any(limit => string.Equals(limit.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Add("memory", new ResourceQuantity(template_resourceLimits.Resources.Limits.Memory));
                }

                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Any(limit => string.Equals(limit.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Add("cpu", new ResourceQuantity(template_resourceLimits.Resources.Limits.Cpu));
                }

                // Add the default value if it's missing
                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Any(request => string.Equals(request.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Add("memory", new ResourceQuantity(template_resourceLimits.Resources.Requests.Memory));
                }

                if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Any(request => string.Equals(request.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
                    k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Requests.Add("cpu", new ResourceQuantity(template_resourceLimits.Resources.Requests.Cpu));
                }


                if (deploymentItem.DeployRequest.GpuRequirement == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions.Nvidia) {
                    if (!k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Any(limit => string.Equals(limit.Key, "nvidia.com/gpu", StringComparison.CurrentCultureIgnoreCase))) {
                        k8sDeployment.Spec.Template.Spec.Containers[x].Resources.Limits.Add("nvidia.com/gpu", new ResourceQuantity("1"));
                    }
                }

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

        public Dictionary<string, string> StandardTemplateRequestItems(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            return new Dictionary<string, string> {
                { "services.payloadapp.payloadappTemplate.enabled", "true" },
                { "services.payloadapp.payloadappTemplate.schedule.startTime", deploymentItem.DeployRequest.StartTime.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "services.payloadapp.payloadappTemplate.schedule.endTime", deploymentItem.DeployRequest.StartTime.ToDateTime().AddSeconds(deploymentItem.DeployRequest.MaxDuration.ToTimeSpan().TotalSeconds).ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "services.payloadapp.payloadappTemplate.schedule.recurringSchedule", deploymentItem.DeployRequest.Schedule },
                { "services.payloadapp.payloadappTemplate.schedule.maxDuration", deploymentItem.DeployRequest.MaxDuration.ToTimeSpan().TotalSeconds.ToString() },
                { "services.payloadapp.payloadappTemplate.appContext", deploymentItem.DeployRequest.AppContextCase.ToString() },
                { "services.payloadapp.payloadappTemplate.appName", deploymentItem.DeployRequest.AppName },
                { "services.payloadapp.payloadappTemplate.appGroup", deploymentItem.DeployRequest.AppGroupLabel },
                { "services.payloadapp.payloadappTemplate.correlationId", deploymentItem.DeployRequest.RequestHeader.CorrelationId },
                { "services.payloadapp.payloadappTemplate.customerTrackingId", deploymentItem.DeployRequest.CustomerTrackingId },
                { "services.payloadapp.payloadappTemplate.serviceNamespace", deploymentItem.DeployRequest.NameSpace },
                { "services.payloadapp.payloadappTemplate.trackingId", deploymentItem.DeployRequest.RequestHeader.TrackingId },
            };
        }

        public Dictionary<string, string> GenerateAnnotations(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem, bool enableDapr = false) {
            _logger.LogDebug("Generating annotations template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.annotations.enabled", "true");


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

        public Dictionary<string, string> GenerateLabels(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating labels template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.labels.enabled", "true");


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

        public Dictionary<string, string> GenerateEnvironmentVariables(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating environment variables template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.environmentVariables.enabled", "true");

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

        public Models.KubernetesObjects.ResourceDefinition GenerateResourceLimits(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating resource limits template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.resources.enabled", "true");

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.LowerCaseNamingConvention.Instance) // Kubernetes YAML typically uses camelCase
                .Build();


            string templateYaml = GenerateTemplate(templateRequest);
            Models.KubernetesObjects.ResourceDefinition returnValue = deserializer.Deserialize<Models.KubernetesObjects.ResourceDefinition>(templateYaml);

            return returnValue;
        }

        public V1ConfigMap GenerateAppSettings(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating appsettings template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.appsettings.enabled", "true");

            string templateYaml = GenerateTemplate(templateRequest);

            V1ConfigMap? returnValue = KubernetesYaml.LoadAllFromString(templateYaml)
                .OfType<V1ConfigMap>()
                .FirstOrDefault();

            if (returnValue == null)
                throw new ApplicationException("Failed to generate AppSettings ConfigMap");

            return returnValue;
        }

        public List<V1Volume> GenerateVolumes(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating volumes template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);


            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.fileServer.volumesEnabled", "true");

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance) // Adjust the naming convention as needed
            .Build();


            string templateYaml = GenerateTemplate(templateRequest);

            Models.KubernetesObjects.VolumeRoot volumeRoot = deserializer.Deserialize<Models.KubernetesObjects.VolumeRoot>(templateYaml);

            return volumeRoot.Volumes;
        }

        public List<V1VolumeMount> GenerateVolumeMounts(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating volume mounts template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);


            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.fileServer.volumeMountsEnabled", "true");

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance) // Adjust the naming convention as needed
                .Build();

            string templateYaml = GenerateTemplate(templateRequest);

            Models.KubernetesObjects.VolumeMountRoot volumeMountRoot = deserializer.Deserialize<Models.KubernetesObjects.VolumeMountRoot>(templateYaml);
            return volumeMountRoot.VolumeMounts;
        }

        public List<V1PersistentVolumeClaim> GeneratePersistentVolumeClaims(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating PersistentVolumeClaim template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.persistentVolumes.claimsEnabled", "true");

            string templateYaml = GenerateTemplate(templateRequest);

            List<V1PersistentVolumeClaim> returnValue = KubernetesYaml.LoadAllFromString(templateYaml).OfType<V1PersistentVolumeClaim>().ToList();

            if (returnValue == null)
                throw new ApplicationException("Failed to generate AppSettings ConfigMap");

            return returnValue;
        }

        public List<V1PersistentVolume> GeneratePersistentVolumes(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _logger.LogDebug("Generating PersistentVolumeClaim template.  (AppName: '{AppName}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId);

            Dictionary<string, string> templateRequest = StandardTemplateRequestItems(deploymentItem);
            templateRequest.Add("services.payloadapp.payloadappTemplate.persistentVolumes.volumesEnabled", "true");

            string templateYaml = GenerateTemplate(templateRequest);

            List<V1PersistentVolume> returnValue = KubernetesYaml.LoadAllFromString(templateYaml).OfType<V1PersistentVolume>().ToList();

            if (returnValue == null)
                throw new ApplicationException("Failed to generate AppSettings ConfigMap");

            return returnValue;
        }
    }
}
