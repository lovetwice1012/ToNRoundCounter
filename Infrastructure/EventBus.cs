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
        private readonly Channel<IDispatchWork> _dispatchQueue;
        private static ImmutableHashSet<Type> _suppressDebugLoggingTypes = ImmutableHashSet<Type>.Empty;
        private static readonly object _suppressSync = new();

        // Pooled work item per message type so steady-state Publish->dispatch generates no closure
        // allocation. Each pool is bounded; if the pool is empty we allocate a fresh instance.
        private interface IDispatchWork
        {
            void Invoke(EventBus owner);
        }

        private sealed class DispatchWork<TMessage> : IDispatchWork
        {
            public ImmutableArray<Delegate> Handlers;
            public TMessage Message = default!;

            // Per-T thread-local pool keeps recycling cheap and lock-free under typical load.
            [ThreadStatic] private static Stack<DispatchWork<TMessage>>? _pool;
            private const int PoolMaxSize = 32;

            public static DispatchWork<TMessage> Rent(ImmutableArray<Delegate> handlers, TMessage message)
            {
                var pool = _pool;
                DispatchWork<TMessage>? item = null;
                if (pool != null && pool.Count > 0)
                {
                    item = pool.Pop();
                }
                item ??= new DispatchWork<TMessage>();
                item.Handlers = handlers;
                item.Message = message;
                return item;
            }

            public void Invoke(EventBus owner)
            {
                var handlers = Handlers;
                var message = Message;
                // Clear references before returning to pool to avoid retaining captured objects.
                Handlers = default;
                Message = default!;
                try
                {
                    foreach (var entry in handlers)
                    {
                        if (entry is Action<TMessage> action)
                        {
                            try
                            {
                                action(message);
                            }
                            catch (Exception ex)
                            {
                                owner._logger?.LogEvent(
                                    "EventBus",
                                    () => $"Handler '{action.Method.DeclaringType?.FullName}.{action.Method.Name}' threw: {ex}",
                                    LogEventLevel.Error);
                            }
                        }
                    }
                }
                finally
                {
                    var pool = _pool ??= new Stack<DispatchWork<TMessage>>(PoolMaxSize);
                    if (pool.Count < PoolMaxSize)
                    {
                        pool.Push(this);
                    }
                }
            }
        }

        public EventBus(IEventLogger? logger = null)
        {
            _logger = logger;
            _dispatchQueue = Channel.CreateUnbounded<IDispatchWork>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false
            });
            _ = Task.Run(ProcessQueueAsync);
            
            // Suppress debug logging for high-frequency event types
            lock (_suppressSync)
            {
                if (!_suppressDebugLoggingTypes.Contains(typeof(OscMessageReceived)))
                {
                    _suppressDebugLoggingTypes = _suppressDebugLoggingTypes.Add(typeof(OscMessageReceived));
                }
            }
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
            var suppressSet = _suppressDebugLoggingTypes;
            if (_handlers.TryGetValue(messageType, out var handlers) && !handlers.IsDefaultOrEmpty)
            {
                if (!suppressSet.Contains(messageType))
                {
                    LogDebug(() => $"Publishing message of type {messageType.FullName} to {handlers.Length} handler(s).");
                }

                // One pooled work item per Publish (was: one Action closure per handler).
                var work = DispatchWork<T>.Rent(handlers, message);
                if (!_dispatchQueue.Writer.TryWrite(work))
                {
                    _logger?.LogEvent("EventBus", () => $"Failed to enqueue dispatch for message type {messageType.FullName}.", LogEventLevel.Warning);
                }
            }
            else
            {
                if (!suppressSet.Contains(messageType))
                {
                    LogDebug(() => $"Publishing message of type {messageType.FullName} with no registered handlers.");
                }
            }
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var workItem in _dispatchQueue.Reader.ReadAllAsync())
            {
                try
                {
                    workItem.Invoke(this);
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
