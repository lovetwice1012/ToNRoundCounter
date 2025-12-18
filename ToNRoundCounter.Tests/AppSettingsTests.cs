using System;
using System.IO;
using System.Linq;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for AppSettings functionality.
    /// </summary>
    public class AppSettingsTests : IDisposable
    {
        private readonly string _testSettingsFile = "test_appsettings.json";
        private readonly MockEventLogger _logger;
        private readonly MockEventBus _eventBus;

        public AppSettingsTests()
        {
            _logger = new MockEventLogger();
            _eventBus = new MockEventBus();

            // Clean up test file if it exists
            if (File.Exists(_testSettingsFile))
            {
                File.Delete(_testSettingsFile);
            }
        }

        public void Dispose()
        {
            // Clean up test file
            if (File.Exists(_testSettingsFile))
            {
                File.Delete(_testSettingsFile);
            }
        }

        [Fact]
        public void DefaultValues_AreSetCorrectly()
        {
            var settings = CreateTestSettings();

            Assert.Equal(9001, settings.OSCPort);
            Assert.Equal("127.0.0.1", settings.WebSocketIp);
            Assert.True(settings.ShowStats);
            Assert.False(settings.ShowDebug);
            Assert.True(settings.ShowRoundLog);
        }

        [Fact]
        public void OSCPort_CanBeChanged()
        {
            var settings = CreateTestSettings();
            settings.OSCPort = 9002;

            Assert.Equal(9002, settings.OSCPort);
        }

        [Fact]
        public void ApiKey_StartsEmpty()
        {
            var settings = CreateTestSettings();
            Assert.Equal(string.Empty, settings.ApiKey);
        }

        [Fact]
        public void ApiKey_CanBeSet()
        {
            var settings = CreateTestSettings();
            var testKey = "test_api_key_12345678901234567890123456";

            settings.ApiKey = testKey;
            Assert.Equal(testKey, settings.ApiKey);
        }

        [Fact]
        public void CloudSettings_DefaultValues()
        {
            var settings = CreateTestSettings();

            Assert.False(settings.CloudSyncEnabled);
            Assert.Equal(string.Empty, settings.CloudWebSocketUrl);
            Assert.Equal(string.Empty, settings.CloudPlayerName);
        }

        [Fact]
        public void AutoLaunchEntries_InitializedAsEmptyList()
        {
            var settings = CreateTestSettings();

            Assert.NotNull(settings.AutoLaunchEntries);
            Assert.Empty(settings.AutoLaunchEntries);
        }

        [Fact]
        public void Overlay_DefaultSettingsAreCorrect()
        {
            var settings = CreateTestSettings();

            Assert.True(settings.OverlayShowVelocity);
            Assert.True(settings.OverlayShowTerror);
            Assert.True(settings.OverlayShowDamage);
            Assert.True(settings.OverlayShowNextRound);
            Assert.True(settings.OverlayShowRoundStatus);
            Assert.True(settings.OverlayShowRoundHistory);
        }

        [Fact]
        public void DiscordWebhookUrl_CanBeSet()
        {
            var settings = CreateTestSettings();
            var testUrl = "https://discord.com/api/webhooks/test";

            settings.DiscordWebhookUrl = testUrl;
            Assert.Equal(testUrl, settings.DiscordWebhookUrl);
        }

        [Fact]
        public void AutoRecording_DefaultConfiguration()
        {
            var settings = CreateTestSettings();

            Assert.False(settings.AutoRecordingEnabled);
            Assert.Equal("VRChat", settings.AutoRecordingWindowTitle);
            Assert.Equal(30, settings.AutoRecordingFrameRate);
            Assert.Equal("recordings", settings.AutoRecordingOutputDirectory);
        }

        [Fact]
        public void NetworkAnalyzer_DefaultSettings()
        {
            var settings = CreateTestSettings();

            Assert.False(settings.NetworkAnalyzerConsentGranted);
            Assert.Null(settings.NetworkAnalyzerConsentTimestamp);
            Assert.Equal(8890, settings.NetworkAnalyzerProxyPort);
        }

        [Fact]
        public void RoundTypeStats_ContainsExpectedRoundTypes()
        {
            var settings = CreateTestSettings();

            Assert.NotNull(settings.RoundTypeStats);
            Assert.Contains("クラシック", settings.RoundTypeStats);
            Assert.Contains("走れ！", settings.RoundTypeStats);
            Assert.Contains("パニッシュ", settings.RoundTypeStats);
            Assert.Contains("8ページ", settings.RoundTypeStats);
        }

        [Fact]
        public void ItemMusicEntries_InitializedCorrectly()
        {
            var settings = CreateTestSettings();

            Assert.NotNull(settings.ItemMusicEntries);
            Assert.Empty(settings.ItemMusicEntries);
            Assert.False(settings.ItemMusicEnabled);
        }

        [Fact]
        public void RoundBgmEntries_InitializedCorrectly()
        {
            var settings = CreateTestSettings();

            Assert.NotNull(settings.RoundBgmEntries);
            Assert.Empty(settings.RoundBgmEntries);
            Assert.False(settings.RoundBgmEnabled);
        }

        private AppSettings CreateTestSettings()
        {
            // Create a mock settings instance without file I/O
            // This is a simplified version for testing
            var settings = new AppSettings(_logger, _eventBus, null);
            return settings;
        }

        // Mock implementations for testing
        private class MockEventLogger : IEventLogger
        {
            public void LogEvent(string category, string message, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
                // Mock implementation - do nothing
            }

            public void LogEvent(string category, Func<string> messageFactory, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
                // Mock implementation - do nothing
            }
        }

        private class MockEventBus : IEventBus
        {
            public void Publish<T>(T ev) where T : class
            {
                // Mock implementation - do nothing
            }

            public IDisposable Subscribe<T>(Action<T> handler) where T : class
            {
                // Mock implementation - return dummy disposable
                return new DummyDisposable();
            }

            public void Unsubscribe<T>(IDisposable subscription) where T : class
            {
                // Mock implementation - do nothing
            }

            private class DummyDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
