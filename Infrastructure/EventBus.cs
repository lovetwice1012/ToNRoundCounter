using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Lightweight in-memory event bus.
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, ImmutableArray<Delegate>> _handlers = new();
        private readonly IEventLogger? _logger;

        public EventBus(IEventLogger? logger = null)
        {
            _logger = logger;
        }

        public void Subscribe<T>(Action<T> handler)
        {
            var messageType = typeof(T);
            while (true)
            {
                if (_handlers.TryGetValue(messageType, out var existing))
                {
                    var updated = existing.Add(handler);
                    if (_handlers.TryUpdate(messageType, updated, existing))
                    {
                        _logger?.LogEvent("EventBus", () => $"Subscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Total handlers: {updated.Length}");
                        return;
                    }
                }
                else
                {
                    var initial = ImmutableArray.Create<Delegate>(handler);
                    if (_handlers.TryAdd(messageType, initial))
                    {
                        _logger?.LogEvent("EventBus", () => $"Subscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Total handlers: {initial.Length}");
                        return;
                    }
                }
            }
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var messageType = typeof(T);
            while (_handlers.TryGetValue(messageType, out var existing))
            {
                var updated = existing.Remove(handler);
                if (updated.Length == existing.Length)
                {
                    _logger?.LogEvent("EventBus", () => $"Attempted to unsubscribe handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for unregistered message type {messageType.FullName}.");
                    return;
                }

                if (_handlers.TryUpdate(messageType, updated, existing))
                {
                    if (updated.IsEmpty)
                    {
                        _handlers.TryRemove(messageType, out _);
                    }

                    _logger?.LogEvent("EventBus", () => $"Unsubscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Remaining handlers: {updated.Length}");
                    return;
                }
            }

            _logger?.LogEvent("EventBus", () => $"Attempted to unsubscribe handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for unregistered message type {messageType.FullName}.");
        }

        public void Publish<T>(T message)
        {
            var messageType = typeof(T);
            if (_handlers.TryGetValue(messageType, out var handlers) && !handlers.IsDefaultOrEmpty)
            {
                _logger?.LogEvent("EventBus", () => $"Publishing message of type {messageType.FullName} to {handlers.Length} handler(s).");
                foreach (var entry in handlers)
                {
                    if (entry is Action<T> action)
                    {
                        var actionCopy = action;
                        var messageCopy = message;
                        Task.Run(() =>
                        {
                            try
                            {
                                actionCopy(messageCopy);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogEvent("EventBus", () => $"Handler '{actionCopy.Method.DeclaringType?.FullName}.{actionCopy.Method.Name}' threw: {ex}", LogEventLevel.Error);
                            }
                        });
                    }
                }
            }
            else
            {
                _logger?.LogEvent("EventBus", () => $"Publishing message of type {messageType.FullName} with no registered handlers.");
            }
        }
    }
}
