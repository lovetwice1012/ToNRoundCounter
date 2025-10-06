using System;
using Serilog;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    public class EventLogger : IEventLogger
    {
        private readonly ILogger _logger;
        private readonly IEventLogRepository? _repository;

        public EventLogger(IEventLogRepository? repository = null)
        {
            _logger = Log.Logger;
            _repository = repository;
        }

        public void LogEvent(string eventType, string message, LogEventLevel level = LogEventLevel.Information)
        {
            _logger.Write(level, "{EventType} - {Message}", eventType, message);

            try
            {
                _repository?.WriteLog(eventType, message, level, DateTime.UtcNow);
            }
            catch
            {
                // Swallow persistence errors to avoid disrupting the primary logging pipeline.
            }
        }
    }
}
