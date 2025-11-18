using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for MainPresenter functionality.
    /// </summary>
    public class MainPresenterTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            var logger = new MockEventLogger();
            var settings = CreateMockSettings();
            var stateService = new StateService();
            var httpClient = new MockHttpClient();

            var presenter = new MainPresenter(logger, settings, stateService, httpClient);

            Assert.NotNull(presenter);
        }

        [Fact]
        public async Task UploadRoundLogAsync_WithoutApiKey_SkipsUpload()
        {
            var logger = new MockEventLogger();
            var settings = CreateMockSettings();
            settings.ApiKey = string.Empty;
            var stateService = new StateService();
            var httpClient = new MockHttpClient();

            var presenter = new MainPresenter(logger, settings, stateService, httpClient);

            var round = new Round
            {
                RoundType = "Test",
                TerrorKey = "TestTerror",
                MapName = "TestMap"
            };

            // Should not throw and should skip upload
            await presenter.UploadRoundLogAsync(round, "test-status");

            Assert.Equal(0, httpClient.PostCallCount);
        }

        [Fact]
        public async Task UploadRoundLogAsync_WithApiKey_AttemptsUpload()
        {
            var logger = new MockEventLogger();
            var settings = CreateMockSettings();
            settings.ApiKey = "test_api_key_32_characters_long!!!";
            var stateService = new StateService();
            var httpClient = new MockHttpClient();

            var presenter = new MainPresenter(logger, settings, stateService, httpClient);

            var round = new Round
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "Asylum",
                Damage = 15,
                IsDeath = false
            };

            await presenter.UploadRoundLogAsync(round, "completed");

            Assert.Equal(1, httpClient.PostCallCount);
            Assert.Contains("roundlogs/create", httpClient.LastPostUrl);
        }

        private MockAppSettings CreateMockSettings()
        {
            return new MockAppSettings();
        }

        // Mock implementations
        private class MockEventLogger : IEventLogger
        {
            public List<string> LoggedMessages { get; } = new List<string>();

            public void LogEvent(string category, string message, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
                LoggedMessages.Add($"{category}: {message}");
            }

            public void LogEvent(string category, Func<string> messageFactory, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
                LoggedMessages.Add($"{category}: {messageFactory()}");
            }
        }

        private class MockAppSettings : IAppSettings
        {
            public int OSCPort { get; set; } = 9001;
            public bool OSCPortChanged { get; set; }
            public System.Drawing.Color BackgroundColor_InfoPanel { get; set; }
            public System.Drawing.Color BackgroundColor_Stats { get; set; }
            public System.Drawing.Color BackgroundColor_Log { get; set; }
            public System.Drawing.Color FixedTerrorColor { get; set; }
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
            public bool OverlayShowAngle { get; set; }
            public bool OverlayShowTerror { get; set; }
            public bool OverlayShowUnboundTerrorDetails { get; set; }
            public bool OverlayShowDamage { get; set; }
            public bool OverlayShowNextRound { get; set; }
            public bool OverlayShowRoundStatus { get; set; }
            public bool OverlayShowRoundHistory { get; set; }
            public int OverlayRoundHistoryLength { get; set; } = 3;
            public Dictionary<string, System.Drawing.Point> OverlayPositions { get; set; } = new Dictionary<string, System.Drawing.Point>();
            public Dictionary<string, float> OverlayScaleFactors { get; set; } = new Dictionary<string, float>();
            public Dictionary<string, System.Drawing.Size> OverlaySizes { get; set; } = new Dictionary<string, System.Drawing.Size>();
            public bool OverlayShowRoundStats { get; set; }
            public bool OverlayShowTerrorInfo { get; set; }
            public bool OverlayShowShortcuts { get; set; }
            public bool OverlayShowClock { get; set; }
            public bool OverlayShowInstanceTimer { get; set; }
            public bool OverlayShowInstanceMembers { get; set; }
            public double OverlayOpacity { get; set; } = 100.0;
            public List<string> AutoSuicideRoundTypes { get; set; } = new List<string>();
            public Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; } = new Dictionary<string, AutoSuicidePreset>();
            public List<string> AutoSuicideDetailCustom { get; set; } = new List<string>();
            public bool AutoSuicideFuzzyMatch { get; set; }
            public bool AutoSuicideUseDetail { get; set; }
            public List<string> RoundTypeStats { get; set; } = new List<string>();
            public bool AutoSuicideEnabled { get; set; }
            public string apikey { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string ThemeKey { get; set; } = "default";
            public string Language { get; set; } = "ja-JP";
            public string LogFilePath { get; set; } = "logs/log-.txt";
            public string WebSocketIp { get; set; } = "127.0.0.1";
            public string CloudWebSocketUrl { get; set; } = string.Empty;
            public bool CloudSyncEnabled { get; set; }
            public string CloudPlayerName { get; set; } = string.Empty;
            public bool AutoLaunchEnabled { get; set; }
            public List<AutoLaunchEntry> AutoLaunchEntries { get; set; } = new List<AutoLaunchEntry>();
            public string AutoLaunchExecutablePath { get; set; } = string.Empty;
            public string AutoLaunchArguments { get; set; } = string.Empty;
            public bool ItemMusicEnabled { get; set; }
            public List<ItemMusicEntry> ItemMusicEntries { get; set; } = new List<ItemMusicEntry>();
            public bool RoundBgmEnabled { get; set; }
            public List<RoundBgmEntry> RoundBgmEntries { get; set; } = new List<RoundBgmEntry>();
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
            public List<string> AutoRecordingRoundTypes { get; set; } = new List<string>();
            public List<string> AutoRecordingTerrors { get; set; } = new List<string>();
            public string DiscordWebhookUrl { get; set; } = string.Empty;
            public string LastSaveCode { get; set; } = string.Empty;
            public bool AfkSoundCancelEnabled { get; set; } = true;
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

                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                return Task.FromResult(response);
            }
        }
    }
}
