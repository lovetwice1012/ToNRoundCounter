using System;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Simple event aggregator for decoupled publish/subscribe communication.
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler);
        void Publish<T>(T message);
    }
}
