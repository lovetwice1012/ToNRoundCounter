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
        private readonly IEventLogger? _logger;

        public EventBus(IEventLogger? logger = null)
        {
            _logger = logger;
        }

        public void Subscribe<T>(Action<T> handler)
        {
            var dict = _handlers.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<Delegate, byte>());
            dict.TryAdd(handler, 0);
            _logger?.LogEvent("EventBus", $"Subscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {typeof(T).FullName}. Total handlers: {dict.Count}");
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
                _logger?.LogEvent("EventBus", $"Unsubscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {typeof(T).FullName}. Remaining handlers: {dict.Count}");
            }
            else
            {
                _logger?.LogEvent("EventBus", $"Attempted to unsubscribe handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for unregistered message type {typeof(T).FullName}.");
            }
        }

        public void Publish<T>(T message)
        {
            if (_handlers.TryGetValue(typeof(T), out var dict))
            {
                _logger?.LogEvent("EventBus", $"Publishing message of type {typeof(T).FullName} to {dict.Count} handler(s).");
                foreach (var d in dict.Keys)
                {
                    if (d is Action<T> action)
                    {
                        action(message);
                    }
                }
            }
            else
            {
                _logger?.LogEvent("EventBus", $"Publishing message of type {typeof(T).FullName} with no registered handlers.");
            }
        }
    }
}
