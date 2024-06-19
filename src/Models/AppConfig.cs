using YamlDotNet.Serialization;

namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public static class Models {
    public class APP_CONFIG : Core.APP_CONFIG {
        [Flags]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum PluginPermissions {
            NONE = 0,
            DEPLOY_REQUEST = 1 << 0,
            DEPLOY_RESPONSE = 1 << 1,
            LISTEM_ITEM_REQUEST = 1 << 2,
            LISTEM_ITEM_RESPONSE = 1 << 3,
            LOG_REQUEST = 1 << 4,
            LOG_RESPONSE = 1 << 5,
            PROCESS_SCHEDULE_FILE = 1 << 6,
            PRE_KUBERNETES_DEPLOYMENT = 1 << 7,
            POST_KUBERNETES_DEPLOYMENT = 1 << 8,
            ALL = DEPLOY_REQUEST | DEPLOY_RESPONSE | LISTEM_ITEM_REQUEST | LISTEM_ITEM_RESPONSE | LOG_REQUEST | LOG_RESPONSE | PROCESS_SCHEDULE_FILE | PRE_KUBERNETES_DEPLOYMENT | POST_KUBERNETES_DEPLOYMENT
        }

        public class PLUG_IN : Core.Models.PLUG_IN {
            [JsonConverter(typeof(JsonStringEnumConverter))]

            public PluginPermissions CALCULATED_PLUGIN_PERMISSIONS {
                get {
                    PluginPermissions result;
                    System.Enum.TryParse(PLUGIN_PERMISSIONS, out result);
                    return result;
                }
            }

            public PLUG_IN() {
                PLUGIN_PERMISSIONS = "";
                PROCESSING_ORDER = 100;
            }
        }


        public int SCHEDULE_FILE_COPY_TIMEOUT_MS { get; set; }
        public int SCHEDULE_DIRECTORY_POLLING_MS { get; set; }
        public int SCHEDULE_SERVICE_POLLING_MS { get; set; }
        public bool ENABLE_YAML_DEBUG { get; set; }
        public bool BUILD_SERIVCE_ENABLED { get; set; }
        public string BUILD_SERIVCE_REPOSITORY { get; set; }
        public string BUILD_SERIVCE_TAG { get; set; }
        public bool PURGE_SCHEDULE_ON_BOOTUP { get; set; }
        public string CONTAINER_REGISTRY { get; set; }
        public string CONTAINER_REGISTRY_INTERNAL { get; set; }
        public string SCHEDULE_IMPORT_DIRECTORY { get; set; }
        public string DAPR_ANNOTATIONS { get; set; }
        public string DEFAULT_LIMIT_MEMORY { get; set; }
        public string DEFAULT_LIMIT_CPU { get; set; }
        public string DEFAULT_REQUEST_MEMORY { get; set; }
        public string DEFAULT_REQUEST_CPU { get; set; }
        public string FILESERVER_APP_CRED_NAME { get; set; }
        public string FILESERVER_CRED_NAME { get; set; }
        public string FILESERVER_CRED_NAMESPACE { get; set; }
        public string FILESERVER_PERSISTENT_VOLUMES { get; set; }
        public string FILESERVER_PERSISTENT_VOLUMECLAIMS { get; set; }
        public string FILESERVER_CLIENT_VOLUME_MOUNTS { get; set; }
        public string FILESERVER_CLIENT_VOLUMES { get; set; }
        public string PAYLOAD_APP_ANNOTATIONS { get; set; }
        public string PAYLOAD_APP_CONFIG { get; set; }
        public string PAYLOAD_APP_LABELS { get; set; }
        public string PAYLOAD_APP_ENVIRONMENTVARIABLES { get; set; }
        public TimeSpan DEFAULT_MAX_DURATION { get; set; }

        public APP_CONFIG() : base() {
            SCHEDULE_DIRECTORY_POLLING_MS = int.Parse(Core.GetConfigSetting("scheduledirectorypollingms").Result);
            SCHEDULE_FILE_COPY_TIMEOUT_MS = int.Parse(Core.GetConfigSetting("scheduledirectorycopytimeoutms").Result);
            SCHEDULE_SERVICE_POLLING_MS = int.Parse(Core.GetConfigSetting("scheduleservicepollingms").Result);

            ENABLE_YAML_DEBUG = bool.Parse(Core.GetConfigSetting("enableyamldebug").Result);
            BUILD_SERIVCE_ENABLED = bool.Parse(Core.GetConfigSetting("buildserviceenabled").Result);
            BUILD_SERIVCE_REPOSITORY = Core.GetConfigSetting("buildservicerepository").Result;
            BUILD_SERIVCE_TAG = Core.GetConfigSetting("buildservicetag").Result;

            PURGE_SCHEDULE_ON_BOOTUP = bool.Parse(Core.GetConfigSetting("purgescheduleonboot").Result);
            CONTAINER_REGISTRY = Core.GetConfigSetting("containerregistry").Result;
            CONTAINER_REGISTRY_INTERNAL = Core.GetConfigSetting("containerregistryinternal").Result;
            SCHEDULE_IMPORT_DIRECTORY = Core.GetConfigSetting("scheduledirectory").Result;
            DEFAULT_MAX_DURATION = TimeSpan.Parse(Core.GetConfigSetting("defaultappmaxruntime").Result);
            SCHEDULE_IMPORT_DIRECTORY = Core.GetConfigSetting("scheduledirectory").Result;
            DEFAULT_LIMIT_MEMORY = Core.GetConfigSetting("defaultlimitmemory").Result;
            DEFAULT_LIMIT_CPU = Core.GetConfigSetting("defaultlimitcpu").Result;
            DEFAULT_REQUEST_MEMORY = Core.GetConfigSetting("defaultrequestmemory").Result;
            DEFAULT_REQUEST_CPU = Core.GetConfigSetting("defaultrequestcpu").Result;

            FILESERVER_APP_CRED_NAME = Core.GetConfigSetting("fileserverappcredname").Result;
            FILESERVER_CRED_NAME = Core.GetConfigSetting("fileservercredname").Result;
            FILESERVER_CRED_NAMESPACE = Core.GetConfigSetting("fileservercrednamespace").Result;

            FILESERVER_PERSISTENT_VOLUMES = Core.GetConfigSetting("fileserverclientpv").Result;
            FILESERVER_PERSISTENT_VOLUMECLAIMS = Core.GetConfigSetting("fileserverclientpvc").Result;

            FILESERVER_CLIENT_VOLUMES = Core.GetConfigSetting("fileServerclientvolumes").Result;
            FILESERVER_CLIENT_VOLUME_MOUNTS = Core.GetConfigSetting("fileServerclientvolumemounts").Result;

            DAPR_ANNOTATIONS = Core.GetConfigSetting("daprannotations").Result;

            PAYLOAD_APP_ANNOTATIONS = Core.GetConfigSetting("payloadappannotations").Result;
            PAYLOAD_APP_CONFIG = Core.GetConfigSetting("payloadappconfig").Result;
            PAYLOAD_APP_LABELS = Core.GetConfigSetting("payloadapplabels").Result;
            PAYLOAD_APP_ENVIRONMENTVARIABLES = Core.GetConfigSetting("payloadappenvironmentvariables").Result;

            if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development" || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "IntegrationTest") {
                ENABLE_YAML_DEBUG = true;
                PURGE_SCHEDULE_ON_BOOTUP = true;
            }
        }
    }
}