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

        private static IEnumerable<string> SplitTerrorNames(string? terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey))
            {
                yield break;
            }

            foreach (var part in terrorKey!.Split(new[] { '&', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
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
