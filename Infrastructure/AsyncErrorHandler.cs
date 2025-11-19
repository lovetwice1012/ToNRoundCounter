using System;
using System.Threading.Tasks;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Provides centralized error handling for async void methods (primarily event handlers).
    /// </summary>
    public static class AsyncErrorHandler
    {
        private static IEventLogger? _logger;

        /// <summary>
        /// Initializes the async error handler with a logger.
        /// </summary>
        public static void Initialize(IEventLogger logger)
        {
            _logger = logger;

            // Subscribe to unobserved task exceptions
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                _logger?.LogEvent("AsyncError",
                    $"Unobserved task exception: {args.Exception?.GetBaseException()?.Message}",
                    Serilog.Events.LogEventLevel.Error);
                args.SetObserved(); // Prevent app crash
            };
        }

        /// <summary>
        /// Executes an async operation with comprehensive error handling.
        /// Use this wrapper for async void event handlers.
        /// </summary>
        public static async void Execute(Func<Task> operation, string operationName = "Unknown")
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown, don't log
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("AsyncError",
                    $"Unhandled exception in async operation '{operationName}': {ex.Message}",
                    Serilog.Events.LogEventLevel.Error);
            }
        }

        /// <summary>
        /// Executes an async operation with comprehensive error handling and a custom error handler.
        /// </summary>
        public static async void Execute(Func<Task> operation, Action<Exception> errorHandler, string operationName = "Unknown")
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown, don't log
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("AsyncError",
                    $"Unhandled exception in async operation '{operationName}': {ex.Message}",
                    Serilog.Events.LogEventLevel.Error);

                try
                {
                    errorHandler?.Invoke(ex);
                }
                catch (Exception handlerEx)
                {
                    _logger?.LogEvent("AsyncError",
                        $"Error handler itself threw exception: {handlerEx.Message}",
                        Serilog.Events.LogEventLevel.Error);
                }
            }
        }
    }
}
