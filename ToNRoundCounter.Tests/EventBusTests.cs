using System;
using System.Threading;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class EventBusTests
    {
        private sealed record TestMessage(string Value);

        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            var bus = new EventBus();
            Assert.NotNull(bus);
        }

        [Fact]
        public void Publish_DeliversMessageToSubscribedHandler()
        {
            var bus = new EventBus();
            string? received = null;
            var evt = new ManualResetEventSlim(false);

            Action<TestMessage> handler = msg =>
            {
                received = msg.Value;
                evt.Set();
            };
            bus.Subscribe(handler);

            bus.Publish(new TestMessage("hello"));

            Assert.True(evt.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("hello", received);

            bus.Unsubscribe(handler);
        }

        [Fact]
        public void Unsubscribe_StopsReceivingMessages()
        {
            var bus = new EventBus();
            int count = 0;
            Action<TestMessage> handler = _ => Interlocked.Increment(ref count);

            bus.Subscribe(handler);
            bus.Unsubscribe(handler);

            bus.Publish(new TestMessage("ignored"));

            Thread.Sleep(100);
            Assert.Equal(0, count);
        }

        [Fact]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            var bus = new EventBus();
            var ex = Record.Exception(() => bus.Publish(new TestMessage("orphan")));
            Assert.Null(ex);
        }
    }
}
