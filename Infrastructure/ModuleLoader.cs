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
            logger.LogEvent("ModuleLoader", $"Starting module discovery in '{Path.GetFullPath(path)}'.");
            host.NotifyDiscoveryStarted(path);

            if (!Directory.Exists(path))
            {
                logger.LogEvent("ModuleLoader", $"Module directory '{Path.GetFullPath(path)}' does not exist. Skipping discovery.");
                host.NotifyDiscoveryCompleted();
                return;
            }

            var discoveredFiles = Directory.GetFiles(path, "*.dll");
            logger.LogEvent("ModuleLoader", $"Found {discoveredFiles.Length} candidate assembly file(s).");

            foreach (var file in discoveredFiles)
            {
                try
                {
                    var asm = Assembly.LoadFrom(file);
                    var modules = asm.GetTypes()
                        .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
                    logger.LogEvent("ModuleLoader", $"Loaded assembly '{Path.GetFileName(file)}'. Discovering modules.");
                    foreach (var type in modules)
                    {
                        if (Activator.CreateInstance(type) is IModule module)
                        {
                            var moduleName = type.FullName ?? type.Name;
                            logger.LogEvent("ModuleLoader", $"Discovered module '{moduleName}' from '{file}'.");
                            var discovery = new ModuleDiscoveryContext(moduleName, asm, file);
                            host.RegisterModule(module, discovery, services);
                        }
                        else
                        {
                            logger.LogEvent("ModuleLoader", $"Failed to instantiate module type '{type.FullName}'.", Serilog.Events.LogEventLevel.Warning);
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
            logger.LogEvent("ModuleLoader", "Module discovery notifications completed.");
        }
    }
}
