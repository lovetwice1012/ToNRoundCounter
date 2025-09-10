using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Rug.Osc;

namespace ToNRoundCounter.Services
{
    /// <summary>
    /// Placeholder for OSC listener logic.
    /// </summary>
    public class OSCService
    {
        public event Action<OscMessage> MessageReceived;

        public async Task StartAsync(int port, CancellationToken token)
        {
            await Task.Run(() =>
            {
                using (var listener = new OscReceiver(IPAddress.Parse("127.0.0.1"), port))
                {
                    listener.Connect();
                    while (!token.IsCancellationRequested)
                    {
                        if (listener.State != OscSocketState.Connected)
                            break;
                        if (listener.TryReceive(out OscPacket packet))
                        {
                            if (packet is OscMessage msg)
                            {
                                MessageReceived?.Invoke(msg);
                            }
                        }
                    }
                }
            }, token);
        }
    }
}
