using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Channels;
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
        private readonly Channel<Action> _dispatchQueue;
        private static readonly HashSet<Type> _suppressDebugLoggingTypes = new();

        public EventBus(IEventLogger? logger = null)
        {
            _logger = logger;
            _dispatchQueue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false
            });
            _ = Task.Run(ProcessQueueAsync);
            
            // Suppress debug logging for high-frequency event types
            _suppressDebugLoggingTypes.Add(typeof(OscMessageReceived));
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
                        LogDebug(() => $"Subscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Total handlers: {updated.Length}");
                        return;
                    }
                }
                else
                {
                    var initial = ImmutableArray.Create<Delegate>(handler);
                    if (_handlers.TryAdd(messageType, initial))
                    {
                        LogDebug(() => $"Subscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Total handlers: {initial.Length}");
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
                    LogDebug(() => $"Attempted to unsubscribe handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for unregistered message type {messageType.FullName}.");
                    return;
                }

                if (_handlers.TryUpdate(messageType, updated, existing))
                {
                    if (updated.IsEmpty)
                    {
                        _handlers.TryRemove(messageType, out _);
                    }

                    LogDebug(() => $"Unsubscribed handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for message type {messageType.FullName}. Remaining handlers: {updated.Length}");
                    return;
                }
            }

            LogDebug(() => $"Attempted to unsubscribe handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for unregistered message type {messageType.FullName}.");
        }

        public void Publish<T>(T message)
        {
            var messageType = typeof(T);
            if (_handlers.TryGetValue(messageType, out var handlers) && !handlers.IsDefaultOrEmpty)
            {
                if (!_suppressDebugLoggingTypes.Contains(messageType))
                {
                    LogDebug(() => $"Publishing message of type {messageType.FullName} to {handlers.Length} handler(s).");
                }
                
                foreach (var entry in handlers)
                {
                    if (entry is Action<T> action)
                    {
                        QueueInvocation(action, message);
                    }
                }
            }
            else
            {
                if (!_suppressDebugLoggingTypes.Contains(messageType))
                {
                    LogDebug(() => $"Publishing message of type {messageType.FullName} with no registered handlers.");
                }
            }
        }

        private void QueueInvocation<TMessage>(Action<TMessage> handler, TMessage message)
        {
            Action workItem = () => InvokeHandler(handler, message);
            if (!_dispatchQueue.Writer.TryWrite(workItem))
            {
                _logger?.LogEvent("EventBus", () => $"Failed to enqueue handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' for execution.", LogEventLevel.Warning);
            }
        }

        private void InvokeHandler<TMessage>(Action<TMessage> handler, TMessage message)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("EventBus", () => $"Handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' threw: {ex}", LogEventLevel.Error);
            }
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var workItem in _dispatchQueue.Reader.ReadAllAsync())
            {
                try
                {
                    workItem();
                }
                catch (Exception ex)
                {
                    _logger?.LogEvent("EventBus", () => $"Queued handler execution threw: {ex}", LogEventLevel.Error);
                }
            }
        }

        private void LogDebug(Func<string> messageFactory)
        {
            if (_logger?.IsEnabled(LogEventLevel.Debug) == true)
            {
                _logger.LogEvent("EventBus", messageFactory, LogEventLevel.Debug);
            }
        }
    }
}
