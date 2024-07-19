namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {

    /// <summary>
    /// Allows async events to be stored and called from one spot
    /// </summary>
    public class K8sClient {
        private readonly ILogger<K8sClient> _logger;
        private readonly Kubernetes _k8sClient;
        private readonly Core.Client _client;
        private readonly IServiceProvider _serviceProvider;
        private readonly Models.APP_CONFIG _appConfig;
        private readonly string _deploymentOutputDir;
        private Utils.TemplateUtil _templateUtil;
        public K8sClient(ILogger<K8sClient> logger, IServiceProvider serviceProvider, Core.Client client, IOptions<Models.APP_CONFIG> appConfig, Utils.TemplateUtil templateUtil) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _client = client;
            _appConfig = appConfig.Value;
            _templateUtil = templateUtil;
            _deploymentOutputDir = Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, "deployments");

            if (_appConfig.PURGE_SCHEDULE_ON_BOOTUP && Directory.Exists(_deploymentOutputDir)) {
                Directory.Delete(_deploymentOutputDir, true);
            }


            Directory.CreateDirectory(_deploymentOutputDir);

            KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildDefaultConfig();
            _k8sClient = new Kubernetes(config);



            _logger.LogInformation("Services.{serviceName} Initialized.", nameof(K8sClient));
        }

        public MessageFormats.PlatformServices.Deployment.DeployResponse DeployItem(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            if (string.IsNullOrWhiteSpace(deploymentItem.DeployRequest.AppGroupLabel)) deploymentItem.DeployRequest.AppGroupLabel = deploymentItem.DeployRequest.AppName;
            if (string.IsNullOrWhiteSpace(deploymentItem.DeployRequest.CustomerTrackingId)) deploymentItem.DeployRequest.CustomerTrackingId = deploymentItem.ResponseHeader.TrackingId;

            _logger.LogInformation("Starting deployment.  AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}' / Schedule: '{schedule}'",
                deploymentItem.DeployRequest.AppName,
                deploymentItem.DeployRequest.DeployAction,
                deploymentItem.ResponseHeader.TrackingId,
                deploymentItem.ResponseHeader.CorrelationId,
                deploymentItem.DeployRequest.Schedule);


            return DeployToKubernetes(deploymentItem);
        }

        private MessageFormats.PlatformServices.Deployment.DeployResponse DeployToKubernetes(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            string tokensizedYamlObject = "";
            IKubernetesObject? processed_kubernetesObject;

            _logger.LogInformation("Deployment Started.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
            // _logger.LogInformation("Passing {requestType} and {responseType} to plugins (trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.GetType().Name, returnResponse.GetType().Name, deployRequest.RequestHeader.TrackingId, deployRequest.RequestHeader.CorrelationId);
            // // Pass the request to the plugins before we process it
            // (output_request, output_response) =
            //                 Core.CallPlugins<DeployRequest?, Plugins.PluginBase, DeployResponse>(
            //                     orig_request: deployRequest, orig_response: returnResponse,
            //                     pluginDelegate: _pluginDelegates.PreKubernetesDeployment).Result;

            // _logger.LogTrace("Plugins complete for {messageType}. (trackingId: '{trackingId}' / correlationId: '{correlationId}')", deployRequest.GetType().Name, deployRequest.RequestHeader.TrackingId, deployRequest.RequestHeader.CorrelationId);

            // // Update the request if our plugins changed it
            // if (output_request == null || output_request == default(DeployRequest)) {
            //     _logger.LogInformation("Plugins nullified {messageType}.  Dropping deploy request (trackingId: '{trackingId}' / correlationId: '{correlationId}')", deployRequest.GetType().Name, deployRequest.RequestHeader.TrackingId, deployRequest.RequestHeader.CorrelationId);
            //     returnResponse.ResponseHeader.Status = StatusCodes.Rejected;
            //     if (string.IsNullOrWhiteSpace(returnResponse.ResponseHeader.Message)) returnResponse.ResponseHeader.Message = "DeployRequest was nullified by plugins.  Unable to process request";
            //     return returnResponse;
            // }

            // if (output_response == null || output_response == default(DeployResponse)) {
            //     _logger.LogInformation("Plugins nullified {messageType}.  Dropping deploy request (trackingId: '{trackingId}' / correlationId: '{correlationId}')", returnResponse.GetType().Name, deployRequest.RequestHeader.TrackingId, deployRequest.RequestHeader.CorrelationId);
            //     returnResponse.ResponseHeader.Status = StatusCodes.Rejected;
            //     if (string.IsNullOrWhiteSpace(returnResponse.ResponseHeader.Message)) returnResponse.ResponseHeader.Message = "DeployRequest was nullified by plugins.  Unable to process request";
            //     return returnResponse;
            // }

            // deployRequest = output_request;
            // returnResponse = output_response;

            try {
                // Output the files to the deployments directory to help with troubleshooting if we've enabled the debug property
                if (_appConfig.ENABLE_YAML_DEBUG) {
                    _logger.LogDebug("ENABLE_YAML_DEBUG = 'true'.  Outputting original yaml contents to '{yamlDestination}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                    File.WriteAllText(Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_orig"), deploymentItem.DeployRequest.YamlFileContents);
                }


                if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.RestartDeployment) {
                    _logger.LogInformation("Restarting deployment '{AppName}' in Namespace '{NameSpace}' (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.NameSpace, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                    var deployment = _k8sClient.ReadNamespacedDeployment(deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.NameSpace);
                    deployment.Spec.Template.Metadata.Annotations["deployment.kubernetes.io/restart"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    _k8sClient.ReplaceNamespacedDeployment(deployment, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.NameSpace);
                    deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
                    return deploymentItem;
                }

                if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.BuildImage) {
                    if (_appConfig.BUILD_SERIVCE_ENABLED) {
                        _logger.LogInformation("Container build requested.  Trigger build service to build '{DestinationRepository}:{DestinationTag}' (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContainerBuild.DestinationRepository, deploymentItem.DeployRequest.AppContainerBuild.DestinationTag, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                        BuildContainerImage(deploymentItem);
                        deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
                        return deploymentItem;
                    } else {
                        _logger.LogError("Build service is not enabled.  Unable to build container image.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                        deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.GeneralFailure;
                        deploymentItem.ResponseHeader.Message = "Build service is not enabled.  Unable to build container image.";
                        return deploymentItem;
                    }

                }

                List<IKubernetesObject> kubernetesObjects = _templateUtil.GenerateKubernetesObjectsFromDeployment(deploymentItem);

                // Loop through the yaml objects and start deploying them
                for (int itemX = 0; itemX < kubernetesObjects.Count; itemX++) {
                    IKubernetesObject kubernetesObject = kubernetesObjects[itemX];

                    if (_appConfig.ENABLE_YAML_DEBUG) {
                        _logger.LogDebug("ENABLE_YAML_DEBUG = 'true'.  Outputting generated file to '{yamlDestination}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", itemX.ToString(), Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_item" + itemX.ToString() + "_post"), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                        File.WriteAllText(Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_post_" + itemX.ToString() + "_post"), KubernetesYaml.Serialize(kubernetesObject));
                    }
                    switch (deploymentItem.DeployRequest.DeployAction) {
                        case MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply:
                            PatchViaYamlObject(kubernetesObject);
                            break;
                        case MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Delete:
                            DeleteViaYamlObject(kubernetesObject);
                            break;
                        case MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create:
                            CreateViaYamlObject(kubernetesObject);
                            break;
                        default:
                            throw new Exception(string.Format($"Unknown DeployAction: {deploymentItem.DeployRequest.DeployAction}"));
                    }
                }
                deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
            } catch (Exception ex) {
                _logger.LogError("Failed to deploy action '{action}'.  Error: {ex}  (trackingId: '{trackingId}' / correlationId: '{correlationId}')",
                                    deploymentItem.DeployRequest.DeployAction, ex.Message, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.GeneralFailure;
                deploymentItem.ResponseHeader.Message = string.Format($"Failed '{deploymentItem.DeployRequest.DeployAction}' action.  Error: {ex.Message}");
            }

            return deploymentItem;
        }

        private void PatchViaYamlObject(IKubernetesObject yamlObject) {
            try {
                switch (yamlObject) {
                    case V1PersistentVolumeClaim pvc:
                        _k8sClient.PatchNamespacedPersistentVolumeClaim(new V1Patch(pvc, V1Patch.PatchType.MergePatch), name: pvc.Metadata.Name, namespaceParameter: pvc.Metadata.NamespaceProperty);
                        break;
                    case V1PersistentVolume pv:
                        _k8sClient.PatchPersistentVolume(new V1Patch(pv, V1Patch.PatchType.MergePatch), name: pv.Metadata.Name);
                        break;
                    case V1ConfigMap cm:
                        _k8sClient.PatchNamespacedConfigMap(new V1Patch(cm, V1Patch.PatchType.MergePatch), name: cm.Metadata.Name, namespaceParameter: cm.Metadata.NamespaceProperty);
                        break;
                    case V1Namespace ns:
                        _k8sClient.PatchNamespace(new V1Patch(ns, V1Patch.PatchType.MergePatch), name: ns.Metadata.Name);
                        break;
                    case V1ServiceAccount svc:
                        if (svc.Metadata == null || string.IsNullOrEmpty(svc.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.PatchNamespacedServiceAccount(new V1Patch(svc, V1Patch.PatchType.MergePatch), name: svc.Metadata.Name, namespaceParameter: svc.Metadata.NamespaceProperty);
                        break;
                    case V1ClusterRole cr:
                        _k8sClient.PatchClusterRole(new V1Patch(cr, V1Patch.PatchType.MergePatch), name: cr.Metadata.Name);
                        break;
                    case V1ClusterRoleBinding csr:
                        _k8sClient.PatchClusterRoleBinding(new V1Patch(csr, V1Patch.PatchType.MergePatch), name: csr.Metadata.Name);
                        break;
                    case V1Deployment deployment:
                        if (deployment.Metadata == null || string.IsNullOrEmpty(deployment.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.PatchNamespacedDeployment(new V1Patch(deployment, V1Patch.PatchType.MergePatch), name: deployment.Metadata.Name, namespaceParameter: deployment.Metadata.NamespaceProperty);
                        break;
                    case V1CronJob cron:
                        if (cron.Metadata == null || string.IsNullOrEmpty(cron.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.PatchNamespacedCronJob(new V1Patch(cron, V1Patch.PatchType.MergePatch), name: cron.Metadata.Name, namespaceParameter: cron.Metadata.NamespaceProperty);
                        break;
                    case V1Pod pod:
                        if (pod.Metadata == null || string.IsNullOrEmpty(pod.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.PatchNamespacedPod(new V1Patch(pod, V1Patch.PatchType.ApplyPatch), name: pod.Metadata.Name, namespaceParameter: pod.Metadata.NamespaceProperty);
                        break;
                    case V1Job job:
                        if (job.Metadata == null || string.IsNullOrEmpty(job.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.PatchNamespacedJob(new V1Patch(job, V1Patch.PatchType.MergePatch), name: job.Metadata.Name, namespaceParameter: job.Metadata.NamespaceProperty);
                        break;
                }
            } catch (k8s.Autorest.HttpOperationException ex) {
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) {
                    // Item doesn't allow a patch.  Kick to a delete / create action
                    DeleteViaYamlObject(yamlObject);
                    CreateViaYamlObject(yamlObject);
                } else if (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                    // Item wasn't found.  Kick to a create action
                    CreateViaYamlObject(yamlObject);
                } else {
                    // I dunno what happened.
                    _logger.LogError("Failed to patch object of type '{yamlObjectKind}'.  Error: {error}", yamlObject.Kind, ex.Message);
                    throw;
                }
            } catch (Exception ex) {
                // I dunno what happened.
                _logger.LogError("Failed to patch object of type '{yamlObjectKind}'.  Error: {error}", yamlObject.Kind, ex.Message);
                throw;
            }
        }
        private void DeleteViaYamlObject(IKubernetesObject yamlObject) {
            try {
                switch (yamlObject) {
                    case V1PersistentVolumeClaim pvc:
                        _k8sClient.DeleteNamespacedPersistentVolumeClaim(name: pvc.Metadata.Name, namespaceParameter: pvc.Metadata.NamespaceProperty);
                        break;
                    case V1PersistentVolume pv:
                        _k8sClient.DeletePersistentVolume(name: pv.Metadata.Name);
                        break;
                    case V1ConfigMap cm:
                        if (cm.Metadata == null || string.IsNullOrEmpty(cm.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedConfigMap(name: cm.Metadata.Name, namespaceParameter: cm.Metadata.NamespaceProperty);
                        break;
                    case V1Namespace ns:
                        _k8sClient.DeleteNamespace(name: ns.Metadata.Name);
                        break;
                    case V1ServiceAccount svc:
                        if (svc.Metadata == null || string.IsNullOrEmpty(svc.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedServiceAccount(name: svc.Metadata.Name, namespaceParameter: svc.Metadata.NamespaceProperty);
                        break;
                    case V1ClusterRole cr:
                        _k8sClient.DeleteClusterRole(name: cr.Metadata.Name);
                        break;
                    case V1ClusterRoleBinding csr:
                        _k8sClient.DeleteClusterRoleBinding(name: csr.Metadata.Name);
                        break;
                    case V1Deployment deployment:
                        if (deployment.Metadata == null || string.IsNullOrEmpty(deployment.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedDeployment(name: deployment.Metadata.Name, namespaceParameter: deployment.Metadata.NamespaceProperty);
                        break;
                    case V1CronJob cron:
                        if (cron.Metadata == null || string.IsNullOrEmpty(cron.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedCronJob(name: cron.Metadata.Name, namespaceParameter: cron.Metadata.NamespaceProperty);
                        break;
                    case V1Pod pod:
                        if (pod.Metadata == null || string.IsNullOrEmpty(pod.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedPod(name: pod.Metadata.Name, namespaceParameter: pod.Metadata.NamespaceProperty);
                        break;
                    case V1Job job:
                        if (job.Metadata == null || string.IsNullOrEmpty(job.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.DeleteNamespacedJob(name: job.Metadata.Name, namespaceParameter: job.Metadata.NamespaceProperty, propagationPolicy: "Background");
                        break;
                }
            } catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                // We can ignore the error if we're trying to delete something and it's not found.
            } catch (Exception ex) {
                _logger.LogError("Failed to patch object of type '{yamlKind}'.  Error: {error}", yamlObject.Kind, ex.Message);
                throw;
            }
        }

        private void CreateViaYamlObject(IKubernetesObject yamlObject) {
            try {
                switch (yamlObject) {
                    case V1PersistentVolume pv:
                        _k8sClient.CreatePersistentVolume(body: pv);
                        break;
                    case V1PersistentVolumeClaim pvc:
                        _k8sClient.CreateNamespacedPersistentVolumeClaim(body: pvc, namespaceParameter: pvc.Metadata.NamespaceProperty);
                        break;
                    case V1Namespace ns:
                        _k8sClient.CreateNamespace(body: ns);
                        break;
                    case V1ConfigMap cm:
                        if (cm.Metadata == null || string.IsNullOrEmpty(cm.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedConfigMap(body: cm, namespaceParameter: cm.Metadata.NamespaceProperty);
                        break;
                    case V1ServiceAccount svc:
                        if (svc.Metadata == null || string.IsNullOrEmpty(svc.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedServiceAccount(body: svc, namespaceParameter: svc.Metadata.NamespaceProperty);
                        break;
                    case V1ClusterRole cr:
                        _k8sClient.CreateClusterRole(body: cr);
                        break;
                    case V1ClusterRoleBinding csr:
                        _k8sClient.CreateClusterRoleBinding(body: csr);
                        break;
                    case V1Deployment deployment:
                        if (deployment.Metadata == null || string.IsNullOrEmpty(deployment.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedDeployment(body: deployment, namespaceParameter: deployment.Metadata.NamespaceProperty);
                        break;
                    case V1CronJob cron:
                        if (cron.Metadata == null || string.IsNullOrEmpty(cron.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedCronJob(body: cron, namespaceParameter: cron.Metadata.NamespaceProperty);
                        break;
                    case V1Pod pod:
                        if (pod.Metadata == null || string.IsNullOrEmpty(pod.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedPod(body: pod, namespaceParameter: pod.Metadata.NamespaceProperty);
                        break;
                    case V1Job job:
                        if (job.Metadata == null || string.IsNullOrEmpty(job.Metadata.NamespaceProperty)) { throw new NullReferenceException("Metadata.NamespaceProperty is null or empty"); }
                        _k8sClient.CreateNamespacedJob(body: job, namespaceParameter: job.Metadata.NamespaceProperty);
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError("Failed to create object of type '{yamlKind}'.  Error: {error}", yamlObject.Kind, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Check and make sure the fileserver has the credentials for this app
        /// </summary>
        private string GenerateAPassword() {
            int length = 12;
            const string allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var buffer = new byte[length];

            // Fill the buffer with random bytes
            rng.GetBytes(buffer);

            // Convert the random bytes to characters from the allowed character set
            var password = new char[length];
            for (int i = 0; i < length; i++) {
                password[i] = allowedChars[buffer[i] % allowedChars.Length];
            }

            return new string(password);
        }

        /// <summary>
        /// Trigger a container build using the build service
        /// </summary>
        private void BuildContainerImage(MessageFormats.PlatformServices.Deployment.DeployResponse deployResponse) {

            V1Job job = new V1Job {
                ApiVersion = "batch/v1",
                Kind = "Job",
                Metadata = new V1ObjectMeta {
                    Name = deployResponse.DeployRequest.AppName.ToLower() + "-build-" + DateTime.UtcNow.ToString("ddMMyy-HHmmss"),
                },
                Spec = new V1JobSpec {
                    BackoffLimit = 0,
                    Completions = 1,
                    Parallelism = 1,
                    TtlSecondsAfterFinished = 300,
                    Template = new V1PodTemplateSpec {
                        Spec = new V1PodSpec {
                            RestartPolicy = "Never",
                            Containers = new List<V1Container>
                       {
                            new V1Container
                            {
                                Name = "buildservice",
                                Image = $"{_appConfig.CONTAINER_REGISTRY}/{_appConfig.BUILD_SERIVCE_REPOSITORY}:{_appConfig.BUILD_SERIVCE_TAG}",
                                Args = new List<string> { $"--dockerfile={deployResponse.DeployRequest.AppContainerBuild.DockerFile}",
                                                            $"--context=dir://{Path.Combine(_client.GetXFerDirectories().Result.inbox_directory, _appConfig.SCHEDULE_IMPORT_DIRECTORY)}",
                                                            $"--destination={_appConfig.CONTAINER_REGISTRY_INTERNAL}/{deployResponse.DeployRequest.AppContainerBuild.DestinationRepository}:{deployResponse.DeployRequest.AppContainerBuild.DestinationTag}",
                                                            "--snapshot-mode=redo",
                                                            $"--insecure-registry={_appConfig.CONTAINER_REGISTRY_INTERNAL}",
                                                            $"--registry-mirror={_appConfig.CONTAINER_REGISTRY_INTERNAL}",
                                                            "--single-snapshot",
                                                            "--skip-tls-verify",
                                                            "--skip-tls-verify-pull",
                                                            "--skip-tls-verify-registry",
                                                            "--verbosity=trace"},
                                VolumeMounts = new List<V1VolumeMount> {
                                    new V1VolumeMount {
                                        Name = "context-dir",
                                        MountPath = "/var/spacedev"
                                    },
                                }
                            }
                        },
                            Volumes = new List<V1Volume> {
                                new V1Volume {
                                    Name = "context-dir",
                                    HostPath = new V1HostPathVolumeSource {
                                        Path = _appConfig.SPACEFX_DIR
                                    }
                                }
                            }
                        }
                    }
                }
            };


            foreach (KeyValuePair<string, string> buildArg in deployResponse.DeployRequest.AppContainerBuild.BuildArguments) {
                job.Spec.Template.Spec.Containers[0].Args.Add($"--build-arg={buildArg.Key}={buildArg.Value}");
            }
            try {
                _k8sClient.CreateNamespacedJobAsync(body: job, namespaceParameter: "core").Wait();
            } catch (Exception ex) {

                _logger.LogError("Failed to create object of type Error: {error}", ex.Message);
                throw;
            }

        }

        /// <summary>
        /// Check and make sure the fileserver has the credentials for this app
        /// </summary>
        private void AddFileServerCredentials(MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {
            V1SecretList allFileServerSecrets = _k8sClient.ListNamespacedSecretAsync(namespaceParameter: _appConfig.FILESERVER_CRED_NAMESPACE).Result;
            V1SecretList allNameSpaceSecrets = _k8sClient.ListNamespacedSecretAsync(namespaceParameter: deployRequest.NameSpace).Result;

            V1Secret? fileServerCredSecret = null;
            V1Secret? appFileServerCreds = null;
            string generatedPassword = GenerateAPassword();

            _logger.LogDebug("Pulling fileserver credentials '{fileServerCreds}' and checking for 'user-{appId}'", _appConfig.FILESERVER_CRED_NAME, deployRequest.AppName.ToLower());

            // Select the fileServer creds if it exists
            fileServerCredSecret = allFileServerSecrets.Items.FirstOrDefault(secret => string.Equals(secret.Name(), _appConfig.FILESERVER_CRED_NAME, comparisonType: StringComparison.InvariantCultureIgnoreCase));

            // Select the appFileServerCreds secret if it already exists
            appFileServerCreds = allNameSpaceSecrets.Items.FirstOrDefault(secret => string.Equals(secret.Name(), $"fileserver-{deployRequest.AppName.ToLower()}", comparisonType: StringComparison.InvariantCultureIgnoreCase));

            if (fileServerCredSecret == null) throw new NullReferenceException($"Unable to find secret '${_appConfig.FILESERVER_CRED_NAME}'");

            if (!fileServerCredSecret.Data.Any(kvp => kvp.Key == $"user-{deployRequest.AppName.ToLower()}")) {
                _logger.LogDebug("Adding / Updating 'user-{appId}' to '{fileServerCreds}'...", deployRequest.AppName.ToLower(), _appConfig.FILESERVER_CRED_NAME);
                fileServerCredSecret.Data.Add($"user-{deployRequest.AppName.ToLower()}", System.Text.Encoding.UTF8.GetBytes(generatedPassword));
                _k8sClient.ReplaceNamespacedSecret(fileServerCredSecret, _appConfig.FILESERVER_CRED_NAME, _appConfig.FILESERVER_CRED_NAMESPACE);
                _logger.LogDebug("Successfully added 'user-{appId}' to '{fileServerCreds}'", deployRequest.AppName.ToLower(), _appConfig.FILESERVER_CRED_NAME);
            }


            if (appFileServerCreds == null) {
                _logger.LogDebug("Generating fileserver-{appId}...", deployRequest.AppName.ToLower());
                Dictionary<string, string> initialSecret = new Dictionary<string, string>(){
                        { "username", deployRequest.AppName.ToLower() },
                        { "password", generatedPassword }
                    };

                appFileServerCreds = new V1Secret(stringData: initialSecret) {
                    Metadata = new V1ObjectMeta {
                        Name = $"fileserver-{deployRequest.AppName.ToLower()}",
                        NamespaceProperty = deployRequest.NameSpace
                    },
                    Type = "Opaque"
                };

                _k8sClient.CreateNamespacedSecret(appFileServerCreds, deployRequest.NameSpace);

                _logger.LogDebug("fileserver-{appId} successfully generated.", deployRequest.AppName.ToLower());
            } else {
                _logger.LogDebug("fileserver-{appId} already exists.  Skipping regeneration", deployRequest.AppName.ToLower());
            }


            // Output the files to the deployments directory to help with troubleshooting if we've enabled the debug property
            if (_appConfig.ENABLE_YAML_DEBUG) {
                File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_fileServerSecrets"), KubernetesYaml.Serialize(allFileServerSecrets));
                File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_appFileServerCreds"), KubernetesYaml.Serialize(appFileServerCreds));
            }


        }

        /// <summary>
        /// Check if we need to add the Persistent Volumes and Claims
        /// </summary>
        private void AddFileServerVolumesAndClaims(MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {
            _logger.LogDebug("Checking for Persistent Volume Claims for '{appId}'...", deployRequest.AppName.ToLower());
            bool hasClaim = false;
            bool hasVolume = false;

            _logger.LogDebug("Adding '{appId}' file server claims...", deployRequest.AppName.ToLower());

            string input_yaml = replaceTemplateTokens($"{_appConfig.FILESERVER_PERSISTENT_VOLUMES}\n{_appConfig.FILESERVER_PERSISTENT_VOLUMECLAIMS}", deployRequest);

            if (_appConfig.ENABLE_YAML_DEBUG) {
                File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_fileServerVolumeClaims_pre"), input_yaml);
            }

            foreach (var obj in KubernetesYaml.LoadAllFromString(input_yaml)) {
                hasClaim = false;
                hasVolume = false;
                IKubernetesObject k8sObject = (IKubernetesObject) obj;

                if (k8sObject.GetType() == typeof(V1PersistentVolume)) {
                    V1PersistentVolume k8sVolume = (V1PersistentVolume) obj;

                    V1PersistentVolumeList allVolumes = _k8sClient.ListPersistentVolumeAsync().Result;

                    foreach (V1PersistentVolume volume in allVolumes) {
                        if (volume.Name().Equals(k8sVolume.Name(), comparisonType: StringComparison.InvariantCultureIgnoreCase)) hasVolume = true;
                    }

                    if (hasVolume) {
                        _logger.LogDebug("Found pre-existing volume '{volumeName}'.", k8sVolume.Name());
                    } else {
                        _logger.LogDebug("Adding volume '{volumeName}'....", k8sVolume.Name());
                        _ = _k8sClient.CreatePersistentVolumeAsync(k8sVolume).Result;
                    }
                }

                if (k8sObject.GetType() == typeof(V1PersistentVolumeClaim)) {
                    V1PersistentVolumeClaimList allClaims = _k8sClient.ListNamespacedPersistentVolumeClaimAsync(namespaceParameter: deployRequest.NameSpace).Result;
                    V1PersistentVolumeClaim k8sVolumeClaim = (V1PersistentVolumeClaim) obj;

                    foreach (V1PersistentVolumeClaim volumeClaim in allClaims) {
                        if (volumeClaim.Name().Equals(k8sVolumeClaim.Name(), comparisonType: StringComparison.InvariantCultureIgnoreCase)) hasClaim = true;
                    }

                    if (hasClaim) {
                        _logger.LogDebug("Found pre-existing volume claim '{claimName}'.", k8sVolumeClaim.Name());
                    } else {
                        _logger.LogDebug("Adding volume claim '{claimName}'....", k8sVolumeClaim.Name());
                        _ = _k8sClient.CreateNamespacedPersistentVolumeClaimAsync(k8sVolumeClaim, namespaceParameter: deployRequest.NameSpace).Result;
                    }
                }

            }

            if (_appConfig.ENABLE_YAML_DEBUG) {
                File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_fileServerVolumeClaims_post"), input_yaml);
            }

        }

        /// <summary>
        /// Check if we need to add the Persistent Volumes and Claims
        /// </summary>
        private void AddConfigurationAsSecrets(MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {
            _logger.LogDebug("Adding configuration secret for '{appId}'...", deployRequest.AppName.ToLower());

            V1SecretList currentSecrets = _k8sClient.ListNamespacedSecretAsync(namespaceParameter: deployRequest.NameSpace).Result;

            string input_yaml = replaceTemplateTokens($"{_appConfig.PAYLOAD_APP_CONFIG}", deployRequest);

            foreach (var obj in KubernetesYaml.LoadAllFromString(input_yaml)) {
                IKubernetesObject k8sObject = (IKubernetesObject) obj;

                if (k8sObject.GetType() == typeof(V1Secret)) {
                    V1Secret k8sSecret = (V1Secret) obj;

                    // The payload_app_config value is base64 encoded.  We need to decode it and then re-encode it
                    k8sSecret.Data = k8sSecret.Data.ToDictionary(
                        data => data.Key,
                        data => Encoding.UTF8.GetBytes(replaceTemplateTokens(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(data.Value))), deployRequest))
                    );

                    if (currentSecrets.Items.Any(secret => secret.EnsureMetadata().Name == k8sSecret.Metadata.Name)) {
                        _ = _k8sClient.PatchNamespacedSecretAsync(new V1Patch(k8sSecret, V1Patch.PatchType.MergePatch), name: k8sSecret.Metadata.Name, namespaceParameter: deployRequest.NameSpace).Result;
                    } else {
                        _ = _k8sClient.CreateNamespacedSecretAsync(k8sSecret, namespaceParameter: deployRequest.NameSpace).Result;
                    }
                }
            }

            _logger.LogDebug("Configuration secret successfully added for '{appId}'...", deployRequest.AppName.ToLower());

        }

        /// <summary>
        /// Add any missing labels and anootations to a deployment
        /// </summary>
        private V1Deployment AddUpdateMetaDataToDeployment(V1Deployment yamlDeployment, string containerInjectionTarget, MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {
            // yamlDeployment.EnsureMetadata();
            // yamlDeployment.Metadata.EnsureAnnotations();
            // yamlDeployment.Metadata.EnsureLabels();
            // yamlDeployment.Spec.Template.EnsureMetadata();
            // yamlDeployment.Spec.Template.Metadata.EnsureAnnotations();
            // yamlDeployment.Spec.Template.Metadata.EnsureLabels();


            // string input_yaml = _templateUtil.GenerateAnnotations();

            // if (_appConfig.ENABLE_YAML_DEBUG) {
            //     File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_payloadAppNotations_pre"), input_yaml);
            // }

            // // Loop through and add annotations
            // foreach (string annotation in input_yaml.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)) {
            //     string[] parts = annotation.Split(':');
            //     string key = parts[0].Trim();
            //     string value = parts[1].Trim();

            //     // Remove the quotes that are automatically added by chart
            //     value = value.Replace("\"", "");

            //     if (!yamlDeployment.Metadata.Annotations.ContainsKey(key)) yamlDeployment.Metadata.Annotations.Add(key, value);
            //     if (!yamlDeployment.Spec.Template.Metadata.Annotations.ContainsKey(key)) yamlDeployment.Spec.Template.Metadata.Annotations.Add(key, value);
            // }

            // int containerInjectionTargetX = yamlDeployment.Spec.Template.Spec.Containers.IndexOf(yamlDeployment.Spec.Template.Spec.Containers.FirstOrDefault(_container => _container.Name == containerInjectionTarget));
            // if (containerInjectionTargetX == -1) containerInjectionTargetX = 0;

            // // Loop through the environment variables and add them to the target container
            // input_yaml = replaceTemplateTokens($"{_appConfig.PAYLOAD_APP_ENVIRONMENTVARIABLES}", deployRequest);

            // var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            //     .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            //     .Build();

            // yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env ??= new List<V1EnvVar>();

            // foreach (var environmentvariable in deserializer.Deserialize<List<Dictionary<string, string>>>(input_yaml)) {
            //     if (!yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Any(yamlEnvVar => yamlEnvVar.Name == environmentvariable["name"])) {
            //         yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Add(new V1EnvVar() { Name = environmentvariable["name"], Value = environmentvariable["value"] });
            //     }
            // }

            // // Add the SPACEFX_DIR and SPACEFX_SECRET_DIR since the path may be dynamically changed
            // if (yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.FirstOrDefault(yamlEnvVar => yamlEnvVar.Name == "SPACEFX_DIR") == null) {
            //     // Value doesn't exist and needs to be created
            //     yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Add(new V1EnvVar() { Name = "SPACEFX_DIR", Value = Environment.GetEnvironmentVariable("SPACEFX_DIR") });
            // } else {
            //     // Value is already specified.  Overwrite it with the value from Platform-Deployment
            //     yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.First(yamlEnvVar => yamlEnvVar.Name == "SPACEFX_DIR").Value = Environment.GetEnvironmentVariable("SPACEFX_DIR");
            // }

            // if (yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.FirstOrDefault(yamlEnvVar => yamlEnvVar.Name == "SPACEFX_SECRET_DIR") == null) {
            //     // Value doesn't exist and needs to be created
            //     yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Add(new V1EnvVar() { Name = "SPACEFX_SECRET_DIR", Value = Environment.GetEnvironmentVariable("SPACEFX_SECRET_DIR") });
            // } else {
            //     // Value is already specified.  Overwrite it with the value from Platform-Deployment
            //     yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.First(yamlEnvVar => yamlEnvVar.Name == "SPACEFX_SECRET_DIR").Value = Environment.GetEnvironmentVariable("SPACEFX_SECRET_DIR");
            // }

            // if (deployRequest.AppContextString != null && !string.IsNullOrWhiteSpace(deployRequest.AppContextString.AppContext)) {
            //     if (yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.FirstOrDefault(yamlEnvVar => yamlEnvVar.Name == "APP_CONTEXT") == null) {
            //         // Value doesn't exist and needs to be created
            //         yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.Add(new V1EnvVar() { Name = "APP_CONTEXT", Value = deployRequest.AppContextString.AppContext });
            //     } else {
            //         // Value is already specified.  Overwrite it with the value from Platform-Deployment
            //         yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].Env.First(yamlEnvVar => yamlEnvVar.Name == "APP_CONTEXT").Value = deployRequest.AppContextString.AppContext;
            //     }
            // }

            // // Add default limits and requests to all containers
            // for (int i = 0; i < yamlDeployment.Spec.Template.Spec.Containers.Count; i++) {
            //     // Only update the target container with the metadata info.  Otherwise all the containers get the injections
            //     if (!string.IsNullOrWhiteSpace(containerInjectionTarget) && yamlDeployment.Spec.Template.Spec.Containers[i].Name != containerInjectionTarget) continue;

            //     yamlDeployment.Spec.Template.Spec.Containers[i].Resources ??= new V1ResourceRequirements();
            //     yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits ??= new Dictionary<string, ResourceQuantity>();
            //     yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Requests ??= new Dictionary<string, ResourceQuantity>();

            //     // Add the default value if it's missing
            //     if (!yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Any(limit => string.Equals(limit.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
            //         yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Add("memory", new ResourceQuantity(_appConfig.DEFAULT_LIMIT_MEMORY));
            //     }

            //     if (!yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Any(limit => string.Equals(limit.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
            //         yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Add("cpu", new ResourceQuantity(_appConfig.DEFAULT_LIMIT_CPU));
            //     }

            //     // Add the default value if it's missing
            //     if (!yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Requests.Any(request => string.Equals(request.Key, "memory", StringComparison.CurrentCultureIgnoreCase))) {
            //         yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Requests.Add("memory", new ResourceQuantity(_appConfig.DEFAULT_REQUEST_MEMORY));
            //     }

            //     if (!yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Requests.Any(request => string.Equals(request.Key, "cpu", StringComparison.CurrentCultureIgnoreCase))) {
            //         yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Requests.Add("cpu", new ResourceQuantity(_appConfig.DEFAULT_REQUEST_CPU));
            //     }


            //     if (deployRequest.GpuRequirement == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions.Nvidia) {
            //         if (!yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Any(limit => string.Equals(limit.Key, "nvidia.com/gpu", StringComparison.CurrentCultureIgnoreCase))) {
            //             yamlDeployment.Spec.Template.Spec.Containers[i].Resources.Limits.Add("nvidia.com/gpu", new ResourceQuantity("1"));
            //         }
            //     }

            // }

            return yamlDeployment;
        }

        /// <summary>
        /// Adds any missing container and volume injections
        /// </summary>
        private V1Deployment AddContainerAndVolumeInjections(V1Deployment yamlDeployment, string containerInjectionTarget, MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();

            // Calculate our target container
            int containerInjectionTargetX = yamlDeployment.Spec.Template.Spec.Containers.IndexOf(yamlDeployment.Spec.Template.Spec.Containers.FirstOrDefault(_container => _container.Name == containerInjectionTarget));
            if (containerInjectionTargetX == -1) containerInjectionTargetX = 0;

            // Loop through the volumemounts and add it to the target container
            yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts ??= new List<V1VolumeMount>();
            string input_yaml = replaceTemplateTokens($"{_appConfig.FILESERVER_CLIENT_VOLUME_MOUNTS}", deployRequest);

            if (_appConfig.ENABLE_YAML_DEBUG) File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_fileServerVolumeMounts_pre"), input_yaml);


            foreach (V1VolumeMount volumeMount in deserializer.Deserialize<List<V1VolumeMount>>(input_yaml)) {
                if (!yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts.Any(yamlVolumeMount => yamlVolumeMount.Name == volumeMount.Name)) {
                    yamlDeployment.Spec.Template.Spec.Containers[containerInjectionTargetX].VolumeMounts.Add(new V1VolumeMount() { Name = volumeMount.Name, MountPath = volumeMount.MountPath });
                }
            }


            yamlDeployment.Spec.Template.Spec.Volumes ??= new List<V1Volume>();

            input_yaml = replaceTemplateTokens($"{_appConfig.FILESERVER_CLIENT_VOLUMES}", deployRequest);

            if (_appConfig.ENABLE_YAML_DEBUG) File.WriteAllText(Path.Combine(_deploymentOutputDir, deployRequest.RequestHeader.TrackingId + "_fileServerVolumes_pre"), input_yaml);

            foreach (V1Volume volume in deserializer.Deserialize<List<V1Volume>>(input_yaml)) {
                if (!yamlDeployment.Spec.Template.Spec.Volumes.Any(yamlVolume => yamlVolume.Name == volume.Name)) {
                    yamlDeployment.Spec.Template.Spec.Volumes.Add(volume);
                }
            }

            return yamlDeployment;
        }

        /// <summary>
        /// Add service account to deployment
        /// </summary>
        private V1Deployment AddServiceAccountName(V1Deployment yamlDeployment) {
            //yamlDeployment.Spec.Template.Spec.ServiceAccountName = _appConfig.DEFAULT_SERVICE_ACCOUNT_NAME;
            return yamlDeployment;
        }

        private static string replaceTemplateTokens(string yamlContents, MessageFormats.PlatformServices.Deployment.DeployRequest deployRequest) {

            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_NAMESPACE", deployRequest.NameSpace);
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_NAME", deployRequest.AppName.ToLower());
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_GROUP", deployRequest.AppGroupLabel);
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_TRACKING_ID", deployRequest.RequestHeader.TrackingId);
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_CUSTOMER_TRACKING_ID", deployRequest.CustomerTrackingId);
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_CORRELATION_ID", deployRequest.RequestHeader.CorrelationId);
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_START_TIME", deployRequest.StartTime.ToDateTime().ToUniversalTime().ToString("o"));
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_MAX_DURATION", (deployRequest.MaxDuration ??= Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromHours(5))).Seconds.ToString());
            yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_SCHEDULE", deployRequest.Schedule);

            if (deployRequest.AppContextCase != MessageFormats.PlatformServices.Deployment.DeployRequest.AppContextOneofCase.None) {
                switch (deployRequest.AppContextCase) {
                    case MessageFormats.PlatformServices.Deployment.DeployRequest.AppContextOneofCase.AppContextString:
                        yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_CONTEXT", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(deployRequest.AppContextString.AppContext)));
                        break;
                    case MessageFormats.PlatformServices.Deployment.DeployRequest.AppContextOneofCase.AppContextFile:
                        yamlContents = yamlContents.Replace("SPACEFX-TEMPLATE_APP_CONTEXT", "\"\"");
                        break;
                }
            }

            return yamlContents;
        }
    }
}
