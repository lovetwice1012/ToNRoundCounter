using System;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for EventBus functionality.
    /// </summary>
    public class EventBusTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            var logger = new MockEventLogger();

            var bus = new EventBus(logger);

            Assert.NotNull(bus);
        }

        [Fact]
        public void PublishWebSocketConnected_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler<WebSocketConnectionEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal("test_endpoint", e.Endpoint);
                if (handler != null)
                {
                    bus.WebSocketConnected -= handler;
                }
            };
            bus.WebSocketConnected += handler;

            bus.PublishWebSocketConnected("test_endpoint");

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishWebSocketDisconnected_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler<WebSocketConnectionEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal("test_endpoint", e.Endpoint);
                if (handler != null)
                {
                    bus.WebSocketDisconnected -= handler;
                }
            };
            bus.WebSocketDisconnected += handler;

            bus.PublishWebSocketDisconnected("test_endpoint");

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishOSCConnected_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler<OSCConnectionEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal(9001, e.Port);
                if (handler != null)
                {
                    bus.OSCConnected -= handler;
                }
            };
            bus.OSCConnected += handler;

            bus.PublishOSCConnected(9001);

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishOSCDisconnected_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler<OSCConnectionEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal(9001, e.Port);
                if (handler != null)
                {
                    bus.OSCDisconnected -= handler;
                }
            };
            bus.OSCDisconnected += handler;

            bus.PublishOSCDisconnected(9001);

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishRoundStarted_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            var testRound = new Round
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            EventHandler<RoundEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal("Classic", e.Round.RoundType);
                Assert.Equal("Tornado", e.Round.TerrorKey);
                if (handler != null)
                {
                    bus.RoundStarted -= handler;
                }
            };
            bus.RoundStarted += handler;

            bus.PublishRoundStarted(testRound);

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishRoundEnded_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            var testRound = new Round
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School",
                IsDeath = false
            };

            EventHandler<RoundEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal("Run", e.Round.RoundType);
                Assert.False(e.Round.IsDeath);
                if (handler != null)
                {
                    bus.RoundEnded -= handler;
                }
            };
            bus.RoundEnded += handler;

            bus.PublishRoundEnded(testRound);

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishSettingsChanged_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                if (handler != null)
                {
                    bus.SettingsChanged -= handler;
                }
            };
            bus.SettingsChanged += handler;

            bus.PublishSettingsChanged();

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishAutoSuicideExecute_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                if (handler != null)
                {
                    bus.AutoSuicideExecute -= handler;
                }
            };
            bus.AutoSuicideExecute += handler;

            bus.PublishAutoSuicideExecute();

            Assert.True(eventRaised);
        }

        [Fact]
        public void PublishSettingsValidationFailed_RaisesEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            var errors = new[] { "Error 1", "Error 2" };

            EventHandler<SettingsValidationFailedEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                eventRaised = true;
                Assert.Equal(2, e.Errors.Count);
                Assert.Contains("Error 1", e.Errors);
                if (handler != null)
                {
                    bus.SettingsValidationFailed -= handler;
                }
            };
            bus.SettingsValidationFailed += handler;

            bus.PublishSettingsValidationFailed(errors);

            Assert.True(eventRaised);
        }

        [Fact]
        public void MultipleSubscribers_AllReceiveEvents()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            int count = 0;
            EventHandler? handler1 = null;
            EventHandler? handler2 = null;
            EventHandler? handler3 = null;

            handler1 = (sender, e) =>
            {
                count++;
                if (handler1 != null)
                {
                    bus.SettingsChanged -= handler1;
                }
            };
            handler2 = (sender, e) =>
            {
                count++;
                if (handler2 != null)
                {
                    bus.SettingsChanged -= handler2;
                }
            };
            handler3 = (sender, e) =>
            {
                count++;
                if (handler3 != null)
                {
                    bus.SettingsChanged -= handler3;
                }
            };

            bus.SettingsChanged += handler1;
            bus.SettingsChanged += handler2;
            bus.SettingsChanged += handler3;

            bus.PublishSettingsChanged();

            Assert.Equal(3, count);
        }

        [Fact]
        public void UnsubscribedHandler_DoesNotReceiveEvents()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            bool eventRaised = false;
            EventHandler handler = (sender, e) => eventRaised = true;

            bus.SettingsChanged += handler;
            bus.SettingsChanged -= handler;

            bus.PublishSettingsChanged();

            Assert.False(eventRaised);
        }

        // Mock implementation
        private class MockEventLogger : IEventLogger
        {
            public void LogEvent(string category, string message, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
            }

            public void LogEvent(string category, Func<string> messageFactory, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
            }
        }
    }
}
