namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;
public partial class Utils {

    /// <summary>
    /// Downlinks a deployment response
    /// </summary>
    public class TimeUtils {

        private readonly ILogger<TimeUtils> _logger;

        public TimeUtils(ILogger<TimeUtils> logger) {
            _logger = logger;

        }

        /// <summary>
        /// Rounds down a DateTime value to the nearest multiple of a specified TimeSpan.
        /// </summary>
        /// <param name="dt">The DateTime value to round down.</param>
        /// <param name="d">The TimeSpan representing the interval to round down to.</param>
        /// <returns>The rounded down DateTime value.</returns>
        internal DateTime RoundDown(DateTime dt, TimeSpan d) {
            var delta = dt.Ticks % d.Ticks;
            return new DateTime(dt.Ticks - delta, dt.Kind).ToUniversalTime();
        }

        /// <summary>
        /// Rounds up a DateTime value to the nearest TimeSpan interval.
        /// </summary>
        /// <param name="dt">The DateTime value to round up.</param>
        /// <param name="d">The TimeSpan interval to round up to.</param>
        /// <returns>The rounded up DateTime value.</returns>
        internal DateTime RoundUp(DateTime dt, TimeSpan d) {
            var modTicks = dt.Ticks % d.Ticks;
            var delta = modTicks != 0 ? d.Ticks - modTicks : 0;
            return new DateTime(dt.Ticks + delta, dt.Kind).ToUniversalTime();
        }

        /// <summary>
        /// Calculates the cache name from the given timestamp by rounding down to the nearest minute for the given time
        /// </summary>
        /// <param name="timeStamp">The timestamp to calculate the cache name from.</param>
        /// <returns>The calculated cache name.</returns>
        internal string CalculateCacheNameFromTimeStamp(DateTime timeStamp) {
            DateTime roundedTimeStamp = RoundDown(timeStamp, TimeSpan.FromMinutes(1));
            return string.Format($"scheduleQueue_{CalculateTimeSuffixFromTimeStamp(roundedTimeStamp.ToUniversalTime())}");
        }

        /// <summary>
        /// Calculates the time suffix from a given timestamp by rounding down to the nearest minute for the given time
        /// </summary>
        /// <param name="timeStamp">The timestamp to calculate the suffix from.</param>
        /// <returns>The calculated time suffix.</returns>
        internal string CalculateTimeSuffixFromTimeStamp(DateTime timeStamp) {
            DateTime roundedTimeStamp = RoundDown(timeStamp, TimeSpan.FromMinutes(1));
            return string.Format($"{roundedTimeStamp.ToUniversalTime():yyyyMMdd-HHmmss}");
        }


    }
}
