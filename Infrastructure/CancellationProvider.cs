using System.Threading;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    public class CancellationProvider : ICancellationProvider
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public CancellationToken Token => _cts.Token;
        public void Cancel() => _cts.Cancel();
    }
}
