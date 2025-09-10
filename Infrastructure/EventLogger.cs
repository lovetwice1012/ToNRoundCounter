using Serilog;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    public class EventLogger : IEventLogger
    {
        private readonly ILogger _logger;

        public EventLogger()
        {
            _logger = Log.Logger;
        }

        public void LogEvent(string eventType, string message, LogEventLevel level = LogEventLevel.Information)
        {
            _logger.Write(level, "{EventType} - {Message}", eventType, message);
        }
    }
}
