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
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
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
                    };
                    members.Add(member);
                }

                currentInstanceMembers = members;
                UpdateInstanceMembersOverlay();
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("InstanceMembers", $"Failed to update instance members: {ex.Message}", LogEventLevel.Debug);
            }
        }

        private void UpdateInstanceMembersOverlay()
        {
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
                currentDesirePlayers.Clear();
                return;
            }

            if (string.IsNullOrEmpty(currentInstanceId) || string.IsNullOrEmpty(terrorKey))
            {
                currentDesirePlayers.Clear();
                return;
            }

            try
            {
                var desirePlayers = await _cloudClient.FindDesirePlayersAsync(
                    currentInstanceId,
                    terrorKey ?? "",
                    roundType
                );

                currentDesirePlayers = desirePlayers
                    .Select(p => p.player_id)
                    .ToList();

                UpdateInstanceMembersOverlay();

                _logger?.LogEvent("DesirePlayers", $"Found {currentDesirePlayers.Count} desire players for {terrorKey} in {roundType}");
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("DesirePlayers", $"Failed to check desire players: {ex.Message}", LogEventLevel.Warning);
                currentDesirePlayers.Clear();
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
                ScheduleAutoSuicide(extendedDelay, resetStartTime, fromAllRoundsMode, false);

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
                            CancelAutoSuicide(true);
                            _logger?.LogEvent("AutoSuicide", "User cancelled auto suicide due to desire players");
                        }
                    }
                });
            }
            else
            {
                // No desire players - proceed normally
                ScheduleAutoSuicide(delay, resetStartTime, fromAllRoundsMode, false);
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

            // Throttle updates to every 2 seconds
            var now = DateTime.Now;
            if ((now - lastCloudStateUpdate).TotalSeconds < CloudStateUpdateIntervalSeconds)
            {
                return;
            }
            lastCloudStateUpdate = now;

            try
            {
                var currentRound = stateService.CurrentRound;
                var damage = currentRound?.Damage ?? 0;
                var isDead = currentRound?.IsDeath ?? false;

                // Get current item from InfoPanel
                string currentItem = "None";
                _dispatcher.Invoke(() =>
                {
                    currentItem = InfoPanel?.ItemValue?.Text ?? "None";
                });

                // Get player name from settings
                var playerName = string.IsNullOrWhiteSpace(_settings.CloudPlayerName)
                    ? Environment.UserName
                    : _settings.CloudPlayerName;

                await _cloudClient.UpdatePlayerStateAsync(
                    instanceId: currentInstanceId,
                    playerId: playerName, // Use player name as ID
                    velocity: currentVelocity,
                    afkDuration: lastIdleSeconds,
                    items: string.IsNullOrEmpty(currentItem) ? new List<string>() : new List<string> { currentItem },
                    damage: damage,
                    isAlive: !isDead
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
