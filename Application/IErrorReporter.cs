using System;

namespace ToNRoundCounter.Application
{
    public interface IErrorReporter
    {
        void Register();
        void Handle(Exception ex);
    }
}
