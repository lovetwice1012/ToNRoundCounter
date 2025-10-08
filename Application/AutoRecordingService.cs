#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Application.Recording;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    public sealed partial class AutoRecordingService : IDisposable
    {
        private readonly StateService _stateService;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly object _sync = new object();
        private InternalScreenRecorder? _recorder;
        private bool _disposed;
        private string? _currentTriggerDescription;

        public AutoRecordingService(StateService stateService, IAppSettings settings, IEventLogger logger)
        {
            _stateService = stateService;
            _settings = settings;
            _logger = logger;
            _stateService.StateChanged += HandleStateChanged;
        }

        private void HandleStateChanged()
        {
            EvaluateRecordingState("StateChanged");
        }

        public void EvaluateRecordingState(string reason)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (!_settings.AutoRecordingEnabled)
                {
                    StopRecording("Disabled");
                    return;
                }

                var round = _stateService.CurrentRound;
                if (round == null)
                {
                    StopRecording("No active round");
                    return;
                }

                var roundTriggers = NormalizeTriggers(_settings.AutoRecordingRoundTypes);
                var terrorTriggers = NormalizeTriggers(_settings.AutoRecordingTerrors);
                bool hasRoundTriggers = roundTriggers.Count > 0;
                bool hasTerrorTriggers = terrorTriggers.Count > 0;

                bool roundTriggered = false;
                if (hasRoundTriggers && !string.IsNullOrWhiteSpace(round.RoundType))
                {
                    roundTriggered = TriggerMatches(round.RoundType!, roundTriggers);
                }

                bool terrorTriggered = false;
                if (hasTerrorTriggers && !string.IsNullOrWhiteSpace(round.TerrorKey))
                {
                    var terrors = SplitTerrorNames(round.TerrorKey);
                    terrorTriggered = terrors.Any(t => TriggerMatches(t, terrorTriggers));
                }

                bool shouldRecord = hasRoundTriggers || hasTerrorTriggers
                    ? roundTriggered || terrorTriggered
                    : false;

                if (!shouldRecord)
                {
                    StopRecording("No matching triggers");
                    return;
                }

                if (_recorder != null)
                {
                    return;
                }

                var triggerDetails = BuildTriggerDescription(round.RoundType, roundTriggered, round.TerrorKey, terrorTriggered);
                StartRecording(triggerDetails);
            }
        }

        private void StartRecording(string triggerDetails)
        {
            string outputDirectory = ResolveOutputDirectory();
            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogEvent(
                    "AutoRecording",
                    () => $"Failed to create output directory '{outputDirectory}': {ex.Message}",
                    LogEventLevel.Error);
                return;
            }

            string extension = NormalizeExtension(_settings.AutoRecordingOutputExtension);
            string fileName = GenerateOutputFileName(triggerDetails, extension);
            string outputPath = Path.Combine(outputDirectory, fileName);
            int frameRate = NormalizeFrameRate(_settings.AutoRecordingFrameRate);
            string windowHint = string.IsNullOrWhiteSpace(_settings.AutoRecordingWindowTitle)
                ? "VRChat"
                : _settings.AutoRecordingWindowTitle.Trim();

            bool includeOverlay = _settings.AutoRecordingIncludeOverlay;

            string codec = NormalizeCodec(extension, _settings.AutoRecordingVideoCodec);
            int videoBitrate = NormalizeVideoBitrate(_settings.AutoRecordingVideoBitrate);
            int audioBitrate = NormalizeAudioBitrate(_settings.AutoRecordingAudioBitrate);
            string hardwareOptionId = NormalizeHardwareOption(_settings.AutoRecordingHardwareEncoder);
            bool codecSupportsAudio = CodecSupportsAudio(extension, codec);
            bool captureAudio = codecSupportsAudio;
            if (!captureAudio)
            {
                audioBitrate = 0;
            }

            var hardwareSelection = ParseHardwareSelection(hardwareOptionId);

            if (!InternalScreenRecorder.TryCreate(
                windowHint,
                frameRate,
                outputPath,
                extension,
                codec,
                includeOverlay,
                videoBitrate,
                audioBitrate,
                hardwareSelection,
                captureAudio,
                out var recorder,
                out var error))
            {
                _logger.LogEvent(
                    "AutoRecording",
                    () => $"Failed to start built-in recorder: {error}",
                    LogEventLevel.Error);
                return;
            }

            _recorder = recorder;
            _currentTriggerDescription = triggerDetails;
            recorder.Completion.ContinueWith(_ => HandleRecorderCompleted(recorder), TaskScheduler.Default);
            string encoderMode = recorder.IsHardwareAccelerated ? "hardware" : "software";
            string videoBitrateDisplay = videoBitrate > 0 ? $"{videoBitrate / 1000} kbps" : "auto";
            string audioBitrateDisplay = captureAudio ? (audioBitrate > 0 ? $"{audioBitrate / 1000} kbps" : "auto") : "disabled";
            string hardwareDescription = GetHardwareOptionDisplay(hardwareOptionId);
            _logger.LogEvent(
                "AutoRecording",
                () =>
                    $"Recording started. Output: {outputPath}. Trigger: {triggerDetails}. Codec: {codec}. FPS: {frameRate}. Video bitrate: {videoBitrateDisplay}. Audio: {audioBitrateDisplay}. Hardware: {hardwareDescription} ({encoderMode}).");
        }

        private void HandleRecorderCompleted(InternalScreenRecorder recorder)
        {
            string? trigger = null;
            string? reason = null;
            LogEventLevel level = LogEventLevel.Information;
            bool shouldLog = false;

            lock (_sync)
            {
                if (_recorder != recorder)
                {
                    return;
                }

                trigger = _currentTriggerDescription ?? "<unknown>";
                _currentTriggerDescription = null;
                _recorder = null;

                if (!recorder.StoppedByOwner)
                {
                    shouldLog = true;
                    reason = recorder.StopReason;
                    if (recorder.HasError)
                    {
                        level = LogEventLevel.Warning;
                    }
                }
                else
                {
                    reason = recorder.StopReason;
                }
            }

            try
            {
                recorder.Dispose();
            }
            catch
            {
            }

            if (shouldLog)
            {
                string triggerText = trigger ?? "<unknown>";
                string reasonText = reason ?? "<unknown>";
                _logger.LogEvent(
                    "AutoRecording",
                    () => $"Recording stopped automatically. Reason: {reasonText}. Last trigger: {triggerText}.",
                    level);
            }
        }

        private void StopRecording(string reason)
        {
            if (_recorder == null)
            {
                return;
            }

            var recorder = _recorder;
            _recorder = null;
            var trigger = _currentTriggerDescription ?? "<unknown>";
            _currentTriggerDescription = null;

            LogEventLevel level = LogEventLevel.Information;

            try
            {
                recorder.Stop(reason);
                if (recorder.HasError)
                {
                    level = LogEventLevel.Warning;
                }
            }
            catch (Exception ex)
            {
                level = LogEventLevel.Warning;
                _logger.LogEvent(
                    "AutoRecording",
                    () => $"Failed to stop recorder cleanly: {ex.Message}",
                    LogEventLevel.Warning);
            }
            finally
            {
                try
                {
                    recorder.Dispose();
                }
                catch
                {
                }

                _logger.LogEvent(
                    "AutoRecording",
                    () => $"Recording stopped. Reason: {recorder.StopReason}. Last trigger: {trigger}.",
                    level);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stateService.StateChanged -= HandleStateChanged;
                StopRecording("Service disposed");
            }
        }
    }
}
