using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Tracks and manages cloud operations to prevent silent failures and ensure proper error reporting.
    /// Replaces fire-and-forget Task.Run patterns with tracked, cancellable operations.
    /// </summary>
    public interface ICloudOperationTracker
    {
        /// <summary>
        /// Track an async cloud operation with automatic error reporting
        /// </summary>
        Task TrackOperationAsync(string operationName, Func<Task> operation, CancellationToken cancellationToken = default);

        /// <summary>
        /// Track an async cloud operation that returns a result
        /// </summary>
        Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all currently active operations
        /// </summary>
        IReadOnlyList<string> ActiveOperations { get; }

        /// <summary>
        /// Get operation statistics
        /// </summary>
        OperationStatistics GetStatistics();

        /// <summary>
        /// Cancel all pending operations
        /// </summary>
        Task CancelAllAsync();

        /// <summary>
        /// Wait for all operations to complete (with optional timeout)
        /// </summary>
        Task WaitAllAsync(TimeSpan? timeout = null);
    }

    public class OperationStatistics
    {
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public int PendingOperations { get; set; }
        public Dictionary<string, int> OperationCounts { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Default implementation of CloudOperationTracker
    /// </summary>
    public class CloudOperationTracker : ICloudOperationTracker, IDisposable
    {
        private readonly IEventLogger _logger;
        private readonly IErrorReporter _errorReporter;
        private readonly ConcurrentDictionary<string, OperationContext> _activeOperations;
        private readonly ConcurrentDictionary<string, int> _operationCounts;
        private int _totalOperations;
        private int _successfulOperations;
        private int _failedOperations;
        private CancellationTokenSource? _globalCts;
        private int _disposed;

        private class OperationContext
        {
            public required string OperationName { get; set; }
            public required Task Task { get; set; }
            public DateTime StartTime { get; set; }
            public string? OperationId { get; set; }
        }

        public IReadOnlyList<string> ActiveOperations
        {
            get => _activeOperations.Values.Select(op => op.OperationName).ToList();
        }

        public CloudOperationTracker(IEventLogger logger, IErrorReporter errorReporter)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _errorReporter = errorReporter ?? throw new ArgumentNullException(nameof(errorReporter));
            _activeOperations = new ConcurrentDictionary<string, OperationContext>();
            _operationCounts = new ConcurrentDictionary<string, int>();
            _globalCts = new CancellationTokenSource();
        }

        /// <summary>
        /// Track an async cloud operation
        /// </summary>
        public async Task TrackOperationAsync(string operationName, Func<Task> operation, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _globalCts?.Token ?? CancellationToken.None);

            try
            {
                var context = new OperationContext
                {
                    OperationName = operationName,
                    OperationId = operationId,
                    StartTime = DateTime.UtcNow,
                    Task = operation()
                };

                var key = $"{operationName}_{operationId}";
                _activeOperations.TryAdd(key, context);
                _operationCounts.AddOrUpdate(operationName, 1, (_, count) => count + 1);
                Interlocked.Increment(ref _totalOperations);

                _logger.LogEvent(
                    "CloudOperation",
                    $"Starting: {operationName} [{operationId}]",
                    LogEventLevel.Debug);

                await context.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);

                Interlocked.Increment(ref _successfulOperations);
                _logger.LogEvent(
                    "CloudOperation",
                    $"Completed: {operationName} [{operationId}] in {(DateTime.UtcNow - context.StartTime).TotalMilliseconds:F0}ms",
                    LogEventLevel.Debug);
            }
            catch (OperationCanceledException)
            {
                _logger.LogEvent(
                    "CloudOperation",
                    $"Cancelled: {operationName} [{operationId}]",
                    LogEventLevel.Warning);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedOperations);
                _logger.LogEvent(
                    "CloudOperation",
                    $"Failed: {operationName} [{operationId}] - {ex.Message}",
                    LogEventLevel.Error);
                _errorReporter.Handle(ex, isTerminating: false);
                throw;
            }
            finally
            {
                var key = $"{operationName}_{operationId}";
                _activeOperations.TryRemove(key, out _);
                linkedCts.Dispose();
            }
        }

        /// <summary>
        /// Track an async cloud operation that returns a result
        /// </summary>
        public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _globalCts?.Token ?? CancellationToken.None);

            try
            {
                var context = new OperationContext
                {
                    OperationName = operationName,
                    OperationId = operationId,
                    StartTime = DateTime.UtcNow,
                    Task = operation()
                };

                var key = $"{operationName}_{operationId}";
                _activeOperations.TryAdd(key, context);
                _operationCounts.AddOrUpdate(operationName, 1, (_, count) => count + 1);
                Interlocked.Increment(ref _totalOperations);

                _logger.LogEvent(
                    "CloudOperation",
                    $"Starting: {operationName} [{operationId}]",
                    LogEventLevel.Debug);

                var result = await ((Task<T>)context.Task).WaitAsync(linkedCts.Token).ConfigureAwait(false);

                Interlocked.Increment(ref _successfulOperations);
                _logger.LogEvent(
                    "CloudOperation",
                    $"Completed: {operationName} [{operationId}] in {(DateTime.UtcNow - context.StartTime).TotalMilliseconds:F0}ms",
                    LogEventLevel.Debug);

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogEvent(
                    "CloudOperation",
                    $"Cancelled: {operationName} [{operationId}]",
                    LogEventLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedOperations);
                _logger.LogEvent(
                    "CloudOperation",
                    $"Failed: {operationName} [{operationId}] - {ex.Message}",
                    LogEventLevel.Error);
                _errorReporter.Handle(ex, isTerminating: false);
                throw;
            }
            finally
            {
                var key = $"{operationName}_{operationId}";
                _activeOperations.TryRemove(key, out _);
                linkedCts.Dispose();
            }
        }

        public OperationStatistics GetStatistics()
        {
            return new OperationStatistics
            {
                TotalOperations = _totalOperations,
                SuccessfulOperations = _successfulOperations,
                FailedOperations = _failedOperations,
                PendingOperations = _activeOperations.Count,
                OperationCounts = new Dictionary<string, int>(_operationCounts)
            };
        }

        public async Task CancelAllAsync()
        {
            ThrowIfDisposed();
            
            _logger.LogEvent(
                "CloudOperation",
                $"Cancelling all {_activeOperations.Count} pending operations",
                LogEventLevel.Warning);

            _globalCts?.Cancel();

            try
            {
                await WaitAllAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogEvent(
                    "CloudOperation",
                    "Timeout waiting for operations to cancel",
                    LogEventLevel.Error);
            }
        }

        public async Task WaitAllAsync(TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            
            var tasks = _activeOperations.Values.Select(op => op.Task).ToArray();
            if (tasks.Length == 0)
                return;

            if (!timeout.HasValue)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                return;
            }

            var allTasks = Task.WhenAll(tasks);
            var timeoutTask = Task.Delay(timeout.Value);
            var completed = await Task.WhenAny(allTasks, timeoutTask).ConfigureAwait(false);

            if (completed == timeoutTask)
            {
                throw new TimeoutException($"Timed out waiting for {tasks.Length} cloud operation(s) to complete.");
            }

            await allTasks.ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _globalCts?.Dispose();
                _globalCts = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(CloudOperationTracker));
        }
    }
}
