using System.Collections.Generic;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Strongly typed store for terror aggregates keyed by round and terror.
    /// </summary>
    public class TerrorAggregateCollection
    {
        private readonly Dictionary<string, Dictionary<string, TerrorAggregate>> _data = new();

        public TerrorAggregate Get(string round, string terror)
        {
            if (!_data.TryGetValue(round, out var terrorDict))
            {
                terrorDict = new Dictionary<string, TerrorAggregate>();
                _data[round] = terrorDict;
            }
            if (!terrorDict.TryGetValue(terror, out var agg))
            {
                agg = new TerrorAggregate();
                terrorDict[terror] = agg;
            }
            return agg;
        }

        public bool TryGetRound(string round, out Dictionary<string, TerrorAggregate> terrorDict)
            => _data.TryGetValue(round, out terrorDict);

        public void Clear() => _data.Clear();
    }
}
