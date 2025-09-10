using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
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
    }
}
