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
        public DateTime RoundStartTime { get; private set; }

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
                        }
                    }
                    cts.Dispose();
                }
            });
        }

        public void Cancel()
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                cts = _tokenSource;
                _tokenSource = null;
            }
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}
