using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Rug.Osc;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// OSC listener implementation.
    /// </summary>
    public class OSCListener : IOSCListener, IDisposable
    {
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellation;
        private readonly IEventLogger _logger;
        private readonly Channel<OscMessage> _channel;
        private readonly object _lifecycleLock = new();
        private CancellationTokenSource? _listenerCts;
        private Task? _processingTask;
        private Task? _listenerTask;

        public OSCListener(IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
            _channel = Channel.CreateBounded<OscMessage>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public Task StartAsync(int port)
        {
            _logger.LogEvent("OSC", $"Starting OSC listener on port {port}.");

            lock (_lifecycleLock)
            {
                if (_listenerTask != null)
                {
                    if (!_listenerTask.IsCompleted)
                    {
                        _logger.LogEvent("OSC", "Start requested while listener is already running.", Serilog.Events.LogEventLevel.Debug);
                        return _listenerTask;
                    }

                    _listenerTask = null;
                }

                if (_processingTask != null && _processingTask.IsCompleted)
                {
                    _processingTask = null;
                }

                if (_listenerCts != null)
                {
                    try
                    {
                        _listenerCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    _listenerCts.Dispose();
                    _listenerCts = null;
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                _listenerCts = cts;
                var token = cts.Token;
                _processingTask = Task.Run(() => ProcessMessagesAsync(token), token);
                _listenerTask = Task.Run(() => RunListener(port, cts), token);
                return _listenerTask;
            }
        }

        private void RunListener(int port, CancellationTokenSource cts)
        {
            var token = cts.Token;
            using var listener = new OscReceiver(IPAddress.Parse("127.0.0.1"), port);

            Exception? failure = null;
            int messageCount = 0;

            try
            {
                _bus.Publish(new OscConnecting(port));
                _logger.LogEvent("OSC", $"Connecting to OSC endpoint 127.0.0.1:{port}.");
                listener.Connect();
                _bus.Publish(new OscConnected(port));
                _logger.LogEvent("OSC", "OSC listener connected.");
                bool isDebugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
                while (!token.IsCancellationRequested)
                {
                    if (listener.State != OscSocketState.Connected)
                    {
                        _logger.LogEvent("OSC", "Listener state changed from Connected. Exiting receive loop.");
                        break;
                    }

                    if (listener.TryReceive(out OscPacket packet) && packet is OscMessage msg)
                    {
                        _channel.Writer.TryWrite(msg);
                        messageCount++;
                        if (isDebugLoggingEnabled && ShouldLogSample(messageCount))
                        {
                            var capturedCount = messageCount;
                            _logger.LogEvent("OSC", () => $"Queued OSC message #{capturedCount}: {FormatOscMessage(msg)}", Serilog.Events.LogEventLevel.Debug);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
                _logger.LogEvent("OSC", ex.Message, Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                _logger.LogEvent("OSC", failure == null
                    ? $"OSC listener stopped after processing {messageCount} message(s)."
                    : $"OSC listener stopped with failure after {messageCount} message(s): {failure?.Message}");
                _bus.Publish(new OscDisconnected(port, failure));

                try
                {
                    if (!cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    while (_channel.Reader.TryRead(out _))
                    {
                    }

                    lock (_lifecycleLock)
                    {
                        if (ReferenceEquals(_listenerCts, cts))
                        {
                            _listenerCts = null;
                        }
                    }

                    cts.Dispose();
                }
            }
        }

        private async Task ProcessMessagesAsync(CancellationToken token)
        {
            try
            {
                int dispatched = 0;
                bool isDebugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
                await foreach (var msg in _channel.Reader.ReadAllAsync(token))
                {
                    _bus.Publish(new OscMessageReceived(msg));
                    dispatched++;
                    if (isDebugLoggingEnabled && ShouldLogSample(dispatched))
                    {
                        var capturedCount = dispatched;
                        _logger.LogEvent("OSC", () => $"Dispatched OSC message #{capturedCount}: {FormatOscMessage(msg)}", Serilog.Events.LogEventLevel.Debug);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _logger.LogEvent("OSC", "OSC message processing loop completed.");
            }
        }

        public void Stop()
        {
            _logger.LogEvent("OSC", "Stop requested.");

            Task? listenerTask;
            Task? processingTask;
            CancellationTokenSource? cts;

            lock (_lifecycleLock)
            {
                cts = _listenerCts;
                listenerTask = _listenerTask;
                processingTask = _processingTask;
                _listenerCts = null;
                _listenerTask = null;
                _processingTask = null;
            }

            if (cts == null)
            {
                _logger.LogEvent("OSC", "Stop requested but listener is not running.", Serilog.Events.LogEventLevel.Debug);
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                listenerTask?.ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("OSC", $"Listener loop faulted during stop: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }

            try
            {
                processingTask?.ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("OSC", $"Message processing task faulted during stop: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                while (_channel.Reader.TryRead(out _))
                {
                }

                cts.Dispose();
            }

            _logger.LogEvent("OSC", "Listener stop completed.");
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                _logger.LogEvent("OSC", $"Dispose encountered error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                _logger.LogEvent("OSC", "Listener disposed.");
            }
        }

        private static string FormatOscMessage(OscMessage message)
        {
            var data = message.Select(arg => arg?.ToString() ?? "<null>");
            return $"{message.Address} [{string.Join(", ", data)}]";
        }

        private static bool ShouldLogSample(int count)
        {
            return count <= 5 || count % 50 == 0;
        }
    }
}
