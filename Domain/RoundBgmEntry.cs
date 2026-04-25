using System;

namespace ToNRoundCounter.Domain
{
    public class RoundBgmEntry
    {
        public bool Enabled { get; set; }

        public string RoundType { get; set; } = string.Empty;

        public string TerrorType { get; set; } = string.Empty;

        public string SoundPath { get; set; } = string.Empty;

        /// <summary>
        /// Playback volume in the range [0.0, 1.0]. Defaults to 1.0 (100%).
        /// </summary>
        public double Volume { get; set; } = 1.0;

        public RoundBgmEntry Clone()
        {
            return new RoundBgmEntry
            {
                Enabled = Enabled,
                RoundType = RoundType ?? string.Empty,
                TerrorType = TerrorType ?? string.Empty,
                SoundPath = SoundPath ?? string.Empty,
                Volume = Volume,
            };
        }
    }
}
