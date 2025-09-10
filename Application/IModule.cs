using Microsoft.Extensions.DependencyInjection;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Represents an extension module that can register its own services.
    /// </summary>
    public interface IModule
    {
        void RegisterServices(IServiceCollection services);
    }
}
