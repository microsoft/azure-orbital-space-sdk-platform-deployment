
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public static partial class Models {
    public class KubernetesObjects {
        public class ResourceDefinition {
            public ResourceSection Resources { get; set; }

            public ResourceDefinition() {
                Resources = new ResourceSection();
            }
        }

        public class ResourceSection {
            public ResourceDetails Limits { get; set; }
            public ResourceDetails Requests { get; set; }

            public ResourceSection() {
                Limits = new ResourceDetails();
                Requests = new ResourceDetails();
            }
        }

        public class ResourceDetails {
            public string Cpu { get; set; }
            public string Memory { get; set; }

            public ResourceDetails() {
                Cpu = "";
                Memory = "";
            }
        }

        public class ConfigVolume {
            public string Name { get; set; }
            public ConfigMap ConfigMap { get; set; }
            public ConfigVolume() {
                ConfigMap = new ConfigMap();
                Name = "";
            }
        }

        public class ConfigMap {
            public string Name { get; set; }
            public ConfigMap() {
                Name = "";
            }
        }

        public class VolumeConfig {
            public string Name { get; set; }
            public string MountPath { get; set; }

            public VolumeConfig() {
                Name = "";
                MountPath = "";
            }
        }


    }

}
