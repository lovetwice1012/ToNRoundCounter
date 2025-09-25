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

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private float currentVelocity = 0;
        private DateTime velocityInRangeStart = DateTime.MinValue;
        private DateTime idleStartTime = DateTime.MinValue;
        private System.Windows.Forms.Timer velocityTimer; // Windows.Forms.Timer
        private float receivedVelocityMagnitude = 0;
        private bool afkSoundPlayed = false;
        private bool punishSoundPlayed = false;

        private async Task InitializeOSCRepeater()
        {
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
                    _settings.OSCPort = port;
                    _settings.OSCPortChanged = true;
                    await _settings.SaveAsync();
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "./OscRepeater.exe";
                psi.Arguments = $"--autostart --autoconfig 127.0.0.1:{_settings.OSCPort} --minimized";
                psi.UseShellExecute = false;
                oscRepeaterProcess = Process.Start(psi);
                if (oscRepeaterProcess != null)
                {
                    oscRepeaterProcess.EnableRaisingEvents = true;
                    oscRepeaterProcess.Exited += (s, ev) =>
                    {
                        _dispatcher.Invoke(() => { this.Close(); });
                    };
                }
            }
        }

        private void HandleOscMessage(OscMessage message)
        {
            if (message == null) return;
            if (message.Address == "/avatar/parameters/VelocityMagnitude")
            {
                try { receivedVelocityMagnitude = Convert.ToSingle(message.ToArray()[0]); } catch { }
            }
            else if (message.Address == "/avatar/parameters/suside")
            {
                bool suicideFlag = false;
                if (message.Count > 0)
                {
                    try { suicideFlag = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                if (suicideFlag)
                {
                    _ = Task.Run(() => PerformAutoSuicide());
                }
            }
            else if (message.Address == "/avatar/parameters/autosuside")
            {
                bool autoSuicideOSC = false;
                if (message.Count > 0)
                {
                    try { autoSuicideOSC = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                    _settings.AutoSuicideEnabled = autoSuicideOSC;
                }
            }
            else if (message.Address == "/avatar/parameters/abortAutoSuside")
            {
                bool abortFlag = true;
                if (message.Count > 0)
                {
                    try { abortFlag = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                if (abortFlag)
                {
                    autoSuicideService.Cancel();
                }
            }
            else if (message.Address == "/avatar/parameters/delayAutoSuside")
            {
                bool delayFlag = true;
                if (message.Count > 0)
                {
                    try { delayFlag = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                if (delayFlag && autoSuicideService.HasScheduled)
                {
                    TimeSpan remaining = TimeSpan.FromSeconds(40) - (DateTime.UtcNow - autoSuicideService.RoundStartTime);
                    if (remaining > TimeSpan.Zero)
                    {
                        autoSuicideService.Schedule(remaining, false, PerformAutoSuicide);
                    }
                }
            }
            else if (message.Address == "/avatar/parameters/setalert")
            {
                float setAlertValue = 0;
                if (message.Count > 0)
                {
                    try { setAlertValue = Convert.ToSingle(message.ToArray()[0]); } catch { }
                }
                if (setAlertValue != 0)
                {
                    _ = SendAlertToTonSprinkAsync(setAlertValue);
                }
            }
            else if (message.Address == "/avatar/parameters/getlatestsavecode")
            {
                bool getLatestSaveCode = false;
                if (message.Count > 0)
                {
                    try { getLatestSaveCode = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                if (getLatestSaveCode)
                {
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
                    try { issetAllSelfKillMode = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
            }
            else if (message.Address == "/avatar/parameters/followAutoSelfKill")
            {
                if (message.Count > 0)
                {
                    try { followAutoSelfKill = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
            }

            currentVelocity = Math.Abs(receivedVelocityMagnitude);
            _logger.LogEvent("Receive: ", $"{message.Address} => Computed Velocity: {currentVelocity:F2}");
            _dispatcher.Invoke(() =>
            {
                lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}  Members: {connected}";
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
