using System;

namespace ToNRoundCounter.Domain
{
    public class RoundBgmEntry
    {
        public bool Enabled { get; set; }

        public string RoundType { get; set; } = string.Empty;

        public string TerrorType { get; set; } = string.Empty;

        public string SoundPath { get; set; } = string.Empty;

        public RoundBgmEntry Clone()
        {
            return new RoundBgmEntry
            {
                Enabled = Enabled,
                RoundType = RoundType ?? string.Empty,
                TerrorType = TerrorType ?? string.Empty,
                SoundPath = SoundPath ?? string.Empty,
            };
        }
    }
}
