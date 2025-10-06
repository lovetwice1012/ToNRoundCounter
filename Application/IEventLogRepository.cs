using System;
using Serilog.Events;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides persistence for application event logs.
    /// </summary>
    public interface IEventLogRepository
    {
        void WriteLog(string eventType, string message, LogEventLevel level, DateTime recordedAt);
    }
}

