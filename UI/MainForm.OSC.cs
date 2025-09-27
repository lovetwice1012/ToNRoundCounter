using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
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
        private float receivedVelocityMagnitude = 0;
        private float currentVelocityX = 0;
        private float currentVelocityZ = 0;
        private float lastKnownFacingAngle = 0;
        private bool hasFacingAngleMeasurement = false;
        private bool afkSoundPlayed = false;
        private bool punishSoundPlayed = false;
        private double lastIdleSeconds = 0d;

        private async Task InitializeOSCRepeater()
        {
            LogUi("Initializing OSC repeater integration.", LogEventLevel.Debug);
            if (File.Exists("./OscRepeater.exe"))
            {
                foreach (var processName in new[] { "OscRepeater", "OSCRepeater" })
                {
                    foreach (var existingProcess in Process.GetProcessesByName(processName))
                    {
                        try
                        {
                            if (!existingProcess.HasExited)
                            {
                                _logger?.LogEvent("OSCRepeater", "Existing OSCRepeater instance detected. Terminating before restart.");
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
                    LogUi("Selecting OSC port for repeater.", LogEventLevel.Debug);
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
                    LogUi($"OSC repeater port selected: {port}.", LogEventLevel.Debug);
                    _settings.OSCPort = port;
                    _settings.OSCPortChanged = true;
                    await _settings.SaveAsync();
                    LogUi("OSC port persisted to settings.", LogEventLevel.Debug);
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "./OscRepeater.exe";
                psi.Arguments = $"--autostart --autoconfig 127.0.0.1:{_settings.OSCPort} --minimized";
                psi.UseShellExecute = false;
                LogUi($"Starting OSC repeater process with arguments '{psi.Arguments}'.");
                oscRepeaterProcess = Process.Start(psi);
                if (oscRepeaterProcess != null)
                {
                    LogUi($"OSC repeater process started (PID: {oscRepeaterProcess.Id}).", LogEventLevel.Debug);
                    oscRepeaterProcess.EnableRaisingEvents = true;
                    oscRepeaterProcess.Exited += (s, ev) =>
                    {
                        LogUi("OSC repeater process exited. Closing application.", LogEventLevel.Warning);
                        _dispatcher.Invoke(() => { this.Close(); });
                    };
                }
                else
                {
                    LogUi("Failed to start OSC repeater process.", LogEventLevel.Error);
                }
            }
            else
            {
                LogUi("OscRepeater.exe not found. Skipping repeater startup.", LogEventLevel.Warning);
            }
        }

        private void HandleOscMessage(OscMessage message)
        {
            if (message == null)
            {
                LogUi("Received null OSC message.", LogEventLevel.Warning);
                return;
            }
            LogUi($"OSC message received: {message.Address} ({message.Count} argument(s)).", LogEventLevel.Debug);
            if (message.Address == "/avatar/parameters/VelocityMagnitude")
            {
                try
                {
                    receivedVelocityMagnitude = Convert.ToSingle(message.ToArray()[0]);
                    LogUi($"Velocity magnitude updated to {receivedVelocityMagnitude}.", LogEventLevel.Debug);
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
                    currentVelocityX = Convert.ToSingle(message.ToArray()[0]);
                    LogUi($"Velocity X updated to {currentVelocityX}.", LogEventLevel.Debug);
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
                    currentVelocityZ = Convert.ToSingle(message.ToArray()[0]);
                    LogUi($"Velocity Z updated to {currentVelocityZ}.", LogEventLevel.Debug);
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
                        suicideFlag = Convert.ToBoolean(message.ToArray()[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse suicide flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (suicideFlag)
                {
                    LogUi("Immediate suicide flag received. Executing auto suicide action.");
                    _ = Task.Run(() => PerformAutoSuicide());
                }
            }
            else if (message.Address == "/avatar/parameters/autosuside")
            {
                bool autoSuicideOSC = false;
                if (message.Count > 0)
                {
                    try
                    {
                        autoSuicideOSC = Convert.ToBoolean(message.ToArray()[0]);
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
                    UpdateShortcutOverlayState();
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
                        abortFlag = Convert.ToBoolean(message.ToArray()[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse abort flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (abortFlag)
                {
                    LogUi("Abort auto suicide requested via OSC.");
                    CancelAutoSuicide();
                }
            }
            else if (message.Address == "/avatar/parameters/delayAutoSuside")
            {
                bool delayFlag = true;
                if (message.Count > 0)
                {
                    try
                    {
                        delayFlag = Convert.ToBoolean(message.ToArray()[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse delay flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (delayFlag && autoSuicideService.HasScheduled)
                {
                    TimeSpan remaining = TimeSpan.FromSeconds(40) - (DateTime.UtcNow - autoSuicideService.RoundStartTime);
                    if (remaining > TimeSpan.Zero)
                    {
                        LogUi($"Delaying auto suicide by {remaining}.", LogEventLevel.Debug);
                        ScheduleAutoSuicide(remaining, false);
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
                        setAlertValue = Convert.ToSingle(message.ToArray()[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse alert value: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (setAlertValue != 0)
                {
                    LogUi($"Forwarding alert value {setAlertValue} to TonSprink.", LogEventLevel.Debug);
                    _ = SendAlertToTonSprinkAsync(setAlertValue);
                }
            }
            else if (message.Address == "/avatar/parameters/getlatestsavecode")
            {
                bool getLatestSaveCode = false;
                if (message.Count > 0)
                {
                    try
                    {
                        getLatestSaveCode = Convert.ToBoolean(message.ToArray()[0]);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse save code request flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                if (getLatestSaveCode)
                {
                    LogUi("OSC request received for latest save code.");
                    if (string.IsNullOrEmpty(_settings.apikey))
                    {
                        CopyCachedSaveCode("API key is not configured");
                    }
                    else
                    {
                        _ = Task.Run(async () =>
                        {
                            using (var client = new HttpClient())
                            {
                                client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/savecode/get/" + _settings.apikey + "/latest");
                                try
                                {
                                    var response = await client.GetAsync("");
                                    if (response.IsSuccessStatusCode)
                                    {
                                        var jsonResponse = await response.Content.ReadAsStringAsync();
                                        var json = JObject.Parse(jsonResponse);
                                        string saveCode = json.Value<string>("savecode") ?? "";
                                        await PersistLastSaveCodeAsync(saveCode).ConfigureAwait(false);
                                        CopySaveCodeToClipboard(saveCode);
                                        _logger.LogEvent("SaveCode", "Latest save code copied to clipboard: " + saveCode);
                                    }
                                    else if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 600)
                                    {
                                        CopyCachedSaveCode($"Failed to get latest save code: {response.StatusCode}");
                                    }
                                    else
                                    {
                                        _logger.LogEvent("SaveCodeError", $"Failed to get latest save code: {response.StatusCode}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    CopyCachedSaveCode($"Exception occurred: {ex.Message}");
                                }
                            }
                        });
                    }
                }
            }
            else if (message.Address == "/avatar/parameters/isAllSelfKill")
            {
                if (message.Count > 0)
                {
                    try
                    {
                        issetAllSelfKillMode = Convert.ToBoolean(message.ToArray()[0]);
                        LogUi($"Received 'isAllSelfKill' flag: {issetAllSelfKillMode}.", LogEventLevel.Debug);
                        UpdateShortcutOverlayState();
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
                        followAutoSelfKill = Convert.ToBoolean(message.ToArray()[0]);
                        LogUi($"Received 'followAutoSelfKill' flag: {followAutoSelfKill}.", LogEventLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to parse followAutoSelfKill flag: {ex.Message}", LogEventLevel.Warning);
                    }
                }
            }

            currentVelocity = Math.Abs(receivedVelocityMagnitude);
            float planarMagnitudeSquared = (currentVelocityX * currentVelocityX) + (currentVelocityZ * currentVelocityZ);
            if (planarMagnitudeSquared > 0.0001f)
            {
                double rawAngle = Math.Atan2(currentVelocityX, currentVelocityZ) * (180.0 / Math.PI);
                double normalizedAngle = (rawAngle + 360.0) % 360.0;

                lastKnownFacingAngle = (float)normalizedAngle;
                hasFacingAngleMeasurement = true;
            }

            _logger.LogEvent("Receive: ", $"{message.Address} => Computed Velocity: {currentVelocity:F2}");
            _dispatcher.Invoke(() =>
            {
                string angleText = GetOverlayAngleDisplayText();
                lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2} (Angle: {angleText})  Members: {connected}";
                UpdateVelocityOverlay();
                UpdateOverlay(OverlaySection.Angle, form =>
                {
                    if (form is OverlayAngleForm angleForm)
                    {
                        angleForm.SetAngle(lastKnownFacingAngle);
                        angleForm.SetValue(angleText);
                    }
                    else
                    {
                        form.SetValue(angleText);
                    }
                });
            });
        }

        private void CopySaveCodeToClipboard(string saveCode)
        {
            Thread thread = new Thread(() =>
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
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
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
