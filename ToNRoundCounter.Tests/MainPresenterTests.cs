using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class MainPresenterTests
    {
        [Fact]
        public void Constructor_InitializesWithoutThrowing()
        {
            var logger = new MockEventLogger();
            var settings = new MockAppSettings();
            var stateService = new StateService();
            var httpClient = new MockHttpClient();

            var presenter = new MainPresenter(stateService, settings, logger, httpClient);

            Assert.NotNull(presenter);
        }

        [Fact]
        public async Task UploadRoundLogAsync_WithoutWebhookOrCloud_DoesNothing()
        {
            var logger = new MockEventLogger();
            var settings = new MockAppSettings();
            settings.CloudSyncEnabled = false;
            settings.DiscordWebhookUrl = string.Empty;
            var stateService = new StateService();
            var httpClient = new MockHttpClient();

            var presenter = new MainPresenter(stateService, settings, logger, httpClient);

            var round = new Round
            {
                RoundType = "Test",
                TerrorKey = "TestTerror",
                MapName = "TestMap"
            };

            await presenter.UploadRoundLogAsync(round, "test-status");

            Assert.Equal(0, httpClient.PostCallCount);
        }

        private class MockEventLogger : IEventLogger
        {
            public List<string> LoggedMessages { get; } = new();
            public void LogEvent(string c, string m, Serilog.Events.LogEventLevel l = Serilog.Events.LogEventLevel.Information) => LoggedMessages.Add($"{c}: {m}");
            public void LogEvent(string c, Func<string> mf, Serilog.Events.LogEventLevel l = Serilog.Events.LogEventLevel.Information) => LoggedMessages.Add($"{c}: {mf()}");
            public bool IsEnabled(Serilog.Events.LogEventLevel level) => true;
        }

        private class MockAppSettings : IAppSettings
        {
            public int OSCPort { get; set; } = 9001;
            public bool OSCPortChanged { get; set; }
            public Color BackgroundColor_InfoPanel { get; set; }
            public Color BackgroundColor_Stats { get; set; }
            public Color BackgroundColor_Log { get; set; }
            public Color FixedTerrorColor { get; set; }
            public bool ShowStats { get; set; } = true;
            public bool ShowDebug { get; set; }
            public bool ShowRoundLog { get; set; } = true;
            public bool Filter_RoundType { get; set; } = true;
            public bool Filter_Terror { get; set; } = true;
            public bool Filter_Appearance { get; set; } = true;
            public bool Filter_Survival { get; set; } = true;
            public bool Filter_Death { get; set; } = true;
            public bool Filter_SurvivalRate { get; set; } = true;
            public bool OverlayShowVelocity { get; set; }
            public bool OverlayShowTerror { get; set; }
            public bool OverlayShowDamage { get; set; }
            public bool OverlayShowNextRound { get; set; }
            public bool OverlayShowRoundStatus { get; set; }
            public bool OverlayShowRoundHistory { get; set; }
            public bool OverlayShowRoundStats { get; set; }
            public bool OverlayShowTerrorInfo { get; set; }
            public bool OverlayShowShortcuts { get; set; }
            public bool OverlayShowAngle { get; set; }
            public bool OverlayShowClock { get; set; }
            public bool OverlayShowInstanceTimer { get; set; }
            public bool OverlayShowInstanceMembers { get; set; }
            public bool OverlayShowVoting { get; set; }
            public bool OverlayShowUnboundTerrorDetails { get; set; }
            public double OverlayOpacity { get; set; } = 1.0;
            public int OverlayRoundHistoryLength { get; set; } = 3;
            public Dictionary<string, Point> OverlayPositions { get; set; } = new();
            public Dictionary<string, float> OverlayScaleFactors { get; set; } = new();
            public Dictionary<string, Size> OverlaySizes { get; set; } = new();
            public List<string> AutoSuicideRoundTypes { get; set; } = new();
            public Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; } = new();
            public List<string> AutoSuicideDetailCustom { get; set; } = new();
            public bool AutoSuicideFuzzyMatch { get; set; }
            public bool AutoSuicideUseDetail { get; set; }
            public List<string> RoundTypeStats { get; set; } = new();
            public bool AutoSuicideEnabled { get; set; }
            public string ApiKey { get; set; } = string.Empty;
            public string apikey { get => ApiKey; set => ApiKey = value; }
            public string ThemeKey { get; set; } = "default";
            public string Language { get; set; } = "ja-JP";
            public string LogFilePath { get; set; } = "logs/log-.txt";
            public string WebSocketIp { get; set; } = "127.0.0.1";
            public string CloudWebSocketUrl { get; set; } = string.Empty;
            public bool CloudSyncEnabled { get; set; }
            public string CloudPlayerName { get; set; } = string.Empty;
            public string CloudApiKey { get; set; } = string.Empty;
            public bool AutoLaunchEnabled { get; set; }
            public List<AutoLaunchEntry> AutoLaunchEntries { get; set; } = new();
            public string AutoLaunchExecutablePath { get; set; } = string.Empty;
            public string AutoLaunchArguments { get; set; } = string.Empty;
            public bool ItemMusicEnabled { get; set; }
            public List<ItemMusicEntry> ItemMusicEntries { get; set; } = new();
            public bool RoundBgmEnabled { get; set; }
            public List<RoundBgmEntry> RoundBgmEntries { get; set; } = new();
            public RoundBgmItemConflictBehavior RoundBgmItemConflictBehavior { get; set; }
            public string ItemMusicItemName { get; set; } = string.Empty;
            public string ItemMusicSoundPath { get; set; } = string.Empty;
            public double ItemMusicMinSpeed { get; set; }
            public double ItemMusicMaxSpeed { get; set; }
            public bool AutoRecordingEnabled { get; set; }
            public string AutoRecordingWindowTitle { get; set; } = "VRChat";
            public string AutoRecordingCommand { get; set; } = string.Empty;
            public int AutoRecordingFrameRate { get; set; } = 30;
            public string AutoRecordingResolution { get; set; } = "1920x1080";
            public string AutoRecordingArguments { get; set; } = string.Empty;
            public string AutoRecordingOutputDirectory { get; set; } = "recordings";
            public string AutoRecordingOutputExtension { get; set; } = "avi";
            public string AutoRecordingVideoCodec { get; set; } = "h264";
            public int AutoRecordingVideoBitrate { get; set; }
            public int AutoRecordingAudioBitrate { get; set; }
            public string AutoRecordingHardwareEncoder { get; set; } = "none";
            public bool AutoRecordingIncludeOverlay { get; set; }
            public List<string> AutoRecordingRoundTypes { get; set; } = new();
            public List<string> AutoRecordingTerrors { get; set; } = new();
            public string DiscordWebhookUrl { get; set; } = string.Empty;
            public string LastSaveCode { get; set; } = string.Empty;
            public bool AfkSoundCancelEnabled { get; set; } = true;
            public double NotificationSoundVolume { get; set; } = 1.0;
            public double AfkSoundVolume { get; set; } = 1.0;
            public double PunishSoundVolume { get; set; } = 1.0;
            public double MasterVolume { get; set; } = 1.0;
            public bool MasterMuted { get; set; }
            public bool NotificationSoundMuted { get; set; }
            public bool AfkSoundMuted { get; set; }
            public bool PunishSoundMuted { get; set; }
            public bool ItemMusicMuted { get; set; }
            public bool RoundBgmMuted { get; set; }
            public int AudioOutputDeviceNumber { get; set; } = -1;
            public string MasterMuteHotkey { get; set; } = string.Empty;
            public bool EqualizerEnabled { get; set; }
            public double[] EqualizerBandGains { get; set; } = new double[10];
            public bool CoordinatedAutoSuicideBrainEnabled { get; set; } = true;
            public bool NetworkAnalyzerConsentGranted { get; set; }
            public DateTimeOffset? NetworkAnalyzerConsentTimestamp { get; set; }
            public string? NetworkAnalyzerConsentMarkerId { get; set; }
            public int NetworkAnalyzerProxyPort { get; set; } = 8890;

            public void Load() { }
            public Task SaveAsync() => Task.CompletedTask;
        }

        private class MockHttpClient : IHttpClient
        {
            public int PostCallCount { get; private set; }
            public string? LastPostUrl { get; private set; }

            public Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken cancellationToken)
            {
                PostCallCount++;
                LastPostUrl = url;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
