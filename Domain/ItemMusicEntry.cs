using System;

namespace ToNRoundCounter.Domain
{
    public class ItemMusicEntry
    {
        public bool Enabled { get; set; }

        public string ItemName { get; set; } = string.Empty;

        public string SoundPath { get; set; } = string.Empty;

        public double MinSpeed { get; set; }

        public double MaxSpeed { get; set; }

        /// <summary>
        /// Playback volume in the range [0.0, 1.0]. Defaults to 1.0 (100%).
        /// </summary>
        public double Volume { get; set; } = 1.0;

        public ItemMusicEntry Clone()
        {
            return new ItemMusicEntry
            {
                Enabled = Enabled,
                ItemName = ItemName ?? string.Empty,
                SoundPath = SoundPath ?? string.Empty,
                MinSpeed = MinSpeed,
                MaxSpeed = MaxSpeed,
                Volume = Volume
            };
        }
    }
}
