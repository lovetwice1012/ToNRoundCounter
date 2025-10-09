using System;
using System.Threading;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    public class CancellationProvider : ICancellationProvider
    {
        private readonly object _sync = new();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public CancellationToken Token
        {
            get
            {
                lock (_sync)
                {
                    return _cts.Token;
                }
            }
        }

        public void Cancel()
        {
            CancellationTokenSource toCancel;
            lock (_sync)
            {
                toCancel = _cts;
                _cts = new CancellationTokenSource();
            }

            try
            {
                toCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // If the previous source was already disposed we simply ignore the signal.
            }
            finally
            {
                toCancel.Dispose();
            }
        }
    }
}
