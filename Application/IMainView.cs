using System.Collections.Generic;

namespace ToNRoundCounter.Application
{
    public interface IMainView
    {
        void UpdateRoundLog(IEnumerable<string> logEntries);
        void AppendRoundLogEntry(string logEntry);
    }
}

