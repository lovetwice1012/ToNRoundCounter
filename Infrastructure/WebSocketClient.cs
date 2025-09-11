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
    public class WebSocketClient : IWebSocketClient, IDisposable
    {
        private readonly Uri _uri;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
        private Task? _processingTask;

        public WebSocketClient(string url, IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
        }

        public async Task StartAsync()
        {
            _cts?.Dispose();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            _cts = cts;
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    _socket = new ClientWebSocket();
                    try
                    {
                        await _socket.ConnectAsync(_uri, token);
                        _bus.Publish(new WebSocketConnected());
                        _processingTask ??= Task.Run(() => ProcessMessagesAsync(token), token);
                        await ReceiveLoopAsync(token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("WebSocket", ex.Message, Serilog.Events.LogEventLevel.Error);
                        _bus.Publish(new WebSocketDisconnected());
                        if (!token.IsCancellationRequested)
                        {
                            await Task.Delay(300, token);
                        }
                    }
                    finally
                    {
                        _socket?.Dispose();
                    }
                }
            }
            finally
            {
                cts.Dispose();
                _cts = null;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _socket.ReceiveAsync(segment, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        break;
                    }
                    var messageBytes = new List<byte>();
                    messageBytes.AddRange(buffer.Take(result.Count));
                    while (!result.EndOfMessage)
                    {
                        result = await _socket.ReceiveAsync(segment, token);
                        messageBytes.AddRange(buffer.Take(result.Count));
                    }
                    var message = Encoding.UTF8.GetString(messageBytes.ToArray());
                    await _channel.Writer.WriteAsync(message, token);
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

        private async Task ProcessMessagesAsync(CancellationToken token)
        {
            try
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync(token))
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
                _processingTask?.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket stop error: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _socket?.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
