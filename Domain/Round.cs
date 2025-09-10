using System;
using System.Collections.Generic;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Entity representing a round and its details.
    /// </summary>
    public class Round
    {
        public Round(RoundId id)
        {
            Id = id;
            ItemNames = new List<string>();
        }

        public Round() : this(new RoundId(Guid.NewGuid())) { }

        public RoundId Id { get; }
        public string RoundType { get; set; }
        public bool IsDeath { get; set; }
        public string TerrorKey { get; set; }
        public string MapName { get; set; }
        public List<string> ItemNames { get; set; }
        public int Damage { get; set; }
        public int InstancePlayersCount { get; internal set; }
    }
}

