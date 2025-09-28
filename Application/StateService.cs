using System;
using System.Collections.Generic;
using Serilog.Events;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    public class StateService
    {
        public event Action? StateChanged;

        private readonly object _sync = new object();
        private readonly IEventLogger? _logger;

        public string PlayerDisplayName { get; set; } = string.Empty;
        public Round? CurrentRound { get; private set; }
        private readonly Dictionary<string, RoundAggregate> _roundAggregates = new();
        private readonly TerrorAggregateCollection _terrorAggregates = new();
        private readonly Dictionary<string, string?> _roundMapNames = new();
        private readonly TerrorMapNameCollection _terrorMapNames = new();
        private readonly List<Tuple<Round, string>> _roundLogHistory = new();
        private readonly Dictionary<string, object> _stats = new();
        public int RoundCycle { get; private set; } = 0;

        public StateService(IEventLogger? logger = null)
        {
            _logger = logger;
            _logger?.LogEvent("StateService", "State service instantiated.", LogEventLevel.Debug);
        }

        private void NotifyStateChanged(string reason)
        {
            var handlers = StateChanged;
            if (handlers == null)
            {
                _logger?.LogEvent("StateService", $"State change '{reason}' occurred but no subscribers were registered.", LogEventLevel.Debug);
                return;
            }

            var subscriberCount = handlers.GetInvocationList().Length;
            _logger?.LogEvent("StateService", $"Notifying {subscriberCount} subscriber(s) of state change '{reason}'.", LogEventLevel.Debug);
            handlers.Invoke();
            _logger?.LogEvent("StateService", $"State change '{reason}' notifications completed.", LogEventLevel.Debug);
        }

        private static string DescribeRound(Round? round)
        {
            if (round == null)
            {
                return "<null>";
            }

            return $"{round.RoundType ?? "<unknown>"} (Terror: {round.TerrorKey ?? "<none>"}, Map: {round.MapName ?? "<none>"})";
        }

        public void UpdateCurrentRound(Round? round)
        {
            _logger?.LogEvent("StateService", $"Updating current round to {DescribeRound(round)}.");
            lock (_sync)
            {
                CurrentRound = round;
            }
            NotifyStateChanged(nameof(UpdateCurrentRound));
        }

        public void AddRoundLog(Round round, string logEntry)
        {
            _logger?.LogEvent("StateService", $"Appending round log entry for {DescribeRound(round)}: {logEntry}");
            lock (_sync)
            {
                _roundLogHistory.Add(new Tuple<Round, string>(round, logEntry));
            }
            NotifyStateChanged(nameof(AddRoundLog));
        }

        public void IncrementRoundCycle()
        {
            int updatedValue;
            lock (_sync)
            {
                RoundCycle++;
                updatedValue = RoundCycle;
            }
            _logger?.LogEvent("StateService", $"Round cycle incremented to {updatedValue}.");
            NotifyStateChanged(nameof(IncrementRoundCycle));
        }

        public void SetRoundCycle(int value)
        {
            _logger?.LogEvent("StateService", $"Setting round cycle to {value}.");
            lock (_sync)
            {
                RoundCycle = value;
            }
            NotifyStateChanged(nameof(SetRoundCycle));
        }

        public void RecordRoundResult(string roundType, string? terrorType, bool survived)
        {
            _logger?.LogEvent("StateService", $"Recording round result. Round: {roundType}, Terror: {terrorType ?? "<none>"}, Survived: {survived}");
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
                    var safeTerrorType = terrorType!;
                    var terrorAgg = _terrorAggregates.Get(roundType, safeTerrorType);
                    terrorAgg.Total++;
                    if (survived) terrorAgg.Survival++; else terrorAgg.Death++;
                }
            }

            NotifyStateChanged(nameof(RecordRoundResult));
        }

        public void UpdateStat(string name, object value)
        {
            _logger?.LogEvent("StateService", $"Updating stat '{name}' to value '{value}'.", LogEventLevel.Debug);
            lock (_sync)
            {
                _stats[name] = value;
            }
            NotifyStateChanged(nameof(UpdateStat));
        }

        public void Reset()
        {
            _logger?.LogEvent("StateService", "Resetting state service to defaults.");
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

            NotifyStateChanged(nameof(Reset));
        }

        public IReadOnlyDictionary<string, RoundAggregate> GetRoundAggregates()
        {
            Dictionary<string, RoundAggregate> snapshot;
            lock (_sync)
            {
                snapshot = new Dictionary<string, RoundAggregate>(_roundAggregates);
            }
            _logger?.LogEvent("StateService", $"Providing round aggregates snapshot with {snapshot.Count} entries.", LogEventLevel.Debug);
            return snapshot;
        }

        public bool TryGetTerrorAggregates(string round, out Dictionary<string, TerrorAggregate>? terrorDict)
        {
            _logger?.LogEvent("StateService", $"Retrieving terror aggregates for round '{round}'.", LogEventLevel.Debug);
            lock (_sync)
            {
                if (_terrorAggregates.TryGetRound(round, out var dict))
                {
                    terrorDict = new Dictionary<string, TerrorAggregate>(dict);
                    _logger?.LogEvent("StateService", $"Terror aggregates retrieval succeeded for round '{round}' with {terrorDict.Count} entries.", LogEventLevel.Debug);
                    return true;
                }
                terrorDict = null;
                _logger?.LogEvent("StateService", $"Terror aggregates retrieval failed for round '{round}'.", LogEventLevel.Debug);
                return false;
            }
        }

        public IReadOnlyDictionary<string, string?> GetRoundMapNames()
        {
            Dictionary<string, string?> snapshot;
            lock (_sync)
            {
                snapshot = new Dictionary<string, string?>(_roundMapNames);
            }
            _logger?.LogEvent("StateService", $"Providing round map names snapshot with {snapshot.Count} entries.", LogEventLevel.Debug);
            return snapshot;
        }

        public string? GetRoundMapName(string roundType)
        {
            if (string.IsNullOrWhiteSpace(roundType))
            {
                _logger?.LogEvent("StateService", $"Cannot resolve round map name for empty round type '{roundType}'.", LogEventLevel.Debug);
                return null;
            }

            lock (_sync)
            {
                if (_roundMapNames.TryGetValue(roundType, out var storedName) && !string.IsNullOrWhiteSpace(storedName))
                {
                    _logger?.LogEvent("StateService", $"Resolved round map name '{storedName}' for round '{roundType}'.", LogEventLevel.Debug);
                    return storedName;
                }
            }

            _logger?.LogEvent("StateService", $"No stored map name found for round '{roundType}'.", LogEventLevel.Debug);
            return null;
        }

        public void SetRoundMapName(string roundType, string? mapName)
        {
            if (string.IsNullOrWhiteSpace(roundType) || string.IsNullOrWhiteSpace(mapName))
            {
                _logger?.LogEvent("StateService", $"Ignoring empty round or map name assignment. Round: '{roundType}', Map: '{mapName}'.", LogEventLevel.Debug);
                return;
            }

            _logger?.LogEvent("StateService", $"Associating map '{mapName}' with round '{roundType}'.");
            lock (_sync)
            {
                _roundMapNames[roundType] = mapName;
            }
            NotifyStateChanged(nameof(SetRoundMapName));
        }

        public void SetTerrorMapName(string round, string terror, string? mapName)
        {
            if (string.IsNullOrWhiteSpace(round) || string.IsNullOrWhiteSpace(terror) || string.IsNullOrWhiteSpace(mapName))
            {
                _logger?.LogEvent("StateService", $"Ignoring empty terror map assignment. Round: '{round}', Terror: '{terror}', Map: '{mapName}'.", LogEventLevel.Debug);
                return;
            }

            _logger?.LogEvent("StateService", $"Associating terror map '{mapName}' with round '{round}' / terror '{terror}'.");
            lock (_sync)
            {
                _terrorMapNames.Set(round, terror, mapName);
            }
            NotifyStateChanged(nameof(SetTerrorMapName));
        }

        public string? GetTerrorMapName(string round, string terror)
        {
            if (string.IsNullOrWhiteSpace(round) || string.IsNullOrWhiteSpace(terror))
            {
                _logger?.LogEvent("StateService", $"Cannot retrieve terror map name with empty identifiers. Round: '{round}', Terror: '{terror}'.", LogEventLevel.Debug);
                return null;
            }

            lock (_sync)
            {
                if (_terrorMapNames.TryGetRound(round, out var terrorDict) &&
                    terrorDict != null &&
                    terrorDict.TryGetValue(terror, out var storedName) &&
                    !string.IsNullOrWhiteSpace(storedName))
                {
                    _logger?.LogEvent("StateService", $"Resolved terror map name '{storedName}' for round '{round}' and terror '{terror}'.", LogEventLevel.Debug);
                    return storedName;
                }
            }

            _logger?.LogEvent("StateService", $"No terror map name found for round '{round}' and terror '{terror}'.", LogEventLevel.Debug);
            return null;
        }

        public IReadOnlyList<Tuple<Round, string>> GetRoundLogHistory()
        {
            Tuple<Round, string>[] snapshot;
            lock (_sync)
            {
                snapshot = _roundLogHistory.ToArray();
            }
            _logger?.LogEvent("StateService", $"Providing round log history snapshot with {snapshot.Length} entries.", LogEventLevel.Debug);
            return snapshot;
        }

        public IReadOnlyDictionary<string, object> GetStats()
        {
            Dictionary<string, object> snapshot;
            lock (_sync)
            {
                snapshot = new Dictionary<string, object>(_stats);
            }
            _logger?.LogEvent("StateService", $"Providing statistics snapshot with {snapshot.Count} entries.", LogEventLevel.Debug);
            return snapshot;
        }
    }
}
