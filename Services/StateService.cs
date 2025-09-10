using System;
using System.Collections.Generic;
using ToNRoundCounter.Models;

namespace ToNRoundCounter.Services
{
    public class StateService
    {
        public string PlayerDisplayName { get; set; } = string.Empty;
        public RoundData CurrentRound { get; set; }
        public Dictionary<string, RoundAggregate> RoundAggregates { get; } = new Dictionary<string, RoundAggregate>();
        public Dictionary<string, Dictionary<string, TerrorAggregate>> TerrorAggregates { get; } = new Dictionary<string, Dictionary<string, TerrorAggregate>>();
        public Dictionary<string, string> RoundMapNames { get; } = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, string>> TerrorMapNames { get; } = new Dictionary<string, Dictionary<string, string>>();
        public List<Tuple<RoundData, string>> RoundLogHistory { get; } = new List<Tuple<RoundData, string>>();
        public int RoundCycle { get; set; } = 0;

        public void Reset()
        {
            CurrentRound = null;
            RoundAggregates.Clear();
            TerrorAggregates.Clear();
            RoundMapNames.Clear();
            TerrorMapNames.Clear();
            RoundLogHistory.Clear();
            RoundCycle = 0;
        }
    }
}
