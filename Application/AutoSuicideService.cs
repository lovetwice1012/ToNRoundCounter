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
        private CancellationTokenSource _tokenSource;
        public DateTime RoundStartTime { get; private set; }

        public bool HasScheduled => _tokenSource != null;

        public void Schedule(TimeSpan delay, bool resetStartTime, Action action)
        {
            _tokenSource?.Cancel();
            if (resetStartTime || RoundStartTime == default(DateTime))
            {
                RoundStartTime = DateTime.UtcNow;
            }
            var cts = new CancellationTokenSource();
            _tokenSource = cts;
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
                    if (_tokenSource == cts)
                    {
                        _tokenSource = null;
                    }
                }
            });
        }

        public void Cancel()
        {
            _tokenSource?.Cancel();
            _tokenSource = null;
        }
    }
}
