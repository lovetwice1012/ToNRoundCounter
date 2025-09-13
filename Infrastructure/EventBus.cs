using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Lightweight in-memory event bus.
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Delegate, byte>> _handlers = new();

        public void Subscribe<T>(Action<T> handler)
        {
            var dict = _handlers.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<Delegate, byte>());
            dict.TryAdd(handler, 0);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (_handlers.TryGetValue(typeof(T), out var dict))
            {
                dict.TryRemove(handler, out _);
                if (dict.IsEmpty)
                {
                    _handlers.TryRemove(typeof(T), out _);
                }
            }
        }

        public void Publish<T>(T message)
        {
            if (_handlers.TryGetValue(typeof(T), out var dict))
            {
                foreach (var d in dict.Keys)
                {
                    if (d is Action<T> action)
                    {
                        action(message);
                    }
                }
            }
        }
    }
}
