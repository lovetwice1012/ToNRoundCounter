using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;
using Serilog.Events;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private async void InstanceMemberUpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Prevent concurrent execution
            lock (instanceTimerSync)
            {
                if (isInstanceMemberUpdateRunning)
                {
                    return; // Previous update still running, skip this tick
                }
                isInstanceMemberUpdateRunning = true;
            }

            try
            {
                if (!_settings.CloudSyncEnabled || _cloudClient == null)
                {
                    return;
                }

                // Don't clear overlay just because connection is temporarily down
                if (!_cloudClient.IsConnected)
                {
                    // Just skip this update, keep showing last known state
                    return;
                }

                if (string.IsNullOrEmpty(currentInstanceId))
                {
                    // Clear overlay if no instance
                    if (currentInstanceMembers.Count > 0)
                    {
                        currentInstanceMembers.Clear();
                        currentDesirePlayers.Clear();
                        UpdateInstanceMembersOverlay();
                    }
                    return;
                }

                try
                {
                    // Note: UpdateCloudPlayerState is handled by the velocity timer via SendCloudUpdatesAsync

                    // Throttle the heavy fetch (player states + desire check) - they don't need to run at 5Hz
                    var now = DateTime.Now;
                    if ((now - lastInstanceMembersFetch).TotalSeconds < InstanceMembersFetchIntervalSeconds)
                    {
                        return;
                    }
                    lastInstanceMembersFetch = now;

                    await RefreshCoordinatedAutoSuicideStateAsync();

                    // Get player states from cloud
                    var playerStates = await GetPlayerStatesWithMembershipRecoveryAsync();
                    
                    // Convert to InstanceMemberInfo
                    var members = new List<InstanceMemberInfo>(playerStates.Count);
                    foreach (var state in playerStates)
                    {
                        var item = NormalizeCurrentItemText(state.current_item);

                        members.Add(new InstanceMemberInfo
                        {
                            PlayerId = state.player_id,
                            PlayerName = state.player_name,
                            Damage = (int)state.damage,
                            CurrentItem = item,
                            IsDead = state.is_dead,
                            Velocity = state.velocity,
                            AfkDuration = state.afk_duration,
                        });
                    }

                    currentInstanceMembers = members;

                    // 現在のラウンドがあれば生存希望プレイヤーをチェック
                    var currentRound = stateService.CurrentRound;

                    if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                    {
                        _logger.LogEvent("RoundCheck",
                            $"Current round check: Round={currentRound != null}, " +
                            $"TerrorKey={(currentRound?.TerrorKey ?? "null")}, " +
                            $"RoundType={(currentRound?.RoundType ?? "null")}",
                            LogEventLevel.Debug);
                    }

                    if (currentRound != null && !string.IsNullOrEmpty(currentRound.TerrorKey))
                    {
                        // Wait for desire players check to complete before updating overlay
                        try
                        {
                            await CheckDesirePlayersForRoundAsync(
                                currentRound.RoundType ?? "Unknown",
                                currentRound.TerrorKey
                            );
                        }
                        catch (Exception desireEx)
                        {
                            _logger?.LogEvent("DesirePlayers", $"Failed to check desire players in timer: {desireEx.Message}", LogEventLevel.Debug);
                        }
                    }
                    // Don't clear desire players when no round - keep the last known state
                    // Desire players will be cleared when a new round starts

                    UpdateInstanceMembersOverlay();
                }
                catch (Exception ex)
                {
                    _logger?.LogEvent("InstanceMembers", $"Failed to update instance members: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}", LogEventLevel.Warning);
                }
            }
            finally
            {
                lock (instanceTimerSync)
                {
                    isInstanceMemberUpdateRunning = false;
                }
            }
        }

        private void UpdateInstanceMembersOverlay()
        {
            // Debug log (only when Debug enabled to avoid hot-path string allocations)
            if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
            {
                _logger.LogEvent("OverlayUpdate",
                    $"Updating overlay: {currentInstanceMembers.Count} members, {currentDesirePlayers.Count} desire players",
                    LogEventLevel.Debug);
            }

            // Route through OverlayManager which owns the actually-displayed overlay forms.
            // (MainForm.overlayForms dictionary is unused since overlays are managed by IOverlayManager.)
            _overlayManager.UpdateInstanceMembersDetailed(currentInstanceMembers, currentDesirePlayers);
        }

        /// <summary>
        /// Handle real-time player.state.updated stream events from the cloud.
        /// Merges single-player updates into the local member list without a full RPC fetch.
        /// </summary>
        private void HandlePlayerStateUpdatedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null) return;

                // Parse data.player_state from the stream event
                JsonElement dataElement;
                if (message.Data is JsonElement je)
                {
                    dataElement = je;
                }
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                JsonElement psElement = dataElement;
                if (dataElement.TryGetProperty("player_state", out var nestedPlayerState))
                {
                    psElement = nestedPlayerState;
                }

                // Check instance_id matches
                if (dataElement.TryGetProperty("instance_id", out var instanceIdElement))
                {
                    var eventInstanceId = instanceIdElement.GetString();
                    if (!string.Equals(eventInstanceId, currentInstanceId, StringComparison.Ordinal))
                    {
                        return; // Not our instance
                    }
                }

                var playerId = psElement.TryGetProperty("player_id", out var pidEl) ? pidEl.GetString() : null;
                if (string.IsNullOrEmpty(playerId)) return;

                var playerName = psElement.TryGetProperty("player_name", out var pnEl) ? pnEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(playerName) && dataElement.TryGetProperty("player_name", out var rootPnEl))
                {
                    playerName = rootPnEl.GetString() ?? "";
                }
                var damage = psElement.TryGetProperty("damage", out var dmgEl) ? dmgEl.GetDouble() : 0;
                var isAlive = psElement.TryGetProperty("is_alive", out var aliveEl) ? aliveEl.GetBoolean() : true;
                var velocity = psElement.TryGetProperty("velocity", out var velEl) ? velEl.GetDouble() : 0;
                var afkDuration = psElement.TryGetProperty("afk_duration", out var afkEl) ? afkEl.GetDouble() : 0;

                // Parse items/current_item
                string currentItem = string.Empty;
                if (psElement.TryGetProperty("current_item", out var ciEl))
                {
                    currentItem = NormalizeCurrentItemText(ciEl.GetString());
                }

                // Fallback for payloads that only provide items[]
                if (string.IsNullOrEmpty(currentItem) &&
                    psElement.TryGetProperty("items", out var itemsEl) &&
                    itemsEl.ValueKind == JsonValueKind.Array)
                {
                    for (int i = itemsEl.GetArrayLength() - 1; i >= 0; i--)
                    {
                        var candidate = NormalizeCurrentItemText(itemsEl[i].GetString());
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            currentItem = candidate;
                            break;
                        }
                    }
                }

                var updatedMember = new InstanceMemberInfo
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    Damage = (int)damage,
                    CurrentItem = currentItem,
                    IsDead = !isAlive,
                    Velocity = velocity,
                    AfkDuration = afkDuration,
                };

                // Merge into currentInstanceMembers (replace existing or add new)
                var members = new List<InstanceMemberInfo>(currentInstanceMembers);
                var existingIndex = members.FindIndex(m => string.Equals(m.PlayerId, playerId, StringComparison.Ordinal));
                if (existingIndex >= 0)
                {
                    members[existingIndex] = updatedMember;
                }
                else
                {
                    members.Add(updatedMember);
                }
                currentInstanceMembers = members;

                // Coalesce overlay repaints: instead of dispatcher.Invoke per stream message
                // (which can fire ~5Hz × 20 players = 100/sec), set a dirty flag and let the
                // existing instanceMemberUpdateTimer pick it up at its 200ms cadence.
                if (Interlocked.Exchange(ref _instanceMembersOverlayDirty, 1) == 0)
                {
                    var dispatcher = _dispatcher;
                    if (dispatcher != null)
                    {
                        // Schedule one repaint; further sets of the dirty flag during this
                        // dispatch will be absorbed and picked up by the next scheduling.
                        Task.Run(() =>
                        {
                            dispatcher.Invoke(() =>
                            {
                                if (Interlocked.Exchange(ref _instanceMembersOverlayDirty, 0) == 1)
                                {
                                    UpdateInstanceMembersOverlay();
                                }
                            });
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle player.state.updated: {ex.Message}", LogEventLevel.Debug);
            }
        }

        /// <summary>
        /// Handle instance.member.joined stream: trigger immediate full sync of player states.
        /// </summary>
        private void HandleInstanceMemberJoinedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null) return;

                JsonElement dataElement;
                if (message.Data is JsonElement je)
                    dataElement = je;
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                var eventInstanceId = dataElement.TryGetProperty("instance_id", out var iidEl) ? iidEl.GetString() : null;
                if (!string.Equals(eventInstanceId, currentInstanceId, StringComparison.Ordinal))
                    return;

                var playerName = dataElement.TryGetProperty("player_name", out var pnEl) ? pnEl.GetString() : "Unknown";
                _logger?.LogEvent("CloudStream", $"Instance member joined: {playerName}");

                // Force immediate full sync on next timer tick by resetting the fetch timestamp
                lastInstanceMembersFetch = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle instance.member.joined: {ex.Message}", LogEventLevel.Debug);
            }
        }

        /// <summary>
        /// Handle instance.member.left stream: remove the player from the local list immediately.
        /// </summary>
        private void HandleInstanceMemberLeftStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null) return;

                JsonElement dataElement;
                if (message.Data is JsonElement je)
                    dataElement = je;
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                var eventInstanceId = dataElement.TryGetProperty("instance_id", out var iidEl) ? iidEl.GetString() : null;
                if (!string.Equals(eventInstanceId, currentInstanceId, StringComparison.Ordinal))
                    return;

                var playerId = dataElement.TryGetProperty("player_id", out var pidEl) ? pidEl.GetString() : null;
                _logger?.LogEvent("CloudStream", $"Instance member left: {playerId}");

                if (!string.IsNullOrEmpty(playerId))
                {
                    // Remove the player from the local member list
                    var members = new List<InstanceMemberInfo>(currentInstanceMembers);
                    members.RemoveAll(m => string.Equals(m.PlayerId, playerId, StringComparison.Ordinal));
                    currentInstanceMembers = members;

                    // Also remove from desire players
                    var desire = new List<string>(currentDesirePlayers);
                    desire.RemoveAll(id => string.Equals(id, playerId, StringComparison.Ordinal));
                    currentDesirePlayers = desire;

                    _dispatcher.Invoke(() => UpdateInstanceMembersOverlay());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle instance.member.left: {ex.Message}", LogEventLevel.Debug);
            }
        }

        /// <summary>
        /// Handle threat.announced stream: update desire players from broadcast data
        /// instead of waiting for the next polling cycle.
        /// </summary>
        private void HandleThreatAnnouncedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null) return;

                JsonElement dataElement;
                if (message.Data is JsonElement je)
                    dataElement = je;
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                var eventInstanceId = dataElement.TryGetProperty("instance_id", out var iidEl) ? iidEl.GetString() : null;
                if (!string.Equals(eventInstanceId, currentInstanceId, StringComparison.Ordinal))
                    return;

                var terrorName = dataElement.TryGetProperty("terror_name", out var tnEl) ? tnEl.GetString() : null;
                _logger?.LogEvent("CloudStream", $"Threat announced: {terrorName}", LogEventLevel.Information);

                // Extract desire_players array
                if (dataElement.TryGetProperty("desire_players", out var dpElement) && dpElement.ValueKind == JsonValueKind.Array)
                {
                    var desirePlayerIds = new List<string>();
                    foreach (var dp in dpElement.EnumerateArray())
                    {
                        var pid = dp.TryGetProperty("player_id", out var dpIdEl) ? dpIdEl.GetString() : null;
                        if (!string.IsNullOrEmpty(pid))
                        {
                            desirePlayerIds.Add(pid);
                        }
                    }
                    currentDesirePlayers = desirePlayerIds;
                    _dispatcher.Invoke(() => UpdateInstanceMembersOverlay());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle threat.announced: {ex.Message}", LogEventLevel.Debug);
            }
        }

        /// <summary>
        /// Handle coordinated.voting.started stream: forward to voting panel when opened.
        /// </summary>
        private void HandleCoordinatedVotingStartedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null)
                {
                    return;
                }

                JsonElement dataElement;
                if (message.Data is JsonElement je)
                {
                    dataElement = je;
                }
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                var instanceId = dataElement.TryGetProperty("instance_id", out var iidEl) ? iidEl.GetString() : null;
                if (!string.Equals(instanceId, currentInstanceId, StringComparison.Ordinal))
                {
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    activeVotingPanelForm?.ApplyVotingStartedStream(dataElement);

                    // Update overlay with voting status
                    var terrorName = dataElement.TryGetProperty("terror_name", out var tnEl) ? tnEl.GetString() : null;
                    var overlayText = string.IsNullOrEmpty(terrorName)
                        ? "🗳 投票中..."
                        : $"🗳 投票中: {terrorName}";
                    _overlayManager.UpdateVoting(overlayText);
                });
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle coordinated.voting.started: {ex.Message}", LogEventLevel.Debug);
            }
        }

        /// <summary>
        /// Handle coordinated.voting.resolved stream: forward to voting panel when opened.
        /// </summary>
        private void HandleCoordinatedVotingResolvedStream(CloudMessage message)
        {
            try
            {
                if (message.Data == null)
                {
                    return;
                }

                JsonElement dataElement;
                if (message.Data is JsonElement je)
                {
                    dataElement = je;
                }
                else
                {
                    var json = JsonSerializer.Serialize(message.Data);
                    dataElement = JsonSerializer.Deserialize<JsonElement>(json);
                }

                _dispatcher.Invoke(() =>
                {
                    activeVotingPanelForm?.ApplyVotingResolvedStream(dataElement);

                    // Update overlay with voting result
                    var finalDecision = dataElement.TryGetProperty("final_decision", out var decisionEl)
                        ? decisionEl.GetString() ?? "Unknown"
                        : "Unknown";

                    var proceedCount = 0;
                    var cancelCount = 0;
                    if (dataElement.TryGetProperty("vote_count", out var voteCountEl) && voteCountEl.ValueKind == JsonValueKind.Object)
                    {
                        if (voteCountEl.TryGetProperty("proceed", out var proceedEl) && proceedEl.TryGetInt32(out var pv))
                            proceedCount = pv;
                        if (voteCountEl.TryGetProperty("cancel", out var cancelEl) && cancelEl.TryGetInt32(out var cv))
                            cancelCount = cv;
                    }

                    var resultText = finalDecision.Equals("Continue", StringComparison.OrdinalIgnoreCase)
                        || finalDecision.Equals("Proceed", StringComparison.OrdinalIgnoreCase)
                        ? $"✅ 投票結果: 続行 ({proceedCount}/{proceedCount + cancelCount})"
                        : $"⏭ 投票結果: スキップ ({cancelCount}/{proceedCount + cancelCount})";
                    _overlayManager.UpdateVoting(resultText);
                });
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle coordinated.voting.resolved: {ex.Message}", LogEventLevel.Debug);
            }
        }

        private async Task<List<PlayerStateInfo>> GetPlayerStatesWithMembershipRecoveryAsync()
        {
            if (_cloudClient == null)
            {
                return new List<PlayerStateInfo>();
            }

            try
            {
                return await _cloudClient.GetPlayerStatesAsync(currentInstanceId, _cancellation.Token);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Access denied: not a member of this instance", StringComparison.OrdinalIgnoreCase))
            {
                string playerIdForCloud = !string.IsNullOrWhiteSpace(_settings.CloudPlayerName)
                    ? _settings.CloudPlayerName
                    : stateService.PlayerDisplayName;

                if (string.IsNullOrWhiteSpace(playerIdForCloud))
                {
                    throw;
                }

                _logger?.LogEvent(
                    "InstanceMembers",
                    $"Player state fetch denied for instance {currentInstanceId}. Rejoining as {playerIdForCloud} and retrying once.",
                    LogEventLevel.Warning);

                await _cloudClient.JoinInstanceAsync(currentInstanceId, playerIdForCloud, _cancellation.Token);
                return await _cloudClient.GetPlayerStatesAsync(currentInstanceId, _cancellation.Token);
            }
        }

        private async Task CheckDesirePlayersForRoundAsync(string roundType, string? terrorKey)
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                {
                    _logger.LogEvent("DesirePlayers",
                        $"Skipping desire check: CloudSync={_settings.CloudSyncEnabled}, " +
                        $"HasClient={_cloudClient != null}, Connected={_cloudClient?.IsConnected ?? false}",
                        LogEventLevel.Debug);
                }
                // Don't clear - keep last known state
                return;
            }

            if (string.IsNullOrEmpty(currentInstanceId) || string.IsNullOrEmpty(terrorKey))
            {
                if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                {
                    _logger.LogEvent("DesirePlayers",
                        $"Skipping desire check: InstanceId={(currentInstanceId ?? "null")}, TerrorKey={(terrorKey ?? "null")}",
                        LogEventLevel.Debug);
                }
                // Don't clear - keep last known state
                return;
            }

            try
            {
                if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                {
                    _logger.LogEvent("DesirePlayers",
                        $"Calling FindDesirePlayersAsync: instance={currentInstanceId}, terror={terrorKey}, round={roundType}",
                        LogEventLevel.Debug);
                }

                var desirePlayers = await _cloudClient.FindDesirePlayersAsync(
                    currentInstanceId,
                    terrorKey ?? "",
                    roundType,
                    _cancellation.Token
                );

                var newDesirePlayers = desirePlayers
                    .Select(p => p.player_id)
                    .ToList();
                currentDesirePlayers = newDesirePlayers;

                await SyncThreatAnnouncementWithDesirePlayersAsync(
                    currentInstanceId,
                    terrorKey,
                    roundType,
                    desirePlayers);

                if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                {
                    _logger.LogEvent("DesirePlayers",
                        $"Found {currentDesirePlayers.Count} desire players for {terrorKey} in {roundType}",
                        LogEventLevel.Debug);
                }

                TryApplyNoSurvivalWishSkipMode(roundType, terrorKey);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("DesirePlayers", $"Failed to check desire players: {ex.Message}", LogEventLevel.Warning);
                // Don't clear on error - keep last known state
                // currentDesirePlayers.Clear();
            }
        }

        private async Task SyncThreatAnnouncementWithDesirePlayersAsync(
            string? instanceId,
            string? terrorKey,
            string roundType,
            List<DesirePlayerInfo> desirePlayers)
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(terrorKey))
            {
                return;
            }

            var normalizedInstanceId = instanceId.Trim();
            var normalizedTerrorKey = terrorKey.Trim();
            var normalizedRoundType = roundType?.Trim() ?? string.Empty;

            var normalizedIds = desirePlayers
                .Select(player => player.player_id)
                .Where(playerId => !string.IsNullOrWhiteSpace(playerId))
                .OrderBy(playerId => playerId, StringComparer.Ordinal)
                .ToArray();
            var signature = string.Join("|", new[]
            {
                normalizedInstanceId,
                normalizedTerrorKey,
                normalizedRoundType,
                string.Join(",", normalizedIds)
            });
            var now = DateTime.UtcNow;

            if (signature == lastThreatAnnouncementSyncSignature
                && (now - lastThreatAnnouncementSync).TotalSeconds < ThreatAnnouncementSyncIntervalSeconds)
            {
                return;
            }

            await _cloudClient.AnnounceThreatAsync(
                normalizedInstanceId,
                normalizedTerrorKey,
                normalizedRoundType,
                desirePlayers,
                _cancellation.Token);

            lastThreatAnnouncementSync = now;
            lastThreatAnnouncementSyncSignature = signature;
        }

        private async void ScheduleAutoSuicideWithDesireCheck(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false)
        {
            // Check if there are desire players
            if (currentDesirePlayers.Count > 0)
            {
                // Add 10 seconds delay and show confirmation dialog
                var extendedDelay = delay.Add(TimeSpan.FromSeconds(10));

                // Schedule with extended delay first
                _autoSuicideCoordinator.Schedule(extendedDelay, resetStartTime, fromAllRoundsMode, false);

                // Show confirmation dialog after brief delay
                try
                {
                    await Task.Delay(100, _cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                _dispatcher.Invoke(async () =>
                {
                    using (var confirmDialog = new AutoSuicideConfirmationOverlay(currentDesirePlayers.Count))
                    {
                        var result = confirmDialog.ShowDialog();

                        if (result == System.Windows.Forms.DialogResult.OK && confirmDialog.UserConfirmed)
                        {
                            // User confirmed - proceed with auto suicide
                            _logger?.LogEvent("AutoSuicide", $"User confirmed auto suicide despite {currentDesirePlayers.Count} desire players");
                        }
                        else if (result == System.Windows.Forms.DialogResult.Cancel && confirmDialog.UserCancelled)
                        {
                            // User cancelled - cancel auto suicide
                            _autoSuicideCoordinator.Cancel(true);
                            _logger?.LogEvent("AutoSuicide", "User cancelled auto suicide due to desire players");
                        }
                    }
                });
            }
            else
            {
                // No desire players - proceed normally
                _autoSuicideCoordinator.Schedule(delay, resetStartTime, fromAllRoundsMode, false);
            }
        }

        // Coalescing state for UpdateCloudPlayerState. Multiple call sites can request an update;
        // only one runs at a time, and a follow-up runs at most every MinIntervalMs.
        private const long CloudPlayerStateMinIntervalMs = 200;
        private long _cloudPlayerStateLastSentTicks;
        private int _cloudPlayerStatePending; // 0/1
        private int _cloudPlayerStateRunning; // 0/1

        // Coalescing flag for instance member overlay repaints triggered by player.state.updated
        // stream events. Set to 1 when a repaint is needed; only the first set schedules a UI
        // dispatch, subsequent sets are absorbed.
        private int _instanceMembersOverlayDirty;

        /// <summary>
        /// Request a coalesced player-state update. Safe to call from any thread at high frequency.
        /// At most one network call runs concurrently, and successive calls within the throttle
        /// window are merged into a single trailing send so the latest state always wins.
        /// </summary>
        private void RequestCloudPlayerStateUpdate(string operationName)
        {
            // Mark that an update is desired.
            Interlocked.Exchange(ref _cloudPlayerStatePending, 1);

            // If a worker is already running, it will pick up the pending flag.
            if (Interlocked.CompareExchange(ref _cloudPlayerStateRunning, 1, 0) != 0)
            {
                return;
            }

            RunBackgroundOperation(async () =>
            {
                try
                {
                    while (Interlocked.Exchange(ref _cloudPlayerStatePending, 0) == 1)
                    {
                        long nowTicks = Environment.TickCount64;
                        long lastTicks = Interlocked.Read(ref _cloudPlayerStateLastSentTicks);
                        long elapsed = nowTicks - lastTicks;
                        if (elapsed < CloudPlayerStateMinIntervalMs)
                        {
                            try
                            {
                                await Task.Delay((int)(CloudPlayerStateMinIntervalMs - elapsed), _cancellation.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            // Re-set pending so we send the latest state once cooldown ends.
                            Interlocked.Exchange(ref _cloudPlayerStatePending, 1);
                            continue;
                        }

                        Interlocked.Exchange(ref _cloudPlayerStateLastSentTicks, nowTicks);
                        await UpdateCloudPlayerState().ConfigureAwait(false);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _cloudPlayerStateRunning, 0);
                    // Race: a request may have arrived just before we cleared running. Re-launch if so.
                    if (Volatile.Read(ref _cloudPlayerStatePending) == 1 &&
                        Interlocked.CompareExchange(ref _cloudPlayerStateRunning, 1, 0) == 0)
                    {
                        // Hand off to a fresh background op to avoid deep recursion.
                        RunBackgroundOperation(() => UpdateCloudPlayerStateLoopAsync(), operationName, LogEventLevel.Debug);
                    }
                }
            }, operationName, LogEventLevel.Debug);
        }

        private async Task UpdateCloudPlayerStateLoopAsync()
        {
            try
            {
                while (Interlocked.Exchange(ref _cloudPlayerStatePending, 0) == 1)
                {
                    long nowTicks = Environment.TickCount64;
                    long lastTicks = Interlocked.Read(ref _cloudPlayerStateLastSentTicks);
                    long elapsed = nowTicks - lastTicks;
                    if (elapsed < CloudPlayerStateMinIntervalMs)
                    {
                        try
                        {
                            await Task.Delay((int)(CloudPlayerStateMinIntervalMs - elapsed), _cancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        Interlocked.Exchange(ref _cloudPlayerStatePending, 1);
                        continue;
                    }

                    Interlocked.Exchange(ref _cloudPlayerStateLastSentTicks, nowTicks);
                    await UpdateCloudPlayerState().ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _cloudPlayerStateRunning, 0);
            }
        }

        /// <summary>
        /// Update player state to Cloud
        /// </summary>
        private async Task UpdateCloudPlayerState()
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                return;
            }

            if (string.IsNullOrEmpty(currentInstanceId))
            {
                return;
            }

            try
            {
                var currentRound = stateService.CurrentRound;
                var damage = currentRound?.Damage ?? 0;
                var isDead = currentRound?.IsDeath ?? false;

                // Get current item from cached field (updated synchronously by ITEM event handlers)
                // and reconcile with InfoPanel as a defensive fallback. Reading the cache first avoids
                // any UI dispatcher race that could otherwise return an empty string while the panel
                // visibly displays the item.
                string rawItem = currentHeldItemName ?? string.Empty;
                if (string.IsNullOrEmpty(rawItem))
                {
                    try
                    {
                        _dispatcher.Invoke(() =>
                        {
                            rawItem = InfoPanel?.ItemValue?.Text ?? string.Empty;
                        });
                    }
                    catch { }
                }
                string currentItem = NormalizeCurrentItemText(rawItem);

                // Get player name from settings
                var playerName = string.IsNullOrWhiteSpace(_settings.CloudPlayerName)
                    ? Environment.UserName
                    : _settings.CloudPlayerName;

                // Create items list - empty if no item, otherwise single item
                var items = string.IsNullOrWhiteSpace(currentItem) 
                    ? new List<string>() 
                    : new List<string> { currentItem };

                try
                {
                    _logger?.LogEvent(
                        "CloudPlayerState",
                        $"UpdateCloudPlayerState raw='{rawItem}' normalized='{currentItem}' items=[{string.Join(",", items)}] instance={currentInstanceId} player={(_cloudClient.UserId ?? playerName)}",
                        LogEventLevel.Debug);
                }
                catch { }

                await _cloudClient.UpdatePlayerStateAsync(
                    instanceId: currentInstanceId,
                    playerId: _cloudClient.UserId ?? playerName, // Use authenticated user ID for consistent membership identity
                    velocity: currentVelocity,
                    afkDuration: lastIdleSeconds,
                    items: items,
                    damage: damage,
                    isAlive: !isDead,
                    playerName: playerName,  // �v���C���[����n��
                    cancellationToken: _cancellation.Token
                );
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudPlayerState", $"Failed to update player state: {ex.Message}", LogEventLevel.Debug);
            }
        }

        private static string NormalizeCurrentItemText(string? value)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "なし", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return text;
        }

        /// <summary>
        /// Report monitoring status to Cloud (called periodically)
        /// </summary>
        private async Task ReportMonitoringStatusAsync()
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                return;
            }

            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var uptime = (int)(DateTime.Now - process.StartTime).TotalSeconds;
                    var memoryMB = process.WorkingSet64 / (1024.0 * 1024.0);
                    var cpuPercent = 0.0; // CPU calculation is complex, simplified here

                    var oscStatus = "CONNECTED"; // Simplified - assume connected if OSC events are coming
                    var vrchatStatus = !string.IsNullOrEmpty(currentInstanceId) ? "CONNECTED" : "DISCONNECTED";

                    await _cloudClient.ReportMonitoringStatusAsync(
                        instanceId: string.IsNullOrEmpty(currentInstanceId) ? null : currentInstanceId,
                        applicationStatus: "RUNNING",
                        applicationVersion: version,
                        uptime: uptime,
                        memoryUsage: memoryMB,
                        cpuUsage: cpuPercent,
                        oscStatus: oscStatus,
                        oscLatency: null,
                        vrchatStatus: vrchatStatus,
                        vrchatWorldId: null,
                        vrchatInstanceId: currentInstanceId,
                        cancellationToken: _cancellation.Token
                    );
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudMonitoring", $"Failed to report monitoring status: {ex.Message}", LogEventLevel.Debug);
            }
        }
    }
}
