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
        private const int ReceiveBufferSize = 8192;
        private const int MaxMessagePreviewLength = 200;
        private const int MessageChannelCapacity = 256;

        private readonly Uri _uri;
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _channel;
        private Task? _processingTask;
        private int _connectionAttempts;
        private long _receivedMessages;

        public WebSocketClient(string url, IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
            _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MessageChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public async Task StartAsync()
        {
            _logger.LogEvent("WebSocket", () => $"Starting client for {_uri}.");
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
                        _logger.LogEvent("WebSocket", () => $"Attempt {_connectionAttempts}: connecting to {_uri}.");
                        _bus.Publish(new WebSocketConnecting(_uri));
                        await _socket.ConnectAsync(_uri, token).ConfigureAwait(false);
                        _logger.LogEvent("WebSocket", "Connection established.");
                        _bus.Publish(new WebSocketConnected(_uri));
                        _processingTask ??= Task.Run(() => ProcessMessagesAsync(token), token);
                        await ReceiveLoopAsync(token).ConfigureAwait(false);
                        _logger.LogEvent("WebSocket", "Receive loop completed.");
                        _bus.Publish(new WebSocketDisconnected(_uri));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("WebSocket", () => ex.Message, Serilog.Events.LogEventLevel.Error);
                        _bus.Publish(new WebSocketDisconnected(_uri, ex));
                        if (!token.IsCancellationRequested)
                        {
                            _bus.Publish(new WebSocketReconnecting(_uri, ex));
                            _logger.LogEvent("WebSocket", "Scheduling reconnect in 300ms.");
                            await Task.Delay(300, token).ConfigureAwait(false);
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

            var receiveBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            byte[]? messageBuffer = null;
            var messageOffset = 0;

            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(receiveBuffer);
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(segment, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogEvent("WebSocket", () => $"Close message received: {result.CloseStatus} {result.CloseStatusDescription}");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token).ConfigureAwait(false);
                        break;
                    }

                    if (result.EndOfMessage && messageOffset == 0)
                    {
                        var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        await DispatchMessageAsync(message, token).ConfigureAwait(false);
                        continue;
                    }

                    messageBuffer ??= ArrayPool<byte>.Shared.Rent(Math.Max(ReceiveBufferSize, result.Count));
                    EnsureBufferCapacity(ref messageBuffer, messageOffset + result.Count, messageOffset);
                    Buffer.BlockCopy(receiveBuffer, 0, messageBuffer, messageOffset, result.Count);
                    messageOffset += result.Count;

                    if (result.EndOfMessage)
                    {
                        var message = Encoding.UTF8.GetString(messageBuffer, 0, messageOffset);
                        messageOffset = 0;
                        await DispatchMessageAsync(message, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("WebSocketReceive", () => ex.Message, Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(receiveBuffer);
                if (messageBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(messageBuffer);
                }
            }
        }

        private async ValueTask DispatchMessageAsync(string message, CancellationToken token)
        {
            await _channel.Writer.WriteAsync(message, token).ConfigureAwait(false);
            var messageNumber = Interlocked.Increment(ref _receivedMessages);
            _logger.LogEvent("WebSocket", () => $"Received message #{messageNumber}: {Truncate(message, MaxMessagePreviewLength)}");
        }

        private static void EnsureBufferCapacity(ref byte[] buffer, int requiredLength, int preservedLength)
        {
            if (buffer.Length >= requiredLength)
            {
                return;
            }

            var newBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(buffer.Length * 2, requiredLength));
            if (preservedLength > 0)
            {
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, preservedLength);
            }

            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
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
                    _logger.LogEvent("WebSocket", () => $"Dispatched message #{dispatched} to event bus.");
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
                _logger.LogEvent("WebSocket", () => $"Stop error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
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
