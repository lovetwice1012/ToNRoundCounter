using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    public interface IWebSocketClient
    {
        Task StartAsync();
        void Stop();
    }
}
