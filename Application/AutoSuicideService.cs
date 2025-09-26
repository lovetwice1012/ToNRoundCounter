using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides scheduling utilities for auto suicide logic.
    /// </summary>
    public class AutoSuicideService
    {
        private readonly object _lock = new object();
        private CancellationTokenSource _tokenSource;
        private readonly IEventBus? _bus;
        private DateTime? _scheduledAtUtc;
        private TimeSpan _scheduledDelay;
        public DateTime RoundStartTime { get; private set; }

        public AutoSuicideService()
        {
        }

        public AutoSuicideService(IEventBus bus)
        {
            _bus = bus;
        }

        public bool HasScheduled
        {
            get
            {
                lock (_lock)
                {
                    return _tokenSource != null && !_tokenSource.IsCancellationRequested;
                }
            }
        }

        public void Schedule(TimeSpan delay, bool resetStartTime, Action action)
        {
            CancellationTokenSource oldCts;
            CancellationTokenSource cts;
            lock (_lock)
            {
                oldCts = _tokenSource;
                if (resetStartTime || RoundStartTime == default(DateTime))
                {
                    RoundStartTime = DateTime.UtcNow;
                }
                cts = new CancellationTokenSource();
                _tokenSource = cts;
                _scheduledAtUtc = DateTime.UtcNow;
                _scheduledDelay = delay;
            }
            oldCts?.Cancel();
            oldCts?.Dispose();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);
                    if (!cts.IsCancellationRequested)
                    {
                        _bus?.Publish(new AutoSuicideTriggered());
                        action();
                    }
                }
                catch (TaskCanceledException)
                {
                }
                finally
                {
                    lock (_lock)
                    {
                        if (_tokenSource == cts)
                        {
                            _tokenSource = null;
                            _scheduledAtUtc = null;
                            _scheduledDelay = TimeSpan.Zero;
                        }
                    }
                    cts.Dispose();
                }
            });

            _bus?.Publish(new AutoSuicideScheduled(delay, resetStartTime));
        }

        public void Cancel()
        {
            CancellationTokenSource cts;
            TimeSpan? remainingDelay = null;
            lock (_lock)
            {
                if (_tokenSource != null && _scheduledAtUtc.HasValue && _scheduledDelay > TimeSpan.Zero)
                {
                    var elapsed = DateTime.UtcNow - _scheduledAtUtc.Value;
                    var remaining = _scheduledDelay - elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        remainingDelay = remaining;
                    }
                }
                cts = _tokenSource;
                _tokenSource = null;
                _scheduledAtUtc = null;
                _scheduledDelay = TimeSpan.Zero;
            }
            cts?.Cancel();
            cts?.Dispose();
            _bus?.Publish(new AutoSuicideCancelled(remainingDelay));
        }
    }
}
