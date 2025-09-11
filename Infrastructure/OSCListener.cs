using System;
using System.Net;
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
        private Task _processingTask;

        public OSCListener(IEventBus bus, ICancellationProvider cancellation, IEventLogger logger)
        {
            _bus = bus;
            _cancellation = cancellation;
            _logger = logger;
        }

        public async Task StartAsync(int port)
        {
            _processingTask = Task.Run(ProcessMessagesAsync, _cancellation.Token);
            await Task.Run(() =>
            {
                using (var listener = new OscReceiver(IPAddress.Parse("127.0.0.1"), port))
                {
                    try
                    {
                        listener.Connect();
                        _bus.Publish(new OscConnected());
                        while (!_cancellation.Token.IsCancellationRequested)
                        {
                            if (listener.State != OscSocketState.Connected)
                                break;
                            if (listener.TryReceive(out OscPacket packet) && packet is OscMessage msg)
                            {
                                _channel.Writer.TryWrite(msg);
                            }
                        }
                    }
                    finally
                    {
                        _bus.Publish(new OscDisconnected());
                    }
                }
            }, _cancellation.Token).ConfigureAwait(false);
        }

        private async Task ProcessMessagesAsync()
        {
            try
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync(_cancellation.Token))
                {
                    _bus.Publish(new OscMessageReceived(msg));
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Stop()
        {
            _cancellation.Cancel();
        }

        public void Dispose()
        {
            _processingTask?.GetAwaiter().GetResult();
        }
    }
}
