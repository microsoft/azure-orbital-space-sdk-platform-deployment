namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {
    public class NotAScheduleFileException : Exception {
        public NotAScheduleFileException() {
        }

        public NotAScheduleFileException(string message)
            : base(message) {
        }

        public NotAScheduleFileException(string message, Exception inner)
            : base(message, inner) {
        }
    }
}
