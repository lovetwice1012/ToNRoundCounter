using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Discovers and loads external modules from a designated directory.
    /// </summary>
    public static class ModuleLoader
    {
        public static void LoadModules(IServiceCollection services, ModuleHost host, IEventLogger logger, IEventBus bus, string path = "Modules")
        {
            host.NotifyDiscoveryStarted(path);

            if (!Directory.Exists(path))
            {
                host.NotifyDiscoveryCompleted();
                return;
            }

            foreach (var file in Directory.GetFiles(path, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(file);
                    var modules = asm.GetTypes()
                        .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
                    foreach (var type in modules)
                    {
                        if (Activator.CreateInstance(type) is IModule module)
                        {
                            var moduleName = type.FullName ?? type.Name;
                            var discovery = new ModuleDiscoveryContext(moduleName, asm, file);
                            host.RegisterModule(module, discovery, services);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogEvent("ModuleLoad", "Failed to load module " + file + ": " + ex.Message, Serilog.Events.LogEventLevel.Error);
                    bus.Publish(new ModuleLoadFailed(file, ex));
                }
            }

            host.NotifyDiscoveryCompleted();
        }
    }
}
