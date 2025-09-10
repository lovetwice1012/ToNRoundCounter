namespace ToNRoundCounter.Application
{
    using Serilog.Events;

    public interface IEventLogger
    {
        void LogEvent(string eventType, string message, LogEventLevel level = LogEventLevel.Information);
    }
}
