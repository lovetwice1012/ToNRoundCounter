#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ToNRoundCounter.Application
{
    public sealed partial class AutoRecordingService
    {
        // Cache normalized trigger lists keyed by source list reference to avoid re-allocating per StateChanged.
        private List<string>? _cachedRoundTriggers;
        private object? _cachedRoundTriggersSource;
        private List<string>? _cachedTerrorTriggers;
        private object? _cachedTerrorTriggersSource;

        private List<string> GetCachedRoundTriggers()
        {
            var source = _settings.AutoRecordingRoundTypes;
            if (!ReferenceEquals(source, _cachedRoundTriggersSource) || _cachedRoundTriggers == null)
            {
                _cachedRoundTriggers = NormalizeTriggers(source);
                _cachedRoundTriggersSource = source;
            }
            return _cachedRoundTriggers;
        }

        private List<string> GetCachedTerrorTriggers()
        {
            var source = _settings.AutoRecordingTerrors;
            if (!ReferenceEquals(source, _cachedTerrorTriggersSource) || _cachedTerrorTriggers == null)
            {
                _cachedTerrorTriggers = NormalizeTriggers(source);
                _cachedTerrorTriggersSource = source;
            }
            return _cachedTerrorTriggers;
        }

        private static List<string> NormalizeTriggers(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Select(v => (v ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        // Cached last split result. EvaluateRecordingState may be called per OSC tick and almost
        // always with the same terrorKey, so we memoize the last split.
        private static string? _cachedTerrorSplitKey;
        private static List<string>? _cachedTerrorSplitValues;
        private static readonly object _cachedTerrorSplitSync = new object();
        private static readonly char[] TerrorSplitSeparators = new[] { '&', ',', ';' };

        private static IEnumerable<string> SplitTerrorNames(string? terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey))
            {
                return Array.Empty<string>();
            }

            lock (_cachedTerrorSplitSync)
            {
                if (string.Equals(_cachedTerrorSplitKey, terrorKey, StringComparison.Ordinal) && _cachedTerrorSplitValues != null)
                {
                    return _cachedTerrorSplitValues;
                }

                var split = terrorKey!.Split(TerrorSplitSeparators, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>(split.Length);
                for (int i = 0; i < split.Length; i++)
                {
                    var value = split[i].Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        list.Add(value);
                    }
                }

                _cachedTerrorSplitKey = terrorKey;
                _cachedTerrorSplitValues = list;
                return list;
            }
        }

        private static bool TriggerMatches(string value, IReadOnlyCollection<string> triggers)
        {
            if (triggers.Count == 0)
            {
                return false;
            }

            foreach (var trigger in triggers)
            {
                if (string.Equals(trigger, "*", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(trigger, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildTriggerDescription(string? roundType, bool roundTriggered, string? terrorKey, bool terrorTriggered)
        {
            var builder = new StringBuilder();
            if (roundTriggered && !string.IsNullOrWhiteSpace(roundType))
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "Round='{0}'", roundType);
            }

            if (terrorTriggered && !string.IsNullOrWhiteSpace(terrorKey))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.AppendFormat(CultureInfo.InvariantCulture, "Terror='{0}'", terrorKey);
            }

            if (builder.Length == 0)
            {
                builder.Append("Manual");
            }

            return builder.ToString();
        }
    }
}
