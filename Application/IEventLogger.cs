using System;
using Serilog.Events;

namespace ToNRoundCounter.Application
{

    public interface IEventLogger
    {
        void LogEvent(string eventType, string message, LogEventLevel level = LogEventLevel.Information);

        void LogEvent(string eventType, Func<string> messageFactory, LogEventLevel level = LogEventLevel.Information);

        bool IsEnabled(LogEventLevel level);
    }
}
