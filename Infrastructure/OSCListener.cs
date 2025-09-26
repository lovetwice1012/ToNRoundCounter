using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rug.Osc;
using ToNRoundCounter.Application;
using System.Threading.Channels;

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
        private readonly Channel<OscMessage> _channel = Channel.CreateUnbounded<OscMessage>();
        private Task? _processingTask;

        public OSCListener(IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
        }

        public async Task StartAsync(int port)
        {
            _logger.LogEvent("OSC", $"Starting OSC listener on port {port}.");
            _processingTask = Task.Run(ProcessMessagesAsync, _cancellation.Token);
            await Task.Run(() =>
            {
                using (var listener = new OscReceiver(IPAddress.Parse("127.0.0.1"), port))
                {
                    Exception? failure = null;
                    int messageCount = 0;
                    try
                    {
                        _bus.Publish(new OscConnecting(port));
                        _logger.LogEvent("OSC", $"Connecting to OSC endpoint 127.0.0.1:{port}.");
                        listener.Connect();
                        _bus.Publish(new OscConnected(port));
                        _logger.LogEvent("OSC", "OSC listener connected.");
                        while (!_cancellation.Token.IsCancellationRequested)
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
                                _logger.LogEvent("OSC", $"Queued OSC message #{messageCount}: {FormatOscMessage(msg)}");
                            }
                        }
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
                            : $"OSC listener stopped with failure after {messageCount} message(s): {failure.Message}");
                        _bus.Publish(new OscDisconnected(port, failure));
                    }
                }
            }, _cancellation.Token).ConfigureAwait(false);
        }

        private async Task ProcessMessagesAsync()
        {
            try
            {
                int dispatched = 0;
                await foreach (var msg in _channel.Reader.ReadAllAsync(_cancellation.Token))
                {
                    _bus.Publish(new OscMessageReceived(msg));
                    dispatched++;
                    _logger.LogEvent("OSC", $"Dispatched OSC message #{dispatched}: {FormatOscMessage(msg)}");
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _logger.LogEvent("OSC", "OSC message processing loop completed.");
            }
        }

        public void Stop()
        {
            _logger.LogEvent("OSC", "Stop requested.");
            _cancellation.Cancel();
        }

        public void Dispose()
        {
            try
            {
                _processingTask?.GetAwaiter().GetResult();
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
    }
}
