using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rug.Osc;
using Serilog.Events;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private ClientWebSocket? instanceWsConnection;
        private static readonly SemaphoreSlim sendAlertSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _instanceConnectionSync = new object();
        private Task? _instanceReconnectTask;
        private int connected = 0;
        private bool followAutoSelfKill = false;

        private async Task ConnectToInstance(string instanceValue)
        {
            if (_cancellation.Token.IsCancellationRequested)
            {
                return;
            }

            // NOTE: Remote instance join/leave removed due to VRChat platform constraints
            // VRChat does not allow external applications to control world joining
            // Players must manually join worlds through VRChat client
            // 
            // Cloud sync is still active for:
            // - Game lifecycle logging (round start/end)
            // - Player state synchronization
            // - Statistics collection
            // - Voting system coordination

            // Legacy external backend has been retired; use configured cloud endpoint if needed.
            string url = string.IsNullOrWhiteSpace(_settings.CloudWebSocketUrl)
                ? "ws://localhost:3000/ws"
                : _settings.CloudWebSocketUrl;
            var socket = new ClientWebSocket();
            var previousSocket = Interlocked.Exchange(ref instanceWsConnection, socket);
            previousSocket?.Dispose();
            try
            {
                LogUi($"Connecting to shared instance stream at {url}.");
                await socket.ConnectAsync(new Uri(url), _cancellation.Token);
                LogUi("Instance WebSocket connection established.", LogEventLevel.Debug);
                while (socket.State == WebSocketState.Open)
                {
                    var buffer = new byte[8192];
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        LogUi("Instance WebSocket close frame received. Closing connection.", LogEventLevel.Debug);
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else
                    {
                        var messageBytes = new List<byte>();
                        messageBytes.AddRange(buffer.Take(result.Count));
                        while (!result.EndOfMessage)
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                            messageBytes.AddRange(buffer.Take(result.Count));
                        }
                        string msg = Encoding.UTF8.GetString(messageBytes.ToArray());
                        LogUi($"Instance WebSocket message received ({msg.Length} chars).", LogEventLevel.Debug);
                        ProcessInstanceMessage(msg);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogUi("Instance WebSocket connection cancelled.", LogEventLevel.Debug);
            }
            catch (Exception ex)
            {
                LogUi($"Instance WebSocket connection failed: {ex.Message}", LogEventLevel.Error);
                if (_logger != null)
                {
                    _logger.LogEvent("InstanceError", ex.ToString(), Serilog.Events.LogEventLevel.Error);
                }
            }
            finally
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); } catch { }
                }

                socket.Dispose();

                Interlocked.CompareExchange(ref instanceWsConnection, null, socket);

                if (!_cancellation.Token.IsCancellationRequested)
                {
                    LogUi("Instance WebSocket connection disposed. Scheduling reconnect.", LogEventLevel.Warning);
                    ScheduleInstanceReconnect(instanceValue);
                }
            }
        }

        private void ScheduleInstanceReconnect(string instanceValue)
        {
            lock (_instanceConnectionSync)
            {
                if (_instanceReconnectTask != null && !_instanceReconnectTask.IsCompleted)
                {
                    return;
                }

                _instanceReconnectTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(300, _cancellation.Token);
                        if (!_cancellation.Token.IsCancellationRequested)
                        {
                            await ConnectToInstance(instanceValue);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        lock (_instanceConnectionSync)
                        {
                            _instanceReconnectTask = null;
                        }
                    }
                });
            }
        }

        private void ProcessInstanceMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                string type = json.Value<string>("type") ?? "";
                LogUi($"Processing instance message of type '{type}'.", LogEventLevel.Debug);
                _logger.LogEvent("ReceivedWSType", type);
                if (type == "JoinedMember" || type == "LeavedMember")
                {
                    connected = json.Value<int>("connected");
                    _dispatcher.Invoke(() =>
                    {
                        lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}  Members: {connected}";
                    });
                }
                else if (type == "alertIncoming")
                {
                    LogUi("Instance alert incoming event received.");
                    _logger.LogEvent("alertIncoming", "start process");
                    float alertNum = json.Value<float>("alertNum");
                    bool isLocal = json.Value<bool>("isLocal");
                    RunBackgroundOperation(() => SendAlertOscMessagesAsync(alertNum, isLocal), "InstanceAlertOsc", LogEventLevel.Debug);
                }
                else if (type == "performFollowAutoSucide")
                {
                    if (followAutoSelfKill)
                    {
                        RunBackgroundOperation(() => PerformAutoSuicide(), "FollowAutoSuicide", LogEventLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUi($"Failed to process instance message: {ex.Message}", LogEventLevel.Error);
                _logger.LogEvent("InstanceProcessError", ex.ToString(), Serilog.Events.LogEventLevel.Error);
            }
        }

        private async Task SendAlertOscMessagesAsync(float alertNum, bool isLocal = true)
        {
            LogUi($"Sending OSC alert sequence (Value: {alertNum}, Local: {isLocal}).", LogEventLevel.Debug);
            await sendAlertSemaphore.WaitAsync();
            try
            {
                _logger.LogEvent("SendAlertOscMessagesAsync", "start process");
                using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 0, 9000))
                {
                    string switchString = isLocal ? "_Local" : "";
                    _logger.LogEvent("SendAlertOscMessagesAsync", "start connect");
                    sender.Connect();
                    _logger.LogEvent("SendAlertOscMessagesAsync", "connected");
                    DateTime startTime = DateTime.Now;
                    bool sendAlert = true;
                    _logger.LogEvent("SendAlertOscMessagesAsync", "start send");
                    _soundManager.PlayNotification();
                    while ((DateTime.Now - startTime).TotalSeconds < 2)
                    {
                        _logger.LogEvent("SendAlertOscMessagesAsync", "send " + sendAlert.ToString());
                        var msg = new OscMessage("/avatar/parameters/alert" + switchString, sendAlert ? alertNum : 0);
                        sender.Send(msg);
                        sendAlert = !sendAlert;
                        await Task.Delay(250);
                    }
                    _logger.LogEvent("SendAlertOscMessagesAsync", "send 0");
                    var msg0 = new OscMessage("/avatar/parameters/alert" + switchString, 0f);
                    sender.Send(msg0);
                    _logger.LogEvent("SendAlertOscMessagesAsync", "closing");
                    sender.Close();
                    _logger.LogEvent("SendAlertOscMessagesAsync", "closed");
                }
            }
            finally
            {
                LogUi("OSC alert sequence completed.", LogEventLevel.Debug);
                sendAlertSemaphore.Release();
            }
        }

        private async Task disableAutoFollofSelfKillOscMessagesAsync()
        {
            LogUi("Disabling follow auto self kill via OSC.", LogEventLevel.Debug);
            await sendAlertSemaphore.WaitAsync();
            try
            {
                _logger.LogEvent("disableAutoFollofSelfKillOscMessagesAsync", "start process");
                using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 0, 9000))
                {
                    _logger.LogEvent("disableAutoFollofSelfKillOscMessagesAsync", "send false");
                    sender.Connect();
                    var msg0 = new OscMessage("/avatar/parameters/followAutoSelfKill", false);
                    sender.Send(msg0);
                    _logger.LogEvent("disableAutoFollofSelfKillOscMessagesAsync", "closing");
                    sender.Close();
                    _logger.LogEvent("disableAutoFollofSelfKillOscMessagesAsync", "closed");
                }
            }
            finally
            {
                LogUi("Follow auto self kill disable sequence completed.", LogEventLevel.Debug);
                sendAlertSemaphore.Release();
            }
        }

        private async Task SendPieSizeOscMessagesAsync(float piesizetNum, bool isLocal = true)
        {
            LogUi($"Sending pie size OSC sequence (Value: {piesizetNum}, Local: {isLocal}).", LogEventLevel.Debug);
            await sendAlertSemaphore.WaitAsync();
            try
            {
                _logger.LogEvent("SendPieSizeOscMessagesAsync", "start process");
                using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 0, 9000))
                {
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "start connect");
                    sender.Connect();
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "connected");
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "start send");
                    float normalizedPieSize = piesizetNum / 20f;
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "send " + normalizedPieSize);
                    var msg = new OscMessage("/avatar/parameters/Breast_size", normalizedPieSize);
                    sender.Send(msg);
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "closing");
                    sender.Close();
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "closed");
                }
            }
            finally
            {
                LogUi("Pie size OSC sequence completed.", LogEventLevel.Debug);
                sendAlertSemaphore.Release();
            }
        }

        private async Task SendAlertToCloudAsync(float alertNum)
        {
            if (_settings.CloudSyncEnabled && _cloudClient != null && _cloudClient.IsConnected && !string.IsNullOrWhiteSpace(currentInstanceId))
            {
                LogUi($"Forwarding alert value {alertNum} to integrated cloud backend.", LogEventLevel.Debug);
                var payload = new JObject
                {
                    ["source"] = "osc",
                    ["value"] = alertNum,
                    ["isLocal"] = true
                };

                try
                {
                    await _cloudClient.InstanceAlertAsync(
                        currentInstanceId,
                        "osc-alert",
                        payload.ToString(Newtonsoft.Json.Formatting.None),
                        _cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to forward alert to integrated cloud backend: {ex.Message}", LogEventLevel.Warning);
                    _logger.LogEvent("AlertSendError", $"Integrated cloud alert send failed: {ex.Message}");
                }
            }
            else
            {
                LogUi("Unable to forward alert because cloud connection or instance context is unavailable.", LogEventLevel.Warning);
                _logger.LogEvent("AlertSendError", "Cloud client is not available or not connected.");
            }
        }
    }
}
