namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class Services {
    /// <summary>
    /// Represents a background service that handles scheduling and executing deployment requests.
    /// </summary>
    public class DeployRequestProcessor : BackgroundService {
        private readonly ILogger<DeployRequestProcessor> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Microsoft.Azure.SpaceFx.Core.Services.PluginLoader _pluginLoader;
        private readonly PluginDelegates _pluginDelegates;
        private readonly Models.APP_CONFIG _appConfig;
        private readonly Core.Client _client;
        private readonly Utils.K8sClient _k8sClient;
        private readonly Utils.DownlinkUtil _downlinkUtil;
        private readonly Utils.TimeUtils _timeUtils;
        private string _scheduleImportDirectory;
        private string _regctlApp;
        private readonly ConcurrentDictionary<Guid, MessageFormats.PlatformServices.Deployment.DeployResponse> _deployRequestCache;
        public DeployRequestProcessor(ILogger<DeployRequestProcessor> logger, IServiceProvider serviceProvider, IOptions<Models.APP_CONFIG> appConfig, Core.Services.PluginLoader pluginLoader, Core.Client client, PluginDelegates pluginDelegates, Utils.K8sClient k8sClient, Utils.DownlinkUtil downlinkUtil, Utils.TimeUtils timeUtil) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _pluginDelegates = pluginDelegates;
            _pluginLoader = pluginLoader;
            _appConfig = appConfig.Value;
            _client = client;
            _k8sClient = k8sClient;
            _downlinkUtil = downlinkUtil;
            _timeUtils = timeUtil;
            _deployRequestCache = new ConcurrentDictionary<Guid, MessageFormats.PlatformServices.Deployment.DeployResponse>();

            _scheduleImportDirectory = Path.Combine(_client.GetXFerDirectories().Result.inbox_directory, _appConfig.SCHEDULE_IMPORT_DIRECTORY);

            _regctlApp = Path.Combine(_client.GetXFerDirectories().Result.root_directory, "tmp", "regctl", "regctl");

            if (File.Exists(_regctlApp)) {
                _logger.LogInformation("regctl found at '{regctlApp}'", _regctlApp);
            } else {
                _logger.LogWarning("regctl not found at '{regctlApp}'.  Tarball importing will be disabled", _regctlApp);
                _regctlApp = "";
            }

            if (_appConfig.PURGE_SCHEDULE_ON_BOOTUP) {
                _client.ClearCache();
                if (Directory.Exists(Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, "deploymentResults"))) Directory.Delete(Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, "deploymentResults"), true);
            }

            PopulateCacheFromDisk();
        }


        /// <summary>
        /// Adds a new deployment item to the queue
        /// </summary>
        /// <param name="stoppingToken">The cancellation token to stop the execution.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal MessageFormats.PlatformServices.Deployment.DeployResponse QueueDeployment(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Pending;
            CommitToCache(deploymentItem);
            return deploymentItem;
        }

        /// <summary>
        /// Loads the deployment item from the disk cache
        /// </summary>
        private void PopulateCacheFromDisk() {
            _client.GetAllCacheItems().Result.ToList().ForEach(async (cacheItem) => {
                if (cacheItem.StartsWith("deployCache_")) {
                    MessageFormats.PlatformServices.Deployment.DeployResponse? deployResponse = await _client.GetCacheItem<MessageFormats.PlatformServices.Deployment.DeployResponse>(cacheItemName: cacheItem);
                    if (deployResponse != null) {
                        _deployRequestCache.AddOrUpdate(Guid.Parse(deployResponse.ResponseHeader.TrackingId), deployResponse, (key, oldValue) => deployResponse);
                    }
                }
            });
        }

        /// <summary>
        /// Save the deployment item to the disk cache
        /// </summary>
        private void CommitToCache(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {
            _deployRequestCache.AddOrUpdate(Guid.Parse(deploymentItem.ResponseHeader.TrackingId), deploymentItem, (key, oldValue) => deploymentItem);
            _client.SaveCacheItem(cacheItemName: "deployCache_" + deploymentItem.ResponseHeader.TrackingId, cacheItem: deploymentItem).Wait();
        }


        /// <summary>
        /// Remove item from cache
        /// </summary>
        private void RemoveFromCache(string trackingId) {
            _deployRequestCache.TryRemove(Guid.Parse(trackingId), out _);
            _client.DeleteCacheItem(cacheItemName: "deployCache_" + trackingId).Wait();
        }


        /// <summary>
        /// Executes the scheduler service asynchronously.
        /// </summary>
        /// <param name="stoppingToken">The cancellation token to stop the execution.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                using (var scope = _serviceProvider.CreateScope()) {

                    try {
                        // Loop through the items in the cache
                        foreach (KeyValuePair<Guid, MessageFormats.PlatformServices.Deployment.DeployResponse> item in _deployRequestCache) {

                            // An item that's not pending is in the queue and needs to be removed
                            if (item.Value.ResponseHeader.Status != MessageFormats.Common.StatusCodes.Pending) {
                                RemoveFromCache(item.Value.ResponseHeader.TrackingId);
                                _downlinkUtil.DownlinkDeploymentResponse(item.Value);
                                continue;
                            }

                            // Schedule start time hasn't been reached yet.  Skip to the next one
                            if (_timeUtils.RoundDown(item.Value.DeployRequest.StartTime.ToDateTime(), TimeSpan.FromMinutes(1)) > _timeUtils.RoundDown(DateTime.UtcNow, TimeSpan.FromMinutes(1)))
                                continue;

                            ProcessDeploymentItem(item.Value);
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error in ExecuteAsync");
                    }


                    await Task.Delay(_appConfig.SCHEDULE_SERVICE_POLLING_MS, stoppingToken);
                }
            }
        }

        private void ProcessDeploymentItem(MessageFormats.PlatformServices.Deployment.DeployResponse deploymentItem) {

            _logger.LogInformation("Initiating deployment '{deploymentType}' deployment for '{deploymentTime}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.StartTime.ToDateTime(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

            _logger.LogDebug("Passing message type '{messageType}' and '{responseMessageType}' to plugins. (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.GetType(), deploymentItem.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

            (MessageFormats.PlatformServices.Deployment.DeployRequest? output_request, MessageFormats.PlatformServices.Deployment.DeployResponse? output_response) =
                                                       _pluginLoader.CallPlugins<MessageFormats.PlatformServices.Deployment.DeployRequest?, Plugins.PluginBase, MessageFormats.PlatformServices.Deployment.DeployResponse>(
                                                           orig_request: deploymentItem.DeployRequest, orig_response: deploymentItem,
                                                           pluginDelegate: _pluginDelegates.PreKubernetesDeployment);

            if (output_response == null || output_request == null) {
                _logger.LogInformation("Plugins nullified '{messageType}' or '{responseMessageType}'.  Dropping request (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.GetType(), deploymentItem.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                RemoveFromCache(deploymentItem.ResponseHeader.TrackingId);
                return;
            }

            deploymentItem = output_response;
            deploymentItem.DeployRequest = output_request;

            // We're requesting an image container load
            if (deploymentItem.DeployRequest.AppContainerImage != null && (
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.LoadImageTarball ||
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply ||
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create)) {
                try {
                    _logger.LogInformation("Loading container tarball from '{fileName}' deployment to '{destinationRegistry}/{repo}:{tag}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContainerImage.TarballFileName, _appConfig.CONTAINER_REGISTRY, deploymentItem.DeployRequest.AppContainerImage.DestinationRepository, deploymentItem.DeployRequest.AppContainerImage.DestinationTag, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                    ProcessContainerImage(deploymentItem.DeployRequest.AppContainerImage);
                    if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.LoadImageTarball) {
                        deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
                        deploymentItem.ResponseHeader.Message = $"Image '{deploymentItem.DeployRequest.AppContainerImage.TarballFileName}' loaded successfully as '{deploymentItem.DeployRequest.AppContainerImage.DestinationRepository}:{deploymentItem.DeployRequest.AppContainerImage.DestinationTag}'";
                        CommitToCache(deploymentItem);
                        return;
                    }
                } catch (Exception ex) {
                    deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.GeneralFailure;
                    deploymentItem.ResponseHeader.Message = ex.Message;
                    CommitToCache(deploymentItem);
                    _logger.LogError("Failed to loading container tarball from '{fileName}' deployment to '{destinationRegistry}/{repo}:{tag}'.  Error: {error} (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContainerImage.TarballFileName, _appConfig.CONTAINER_REGISTRY, deploymentItem.DeployRequest.AppContainerImage.DestinationRepository, deploymentItem.DeployRequest.AppContainerImage.DestinationTag, ex.Message, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                    return;
                }
            }

            // Process the request for a build service
            if (deploymentItem.DeployRequest.AppContainerBuild != null && (
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.LoadImageTarball ||
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply ||
                deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create)) {
                _k8sClient.DeployItem(deploymentItem);
                if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.BuildImage) {
                    deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
                    deploymentItem.ResponseHeader.Message = $"Build request for '{deploymentItem.DeployRequest.AppContainerBuild.DestinationRepository}:{deploymentItem.DeployRequest.AppContainerBuild.DestinationTag}' submitted successfully";
                    CommitToCache(deploymentItem);
                    return;
                }
            }

            // This deployment has a file to send to the app.  Go ahead and send it
            if (deploymentItem.DeployRequest.AppContextFile != null) {
                string sourceFileName = Path.Combine(_scheduleImportDirectory, deploymentItem.DeployRequest.AppContextFile.FileName);
                if (!File.Exists(sourceFileName)) {
                    if (deploymentItem.DeployRequest.AppContextFile.Required) {
                        _logger.LogError("AppContextFile '{fileName}' to '{AppName}' specified in deployment, but file is not found and required = 'TRUE' (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContextFile.FileName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                        deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.GeneralFailure;
                        deploymentItem.ResponseHeader.Message = $"AppContextFile File '{sourceFileName}' not found and required = 'TRUE'.";
                        CommitToCache(deploymentItem);
                        return;
                    } else {
                        _logger.LogWarning("AppContextFile '{fileName}' to '{AppName}' specified in deployment, but file is not found and required = 'FALSE'.(AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContextFile.FileName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

                        // Link File only requested and nothing else to do
                        if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.UplinkFile) {
                            deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.NotFound;
                            deploymentItem.ResponseHeader.Message = $"File '{deploymentItem.DeployRequest.AppContextFile.FileName}' not found.  Unable to send to '{deploymentItem.DeployRequest.AppName}'";
                            CommitToCache(deploymentItem);
                            return;
                        }
                    }
                } else {
                    // Move the file from the inbox to the outbox
                    File.Move(sourceFileName, Path.Combine(_client.GetXFerDirectories().Result.outbox_directory, deploymentItem.DeployRequest.AppContextFile.FileName), overwrite: true);
                    // Link it to the app
                    MessageFormats.HostServices.Link.LinkRequest linkRequest = new() {
                        RequestHeader = new() {
                            TrackingId = Guid.NewGuid().ToString(),
                            CorrelationId = deploymentItem.DeployRequest.RequestHeader.CorrelationId
                        },
                        FileName = deploymentItem.DeployRequest.AppContextFile.FileName,
                        DestinationAppId = deploymentItem.DeployRequest.AppName,
                        LeaveSourceFile = false,
                        LinkType = MessageFormats.HostServices.Link.LinkRequest.Types.LinkType.Uplink,
                        Overwrite = true
                    };

                    _logger.LogInformation("AppContextFile '{fileName}' specified in deployment item.  Uplinking to '{AppName}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.AppContextFile.FileName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

                    // Send the new message to the link service
                    _client.DirectToApp(appId: $"hostsvc-{nameof(MessageFormats.Common.HostServices.Link).ToLower()}", message: linkRequest).Wait();

                    // Link File only requested and nothing else to do
                    if (deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.UplinkFile) {
                        deploymentItem.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
                        deploymentItem.ResponseHeader.Message = $"Successfully uplinked file '{deploymentItem.DeployRequest.AppContextFile.FileName}' to '{deploymentItem.DeployRequest.AppName}'";
                        CommitToCache(deploymentItem);
                        return;
                    }
                }
            }


            // Pass the deployment request to the k8s client for processing
            deploymentItem = _k8sClient.DeployItem(deploymentItem: deploymentItem);

            if (deploymentItem.ResponseHeader.Status != MessageFormats.Common.StatusCodes.Successful) {
                _logger.LogError("Failed to process '{messageType}' for '{AppName}' / '{DeployAction}' / '{trackingId}' / '{correlationId}'.  Error: {error}", deploymentItem.DeployRequest.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId, deploymentItem.ResponseHeader.Message);
                CommitToCache(deploymentItem);
                return;
            }

            _logger.LogDebug("Passing message type '{messageType}' and '{responseMessageType}' to plugins. (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.GetType(), deploymentItem.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

            (output_request, output_response) = _pluginLoader.CallPlugins<MessageFormats.PlatformServices.Deployment.DeployRequest?, Plugins.PluginBase, MessageFormats.PlatformServices.Deployment.DeployResponse>(
                                                           orig_request: deploymentItem.DeployRequest, orig_response: deploymentItem,
                                                           pluginDelegate: _pluginDelegates.PostKubernetesDeployment);

            _logger.LogDebug("Plugins finished processing '{messageType}' and '{responseMessageType}'. (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.GetType(), deploymentItem.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);


            if (output_response == null || output_request == null) {
                _logger.LogInformation("Plugins nullified '{messageType}' or '{responseMessageType}'.  Dropping request (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.GetType(), deploymentItem.GetType(), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                return;
            }


            if (!string.IsNullOrWhiteSpace(deploymentItem.DeployRequest.Schedule)) {
                CrontabSchedule crontabSchedule = CrontabSchedule.Parse(deploymentItem.DeployRequest.Schedule);

                _logger.LogInformation("Schedule detected '{schedule}'.  Adding next deployment for '{nextDeployRunTime}'.  (AppName: '{AppName}' / DeployAction: '{DeployAction}' / trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.Schedule, crontabSchedule.GetNextOccurrence(DateTime.UtcNow.AddSeconds(1)).ToString("dd-MM-yy-HH.mm.ss"), deploymentItem.DeployRequest.AppName, deploymentItem.DeployRequest.DeployAction, deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);

                MessageFormats.PlatformServices.Deployment.DeployResponse nextDeployment = deploymentItem.Clone();
                nextDeployment.ResponseHeader.TrackingId = Guid.NewGuid().ToString(); // Give the new deployment a new tracking ID
                nextDeployment.DeployRequest.StartTime = Timestamp.FromDateTime(crontabSchedule.GetNextOccurrence(DateTime.UtcNow.AddSeconds(1)));
                nextDeployment.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Pending;
                if (nextDeployment.DeployRequest.AppContainerBuild != null)
                    nextDeployment.DeployRequest.AppContainerBuild = null;

                if (nextDeployment.DeployRequest.AppContainerImage != null)
                    nextDeployment.DeployRequest.AppContainerImage = null;

                QueueDeployment(nextDeployment);
            }

            if ((deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply || deploymentItem.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create) && deploymentItem.DeployRequest.MaxDuration != null) {
                MessageFormats.PlatformServices.Deployment.DeployResponse deleteDeployment = deploymentItem.Clone();
                deleteDeployment.ResponseHeader.TrackingId = Guid.NewGuid().ToString(); // Give the new deployment a new tracking ID
                deleteDeployment.DeployRequest.DeployAction = MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Delete;
                deleteDeployment.DeployRequest.StartTime = Timestamp.FromDateTime(deploymentItem.DeployRequest.StartTime.ToDateTime().AddSeconds(deploymentItem.DeployRequest.MaxDuration.ToTimeSpan().TotalSeconds));
                deleteDeployment.DeployRequest.Priority = MessageFormats.Common.Priority.High;
                deleteDeployment.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Pending;

                if (deleteDeployment.DeployRequest.AppContainerBuild != null)
                    deleteDeployment.DeployRequest.AppContainerBuild = null;

                if (deleteDeployment.DeployRequest.AppContainerImage != null)
                    deleteDeployment.DeployRequest.AppContainerImage = null;

                _logger.LogInformation("Maximum Duration of '{timeSpan}' specified for app.  Requesting app delete for '{datetime}'.  (trackingId: '{trackingId}' / correlationId: '{correlationId}')", deploymentItem.DeployRequest.MaxDuration, deleteDeployment.DeployRequest.StartTime.ToDateTime(), deploymentItem.DeployRequest.RequestHeader.TrackingId, deploymentItem.DeployRequest.RequestHeader.CorrelationId);
                QueueDeployment(deleteDeployment);
            }

            // Write the results back to the cache
            CommitToCache(deploymentItem);
        }

        private void ProcessContainerImage(MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContainerImage appContainerImage) {
            _logger.LogInformation("Processing container image '{imageName}'", appContainerImage.TarballFileName);
            string tarballPath = Path.Combine(_scheduleImportDirectory, appContainerImage.TarballFileName);

            string imageImportName = _appConfig.CONTAINER_REGISTRY_INTERNAL + "/" + appContainerImage.DestinationRepository + ":" + appContainerImage.DestinationTag;

            if (string.IsNullOrWhiteSpace(_regctlApp)) {
                _logger.LogError("regctl not found.  Unable to import image for '{tarballPath}'", tarballPath);
                throw new ApplicationException($"regctl not found.  Unable to import image for '{tarballPath}'.  Rejecting deploy request");
            }

            _logger.LogInformation("Importing image '{tarballPath}' as '{imageImportName}'", tarballPath, imageImportName);

            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = _regctlApp,
                Arguments = $"image import {imageImportName} {tarballPath} --host reg={_appConfig.CONTAINER_REGISTRY_INTERNAL},tls=insecure",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _logger.LogDebug("Starting process: '{regctlApp} {arguments}'", startInfo.FileName, startInfo.Arguments);

            Process process = new Process { StartInfo = startInfo };
            process.Start();
            // process.StandardError.ReadToEnd();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            int returnCode = process.ExitCode;

            if (returnCode > 0) {
                throw new ApplicationException($"regctl failed to import image for '{tarballPath}'.  Rejecting deploy request.  Output: {output}.  Error: {error}  Return Code: {returnCode}");
            }

            _logger.LogDebug("Deleting tarball '{tarballPath}'", tarballPath);
            File.Delete(tarballPath);

            _logger.LogInformation("Successfully imported image '{tarballPath}' as '{imageImportName}'", tarballPath, imageImportName);
        }
    }
}
