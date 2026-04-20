using System;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides an in-process OSC repeater that listens on a source port
    /// and forwards all messages to one or more destination endpoints.
    /// </summary>
    public interface IOscRepeaterService : IDisposable
    {
        /// <summary>
        /// Starts listening on <paramref name="sourcePort"/> and forwarding
        /// received OSC packets to every registered destination.
        /// </summary>
        Task StartAsync(int sourcePort);

        /// <summary>
        /// Adds a forwarding destination.
        /// </summary>
        void AddDestination(string host, int port);

        /// <summary>
        /// Stops listening and forwarding.
        /// </summary>
        void Stop();
    }
}
