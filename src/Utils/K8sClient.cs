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

                V1PersistentVolumeList allVolumes = _k8sClient.ListPersistentVolumeAsync().Result;
                V1PersistentVolumeClaimList allVolumeClaims = _k8sClient.ListPersistentVolumeClaimForAllNamespacesAsync().Result;
                V1ServiceAccountList allServiceAccounts = _k8sClient.ListServiceAccountForAllNamespacesAsync().Result;

                // Loop through the yaml objects and start deploying them
                for (int itemX = 0; itemX < kubernetesObjects.Count; itemX++) {
                    IKubernetesObject kubernetesObject = kubernetesObjects[itemX];



                    if (_appConfig.ENABLE_YAML_DEBUG) {
                        _logger.LogDebug("ENABLE_YAML_DEBUG = 'true'.  Outputting generated file to '{yamlDestination}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')",
                                            Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_post_" + itemX.ToString() + "_post"),
                                            deploymentItem.DeployRequest.AppName,
                                            deploymentItem.DeployRequest.DeployAction,
                                            deploymentItem.DeployRequest.RequestHeader.TrackingId,
                                            deploymentItem.DeployRequest.RequestHeader.CorrelationId);

                        File.WriteAllText(Path.Combine(_deploymentOutputDir, deploymentItem.DeployRequest.RequestHeader.TrackingId + "_post_" + itemX.ToString() + "_post"), KubernetesYaml.Serialize(kubernetesObject));
                    }
                    switch (deploymentItem.DeployRequest.DeployAction) {
                        case MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply:
                            if ((kubernetesObject is V1PersistentVolumeClaim volumeClaim) && allVolumeClaims.Items.Any(pvc => pvc.Name().Equals(volumeClaim.Name(), StringComparison.InvariantCultureIgnoreCase))) {
                                if (volumeClaim.Namespace().Equals(volumeClaim.Namespace(), StringComparison.InvariantCultureIgnoreCase)) {
                                    _logger.LogDebug("Found pre-existing PersistentVolumeClaim '{volumeName}'. Nothing to do", volumeClaim.Name());
                                    break;
                                }
                                _logger.LogDebug("Found pre-existing PersistentVolumeClaim '{volumeName}' in wrong namespace '{volumeNameSpace}'.  Deleting old claim to allow new provision", volumeClaim.Name(), volumeClaim.Namespace());
                                _k8sClient.DeleteNamespacedPersistentVolumeClaim(name: volumeClaim.Name(), namespaceParameter: volumeClaim.Namespace());
                            }

                            if ((kubernetesObject is V1PersistentVolume volume) && allVolumes.Items.Any(pvv => pvv.Name().Equals(volume.Name(), StringComparison.InvariantCultureIgnoreCase))) {
                                _logger.LogDebug("Found pre-existing PersistentVolume '{volumeName}'. Nothing to do", volume.Name());
                                break;
                            }

                            if ((kubernetesObject is V1ServiceAccount serviceAccount) && allServiceAccounts.Items.Any(svc => svc.Name().Equals(serviceAccount.Name(), StringComparison.InvariantCultureIgnoreCase) && svc.Namespace().Equals(serviceAccount.Namespace(), StringComparison.InvariantCultureIgnoreCase))) {
                                _logger.LogDebug("Found pre-existing ServiceAccount '{serviceAccount}'. Nothing to do", serviceAccount.Name());
                                break;
                            }

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


                if (_appConfig.FILESERVER_SMB_ENABLED) {
                    AddFileServerCredentials(deploymentItem.DeployRequest);
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

    }
}
