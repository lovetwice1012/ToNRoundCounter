using System;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides persistence for round related data such as logs and statistics.
    /// </summary>
    public interface IRoundDataRepository
    {
        void AddRoundLog(Round round, string logEntry, DateTime recordedAt);

        void RecordRoundResult(string roundType, string? terrorType, bool survived, DateTime recordedAt);

        void UpsertStat(string name, object? value, DateTime recordedAt);
    }
}

