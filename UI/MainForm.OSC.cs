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

            string address = message.Address;
            bool isHighFrequencyMessage = address == "/avatar/parameters/VelocityX" ||
                                         address == "/avatar/parameters/VelocityZ";

            if (isHighFrequencyMessage)
            {
                oscMessageSkipCounter++;
                if (oscMessageSkipCounter % OscMessageProcessInterval != 0)
                {
                    return;
                }
            }

            // Hashed dispatch via string switch (compiler emits hash-based jump table).
            switch (address)
            {
                case "/avatar/parameters/VelocityMagnitude":
                    if (TryReadSingle(message, out float vm)) receivedVelocityMagnitude = vm;
                    break;
                case "/avatar/parameters/VelocityX":
                    if (TryReadSingle(message, out float vx)) currentVelocityX = vx;
                    break;
                case "/avatar/parameters/VelocityZ":
                    if (TryReadSingle(message, out float vz)) currentVelocityZ = vz;
                    break;
                case "/avatar/parameters/suside":
                    if (TryReadBool(message, out bool suicideFlag) && suicideFlag)
                    {
                        LogUi("Immediate suicide flag received. Executing auto suicide action.");
                        RunBackgroundOperation(() => PerformAutoSuicide(), "OscImmediateSuicide", LogEventLevel.Debug);
                    }
                    break;
                case "/avatar/parameters/autosuside":
                {
                    bool autoSuicideOSC = false;
                    if (message.Count > 0)
                    {
                        TryReadBool(message, out autoSuicideOSC);
                        _settings.AutoSuicideEnabled = autoSuicideOSC;
                        LoadAutoSuicideRules();
                        if (!autoSuicideOSC)
                        {
                            CancelAutoSuicide();
                        }
                        SyncShortcutOverlayState();
                    }
                    if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                    {
                        LogUi($"Auto suicide OSC toggle set to {autoSuicideOSC}.", LogEventLevel.Debug);
                    }
                    break;
                }
                case "/avatar/parameters/abortAutoSuside":
                {
                    bool abortFlag = true;
                    if (message.Count > 0)
                    {
                        TryReadBool(message, out abortFlag);
                    }
                    if (abortFlag)
                    {
                        LogUi("Abort auto suicide requested via OSC.");
                        CancelAutoSuicide(manualOverride: true);
                    }
                    break;
                }
                case "/avatar/parameters/delayAutoSuside":
                {
                    bool delayFlag = true;
                    if (message.Count > 0)
                    {
                        TryReadBool(message, out delayFlag);
                    }
                    if (delayFlag && autoSuicideService.HasScheduled)
                    {
                        var remaining = DelayAutoSuicide(manualOverride: true);
                        if (remaining.HasValue && _logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                        {
                            LogUi($"Delaying auto suicide by {remaining.Value}.", LogEventLevel.Debug);
                        }
                    }
                    break;
                }
                case "/avatar/parameters/setalert":
                {
                    float setAlertValue = 0;
                    if (message.Count > 0)
                    {
                        TryReadSingle(message, out setAlertValue);
                    }
                    if (setAlertValue != 0)
                    {
                        if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                        {
                            LogUi($"Forwarding alert value {setAlertValue} to integrated cloud.", LogEventLevel.Debug);
                        }
                        var alertCopy = setAlertValue;
                        RunBackgroundOperation(() => SendAlertToCloudAsync(alertCopy), "OscForwardAlert", LogEventLevel.Debug);
                    }
                    break;
                }
                case "/avatar/parameters/getlatestsavecode":
                {
                    bool getLatestSaveCode = false;
                    if (message.Count > 0)
                    {
                        TryReadBool(message, out getLatestSaveCode);
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
                    break;
                }
                case "/avatar/parameters/isAllSelfKill":
                    if (TryReadBool(message, out bool newAllSelfKill) && issetAllSelfKillMode != newAllSelfKill)
                    {
                        issetAllSelfKillMode = newAllSelfKill;
                        if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                        {
                            LogUi($"Received 'isAllSelfKill' flag: {issetAllSelfKillMode}.", LogEventLevel.Debug);
                        }
                        SyncShortcutOverlayState();
                    }
                    break;
                case "/avatar/parameters/followAutoSelfKill":
                    if (TryReadBool(message, out bool newFollowAutoSelfKill) && followAutoSelfKill != newFollowAutoSelfKill)
                    {
                        followAutoSelfKill = newFollowAutoSelfKill;
                        if (_logger != null && _logger.IsEnabled(LogEventLevel.Debug))
                        {
                            LogUi($"Received 'followAutoSelfKill' flag: {followAutoSelfKill}.", LogEventLevel.Debug);
                        }
                    }
                    break;
            }

            currentVelocity = Math.Abs(receivedVelocityMagnitude);
            hasFacingAngleMeasurement = false;
            Interlocked.Exchange(ref oscUiUpdatePending, 1);
        }

        private static bool TryReadSingle(OscMessage message, out float value)
        {
            if (message.Count > 0)
            {
                try
                {
                    object o = message[0];
                    value = o is float f ? f : Convert.ToSingle(o);
                    return true;
                }
                catch
                {
                }
            }
            value = 0f;
            return false;
        }

        private static bool TryReadBool(OscMessage message, out bool value)
        {
            if (message.Count > 0)
            {
                try
                {
                    object o = message[0];
                    value = o is bool b ? b : Convert.ToBoolean(o);
                    return true;
                }
                catch
                {
                }
            }
            value = false;
            return false;
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
