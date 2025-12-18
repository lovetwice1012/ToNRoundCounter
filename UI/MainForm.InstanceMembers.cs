using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Serilog.Events;

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
                    // �����̃v���C���[��Ԃ�N���E�h�ɑ��M (�Ԋu����)
                    var now = DateTime.Now;
                    if ((now - lastCloudStateUpdate).TotalSeconds >= CloudStateUpdateIntervalSeconds)
                    {
                        await UpdateCloudPlayerState();
                        lastCloudStateUpdate = now;
                    }
                    
                    // Get player states from cloud
                    var playerStates = await _cloudClient.GetPlayerStatesAsync(currentInstanceId);
                    
                    // Convert to InstanceMemberInfo
                    var members = new List<InstanceMemberInfo>();
                    foreach (var state in playerStates)
                    {
                        var member = new InstanceMemberInfo
                        {
                            PlayerId = state.player_id,
                            PlayerName = state.player_name,
                            Damage = (int)state.damage,
                            CurrentItem = state.current_item,
                            IsDead = state.is_dead,
                            Velocity = state.velocity,
                            AfkDuration = state.afk_duration,
                        };
                        members.Add(member);
                    }

                    currentInstanceMembers = members;
                    
                    // ���݂̃��E���h������ΐ�����]�v���C���[��`�F�b�N
                    var currentRound = stateService.CurrentRound;
                    
                    _logger?.LogEvent("RoundCheck", 
                        $"Current round check: Round={currentRound != null}, " +
                        $"TerrorKey={(currentRound?.TerrorKey ?? "null")}, " +
                        $"RoundType={(currentRound?.RoundType ?? "null")}", 
                        LogEventLevel.Information);
                    
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
                    _logger?.LogEvent("InstanceMembers", $"Failed to update instance members: {ex.Message}", LogEventLevel.Debug);
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
            // Debug log
            _logger?.LogEvent("OverlayUpdate", 
                $"Updating overlay: {currentInstanceMembers.Count} members, {currentDesirePlayers.Count} desire players. " +
                $"Desire IDs: [{string.Join(", ", currentDesirePlayers)}]", 
                LogEventLevel.Information);

            if (overlayForms.TryGetValue(OverlaySection.InstanceMembers, out var form))
            {
                if (form is OverlayInstanceMembersForm membersForm)
                {
                    _dispatcher.Invoke(() =>
                    {
                        membersForm.UpdateMembers(currentInstanceMembers, currentDesirePlayers);
                    });
                }
            }
        }

        private async Task CheckDesirePlayersForRoundAsync(string roundType, string? terrorKey)
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                _logger?.LogEvent("DesirePlayers", 
                    $"Skipping desire check: CloudSync={_settings.CloudSyncEnabled}, " +
                    $"HasClient={_cloudClient != null}, Connected={_cloudClient?.IsConnected ?? false}", 
                    LogEventLevel.Information);
                // Don't clear - keep last known state
                return;
            }

            if (string.IsNullOrEmpty(currentInstanceId) || string.IsNullOrEmpty(terrorKey))
            {
                _logger?.LogEvent("DesirePlayers", 
                    $"Skipping desire check: InstanceId={(currentInstanceId ?? "null")}, TerrorKey={(terrorKey ?? "null")}", 
                    LogEventLevel.Information);
                // Don't clear - keep last known state
                return;
            }

            try
            {
                _logger?.LogEvent("DesirePlayers", 
                    $"Calling FindDesirePlayersAsync: instance={currentInstanceId}, terror={terrorKey}, round={roundType}", 
                    LogEventLevel.Information);
                
                var desirePlayers = await _cloudClient.FindDesirePlayersAsync(
                    currentInstanceId,
                    terrorKey ?? "",
                    roundType
                );

                currentDesirePlayers = desirePlayers
                    .Select(p => p.player_id)
                    .ToList();

                _logger?.LogEvent("DesirePlayers", 
                    $"Found {currentDesirePlayers.Count} desire players for {terrorKey} in {roundType}. " +
                    $"IDs: [{string.Join(", ", currentDesirePlayers)}]", 
                    LogEventLevel.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("DesirePlayers", $"Failed to check desire players: {ex.Message}", LogEventLevel.Warning);
                // Don't clear on error - keep last known state
                // currentDesirePlayers.Clear();
            }
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
                await Task.Delay(100);

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

                // Get current item from InfoPanel
                string currentItem = "";
                _dispatcher.Invoke(() =>
                {
                    currentItem = InfoPanel?.ItemValue?.Text ?? "";
                });

                // Get player name from settings
                var playerName = string.IsNullOrWhiteSpace(_settings.CloudPlayerName)
                    ? Environment.UserName
                    : _settings.CloudPlayerName;

                // Create items list - empty if no item, otherwise single item
                var items = string.IsNullOrWhiteSpace(currentItem) 
                    ? new List<string>() 
                    : new List<string> { currentItem };

                await _cloudClient.UpdatePlayerStateAsync(
                    instanceId: currentInstanceId,
                    playerId: playerName, // Use player name as ID
                    velocity: currentVelocity,
                    afkDuration: lastIdleSeconds,
                    items: items,
                    damage: damage,
                    isAlive: !isDead,
                    playerName: playerName  // �v���C���[����n��
                );
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudPlayerState", $"Failed to update player state: {ex.Message}", LogEventLevel.Debug);
            }
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
                        vrchatInstanceId: currentInstanceId
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
