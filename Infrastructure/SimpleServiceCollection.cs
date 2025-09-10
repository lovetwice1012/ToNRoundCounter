using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection
    {
        void AddSingleton<TService, TImplementation>() where TImplementation : TService, new();
        void AddSingleton<TService>() where TService : class, new();
        void AddSingleton<TService>(Func<IServiceProvider, TService> factory);
        void AddSingleton<TService>(TService instance);
        IServiceProvider BuildServiceProvider();
    }

    public class ServiceCollection : IServiceCollection
    {
        private readonly Dictionary<Type, Func<IServiceProvider, object>> _registrations = new();

        public void AddSingleton<TService, TImplementation>() where TImplementation : TService, new()
        {
            _registrations[typeof(TService)] = _ => new TImplementation();
        }

        public void AddSingleton<TService>() where TService : class, new()
        {
            _registrations[typeof(TService)] = _ => new TService();
        }

        public void AddSingleton<TService>(Func<IServiceProvider, TService> factory)
        {
            _registrations[typeof(TService)] = sp => factory(sp)!;
        }

        public void AddSingleton<TService>(TService instance)
        {
            _registrations[typeof(TService)] = _ => instance!;
        }

        public IServiceProvider BuildServiceProvider()
        {
            return new SimpleServiceProvider(_registrations);
        }
    }

    internal class SimpleServiceProvider : IServiceProvider, IDisposable
    {
        private readonly Dictionary<Type, Func<IServiceProvider, object>> _registrations;
        private readonly Dictionary<Type, object> _instances = new();

        public SimpleServiceProvider(Dictionary<Type, Func<IServiceProvider, object>> registrations)
        {
            _registrations = registrations;
        }

        public object GetService(Type serviceType)
        {
            if (!_instances.TryGetValue(serviceType, out var instance))
            {
                if (_registrations.TryGetValue(serviceType, out var factory))
                {
                    instance = factory(this);
                    _instances[serviceType] = instance;
                }
            }
            return instance!;
        }

        public void Dispose()
        {
            foreach (var disposable in _instances.Values)
            {
                (disposable as IDisposable)?.Dispose();
            }
        }
    }

    public static class ServiceProviderServiceExtensions
    {
        public static T GetRequiredService<T>(this IServiceProvider provider)
        {
            var service = provider.GetService(typeof(T));
            if (service == null)
            {
                throw new InvalidOperationException($"Service of type {typeof(T)} is not registered.");
            }
            return (T)service;
        }
    }
}
