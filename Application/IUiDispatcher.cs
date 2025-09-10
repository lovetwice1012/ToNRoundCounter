using System;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Abstraction for scheduling work on the UI thread.
    /// </summary>
    public interface IUiDispatcher
    {
        void Invoke(Action action);
    }
}
