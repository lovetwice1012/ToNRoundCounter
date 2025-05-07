using System;
using System.IO;

namespace ToNRoundCounter.Utils
{
    public static class EventLogger
    {
        private static readonly object lockObj = new object();
        public static void LogEvent(string eventType, string message)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string time = DateTime.Now.ToString("HH:mm:ss");
            string eventTypeLabel = LanguageManager.Translate("EventType:");
            string logLine = string.Format("[{0}] {1} {2} - {3}{4}", time, eventTypeLabel, eventType, message, Environment.NewLine);
            string logFilePrefix = LanguageManager.Translate("Log_");
            string fileName = logFilePrefix + date + ".txt";
            lock (lockObj)
            {
                File.AppendAllText(fileName, logLine);
            }
        }
    }
}
