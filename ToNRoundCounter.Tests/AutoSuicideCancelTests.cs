using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class AutoSuicideCancelTests
    {
        [Fact]
        public async Task TerrorRuleCancelsScheduledSuicide()
        {
            // Arrange
            Assert.True(AutoSuicideRule.TryParse("クラシック::1", out var rule1));
            Assert.True(AutoSuicideRule.TryParse("クラシック:Don't Touch Me:0", out var rule2));
            var rules = new List<AutoSuicideRule>
            {
                Assert.IsType<AutoSuicideRule>(rule1),
                Assert.IsType<AutoSuicideRule>(rule2)
            };

            int ShouldAutoSuicide(string roundType, string? terrorName)
            {
                for (int i = rules.Count - 1; i >= 0; i--)
                {
                    if (rules[i].Matches(roundType, terrorName, (a, b) => a == b))
                        return rules[i].Value;
                }
                return 0;
            }

            var service = new AutoSuicideService();
            bool triggered = false;

            // Simulate ROUND_TYPE event
            int action = ShouldAutoSuicide("クラシック", null);
            if (action == 1)
            {
                service.Schedule(TimeSpan.FromMilliseconds(50), true, () => triggered = true);
            }

            // Act: TERRORS event
            int terrorAction = ShouldAutoSuicide("クラシック", "Don't Touch Me");
            if (terrorAction == 0 && service.HasScheduled)
            {
                service.Cancel();
            }
            else if (terrorAction == 1)
            {
                service.Schedule(TimeSpan.Zero, false, () => triggered = true);
            }
            else if (terrorAction == 2)
            {
                service.Schedule(TimeSpan.FromMilliseconds(50), false, () => triggered = true);
            }

            await Task.Delay(100);

            // Assert
            Assert.False(triggered);
            Assert.False(service.HasScheduled);
        }

        [Fact]
        public async Task TerrorRuleDelayReschedulesSuicide()
        {
            var service = new AutoSuicideService();
            using var originalTriggered = new ManualResetEventSlim(false);
            using var rescheduledTriggered = new ManualResetEventSlim(false);

            service.Schedule(TimeSpan.FromMilliseconds(150), true, () => originalTriggered.Set());
            await Task.Delay(30);

            service.Schedule(TimeSpan.FromMilliseconds(500), false, () => rescheduledTriggered.Set());

            await Task.Delay(250);

            Assert.False(originalTriggered.IsSet);
            Assert.False(rescheduledTriggered.IsSet);
            Assert.True(service.HasScheduled);

            Assert.True(rescheduledTriggered.Wait(TimeSpan.FromSeconds(3)));
            Assert.False(originalTriggered.IsSet);
            Assert.True(await WaitUntilAsync(() => !service.HasScheduled, TimeSpan.FromSeconds(1)));
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(10);
            }

            return condition();
        }
    }
}

