using System;
using System.Buffers;
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
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
        private Task? _processingTask;
        private int _connectionAttempts;
        private long _receivedMessages;

        public WebSocketClient(string url, IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
        }

        public async Task StartAsync()
        {
            _logger.LogEvent("WebSocket", $"Starting client for {_uri}.");
            _connectionAttempts = 0;
            _receivedMessages = 0;
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
                        _connectionAttempts++;
                        _logger.LogEvent("WebSocket", $"Attempt {_connectionAttempts}: connecting to {_uri}.");
                        _bus.Publish(new WebSocketConnecting(_uri));
                        await _socket.ConnectAsync(_uri, token);
                        _logger.LogEvent("WebSocket", "Connection established.");
                        _bus.Publish(new WebSocketConnected(_uri));
                        _processingTask ??= Task.Run(() => ProcessMessagesAsync(token), token);
                        await ReceiveLoopAsync(token);
                        _logger.LogEvent("WebSocket", "Receive loop completed.");
                        _bus.Publish(new WebSocketDisconnected(_uri));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("WebSocket", ex.Message, Serilog.Events.LogEventLevel.Error);
                        _bus.Publish(new WebSocketDisconnected(_uri, ex));
                        if (!token.IsCancellationRequested)
                        {
                            _bus.Publish(new WebSocketReconnecting(_uri, ex));
                            _logger.LogEvent("WebSocket", "Scheduling reconnect in 300ms.");
                            await Task.Delay(300, token);
                        }
                    }
                    finally
                    {
                        _logger.LogEvent("WebSocket", "Disposing socket instance.");
                        _socket?.Dispose();
                    }
                }
            }
            finally
            {
                _logger.LogEvent("WebSocket", "StartAsync exiting and disposing cancellation token source.");
                cts.Dispose();
                _cts = null;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var socket = _socket;
            if (socket == null)
            {
                return;
            }
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await socket.ReceiveAsync(segment, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogEvent("WebSocket", $"Close message received: {result.CloseStatus} {result.CloseStatusDescription}");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        break;
                    }
                    string message;
                    if (result.EndOfMessage)
                    {
                        message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else
                    {
                        var totalBytes = result.Count;
                        byte[] currentMessageBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(totalBytes, buffer.Length));
                        try
                        {
                            Buffer.BlockCopy(buffer, 0, currentMessageBuffer, 0, result.Count);

                            while (!result.EndOfMessage)
                            {
                                result = await socket.ReceiveAsync(segment, token);
                                var requiredLength = totalBytes + result.Count;
                                if (requiredLength > currentMessageBuffer.Length)
                                {
                                    var newBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(currentMessageBuffer.Length * 2, requiredLength));
                                    Buffer.BlockCopy(currentMessageBuffer, 0, newBuffer, 0, totalBytes);
                                    ArrayPool<byte>.Shared.Return(currentMessageBuffer);
                                    currentMessageBuffer = newBuffer;
                                }

                                Buffer.BlockCopy(buffer, 0, currentMessageBuffer, totalBytes, result.Count);
                                totalBytes += result.Count;
                            }

                            message = Encoding.UTF8.GetString(currentMessageBuffer, 0, totalBytes);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(currentMessageBuffer);
                        }
                    }
                    await _channel.Writer.WriteAsync(message, token);
                    _receivedMessages++;
                    _logger.LogEvent("WebSocket", $"Received message #{_receivedMessages}: {Truncate(message, 200)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("WebSocketReceive", ex.Message, Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task ProcessMessagesAsync(CancellationToken token)
        {
            try
            {
                long dispatched = 0;
                await foreach (var msg in _channel.Reader.ReadAllAsync(token))
                {
                    _bus.Publish(new WebSocketMessageReceived(msg));
                    dispatched++;
                    _logger.LogEvent("WebSocket", $"Dispatched message #{dispatched} to event bus.");
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _logger.LogEvent("WebSocket", "ProcessMessagesAsync completed.");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _logger.LogEvent("WebSocket", "StopAsync invoked.");
                _cts?.Cancel();
                if (_processingTask != null)
                {
                    try { await _processingTask.ConfigureAwait(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("WebSocket", $"Stop error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                _logger.LogEvent("WebSocket", "StopAsync cleaning up resources.");
                _cts?.Dispose();
                _cts = null;
                _socket?.Dispose();
            }
        }

        public void Dispose()
        {
            _logger.LogEvent("WebSocket", "Dispose called.");
            _ = StopAsync();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "…";
        }
    }
}
