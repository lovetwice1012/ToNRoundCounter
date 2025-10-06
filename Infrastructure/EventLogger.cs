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

        public bool IsEnabled(LogEventLevel level)
        {
            return _logger.IsEnabled(level);
        }

        public void LogEvent(string eventType, string message, LogEventLevel level = LogEventLevel.Information)
        {
            LogEvent(eventType, () => message, level);
        }

        public void LogEvent(string eventType, Func<string> messageFactory, LogEventLevel level = LogEventLevel.Information)
        {
            if (messageFactory == null)
            {
                throw new ArgumentNullException(nameof(messageFactory));
            }

            string? message = null;
            if (_logger.IsEnabled(level))
            {
                message = messageFactory();
                _logger.Write(level, "{EventType} - {Message}", eventType, message);
            }
            else if (_repository != null)
            {
                message = messageFactory();
            }

            try
            {
                if (_repository != null)
                {
                    message ??= messageFactory();
                    _repository.WriteLog(eventType, message, level, DateTime.UtcNow);
                }
            }
            catch
            {
                // Swallow persistence errors to avoid disrupting the primary logging pipeline.
            }
        }
    }
}
