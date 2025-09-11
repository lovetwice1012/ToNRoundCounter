using System;
using System.Collections.Generic;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    public class StateService
    {
        public event Action StateChanged;

        private readonly object _sync = new object();

        public string PlayerDisplayName { get; set; } = string.Empty;
        public Round CurrentRound { get; private set; }
        public Dictionary<string, RoundAggregate> RoundAggregates { get; } = new Dictionary<string, RoundAggregate>();
        public TerrorAggregateCollection TerrorAggregates { get; } = new TerrorAggregateCollection();
        public Dictionary<string, string> RoundMapNames { get; } = new Dictionary<string, string>();
        public TerrorMapNameCollection TerrorMapNames { get; } = new TerrorMapNameCollection();
        public List<Tuple<Round, string>> RoundLogHistory { get; } = new List<Tuple<Round, string>>();
        public Dictionary<string, object> Stats { get; } = new Dictionary<string, object>();
        public int RoundCycle { get; private set; } = 0;

        public void UpdateCurrentRound(Round round)
        {
            lock (_sync)
            {
                CurrentRound = round;
            }
            StateChanged?.Invoke();
        }

        public void AddRoundLog(Round round, string logEntry)
        {
            lock (_sync)
            {
                RoundLogHistory.Add(new Tuple<Round, string>(round, logEntry));
            }
            StateChanged?.Invoke();
        }

        public void IncrementRoundCycle()
        {
            lock (_sync)
            {
                RoundCycle++;
            }
            StateChanged?.Invoke();
        }

        public void SetRoundCycle(int value)
        {
            lock (_sync)
            {
                RoundCycle = value;
            }
            StateChanged?.Invoke();
        }

        public void RecordRoundResult(string roundType, string terrorType, bool survived)
        {
            lock (_sync)
            {
                if (!RoundAggregates.TryGetValue(roundType, out var roundAgg))
                {
                    roundAgg = new RoundAggregate();
                    RoundAggregates[roundType] = roundAgg;
                }
                roundAgg.Total++;
                if (survived) roundAgg.Survival++; else roundAgg.Death++;

                if (!string.IsNullOrEmpty(terrorType))
                {
                    var terrorAgg = TerrorAggregates.Get(roundType, terrorType);
                    terrorAgg.Total++;
                    if (survived) terrorAgg.Survival++; else terrorAgg.Death++;
                }
            }

            StateChanged?.Invoke();
        }

        public void UpdateStat(string name, object value)
        {
            lock (_sync)
            {
                Stats[name] = value;
            }
            StateChanged?.Invoke();
        }

        public void Reset()
        {
            lock (_sync)
            {
                CurrentRound = null;
                RoundAggregates.Clear();
                TerrorAggregates.Clear();
                RoundMapNames.Clear();
                TerrorMapNames.Clear();
                RoundLogHistory.Clear();
                RoundCycle = 0;
                Stats.Clear();
            }

            StateChanged?.Invoke();
        }
    }
}
