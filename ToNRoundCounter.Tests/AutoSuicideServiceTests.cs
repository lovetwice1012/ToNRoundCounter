using System;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class AutoSuicideServiceTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);

            Assert.NotNull(service);
            Assert.False(service.HasScheduled);
        }

        [Fact]
        public void Schedule_SetsHasScheduledToTrue()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);

            service.Schedule(TimeSpan.FromSeconds(10), resetStartTime: true, () => { });

            Assert.True(service.HasScheduled);
            service.Cancel();
        }

        [Fact]
        public void Cancel_ClearsSchedule()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);

            service.Schedule(TimeSpan.FromSeconds(10), resetStartTime: true, () => { });
            Assert.True(service.HasScheduled);

            service.Cancel();
            Assert.False(service.HasScheduled);
        }

        [Fact]
        public void Schedule_InvokesActionAfterDelay()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);
            var triggered = new ManualResetEventSlim(false);

            service.Schedule(TimeSpan.FromMilliseconds(50), resetStartTime: true, () => triggered.Set());

            Assert.True(triggered.Wait(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public async Task Cancel_PreventsActionInvocation()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);
            int invokeCount = 0;

            service.Schedule(TimeSpan.FromMilliseconds(100), resetStartTime: true, () => Interlocked.Increment(ref invokeCount));
            service.Cancel();

            await Task.Delay(250);

            Assert.Equal(0, invokeCount);
        }

        [Fact]
        public void Schedule_MultipleTimesReplacesPrevious()
        {
            var bus = new EventBus();
            var service = new AutoSuicideService(bus);

            service.Schedule(TimeSpan.FromSeconds(10), resetStartTime: true, () => { });
            service.Schedule(TimeSpan.FromSeconds(5), resetStartTime: false, () => { });

            Assert.True(service.HasScheduled);
            service.Cancel();
        }
    }
}
