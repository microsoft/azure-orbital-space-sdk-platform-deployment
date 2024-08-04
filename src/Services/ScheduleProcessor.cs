using System.Data;

namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class Services {
    public class ScheduleProcessor : BackgroundService {
        private readonly ILogger<ScheduleProcessor> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Microsoft.Azure.SpaceFx.Core.Services.PluginLoader _pluginLoader;
        private readonly Services.DeployRequestProcessor _deployRequestProcessor;
        private readonly PluginDelegates _pluginDelegates;
        private readonly Models.APP_CONFIG _appConfig;
        private readonly Core.Client _client;
        private readonly Utils.DownlinkUtil _downlinkUtil;
        private string _scheduleImportDirectory;
        private string _outboxDirectory;
        private string _inboxDirectory;

        public ScheduleProcessor(ILogger<ScheduleProcessor> logger, IServiceProvider serviceProvider, PluginDelegates pluginDelegates, IOptions<Models.APP_CONFIG> appConfig, Core.Services.PluginLoader pluginLoader, Core.Client client, Services.DeployRequestProcessor deployRequestProcessor, Utils.DownlinkUtil downlinkUtil) {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _pluginDelegates = pluginDelegates;
            _pluginLoader = pluginLoader;
            _appConfig = appConfig.Value;
            _client = client;
            _deployRequestProcessor = deployRequestProcessor;
            _scheduleImportDirectory = Path.Combine(_client.GetXFerDirectories().Result.inbox_directory, _appConfig.SCHEDULE_IMPORT_DIRECTORY);
            _outboxDirectory = _client.GetXFerDirectories().Result.outbox_directory;
            _inboxDirectory = _client.GetXFerDirectories().Result.inbox_directory;
            _downlinkUtil = downlinkUtil;

            _logger.LogInformation("Services.{serviceName} Initialized.  SCHEDULE_IMPORT_DIRECTORY: {scheduleDirectory}   SCHEDULE_DIRECTORY_POLLING_MS: {scheduleDirectoryTiming} ", nameof(ScheduleProcessor), _scheduleImportDirectory, _appConfig.SCHEDULE_DIRECTORY_POLLING_MS);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                using (var scope = _serviceProvider.CreateScope()) {
                    // Create the schedule directory if it doesn't already exist
                    if (!Directory.Exists(_scheduleImportDirectory)) {
                        _logger.LogDebug("Schedule drop-off '{drop_off_dir}' directory does not exist.  Creating...", _scheduleImportDirectory);
                        Directory.CreateDirectory(_scheduleImportDirectory);
                        _logger.LogDebug("Successfully created schedule drop-off directory '{drop_off_dir}'", _scheduleImportDirectory);
                    }

                    List<Task<List<MessageFormats.PlatformServices.Deployment.DeployResponse>>> scheduleFileProcessingTasks = new();
                    string[] files = Directory.GetFiles(_scheduleImportDirectory, "*.json", SearchOption.TopDirectoryOnly);

                    if (files.Length == 0) {
                        await Task.Delay(_appConfig.SCHEDULE_DIRECTORY_POLLING_MS, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Schedule import directory '{scheduleDirectory}' scanned.  '{itemCount}' schedule files found.", _scheduleImportDirectory, files.Count());

                    // Enumerate through the files in the schedule directory and asynchronously process them
                    foreach (string file in files) {
                        scheduleFileProcessingTasks.Add(Task.Run(() => {
                            List<MessageFormats.PlatformServices.Deployment.DeployResponse> deployResponses = new();
                            MessageFormats.Common.ResponseHeader responseNotify = new();
                            string downlinkFileName = "";
                            try {
                                deployResponses = ProcessScheduleFile(file);

                                responseNotify = new() {
                                    TrackingId = Guid.NewGuid().ToString(),
                                    CorrelationId = Guid.NewGuid().ToString(),
                                    Status = MessageFormats.Common.StatusCodes.Pending,
                                    Message = $"Schedule file '{file}' has been proceed and items are pending."
                                };

                                File.Move(file, file + ".processed");
                                downlinkFileName = file + ".processed";
                            } catch (FileNotFoundException fileEx) {
                                _logger.LogWarning("Detected a missing file '{file}'.  Likely hasn't finished uploaded.  Will retry. ", fileEx.FileName);
                                return deployResponses; // This'll be empty
                            } catch (Utils.NotAScheduleFileException notAScheduleFileEx) {
                                _logger.LogInformation("Detected a json file that isn't a schedule file.  {exceptionMessage}", notAScheduleFileEx.Message);
                                return deployResponses; // This'll be empty
                            } catch (TimeoutException) {
                                _logger.LogWarning("Timeout detected waiting for files in Error processing schedule file.  Cancelling processing and will reattempt on next iteration");
                                return deployResponses; // This'll be empty
                            } catch (Exception ex) {
                                // We have an error - trigger a downlink of the schedule file and log
                                _logger.LogError("Error processing schedule file '{file}'.  Rejecting schedule file.  Please reupload for reprocessing  Error: {error}", file, ex.Message);
                                responseNotify = new() {
                                    TrackingId = Guid.NewGuid().ToString(),
                                    CorrelationId = Guid.NewGuid().ToString(),
                                    Status = MessageFormats.Common.StatusCodes.Rejected,
                                    Message = $"Schedule file '{file}' rejected.  Error: {ex.Message}"
                                };
                                _logger.LogDebug("Downlinking error log and schedule for '{file}'", file);
                                File.Move(file, file + ".error");
                                downlinkFileName = file + ".error";
                            }

                            // output the values to a disk and downlink it and the schedule file back to MTS
                            var jsonFormatter = new Google.Protobuf.JsonFormatter(Google.Protobuf.JsonFormatter.Settings.Default);
                            File.WriteAllText(Path.Combine(_outboxDirectory, Path.GetFileName(file) + ".response"), jsonFormatter.Format(responseNotify));

                            _downlinkUtil.DownlinkFile(Path.Combine(_outboxDirectory, Path.GetFileName(file) + ".response"));
                            _downlinkUtil.DownlinkFile(downlinkFileName);

                            if (responseNotify.Status == MessageFormats.Common.StatusCodes.Rejected) {
                                return new List<MessageFormats.PlatformServices.Deployment.DeployResponse>();
                            }

                            foreach (MessageFormats.PlatformServices.Deployment.DeployResponse deployItem in deployResponses) {
                                MessageFormats.PlatformServices.Deployment.DeployResponse response = _deployRequestProcessor.QueueDeployment(deployItem);
                                _downlinkUtil.DownlinkDeploymentResponse(response);
                            }

                            return deployResponses;
                        }));
                    }

                    // Wait for all the processing to finish
                    Task.WaitAll(scheduleFileProcessingTasks.ToArray(), stoppingToken);

                    // Analyze the results and send the successes to the scheduler
                    scheduleFileProcessingTasks.ForEach(task => {
                        if (task.IsFaulted || task.Result.Count == 0)
                            return;

                        _logger.LogDebug("Passing '{deployRequestCount}' deploy requests to the deploy request processor", task.Result.Count);
                        foreach (MessageFormats.PlatformServices.Deployment.DeployResponse deployItem in task.Result) {
                            MessageFormats.PlatformServices.Deployment.DeployResponse response = _deployRequestProcessor.QueueDeployment(deployItem);
                            _downlinkUtil.DownlinkDeploymentResponse(response);
                        }

                    });

                    scheduleFileProcessingTasks.Clear();

                    await Task.Delay(_appConfig.SCHEDULE_DIRECTORY_POLLING_MS, stoppingToken);
                }
            }
        }

        private void WaitForFileToFinishCopying(string filePath) {
            DateTime maxTimeToWait = DateTime.Now.Add(TimeSpan.FromMilliseconds(_appConfig.SCHEDULE_FILE_COPY_TIMEOUT_MS));
            bool isFinished = false;

            while (!isFinished && DateTime.Now <= maxTimeToWait) {
                try {
                    using FileStream inputStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    isFinished = true;
                } catch (IOException e) when (e.HResult != -2147024864) {
                    // e.HResult of -2147024864 is a sharing violation; the file is still being copied.
                    // Only rethrow if the error is not a sharing violation
                    throw;
                }
            }

            if (!isFinished) throw new TimeoutException($"Failed to get a write handle to {filePath} within {TimeSpan.FromMilliseconds(_appConfig.SCHEDULE_FILE_COPY_TIMEOUT_MS).TotalSeconds}.");
        }

        private List<MessageFormats.PlatformServices.Deployment.DeployResponse> ProcessScheduleFile(string scheduleFilePath) {
            List<MessageFormats.PlatformServices.Deployment.DeployResponse> returnDeployItems = new();
            int itemCount = 0;

            _logger.LogInformation("Processing schedule '{file}'.", scheduleFilePath);
            _logger.LogDebug("Waiting for '{file}' to finisn copying...", scheduleFilePath);
            try {
                WaitForFileToFinishCopying(scheduleFilePath);
            } catch (TimeoutException) {
                _logger.LogWarning("Timed out waiting for '{file}' to become avilable for processing.  Will reattempt processing on next iteration.", scheduleFilePath);
                throw;
            } catch (Exception ex) {
                _logger.LogError("Error waiting for '{file}' to become available for processing.  Error: {error}", scheduleFilePath, ex.Message);
                throw;
            }


            _logger.LogDebug("'{file}' has finished copying and ready for processing", scheduleFilePath);

            if (JsonDocument.Parse(File.ReadAllText(scheduleFilePath)).RootElement.ValueKind != JsonValueKind.Array) {
                _logger.LogWarning("Found a jsonFile that isn't a schedule file (Root element is not an array).  '{file}' will not be processed", scheduleFilePath);
                throw new Utils.NotAScheduleFileException($"File '{scheduleFilePath}' is not a schedule file.  Root element is not an array.");
            }

            _logger.LogTrace("Passing schedule file '{scheduleFile}' to plugins", scheduleFilePath);
            // Pass the request to the plugins before we process it
            StringValue? pluginResult =
               _pluginLoader.CallPlugins<StringValue?, Plugins.PluginBase>(
                   orig_request: new StringValue() { Value = scheduleFilePath },
                   pluginDelegate: _pluginDelegates.ProcessScheduleFile);

            _logger.LogTrace("Plugins finished processing schedule file '{scheduleFile}'", scheduleFilePath);

            // Update the request if our plugins changed it
            if (pluginResult == null || pluginResult == default(StringValue) || string.IsNullOrWhiteSpace(pluginResult.Value)) {
                _logger.LogWarning("Plugins nullified schedule file.  Rejecting '{filePath}'", scheduleFilePath);
                throw new ApplicationException("Plugins nullified schedule file.  Rejecting processing");
            }

            if (scheduleFilePath != pluginResult.Value) {
                _logger.LogInformation("Updating schedule path source based on plugin result.  Old value: '{oldValue}' ; New Value: '{newValue}'", scheduleFilePath, pluginResult.Value);
                scheduleFilePath = pluginResult.Value;
            }

            if (!File.Exists(scheduleFilePath)) {
                _logger.LogError("Schedule file '{filePath}' does not exist - likely updated to a different path by a plugin.  Unable to process file", scheduleFilePath);
                throw new ApplicationException($"Schedule file '{scheduleFilePath}' does not exist.  Unable to process");
            }

            JsonDocument doc = JsonDocument.Parse(File.ReadAllText(scheduleFilePath));

            _logger.LogInformation("Processing {count} items from '{file}'", doc.RootElement.GetArrayLength(), scheduleFilePath);

            try {
                foreach (JsonElement element in doc.RootElement.EnumerateArray()) {
                    itemCount++;

                    MessageFormats.PlatformServices.Deployment.DeployRequest _request = ProcessScheduleFileItem(element);
                    MessageFormats.PlatformServices.Deployment.DeployResponse _response = ValidatePrerequisites(_request);

                    _response.DeployRequest = _request;

                    if (_response.ResponseHeader.Status == MessageFormats.Common.StatusCodes.InvalidArgument) {
                        _downlinkUtil.DownlinkDeploymentResponse(_response);
                        throw new Exception($"Item #'{itemCount}' in '{scheduleFilePath}' is invalid.  Error: {_response.ResponseHeader.Message}");
                    }


                    // The YAML file doesn't exist yet - kick the out and reprocess on the next iteration
                    if (_response.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply
                                           || _response.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create
                                           || _response.DeployRequest.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Delete) {
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, _response.DeployRequest.YamlFileContents));

                        string yamlFilePath = Path.Combine(_scheduleImportDirectory, _response.DeployRequest.YamlFileContents);
                        _response.DeployRequest.YamlFileContents = File.ReadAllText(Path.Combine(_scheduleImportDirectory, _response.DeployRequest.YamlFileContents));
                        File.Delete(yamlFilePath);
                    }



                    if (_response.DeployRequest.AppContainerImage != null)
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, _response.DeployRequest.AppContainerImage.TarballFileName));


                    if (_response.DeployRequest.AppContainerBuild != null)
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, _response.DeployRequest.AppContainerBuild.DockerFile));

                    returnDeployItems.Add(_response);
                }
            } catch (FileNotFoundException fileEx) {
                _logger.LogError("File '{file}' found.  Likely hasnt finished uploading.  Will retry.", fileEx.FileName);
                throw;
            } catch (DataException dataEx) {
                _logger.LogError("Item #'{itemX}' in '{file}' is invalid.  Rejecting entire schedule file.  Error: '{error}'.  Please re-upload for reprocessing", itemCount, scheduleFilePath, dataEx.Message);
                throw;
            } catch (TimeoutException) {
                _logger.LogWarning("Timed out waiting for a to become avilable for processing.  Will reattempt processing on next iteration.");
                throw;
            } catch (Exception ex) {
                _logger.LogError("Error parsing item #'{itemX}' in '{file}'.  Error: {error}", itemCount, scheduleFilePath, ex.Message);
                throw;
            }

            return returnDeployItems;
        }

        /// <summary>
        /// Custom processes a json element to mitigate case differences.  Returns and returns a <see cref="MessageFormats.PlatformServices.Deployment.DeployRequest"/> object.
        /// </summary>
        /// <param name="element">The <see cref="JsonElement"/> representing the schedule file item.</param>
        /// <returns>A <see cref="MessageFormats.PlatformServices.Deployment.DeployRequest"/> object containing the processed data.</returns>
        private MessageFormats.PlatformServices.Deployment.DeployRequest ProcessScheduleFileItem(JsonElement element) {
            MessageFormats.PlatformServices.Deployment.DeployRequest _request = new MessageFormats.PlatformServices.Deployment.DeployRequest();
            Google.Protobuf.JsonParser protoJsonParser = new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default);
            string? propName, innerPropName;
            int enumValueInt = 0;

            // Parse all the string as they're easy to pull
            if ((propName = GetPropertyNameForJsonElement("AppName", element)) != null)
                _request.AppName = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("NameSpace", element)) != null)
                _request.NameSpace = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("AppGroupLabel", element)) != null)
                _request.AppGroupLabel = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("CustomerTrackingId", element)) != null)
                _request.CustomerTrackingId = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("Schedule", element)) != null)
                _request.Schedule = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("YamlFileContents", element)) != null)
                _request.YamlFileContents = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("ContainerInjectionTarget", element)) != null)
                _request.ContainerInjectionTarget = element.GetProperty(propName).GetString();

            if ((propName = GetPropertyNameForJsonElement("RequestHeader", element)) != null) {
                _request.RequestHeader = JsonSerializer.Deserialize<MessageFormats.Common.RequestHeader>(element.GetProperty(propName).GetRawText());
            } else {
                _request.RequestHeader = new MessageFormats.Common.RequestHeader();
                _request.RequestHeader.TrackingId = Guid.NewGuid().ToString();
                _request.RequestHeader.CorrelationId = _request.RequestHeader.TrackingId;
            }


            MessageFormats.Common.Priority? priorityEnum = null;
            if ((propName = GetPropertyNameForJsonElement("Priority", element)) != null) {
                if (element.GetProperty(propName).ValueKind == JsonValueKind.Number) {
                    priorityEnum = (MessageFormats.Common.Priority) element.GetProperty(propName).GetInt32();
                } else if (element.GetProperty(propName).ValueKind == JsonValueKind.String) {
                    if (int.TryParse(element.GetProperty(propName).GetString(), out enumValueInt))
                        priorityEnum = (MessageFormats.Common.Priority) enumValueInt;
                    else {
                        if (System.Enum.TryParse<MessageFormats.Common.Priority>(element.GetProperty(propName).GetString(), true, out var parsedValue)) {
                            priorityEnum = parsedValue;
                        }
                    }
                }
            }
            _request.Priority = priorityEnum ?? MessageFormats.Common.Priority.Low;



            MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions? deployActionEnum = null;
            if ((propName = GetPropertyNameForJsonElement("DeployAction", element)) != null) {
                if (element.GetProperty(propName).ValueKind == JsonValueKind.Number) {
                    deployActionEnum = (MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions) element.GetProperty(propName).GetInt32();
                } else if (element.GetProperty(propName).ValueKind == JsonValueKind.String) {
                    if (int.TryParse(element.GetProperty(propName).GetString(), out enumValueInt))
                        deployActionEnum = (MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions) enumValueInt;
                    else {
                        if (System.Enum.TryParse<MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions>(element.GetProperty(propName).GetString(), true, out var parsedValue)) {
                            deployActionEnum = parsedValue;
                        }
                    }
                }
            }

            if (deployActionEnum.HasValue == false)
                throw new ArgumentException($"DeployAction is invalid and can't be parsed");

            _request.DeployAction = deployActionEnum.Value;

            if ((propName = GetPropertyNameForJsonElement("StartTime", element)) != null) {
                if (element.GetProperty(propName).ValueKind == JsonValueKind.String) {
                    // String is coming in like '2024-03-18T21:24:21.244022600Z'
                    _request.StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.Parse(element.GetProperty(propName).GetString() ?? DateTime.UtcNow.ToString()), DateTimeKind.Utc));
                } else {
                    //string is coming in like {"Seconds": 1684531385,"Nanos": 687797600}
                    _request.StartTime = JsonSerializer.Deserialize<Timestamp>(element.GetProperty(propName).GetRawText());
                }
            }

            if ((propName = GetPropertyNameForJsonElement("MaxDuration", element)) != null) {
                if (element.GetProperty(propName).ValueKind == JsonValueKind.String) {
                    // String is coming in like '300s'
                    _request.MaxDuration = protoJsonParser.Parse<Duration>(element.GetProperty(propName).GetRawText());
                } else {
                    //string is coming in like {"Seconds": 1684531385,"Nanos": 687797600}
                    _request.MaxDuration = JsonSerializer.Deserialize<Duration>(element.GetProperty(propName).GetRawText());
                }
            }


            MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions? gpuOptionEnum = null;
            if ((propName = GetPropertyNameForJsonElement("GpuRequirement", element)) != null) {
                if (element.GetProperty(propName).ValueKind == JsonValueKind.Number) {
                    gpuOptionEnum = (MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions) element.GetProperty(propName).GetInt32();
                } else if (element.GetProperty(propName).ValueKind == JsonValueKind.String) {
                    if (int.TryParse(element.GetProperty(propName).GetString(), out enumValueInt))
                        gpuOptionEnum = (MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions) enumValueInt;
                    else {
                        if (System.Enum.TryParse<MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions>(element.GetProperty(propName).GetString(), true, out var parsedValue)) {
                            gpuOptionEnum = parsedValue;
                        }
                    }
                }
            }

            _request.GpuRequirement = gpuOptionEnum ?? MessageFormats.PlatformServices.Deployment.DeployRequest.Types.GpuOptions.None;

            if ((propName = GetPropertyNameForJsonElement("AppContainerImage", element)) != null) {
                JsonElement appContainerImage = element.GetProperty(propName);
                if (appContainerImage.ValueKind != JsonValueKind.Null) {
                    _request.AppContainerImage = new MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContainerImage();

                    if ((innerPropName = GetPropertyNameForJsonElement("TarballFileName", appContainerImage)) != null)
                        _request.AppContainerImage.TarballFileName = appContainerImage.GetProperty(innerPropName).GetString();

                    if ((innerPropName = GetPropertyNameForJsonElement("DestinationRepository", appContainerImage)) != null)
                        _request.AppContainerImage.DestinationRepository = appContainerImage.GetProperty(innerPropName).GetString();

                    if ((innerPropName = GetPropertyNameForJsonElement("DestinationTag", appContainerImage)) != null)
                        _request.AppContainerImage.DestinationTag = appContainerImage.GetProperty(innerPropName).GetString();
                }
            }


            if ((propName = GetPropertyNameForJsonElement("AppContainerBuild", element)) != null) {
                JsonElement appContainerBuild = element.GetProperty(propName);
                if (appContainerBuild.ValueKind != JsonValueKind.Null) {
                    _request.AppContainerBuild = new MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContainerBuild();

                    if ((innerPropName = GetPropertyNameForJsonElement("DockerFile", appContainerBuild)) != null)
                        _request.AppContainerBuild.DockerFile = appContainerBuild.GetProperty(innerPropName).GetString();

                    if ((innerPropName = GetPropertyNameForJsonElement("DestinationRepository", appContainerBuild)) != null)
                        _request.AppContainerBuild.DestinationRepository = appContainerBuild.GetProperty(innerPropName).GetString();

                    if ((innerPropName = GetPropertyNameForJsonElement("DestinationTag", appContainerBuild)) != null)
                        _request.AppContainerBuild.DestinationTag = appContainerBuild.GetProperty(innerPropName).GetString();

                    if ((innerPropName = GetPropertyNameForJsonElement("BuildArguments", appContainerBuild)) != null) {
                        JsonElement buildArgsRoot = appContainerBuild.GetProperty(innerPropName);

                        foreach (JsonProperty buildArg in buildArgsRoot.EnumerateObject()) {
                            _request.AppContainerBuild.BuildArguments.Add(buildArg.Name, buildArg.Value.GetString());
                        }
                    }
                }
            }

            if ((propName = GetPropertyNameForJsonElement("AppContextString", element)) != null) {
                JsonElement appContextString = element.GetProperty(propName);
                if (appContextString.ValueKind != JsonValueKind.Null && (innerPropName = GetPropertyNameForJsonElement("AppContext", appContextString)) != null) {
                    _request.AppContextString = new MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContextString();
                    _request.AppContextString.AppContext = appContextString.GetProperty(innerPropName).GetString();
                }
            }

            if ((propName = GetPropertyNameForJsonElement("AppContextFile", element)) != null) {
                JsonElement appContextFile = element.GetProperty(propName);
                if (appContextFile.ValueKind != JsonValueKind.Null && (innerPropName = GetPropertyNameForJsonElement("FileName", appContextFile)) != null) {
                    _request.AppContextFile = new MessageFormats.PlatformServices.Deployment.DeployRequest.Types.AppContextFile() {
                        FileName = appContextFile.GetProperty(innerPropName).GetString()
                    };

                    if ((innerPropName = GetPropertyNameForJsonElement("Required", appContextFile)) != null)
                        _request.AppContextFile.Required = bool.Parse(appContextFile.GetProperty(innerPropName).GetString() ?? "false");
                }
            }

            return _request;
        }

        private string? GetPropertyNameForJsonElement(string propertyName, JsonElement element) {
            var propertyNames = element.EnumerateObject().Select(property => property.Name).ToList();
            return propertyNames.FirstOrDefault(name => name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        }

        private MessageFormats.PlatformServices.Deployment.DeployResponse ValidatePrerequisites(MessageFormats.PlatformServices.Deployment.DeployRequest request) {
            List<string> errorFields = new List<string>();

            if (string.IsNullOrWhiteSpace(request.RequestHeader.TrackingId)) request.RequestHeader.TrackingId = Guid.NewGuid().ToString();
            if (string.IsNullOrWhiteSpace(request.RequestHeader.CorrelationId)) request.RequestHeader.CorrelationId = request.RequestHeader.TrackingId;

            MessageFormats.PlatformServices.Deployment.DeployResponse response = Core.Utils.ResponseFromRequest(request, new MessageFormats.PlatformServices.Deployment.DeployResponse());
            response.ResponseHeader.Message = "";
            response.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Pending;
            response.DeployRequest = request;


            if (string.IsNullOrWhiteSpace(request.AppName))
                errorFields.Add(nameof(request.AppName));


            if (string.IsNullOrWhiteSpace(request.DeployAction.ToString()))
                errorFields.Add(nameof(request.DeployAction));

            if (string.IsNullOrWhiteSpace(request.NameSpace))
                request.NameSpace = "payload-app";

            if (request.StartTime == null)
                request.StartTime = System.DateTime.UtcNow.ToTimestamp();

            if (request.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Apply
                        || request.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Create
                        || request.DeployAction == MessageFormats.PlatformServices.Deployment.DeployRequest.Types.DeployActions.Delete) {

                if (string.IsNullOrWhiteSpace(request.YamlFileContents)) {
                    errorFields.Add(nameof(request.YamlFileContents));
                } else {
                    // This lets the schedule retry next go round
                    if (!File.Exists(Path.Combine(_scheduleImportDirectory, request.YamlFileContents))) {
                        throw new FileNotFoundException($"YamlFile '{request.YamlFileContents}' does not exist at {_scheduleImportDirectory}.", fileName: request.YamlFileContents);
                    }

                    // Wait for the file to finish copying or timeout if we didn't receive one
                    try {
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, request.YamlFileContents));
                    } catch (TimeoutException) {
                        // Only trip an error if the file is required
                        if (request.AppContextFile.Required == true) {
                            throw new FileNotFoundException($"YamlFile '{request.YamlFileContents}' still copying to {_scheduleImportDirectory}.", fileName: request.YamlFileContents);
                        }
                    }
                }


                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^a-zA-Z0-9_-]");
                if (regex.IsMatch(Path.GetFileNameWithoutExtension(request.YamlFileContents))) {
                    response.ResponseHeader.Message += "YamlFileContents value invalid (special characters founds). (RegEx matched on '[^a-zA-Z0-9_]').";
                    errorFields.Add(nameof(request.YamlFileContents));
                }
            }

            if (request.AppContainerImage != null) {
                if (string.IsNullOrWhiteSpace(request.AppContainerImage.TarballFileName)) {
                    errorFields.Add(nameof(request.AppContainerImage.TarballFileName));
                } else {
                    // This lets the schedule retry next go round
                    if (!File.Exists(Path.Combine(_scheduleImportDirectory, request.AppContainerImage.TarballFileName))) {
                        throw new FileNotFoundException($"AppContainerImage Tarball '{request.AppContainerImage.TarballFileName}' does not exist at {_scheduleImportDirectory}.", fileName: request.AppContainerImage.TarballFileName);
                    }

                    // Wait for the file to finish copying or timeout if we didn't receive one
                    try {
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, request.AppContainerImage.TarballFileName));
                    } catch (TimeoutException) {
                        // Only trip an error if the file is required
                        if (request.AppContextFile.Required == true) {
                            throw new FileNotFoundException($"DockerFile '{request.AppContainerImage.TarballFileName}' still copying to {_scheduleImportDirectory}.", fileName: request.AppContainerImage.TarballFileName);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(request.AppContainerImage.DestinationRepository))
                    errorFields.Add(nameof(request.AppContainerImage.DestinationRepository));

                if (string.IsNullOrWhiteSpace(request.AppContainerImage.DestinationTag))
                    errorFields.Add(nameof(request.AppContainerImage.DestinationTag));
            }

            if (request.AppContainerBuild != null) {
                if (string.IsNullOrWhiteSpace(request.AppContainerBuild.DockerFile)) {
                    errorFields.Add(nameof(request.AppContainerBuild.DockerFile));
                } else {
                    // This lets the schedule retry next go round
                    if (!File.Exists(Path.Combine(_scheduleImportDirectory, request.AppContainerBuild.DockerFile))) {
                        throw new FileNotFoundException($"DockerFile'{request.AppContainerBuild.DockerFile}' does not exist at {_scheduleImportDirectory}.", fileName: request.AppContainerBuild.DockerFile);
                    }

                    // Wait for the file to finish copying or timeout if we didn't receive one
                    try {
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, request.AppContainerBuild.DockerFile));
                    } catch (TimeoutException) {
                        // Only trip an error if the file is required
                        if (request.AppContextFile.Required == true) {
                            throw new FileNotFoundException($"DockerFile '{request.AppContainerBuild.DockerFile}' still copying to {_scheduleImportDirectory}.", fileName: request.AppContainerBuild.DockerFile);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(request.AppContainerBuild.DestinationRepository))
                    errorFields.Add(nameof(request.AppContainerBuild.DestinationRepository));

                if (string.IsNullOrWhiteSpace(request.AppContainerBuild.DestinationTag))
                    errorFields.Add(nameof(request.AppContainerBuild.DestinationTag));
            }

            if (request.AppContextFile != null) {
                if (string.IsNullOrWhiteSpace(request.AppContextFile.FileName)) {
                    errorFields.Add(nameof(request.AppContextFile.FileName));
                } else {
                    // This lets the schedule retry next go round
                    if (!File.Exists(Path.Combine(_scheduleImportDirectory, request.AppContextFile.FileName))) {
                        throw new FileNotFoundException($"AppContextFile '{request.AppContextFile.FileName}' does not exist at {_scheduleImportDirectory}.", fileName: request.AppContextFile.FileName);
                    }

                    // Wait for the file to finish copying or timeout if we didn't receive one
                    try {
                        WaitForFileToFinishCopying(Path.Combine(_scheduleImportDirectory, request.AppContextFile.FileName));
                    } catch (TimeoutException) {
                        // Only trip an error if the file is required
                        if (request.AppContextFile.Required == true) {
                            throw new FileNotFoundException($"AppContextFile '{request.AppContextFile.FileName}' still copying to {_scheduleImportDirectory}.", fileName: request.AppContextFile.FileName);
                        }
                    }
                }
            }

            if (errorFields.Count > 0) {
                response.ResponseHeader.Status = MessageFormats.Common.StatusCodes.InvalidArgument;
                response.ResponseHeader.Message += "The following fields are required: " + string.Join(", ", errorFields);
            }

            return response;

        }

    }

}


