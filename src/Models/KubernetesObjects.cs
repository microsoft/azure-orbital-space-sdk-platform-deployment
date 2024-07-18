using OpenTelemetry.Resources;

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
    }

}
