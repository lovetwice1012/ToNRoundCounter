using System.Collections.Generic;

namespace ToNRoundCounter.Domain
{
    public class AutoSuicidePreset
    {
        public List<string> RoundTypes { get; set; } = new List<string>();
        public List<string> DetailCustom { get; set; } = new List<string>();
        public bool Fuzzy { get; set; } = false;
    }
}
