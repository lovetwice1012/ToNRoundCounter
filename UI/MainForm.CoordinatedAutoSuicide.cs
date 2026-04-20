using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private CoordinatedAutoSuicideStateInfo currentCoordinatedAutoSuicideState = new CoordinatedAutoSuicideStateInfo();
        private DateTime lastCoordinatedAutoSuicideStateFetch = DateTime.MinValue;
        private const double CoordinatedAutoSuicideStateFetchIntervalSeconds = 5.0;
        private DateTime currentRoundStartedAtUtc = DateTime.MinValue;
        private DateTime currentTerrorDetectedAtUtc = DateTime.MinValue;
        private string lastAppliedCoordinatedSkipSignature = string.Empty;
        private string lastAppliedNoWishSkipSignature = string.Empty;

        private async Task RefreshCoordinatedAutoSuicideStateAsync(bool force = false)
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected || string.IsNullOrWhiteSpace(currentInstanceId))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (!force && (now - lastCoordinatedAutoSuicideStateFetch).TotalSeconds < CoordinatedAutoSuicideStateFetchIntervalSeconds)
            {
                return;
            }

            try
            {
                var state = await _cloudClient.GetCoordinatedAutoSuicideStateAsync(currentInstanceId, _cancellation.Token).ConfigureAwait(false);
                ApplyCoordinatedAutoSuicideState(state);
                lastCoordinatedAutoSuicideStateFetch = now;
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CoordinatedAutoSuicide", $"Failed to refresh coordinated auto suicide state: {ex.Message}", LogEventLevel.Debug);
            }
        }

        private void ApplyCoordinatedAutoSuicideState(CoordinatedAutoSuicideStateInfo? state)
        {
            currentCoordinatedAutoSuicideState = state ?? new CoordinatedAutoSuicideStateInfo();
        }

        private void HandleCoordinatedAutoSuicideUpdatedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null)
                {
                    return;
                }

                JsonElement dataElement;
                if (message.Data is JsonElement jsonElement)
                {
                    dataElement = jsonElement;
                }
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                var instanceId = dataElement.TryGetProperty("instance_id", out var iidEl) ? iidEl.GetString() : currentInstanceId;
                if (!string.Equals(instanceId, currentInstanceId, StringComparison.Ordinal))
                {
                    return;
                }

                var stateElement = dataElement.TryGetProperty("state", out var stateEl) ? stateEl : dataElement;
                var state = JsonSerializer.Deserialize<CoordinatedAutoSuicideStateInfo>(stateElement.GetRawText())
                    ?? new CoordinatedAutoSuicideStateInfo();
                ApplyCoordinatedAutoSuicideState(state);
                lastCoordinatedAutoSuicideStateFetch = DateTime.UtcNow;

                var currentRound = stateService.CurrentRound;
                if (currentRound != null)
                {
                    if (TryScheduleSharedCoordinatedSkip(currentRound.RoundType ?? string.Empty, currentRound.TerrorKey))
                    {
                        return;
                    }

                    TryApplyNoSurvivalWishSkipMode(currentRound.RoundType ?? string.Empty, currentRound.TerrorKey);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle coordinated.autoSuicide.updated: {ex.Message}", LogEventLevel.Debug);
            }
        }

        private bool IsCoordinatedAutoSuicideEnabledForClient()
        {
            return _settings.CoordinatedAutoSuicideBrainEnabled;
        }

        private bool TryScheduleSharedCoordinatedSkip(string roundType, string? terrorKey)
        {
            if (!IsCoordinatedAutoSuicideEnabledForClient())
            {
                return false;
            }

            if (!HasMatchingSharedSkipEntry(roundType, terrorKey))
            {
                return false;
            }

            var signature = BuildCoordinatedSignature(roundType, terrorKey);
            if (string.Equals(signature, lastAppliedCoordinatedSkipSignature, StringComparison.Ordinal))
            {
                return true;
            }

            lastAppliedCoordinatedSkipSignature = signature;
            CancelAutoSuicide();
            ScheduleAutoSuicide(CalculateRemainingFrom(currentRoundStartedAtUtc, TimeSpan.FromSeconds(13)), false);
            _logger?.LogEvent("CoordinatedAutoSuicide", $"Scheduled shared coordinated skip for {signature}");
            return true;
        }

        private bool TryApplyNoSurvivalWishSkipMode(string roundType, string? terrorKey)
        {
            if (!IsCoordinatedAutoSuicideEnabledForClient())
            {
                return false;
            }

            if (!currentCoordinatedAutoSuicideState.skip_all_without_survival_wish || string.IsNullOrWhiteSpace(terrorKey))
            {
                return false;
            }

            if (currentDesirePlayers.Count > 0)
            {
                return false;
            }

            var signature = BuildCoordinatedSignature(roundType, terrorKey);
            if (string.Equals(signature, lastAppliedNoWishSkipSignature, StringComparison.Ordinal))
            {
                return true;
            }

            lastAppliedNoWishSkipSignature = signature;
            CancelAutoSuicide();
            ScheduleAutoSuicide(CalculateRemainingFrom(currentTerrorDetectedAtUtc, TimeSpan.FromSeconds(1)), false);
            _logger?.LogEvent("CoordinatedAutoSuicide", $"Scheduled no-wish skip mode for {signature}");
            return true;
        }

        private bool HasMatchingSharedSkipEntry(string roundType, string? terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey) || currentCoordinatedAutoSuicideState.entries == null)
            {
                return false;
            }

            return currentCoordinatedAutoSuicideState.entries.Any(entry =>
                EntryMatchesRound(entry.round_key, roundType) && EntryMatchesTerror(entry.terror_name, terrorKey));
        }

        private static bool EntryMatchesRound(string? entryRoundKey, string? roundType)
        {
            var normalizedEntry = NormalizeCoordinatedValue(entryRoundKey);
            if (string.IsNullOrEmpty(normalizedEntry))
            {
                return true;
            }

            return string.Equals(normalizedEntry, NormalizeCoordinatedValue(roundType), StringComparison.Ordinal);
        }

        private static bool EntryMatchesTerror(string? entryTerrorName, string? terrorKey)
        {
            var normalizedEntry = NormalizeCoordinatedValue(entryTerrorName);
            var normalizedTerror = NormalizeCoordinatedValue(terrorKey);
            if (string.IsNullOrEmpty(normalizedEntry) || string.IsNullOrEmpty(normalizedTerror))
            {
                return false;
            }

            if (string.Equals(normalizedEntry, normalizedTerror, StringComparison.Ordinal))
            {
                return true;
            }

            var terrorSegments = (terrorKey ?? string.Empty)
                .Split(new[] { '&', '＆', '/', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeCoordinatedValue)
                .Where(value => !string.IsNullOrEmpty(value));
            return terrorSegments.Any(segment => string.Equals(segment, normalizedEntry, StringComparison.Ordinal));
        }

        private static string NormalizeCoordinatedValue(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static TimeSpan CalculateRemainingFrom(DateTime startedAtUtc, TimeSpan totalDuration)
        {
            if (startedAtUtc == DateTime.MinValue)
            {
                return totalDuration;
            }

            var remaining = totalDuration - (DateTime.UtcNow - startedAtUtc);
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        private static string BuildCoordinatedSignature(string roundType, string? terrorKey)
        {
            return $"{NormalizeCoordinatedValue(roundType)}|{NormalizeCoordinatedValue(terrorKey)}";
        }

        private void ResetCoordinatedAutoSuicideRoundState()
        {
            currentRoundStartedAtUtc = DateTime.UtcNow;
            currentTerrorDetectedAtUtc = DateTime.MinValue;
            lastAppliedCoordinatedSkipSignature = string.Empty;
            lastAppliedNoWishSkipSignature = string.Empty;
        }
    }
}
