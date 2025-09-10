using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Handles WebSocket connections and message dispatching.
    /// </summary>
    public class WebSocketClient : IWebSocketClient
    {
        private readonly Uri _uri;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

        public WebSocketClient(string url, IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
        }

        public async Task StartAsync()
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            while (!_cts.Token.IsCancellationRequested)
            {
                _socket = new ClientWebSocket();
                try
                {
                    await _socket.ConnectAsync(_uri, _cts.Token);
                    _bus.Publish(new WebSocketConnected());
                    _ = Task.Run(ProcessMessagesAsync, _cts.Token);
                    await ReceiveLoopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("WebSocket", ex.Message, Serilog.Events.LogEventLevel.Error);
                    _bus.Publish(new WebSocketDisconnected());
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
                    await _channel.Writer.WriteAsync(message, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("WebSocketReceive", ex.Message, Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                _bus.Publish(new WebSocketDisconnected());
            }
        }

        private async Task ProcessMessagesAsync()
        {
            try
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    _bus.Publish(new WebSocketMessageReceived(msg));
                }
            }
            catch (OperationCanceledException) { }
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
