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
        private readonly Dictionary<string, RoundAggregate> _roundAggregates = new();
        private readonly TerrorAggregateCollection _terrorAggregates = new();
        private readonly Dictionary<string, string> _roundMapNames = new();
        private readonly TerrorMapNameCollection _terrorMapNames = new();
        private readonly List<Tuple<Round, string>> _roundLogHistory = new();
        private readonly Dictionary<string, object> _stats = new();
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
                _roundLogHistory.Add(new Tuple<Round, string>(round, logEntry));
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
                if (!_roundAggregates.TryGetValue(roundType, out var roundAgg))
                {
                    roundAgg = new RoundAggregate();
                    _roundAggregates[roundType] = roundAgg;
                }
                roundAgg.Total++;
                if (survived) roundAgg.Survival++; else roundAgg.Death++;

                if (!string.IsNullOrEmpty(terrorType))
                {
                    var terrorAgg = _terrorAggregates.Get(roundType, terrorType);
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
                _stats[name] = value;
            }
            StateChanged?.Invoke();
        }

        public void Reset()
        {
            lock (_sync)
            {
                CurrentRound = null;
                _roundAggregates.Clear();
                _terrorAggregates.Clear();
                _roundMapNames.Clear();
                _terrorMapNames.Clear();
                _roundLogHistory.Clear();
                RoundCycle = 0;
                _stats.Clear();
            }

            StateChanged?.Invoke();
        }

        public IReadOnlyDictionary<string, RoundAggregate> GetRoundAggregates()
        {
            lock (_sync)
            {
                return new Dictionary<string, RoundAggregate>(_roundAggregates);
            }
        }

        public bool TryGetTerrorAggregates(string round, out Dictionary<string, TerrorAggregate> terrorDict)
        {
            lock (_sync)
            {
                if (_terrorAggregates.TryGetRound(round, out var dict))
                {
                    terrorDict = new Dictionary<string, TerrorAggregate>(dict);
                    return true;
                }
                terrorDict = null;
                return false;
            }
        }

        public IReadOnlyDictionary<string, string> GetRoundMapNames()
        {
            lock (_sync)
            {
                return new Dictionary<string, string>(_roundMapNames);
            }
        }

        public void SetRoundMapName(string roundType, string mapName)
        {
            lock (_sync)
            {
                _roundMapNames[roundType] = mapName;
            }
            StateChanged?.Invoke();
        }

        public void SetTerrorMapName(string round, string terror, string mapName)
        {
            lock (_sync)
            {
                _terrorMapNames.Set(round, terror, mapName);
            }
            StateChanged?.Invoke();
        }

        public IReadOnlyList<Tuple<Round, string>> GetRoundLogHistory()
        {
            lock (_sync)
            {
                return _roundLogHistory.ToArray();
            }
        }

        public IReadOnlyDictionary<string, object> GetStats()
        {
            lock (_sync)
            {
                return new Dictionary<string, object>(_stats);
            }
        }
    }
}
