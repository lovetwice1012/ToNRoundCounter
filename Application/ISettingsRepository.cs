using System;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides persistence for application settings snapshots.
    /// </summary>
    public interface ISettingsRepository
    {
        string? LoadLatest();

        void SaveSnapshot(string json, DateTime recordedAt);
    }
}

