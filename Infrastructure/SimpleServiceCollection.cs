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
        private readonly Dictionary<Type, List<Func<IServiceProvider, object>>> _registrations = new();

        public void AddSingleton<TService, TImplementation>() where TImplementation : TService, new()
        {
            AddSingleton<TService>(_ => new TImplementation());
        }

        public void AddSingleton<TService>() where TService : class, new()
        {
            AddSingleton<TService>(_ => new TService());
        }

        public void AddSingleton<TService>(Func<IServiceProvider, TService> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (!_registrations.TryGetValue(typeof(TService), out var factories))
            {
                factories = new List<Func<IServiceProvider, object>>();
                _registrations[typeof(TService)] = factories;
            }

            factories.Add(sp => factory(sp)!);
        }

        public void AddSingleton<TService>(TService instance)
        {
            AddSingleton<TService>(_ => instance!);
        }

        public IServiceProvider BuildServiceProvider()
        {
            return new SimpleServiceProvider(_registrations);
        }
    }

    internal class SimpleServiceProvider : IServiceProvider, IDisposable
    {
        private readonly Dictionary<Type, List<Func<IServiceProvider, object>>> _registrations;
        private readonly Dictionary<Type, List<object?>> _instances = new();

        public SimpleServiceProvider(Dictionary<Type, List<Func<IServiceProvider, object>>> registrations)
        {
            _registrations = registrations;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var elementType = serviceType.GetGenericArguments()[0];
                return ResolveAll(elementType);
            }

            return ResolveSingle(serviceType);
        }

        private object? ResolveSingle(Type serviceType)
        {
            if (!_registrations.TryGetValue(serviceType, out var factories) || factories.Count == 0)
            {
                return null;
            }

            var instances = GetInstanceList(serviceType, factories.Count);
            if (instances[0] == null)
            {
                instances[0] = factories[0](this);
            }

            return instances[0];
        }

        private object ResolveAll(Type serviceType)
        {
            if (!_registrations.TryGetValue(serviceType, out var factories) || factories.Count == 0)
            {
                return Array.CreateInstance(serviceType, 0);
            }

            var instances = GetInstanceList(serviceType, factories.Count);
            for (var i = 0; i < factories.Count; i++)
            {
                if (instances[i] == null)
                {
                    instances[i] = factories[i](this);
                }
            }

            var array = Array.CreateInstance(serviceType, factories.Count);
            for (var i = 0; i < factories.Count; i++)
            {
                array.SetValue(instances[i], i);
            }
            return array;
        }

        private List<object?> GetInstanceList(Type serviceType, int requiredCount)
        {
            if (!_instances.TryGetValue(serviceType, out var instances))
            {
                instances = new List<object?>(new object?[requiredCount]);
                _instances[serviceType] = instances;
            }
            else if (instances.Count < requiredCount)
            {
                while (instances.Count < requiredCount)
                {
                    instances.Add(null);
                }
            }

            return instances;
        }

        public void Dispose()
        {
            foreach (var instanceList in _instances.Values)
            {
                foreach (var disposable in instanceList)
                {
                    (disposable as IDisposable)?.Dispose();
                }
            }
        }
    }

    public static class ServiceProviderServiceExtensions
    {
        public static T GetRequiredService<T>(this IServiceProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var service = provider.GetService(typeof(T));
            if (service == null)
            {
                throw new InvalidOperationException($"Service of type {typeof(T)} is not registered.");
            }
            return (T)service;
        }

        public static IEnumerable<T> GetServices<T>(this IServiceProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var services = provider.GetService(typeof(IEnumerable<T>));
            return services as IEnumerable<T> ?? Array.Empty<T>();
        }
    }
}
