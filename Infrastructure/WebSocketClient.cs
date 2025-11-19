using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Runtime.ExceptionServices;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Handles WebSocket connections and message dispatching.
    /// </summary>
    public class WebSocketClient : IWebSocketClient, IDisposable, IAsyncDisposable
    {
        private const int ReceiveBufferSize = 8192;
        private const int MaxMessagePreviewLength = 200;
        private const int MaxReconnectDelayMs = 30000; // 30 seconds
        private const int InitialReconnectDelayMs = 1000; // 1 second
        private const double BackoffMultiplier = 1.5;

        private readonly Uri _uri;
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _channel;
        private Task? _processingTask;
        private int _connectionAttempts;
        private int _consecutiveFailures;
        private long _receivedMessages;

        public WebSocketClient(string url, IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
        }

        public async Task StartAsync()
        {
            _logger.LogEvent("WebSocket", () => $"Starting client for {_uri}.");
            _connectionAttempts = 0;
            _consecutiveFailures = 0;
            _receivedMessages = 0;
            _cts?.Dispose();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            _cts = cts;
            var token = cts.Token;
            EnsureProcessingTask(token);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    EnsureProcessingTask(token);
                    _socket = new ClientWebSocket();
                    try
                    {
                        _connectionAttempts++;
                        _logger.LogEvent("WebSocket", () => $"Attempt {_connectionAttempts}: connecting to {_uri}.");
                        _bus.Publish(new WebSocketConnecting(_uri));
                        await _socket.ConnectAsync(_uri, token).ConfigureAwait(false);
                        _logger.LogEvent("WebSocket", "Connection established.");
                        _consecutiveFailures = 0; // Reset on successful connection
                        _bus.Publish(new WebSocketConnected(_uri));
                        await ReceiveLoopAsync(token).ConfigureAwait(false);
                        _logger.LogEvent("WebSocket", "Receive loop completed.");
                        _bus.Publish(new WebSocketDisconnected(_uri));
                    }
                    catch (Exception ex)
                    {
                        _consecutiveFailures++;
                        _logger.LogEvent("WebSocket", () => ex.Message, Serilog.Events.LogEventLevel.Error);
                        _bus.Publish(new WebSocketDisconnected(_uri, ex));
                        if (!token.IsCancellationRequested)
                        {
                            var delayMs = CalculateBackoffDelay(_consecutiveFailures);
                            _bus.Publish(new WebSocketReconnecting(_uri, ex));
                            _logger.LogEvent("WebSocket", $"Scheduling reconnect in {delayMs}ms (failure #{_consecutiveFailures}).");
                            await Task.Delay(delayMs, token).ConfigureAwait(false);
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
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
                cts.Dispose();
            }
        }

        private int CalculateBackoffDelay(int failureCount)
        {
            // Exponential backoff: initialDelay * (multiplier ^ (failureCount - 1))
            // Capped at MaxReconnectDelayMs
            var delay = InitialReconnectDelayMs * Math.Pow(BackoffMultiplier, failureCount - 1);
            return (int)Math.Min(delay, MaxReconnectDelayMs);
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
            var debugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
            if (debugLoggingEnabled && ShouldLogSample(messageNumber))
            {
                _logger.LogEvent("WebSocket", () => $"Received message #{messageNumber}: {Truncate(message, MaxMessagePreviewLength)}", Serilog.Events.LogEventLevel.Debug);
            }
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
                var debugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
                await foreach (var msg in _channel.Reader.ReadAllAsync(token))
                {
                    _bus.Publish(new WebSocketMessageReceived(msg));
                    dispatched++;
                    if (debugLoggingEnabled && ShouldLogSample(dispatched))
                    {
                        var capturedCount = dispatched;
                        _logger.LogEvent("WebSocket", () => $"Dispatched message #{capturedCount} to event bus.", Serilog.Events.LogEventLevel.Debug);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _logger.LogEvent("WebSocket", "ProcessMessagesAsync completed.");
            }
        }

        private static bool ShouldLogSample(long count)
        {
            return count <= 5 || count % 50 == 0;
        }

        public async Task StopAsync()
        {
            _logger.LogEvent("WebSocket", "StopAsync invoked.");

            var cts = Interlocked.Exchange(ref _cts, null);
            Task? processingTask = Interlocked.Exchange(ref _processingTask, null);
            try
            {
                try
                {
                    cts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                Exception? processingError = null;
                if (processingTask != null)
                {
                    try
                    {
                        await processingTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        processingError = ex;
                        _logger.LogEvent("WebSocket", () => $"Stop processing error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
                    }
                }

                while (_channel.Reader.TryRead(out _))
                {
                }

                if (processingError != null)
                {
                    ExceptionDispatchInfo.Capture(processingError).Throw();
                }
            }
            finally
            {
                _logger.LogEvent("WebSocket", "StopAsync cleaning up resources.");
                cts?.Dispose();
                _socket?.Dispose();
                _socket = null;
            }
        }

        public void Dispose()
        {
            _logger.LogEvent("WebSocket", "Dispose called.");
            try
            {
                // Note: Prefer DisposeAsync() when possible. Synchronous disposal blocks on async cleanup.
                StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogEvent("WebSocket", () => $"Dispose error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _logger.LogEvent("WebSocket", "DisposeAsync called.");
            await StopAsync().ConfigureAwait(false);
        }

        private void EnsureProcessingTask(CancellationToken token)
        {
            var existing = _processingTask;
            if (existing == null || existing.IsCompleted)
            {
                _processingTask = Task.Run(() => ProcessMessagesAsync(token), token);
            }
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
