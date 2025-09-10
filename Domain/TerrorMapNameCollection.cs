using System.Collections.Generic;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Store for map names per round and terror.
    /// </summary>
    public class TerrorMapNameCollection
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data = new();

        public string Get(string round, string terror)
        {
            if (!_data.TryGetValue(round, out var terrorDict))
            {
                terrorDict = new Dictionary<string, string>();
                _data[round] = terrorDict;
            }
            terrorDict.TryGetValue(terror, out var name);
            return name;
        }

        public void Set(string round, string terror, string name)
        {
            if (!_data.TryGetValue(round, out var terrorDict))
            {
                terrorDict = new Dictionary<string, string>();
                _data[round] = terrorDict;
            }
            terrorDict[terror] = name;
        }

        public bool TryGetRound(string round, out Dictionary<string, string> terrorDict)
            => _data.TryGetValue(round, out terrorDict);

        public void Clear() => _data.Clear();
    }
}
