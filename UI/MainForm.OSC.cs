using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Rug.Osc;
using Serilog.Events;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private float currentVelocity = 0;
        private DateTime velocityInRangeStart = DateTime.MinValue;
        private DateTime idleStartTime = DateTime.MinValue;
        private System.Windows.Forms.Timer velocityTimer; // Windows.Forms.Timer
        private int oscUiUpdatePending;
        private float receivedVelocityMagnitude = 0;
        private float currentVelocityX = 0;
        private float currentVelocityZ = 0;
        private float lastKnownFacingAngle = 0;
        private bool hasFacingAngleMeasurement = false;
        private bool afkSoundPlayed = false;
        private bool punishSoundPlayed = false;
        private double lastIdleSeconds = 0d;
        private int oscMessageSkipCounter = 0;
        private const int OscMessageProcessInterval = 3;

        private async Task InitializeOSCRepeater()
        {
            LogUi("Initializing in-process OSC repeater.", LogEventLevel.Debug);

            // Kill any leftover external OscRepeater processes from previous runs.
            foreach (var processName in new[] { "OscRepeater", "OSCRepeater" })
            {
                foreach (var existingProcess in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (!existingProcess.HasExited)
                        {
                            _logger?.LogEvent("OSCRepeater", "Existing OSCRepeater instance detected. Terminating.");
                            existingProcess.Kill();
                            existingProcess.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogEvent("OSCRepeater", $"Failed to terminate existing OSCRepeater process: {ex.Message}");
                    }
                    finally
                    {
                        existingProcess.Dispose();
                    }
                }
            }

            if (!_settings.OSCPortChanged)
            {
                int port = 30000;
                bool portFound = false;
                LogUi("Selecting OSC port for internal listener.", LogEventLevel.Debug);
                while (!portFound)
                {
                    try
                    {
                        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                        listener.Start();
                        listener.Stop();
                        portFound = true;
                    }
                    catch (SocketException)
                    {
                        port++;
                    }
                }
                LogUi($"OSC internal listener port selected: {port}.", LogEventLevel.Debug);
                _settings.OSCPort = port;
                _settings.OSCPortChanged = true;
                await _settings.SaveAsync();
                LogUi("OSC port persisted to settings.", LogEventLevel.Debug);
            }

            // Configure the in-process repeater: listen on VRChat's default port (9001)
            // and forward to our internal listener port.
            _oscRepeaterService.AddDestination("127.0.0.1", _settings.OSCPort);
            await _oscRepeaterService.StartAsync(9001);
            LogUi($"In-process OSC repeater started: 9001 -> 127.0.0.1:{_settings.OSCPort}.", LogEventLevel.Information);
        }

        private void HandleOscMessage(OscMessage message)
        {
            if (message == null)
            {
                LogUi("Received null OSC message.", LogEventLevel.Warning);
                return;
            }

            bool isHighFrequencyMessage = message.Address == "/avatar/parameters/VelocityX" ||
                                         message.Address == "/avatar/parameters/VelocityZ";

            if (isHighFrequencyMessage)
            {
                oscMessageSkipCounter++;
                if (oscMessageSkipCounter % OscMessageProcessInterval != 0)
                {
                    return;
                }
            }

            if (message.Address == "/avatar/parameters/VelocityMagnitude")
            {
                try
                {
                    if (message.Count > 0)
                    {
                        receivedVelocityMagnitude = Convert.ToSingle(message[0]);
                    }
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to parse velocity magnitude: {ex.Message}", LogEventLevel.Warning);
                }
            }
            else if (message.Address == "/avatar/parameters/VelocityX")
            {
                try
                {
                    if (message.Count > 0)
                    {
                        currentVelocityX = Convert.ToSingle(message[0]);
                    }
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to parse velocity X: {ex.Message}", LogEventLevel.Warning);
                }
            }
            else if (message.Address == "/avatar/parameters/VelocityZ")
            {
                try
                {
                    if (message.Count > 0)
                    {
                        currentVelocityZ = Convert.ToSingle(message[0]);
                    }
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to parse velocity Z: {ex.Message}", LogEventLevel.Warning);
                }
            }
            else if (message.Address == "/avatar/parameters/suside")
            {
                bool suicideFlag = false;
                if (message.Count > 0)
                {
                    try
                    {
                        suicideFlag = Convert.ToBoolean(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse suicide flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (suicideFlag)
                {
                    LogUi("Immediate suicide flag received. Executing auto suicide action.");
                    RunBackgroundOperation(() => PerformAutoSuicide(), "OscImmediateSuicide", LogEventLevel.Debug);
                }
            }
            else if (message.Address == "/avatar/parameters/autosuside")
            {
                bool autoSuicideOSC = false;
                if (message.Count > 0)
                {
                    try
                    {
                        autoSuicideOSC = Convert.ToBoolean(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse auto-suicide OSC toggle: {ex.Message}", LogEventLevel.Warning);
                    }
                    _settings.AutoSuicideEnabled = autoSuicideOSC;
                    LoadAutoSuicideRules();
                    if (!autoSuicideOSC)
                    {
                        CancelAutoSuicide();
                    }
                    SyncShortcutOverlayState();
                }
                LogUi($"Auto suicide OSC toggle set to {autoSuicideOSC}.", LogEventLevel.Debug);
            }
            else if (message.Address == "/avatar/parameters/abortAutoSuside")
            {
                bool abortFlag = true;
                if (message.Count > 0)
                {
                    try
                    {
                        abortFlag = Convert.ToBoolean(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse abort flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (abortFlag)
                {
                    LogUi("Abort auto suicide requested via OSC.");
                    CancelAutoSuicide(manualOverride: true);
                }
            }
            else if (message.Address == "/avatar/parameters/delayAutoSuside")
            {
                bool delayFlag = true;
                if (message.Count > 0)
                {
                    try
                    {
                        delayFlag = Convert.ToBoolean(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse delay flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (delayFlag && autoSuicideService.HasScheduled)
                {
                    var remaining = DelayAutoSuicide(manualOverride: true);
                    if (remaining.HasValue)
                    {
                        LogUi($"Delaying auto suicide by {remaining.Value}.", LogEventLevel.Debug);
                    }
                }
            }
            else if (message.Address == "/avatar/parameters/setalert")
            {
                float setAlertValue = 0;
                if (message.Count > 0)
                {
                    try
                    {
                        setAlertValue = Convert.ToSingle(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse alert value: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (setAlertValue != 0)
                {
                    LogUi($"Forwarding alert value {setAlertValue} to integrated cloud.", LogEventLevel.Debug);
                    RunBackgroundOperation(() => SendAlertToCloudAsync(setAlertValue), "OscForwardAlert", LogEventLevel.Debug);
                }
            }
            else if (message.Address == "/avatar/parameters/getlatestsavecode")
            {
                bool getLatestSaveCode = false;
                if (message.Count > 0)
                {
                    try
                    {
                        getLatestSaveCode = Convert.ToBoolean(message[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse save code request flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (getLatestSaveCode)
                {
                    LogUi("OSC request received for latest save code.");
                    RunBackgroundOperation(async () =>
                    {
                        try
                        {
                            var saveCode = await GetLatestSaveCodeFromCloudAsync(_cancellation.Token).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(saveCode))
                            {
                                await PersistLastSaveCodeAsync(saveCode).ConfigureAwait(false);
                                CopySaveCodeToClipboard(saveCode);
                                _logger.LogEvent("SaveCode", "Latest save code copied to clipboard from cloud settings: " + saveCode);
                            }
                            else
                            {
                                CopyCachedSaveCode("No save code found in cloud settings.");
                            }
                        }
                        catch (Exception ex)
                        {
                            CopyCachedSaveCode($"Exception occurred while fetching latest save code from cloud settings: {ex.Message}");
                        }
                    }, "OscFetchLatestSaveCode", LogEventLevel.Warning);
                }
            }
            else if (message.Address == "/avatar/parameters/isAllSelfKill")
            {
                if (message.Count > 0)
                {
                    try
                    {
                        var newValue = Convert.ToBoolean(message[0]);
                        if (issetAllSelfKillMode != newValue)
                        {
                            issetAllSelfKillMode = newValue;
                            LogUi($"Received 'isAllSelfKill' flag: {issetAllSelfKillMode}.", LogEventLevel.Debug);
                            SyncShortcutOverlayState();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse isAllSelfKill flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
            }
            else if (message.Address == "/avatar/parameters/followAutoSelfKill")
            {
                if (message.Count > 0)
                {
                    try
                    {
                        var newValue = Convert.ToBoolean(message[0]);
                        if (followAutoSelfKill != newValue)
                        {
                            followAutoSelfKill = newValue;
                            LogUi($"Received 'followAutoSelfKill' flag: {followAutoSelfKill}.", LogEventLevel.Debug);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse followAutoSelfKill flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
            }

            currentVelocity = Math.Abs(receivedVelocityMagnitude);
            hasFacingAngleMeasurement = false;
            Interlocked.Exchange(ref oscUiUpdatePending, 1);
        }

        private void CopySaveCodeToClipboard(string saveCode)
        {
            // Marshal to the existing UI thread (already STA) instead of allocating a new thread per call
            try
            {
                _dispatcher.Invoke(() =>
                {
                    try
                    {
                        Clipboard.SetText(saveCode ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("SaveCodeError", $"Failed to copy save code to clipboard: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SaveCodeError", $"Failed to dispatch clipboard copy: {ex.Message}");
            }
        }

        private async Task PersistLastSaveCodeAsync(string saveCode)
        {
            var normalized = saveCode ?? string.Empty;
            if (_lastSaveCode == normalized && string.Equals(_settings.LastSaveCode ?? string.Empty, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _lastSaveCode = normalized;
            _settings.LastSaveCode = normalized;

            try
            {
                await _settings.SaveAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SaveCodeError", $"Failed to persist save code: {ex.Message}");
            }
        }

        private async Task BackupSaveCodeToCloudAsync(string saveCode)
        {
            var normalized = saveCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                _logger.LogEvent("SaveCode", "Skipped cloud backup because cloud sync is disabled or disconnected.");
                return;
            }

            var settingsPayload = new Dictionary<string, object>
            {
                ["saveCode"] = new Dictionary<string, object>
                {
                    ["latest"] = normalized,
                    ["updatedAtUtc"] = DateTime.UtcNow.ToString("O")
                }
            };

            try
            {
                await _cloudClient.UpdateSettingsAsync(ResolveCloudSettingsUserId(), settingsPayload, _cancellation.Token).ConfigureAwait(false);
                _logger.LogEvent("SaveCode", "Save code backed up to cloud settings successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SaveCodeError", $"Exception occurred while backing up save code to cloud settings: {ex.Message}");
            }
        }

        private async Task<string> GetLatestSaveCodeFromCloudAsync(CancellationToken cancellationToken)
        {
            if (!_settings.CloudSyncEnabled || _cloudClient == null || !_cloudClient.IsConnected)
            {
                return string.Empty;
            }

            var cloudSettings = await _cloudClient.GetSettingsAsync(ResolveCloudSettingsUserId(), cancellationToken).ConfigureAwait(false);
            if (!cloudSettings.TryGetValue("categories", out var categoriesObj) || categoriesObj is not JsonElement categoriesElem || categoriesElem.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!categoriesElem.TryGetProperty("saveCode", out var saveCodeElem) || saveCodeElem.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!saveCodeElem.TryGetProperty("latest", out var latestElem) || latestElem.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return latestElem.GetString() ?? string.Empty;
        }

        private string ResolveCloudSettingsUserId()
        {
            if (!string.IsNullOrWhiteSpace(_settings.CloudPlayerName))
            {
                return _settings.CloudPlayerName;
            }

            return Environment.UserName;
        }

        private void CopyCachedSaveCode(string reason)
        {
            _logger.LogEvent("SaveCodeError", reason);
            var cachedSaveCode = _lastSaveCode ?? string.Empty;
            CopySaveCodeToClipboard(cachedSaveCode);
            if (!string.IsNullOrEmpty(cachedSaveCode))
            {
                _logger.LogEvent("SaveCode", "Latest save code copied to clipboard from cache: " + cachedSaveCode);
            }
            else
            {
                _logger.LogEvent("SaveCodeError", "No cached save code available.");
            }
        }
    }
}
