using System;
using System.IO;
using System.Text.Json;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Coordinates safe mode persistence between application launches.
    /// </summary>
    public sealed class SafeModeManager
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _statePath;
        private readonly IEventLogger _logger;
        private SafeModeState? _state;

        public SafeModeManager(string statePath, IEventLogger logger)
        {
            _statePath = statePath;
            _logger = logger;
            _state = LoadState();
        }

        /// <summary>
        /// Gets the currently scheduled safe mode state, if any.
        /// </summary>
        public SafeModeState? CurrentState
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether safe mode has been scheduled.
        /// </summary>
        public bool IsSafeModeRequested => CurrentState != null;

        /// <summary>
        /// Schedules safe mode for the next launch due to a module failure.
        /// </summary>
        public bool TryScheduleAutomaticSafeMode(string moduleName, string stage, Exception exception)
        {
            lock (_sync)
            {
                if (_state != null)
                {
                    var hasPersistentState = File.Exists(_statePath);
                    if (!hasPersistentState && _state.Trigger == SafeModeTrigger.Manual)
                    {
                        _logger.LogEvent(
                            "SafeMode",
                            "Overriding temporary manual safe mode state with automatic schedule due to module failure.",
                            LogEventLevel.Debug);
                    }
                    else
                    {
                        _logger.LogEvent(
                            "SafeMode",
                            $"Safe mode already scheduled (trigger: {_state.Trigger}). Skipping new automatic request.",
                            LogEventLevel.Debug);
                        return false;
                    }
                }

                var reason = $"Module '{moduleName}' failed during {stage}.";
                if (!string.IsNullOrWhiteSpace(exception.Message))
                {
                    reason += $" {exception.Message}";
                }

                var state = new SafeModeState
                {
                    ModuleName = moduleName,
                    Stage = stage,
                    Reason = reason,
                    ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                    ExceptionMessage = exception.Message,
                    RequestedAtUtc = DateTimeOffset.UtcNow,
                    Trigger = SafeModeTrigger.Automatic
                };

                SaveState(state);
                _logger.LogEvent("SafeMode", $"Safe mode scheduled due to failure in module '{moduleName}' ({stage}).", LogEventLevel.Warning);
                return true;
            }
        }

        /// <summary>
        /// Clears any scheduled safe mode state.
        /// </summary>
        public void ClearScheduledSafeMode()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(_statePath))
                    {
                        File.Delete(_statePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("SafeMode", $"Failed to clear safe mode flag: {ex.Message}", LogEventLevel.Warning);
                }
                finally
                {
                    _state = null;
                }

                _logger.LogEvent("SafeMode", "Safe mode flag cleared.");
            }
        }

        /// <summary>
        /// Provides a human-readable description of the current safe mode state.
        /// </summary>
        public string DescribeCurrentState()
        {
            var state = CurrentState;
            if (state == null)
            {
                return "No safe mode request is present.";
            }

            var description = state.Trigger switch
            {
                SafeModeTrigger.Automatic => $"Automatically scheduled at {state.RequestedAtUtc:u} due to module '{state.ModuleName}' failing during {state.Stage}.",
                SafeModeTrigger.Manual => $"Manually requested at {state.RequestedAtUtc:u}.",
                _ => $"Scheduled at {state.RequestedAtUtc:u}."
            };

            if (!string.IsNullOrWhiteSpace(state.Reason))
            {
                description += $" Reason: {state.Reason}";
            }

            return description;
        }

        /// <summary>
        /// Saves an explicit manual safe mode state without overriding existing requests.
        /// </summary>
        public void RecordManualActivation(string reason)
        {
            lock (_sync)
            {
                if (_state != null)
                {
                    return;
                }

                _state = new SafeModeState
                {
                    Reason = reason,
                    RequestedAtUtc = DateTimeOffset.UtcNow,
                    Trigger = SafeModeTrigger.Manual
                };

                _logger.LogEvent("SafeMode", $"Manual safe mode activation recorded. Reason: {reason}", LogEventLevel.Information);
            }
        }

        private SafeModeState? LoadState()
        {
            try
            {
                if (!File.Exists(_statePath))
                {
                    return null;
                }

                var json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<SafeModeState>(json, SerializerOptions);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SafeMode", $"Failed to load safe mode state: {ex.Message}", LogEventLevel.Warning);
                return null;
            }
        }

        private void SaveState(SafeModeState state)
        {
            try
            {
                var directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(state, SerializerOptions);
                File.WriteAllText(_statePath, json);
                _state = state;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SafeMode", $"Failed to persist safe mode state: {ex.Message}", LogEventLevel.Error);
            }
        }
    }

    public sealed class SafeModeState
    {
        public string? ModuleName { get; set; }
        public string? Stage { get; set; }
        public string? Reason { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public DateTimeOffset RequestedAtUtc { get; set; }
        public SafeModeTrigger Trigger { get; set; }
    }

    public enum SafeModeTrigger
    {
        Automatic,
        Manual
    }
}
