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
        public string? RoundType { get; set; }
        public bool IsDeath { get; set; }
        public string? TerrorKey { get; set; }
        public string? MapName { get; set; }
        public List<string> ItemNames { get; set; }
        public int Damage { get; set; }
        public int PageCount { get; set; }
        public int InstancePlayersCount { get; internal set; }
        public int? RoundColor { get; set; }

        /// <summary>
        /// Creates a deep copy of the round so that snapshots can be stored safely.
        /// </summary>
        public Round Clone()
        {
            var clone = new Round(Id)
            {
                RoundType = RoundType,
                IsDeath = IsDeath,
                TerrorKey = TerrorKey,
                MapName = MapName,
                Damage = Damage,
                PageCount = PageCount,
                InstancePlayersCount = InstancePlayersCount,
                RoundColor = RoundColor
            };

            if (ItemNames != null && ItemNames.Count > 0)
            {
                clone.ItemNames.AddRange(ItemNames);
            }

            return clone;
        }
    }
}

