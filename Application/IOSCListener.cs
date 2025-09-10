using System;
using System.Threading;
using System.Threading.Tasks;
using Rug.Osc;

namespace ToNRoundCounter.Application
{
    public interface IOSCListener
    {
        Task StartAsync(int port);
    }
}
