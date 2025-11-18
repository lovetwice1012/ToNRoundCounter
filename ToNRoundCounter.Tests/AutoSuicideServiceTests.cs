using System;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for AutoSuicideService functionality.
    /// </summary>
    public class AutoSuicideServiceTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);

            var service = new AutoSuicideService(bus, logger);

            Assert.NotNull(service);
            Assert.False(service.HasScheduled);
        }

        [Fact]
        public void Schedule_SetsHasScheduledToTrue()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            service.Schedule(TimeSpan.FromSeconds(1));

            Assert.True(service.HasScheduled);
        }

        [Fact]
        public void Cancel_ClearsSchedule()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            service.Schedule(TimeSpan.FromSeconds(10));
            Assert.True(service.HasScheduled);

            service.Cancel();
            Assert.False(service.HasScheduled);
        }

        [Fact]
        public async Task Schedule_RaisesExecuteEventAfterDelay()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

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

            service.Schedule(TimeSpan.FromMilliseconds(100));

            // Wait for the event to be raised
            await Task.Delay(200);

            Assert.True(eventRaised);
        }

        [Fact]
        public async Task Cancel_PreventsExecuteEvent()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

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

            service.Schedule(TimeSpan.FromMilliseconds(100));
            service.Cancel();

            // Wait to ensure event doesn't fire
            await Task.Delay(200);

            Assert.False(eventRaised);
        }

        [Fact]
        public void Delay_ExtendsSchedule()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            service.Schedule(TimeSpan.FromSeconds(1));
            Assert.True(service.HasScheduled);

            service.Delay(TimeSpan.FromSeconds(2));

            Assert.True(service.HasScheduled);
        }

        [Fact]
        public void Delay_WithoutSchedule_DoesNothing()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            Assert.False(service.HasScheduled);

            service.Delay(TimeSpan.FromSeconds(1));

            Assert.False(service.HasScheduled);
        }

        [Fact]
        public void Schedule_MultipleTimesReplacesSchedule()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            service.Schedule(TimeSpan.FromSeconds(10));
            Assert.True(service.HasScheduled);

            service.Schedule(TimeSpan.FromSeconds(5));
            Assert.True(service.HasScheduled);
        }

        [Fact]
        public async Task Schedule_WithZeroDelay_ExecutesImmediately()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

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

            service.Schedule(TimeSpan.Zero);

            // Wait briefly for async execution
            await Task.Delay(50);

            Assert.True(eventRaised);
        }

        [Fact]
        public void GetRemainingTime_ReturnsCorrectValue()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            service.Schedule(TimeSpan.FromSeconds(10));

            var remaining = service.GetRemainingTime();

            Assert.True(remaining.HasValue);
            Assert.True(remaining.Value.TotalSeconds > 0);
            Assert.True(remaining.Value.TotalSeconds <= 10);
        }

        [Fact]
        public void GetRemainingTime_WithoutSchedule_ReturnsNull()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

            var remaining = service.GetRemainingTime();

            Assert.False(remaining.HasValue);
        }

        [Fact]
        public async Task Dispose_CancelsScheduledTask()
        {
            var logger = new MockEventLogger();
            var bus = new EventBus(logger);
            var service = new AutoSuicideService(bus, logger);

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

            service.Schedule(TimeSpan.FromMilliseconds(100));
            service.Dispose();

            // Wait to ensure event doesn't fire
            await Task.Delay(200);

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
