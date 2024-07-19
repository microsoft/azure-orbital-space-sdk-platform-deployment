
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

        public class VolumeRoot {
            public List<V1Volume> Volumes { get; set; }
            public VolumeRoot() {
                Volumes = new List<V1Volume>();
            }

            public class ConfigMapVolumeSource {
                public string Name { get; set; }
                public ConfigMapVolumeSource() {
                    Name = "";
                }
            }

            public class SecretVolumeSource {
                public string SecretName { get; set; }
                public SecretVolumeSource() {
                    SecretName = "";
                }
            }

            public class PersistentVolumeClaimVolumeSource {
                public string ClaimName { get; set; }
                public PersistentVolumeClaimVolumeSource() {
                    ClaimName = "";
                }
            }
        }
    }

}
