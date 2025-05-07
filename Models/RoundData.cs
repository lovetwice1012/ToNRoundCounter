using System.Collections.Generic;

namespace ToNRoundCounter.Models
{
    public class RoundData
    {
        public string RoundType { get; set; }
        public bool IsDeath { get; set; }
        public string TerrorKey { get; set; }
        public string MapName { get; set; }
        public List<string> ItemNames { get; set; }
        public int Damage { get; set; }

        public RoundData()
        {
            ItemNames = new List<string>();
        }
    }
}
