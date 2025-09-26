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
        private ClientWebSocket instanceWsConnection = null;
        private static readonly SemaphoreSlim sendAlertSemaphore = new SemaphoreSlim(1, 1);
        private int connected = 0;
        private bool followAutoSelfKill = false;

        private async Task ConnectToInstance(string instanceValue)
        {
            string url = $"ws://xy.f5.si:8880/ToNRoundCounter/{instanceValue}";
            instanceWsConnection = new ClientWebSocket();
            try
            {
                LogUi($"Connecting to shared instance stream at {url}.");
                await instanceWsConnection.ConnectAsync(new Uri(url), _cancellation.Token);
                LogUi("Instance WebSocket connection established.", LogEventLevel.Debug);
                while (instanceWsConnection.State == WebSocketState.Open)
                {
                    var buffer = new byte[8192];
                    WebSocketReceiveResult result = await instanceWsConnection.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        LogUi("Instance WebSocket close frame received. Closing connection.", LogEventLevel.Debug);
                        await instanceWsConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cancellation.Token);
                        break;
                    }
                    else
                    {
                        var messageBytes = new List<byte>();
                        messageBytes.AddRange(buffer.Take(result.Count));
                        while (!result.EndOfMessage)
                        {
                            result = await instanceWsConnection.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                            messageBytes.AddRange(buffer.Take(result.Count));
                        }
                        string msg = Encoding.UTF8.GetString(messageBytes.ToArray());
                        LogUi($"Instance WebSocket message received ({msg.Length} chars).", LogEventLevel.Debug);
                        ProcessInstanceMessage(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUi($"Instance WebSocket connection failed: {ex.Message}", LogEventLevel.Error);
                _logger.LogEvent("InstanceError", ex.ToString(), Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                if (instanceWsConnection != null)
                {
                    try { await instanceWsConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cancellation.Token); } catch { }
                    instanceWsConnection.Dispose();
                    instanceWsConnection = null;
                    LogUi("Instance WebSocket connection disposed. Scheduling reconnect.", LogEventLevel.Warning);
                    await Task.Delay(300);
                    _ = Task.Run(() => ConnectToInstance(instanceValue));
                }
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
                    using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 9000))
                    {
                        _logger.LogEvent("alertIncoming", "start process");
                        float alertNum = json.Value<float>("alertNum");
                        bool isLocal = json.Value<bool>("isLocal");
                        _ = Task.Run(() => SendAlertOscMessagesAsync(alertNum, isLocal));
                    }
                }
                else if (type == "performFollowAutoSucide")
                {
                    if (followAutoSelfKill)
                    {
                        _ = Task.Run(() => PerformAutoSuicide());
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
                    PlayFromStart(notifyPlayer);
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
                    string switchString = isLocal ? "_Local" : "";
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "start connect");
                    sender.Connect();
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "connected");
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "start send");
                    _logger.LogEvent("SendPieSizeOscMessagesAsync", "send " + piesizetNum * 1 / 20);
                    var msg = new OscMessage("/avatar/parameters/Breast_size", piesizetNum * 1 / 20);
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

        private async Task SendAlertToTonSprinkAsync(float alertNum)
        {
            if (instanceWsConnection != null && instanceWsConnection.State == WebSocketState.Open)
            {
                LogUi($"Forwarding alert value {alertNum} to TonSprink backend.", LogEventLevel.Debug);
                var jsonMessage = new JObject
                {
                    ["type"] = "Alert",
                    ["alertNum"] = alertNum
                };
                string message = jsonMessage.ToString();
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                await instanceWsConnection.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellation.Token);
            }
            else
            {
                LogUi("Unable to forward alert to TonSprink because instance connection is unavailable.", LogEventLevel.Warning);
                _logger.LogEvent("AlertSendError", "Instance WebSocket connection is not available.");
            }
        }
    }
}
