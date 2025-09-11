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
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

        public void Subscribe<T>(Action<T> handler)
        {
            var list = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
            lock (list)
            {
                list.Add(handler);
            }
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                lock (list)
                {
                    list.Remove(handler);
                    if (list.Count == 0)
                    {
                        _handlers.TryRemove(typeof(T), out _);
                    }
                }
            }
        }

        public void Publish<T>(T message)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                Delegate[] snapshot;
                lock (list)
                {
                    snapshot = list.ToArray();
                }
                foreach (var d in snapshot)
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
