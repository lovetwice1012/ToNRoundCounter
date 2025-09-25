using System;

namespace ToNRoundCounter.Domain
{
    public class AutoLaunchEntry
    {
        public bool Enabled { get; set; }

        public string ExecutablePath { get; set; } = string.Empty;

        public string Arguments { get; set; } = string.Empty;

        public AutoLaunchEntry Clone()
        {
            return new AutoLaunchEntry
            {
                Enabled = Enabled,
                ExecutablePath = ExecutablePath ?? string.Empty,
                Arguments = Arguments ?? string.Empty
            };
        }

        public bool HasExecutablePath()
        {
            return !string.IsNullOrWhiteSpace(ExecutablePath);
        }
    }
}
