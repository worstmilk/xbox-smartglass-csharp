

using Microsoft.Extensions.Logging;

namespace SmartGlass.Common
{
    public class Logging
    {
        public static ILoggerFactory Factory { get; private set; } =
            new LoggerFactory().AddDebug(LogLevel.Trace);
    }
}