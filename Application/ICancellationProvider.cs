using System.Threading;

namespace ToNRoundCounter.Application
{
    public interface ICancellationProvider
    {
        CancellationToken Token { get; }
        void Cancel();
    }
}
