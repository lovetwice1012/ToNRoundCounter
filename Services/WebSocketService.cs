using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Services
{
    /// <summary>
    /// Handles WebSocket connections and message dispatching.
    /// </summary>
    public class WebSocketService
    {
        private readonly Uri _uri;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<string> MessageReceived;

        public WebSocketService(string url)
        {
            _uri = new Uri(url);
        }

        public async Task StartAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            while (!_cts.Token.IsCancellationRequested)
            {
                _socket = new ClientWebSocket();
                try
                {
                    await _socket.ConnectAsync(_uri, _cts.Token);
                    Connected?.Invoke();
                    await ReceiveLoopAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WebSocket connection error: {ex.Message}");
                    Disconnected?.Invoke();
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(300, _cts.Token);
                    }
                }
                finally
                {
                    _socket?.Dispose();
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            try
            {
                while (_socket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
                        break;
                    }
                    var messageBytes = new List<byte>();
                    messageBytes.AddRange(buffer.Take(result.Count));
                    while (!result.EndOfMessage)
                    {
                        result = await _socket.ReceiveAsync(segment, _cts.Token);
                        messageBytes.AddRange(buffer.Take(result.Count));
                    }
                    var message = Encoding.UTF8.GetString(messageBytes.ToArray());
                    MessageReceived?.Invoke(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket receive error: {ex.Message}");
            }
            finally
            {
                Disconnected?.Invoke();
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket stop error: {ex.Message}");
            }
            _socket?.Dispose();
        }
    }
}
