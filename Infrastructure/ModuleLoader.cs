using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
                    var asm = LoadAssemblySafely(file, logger);
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
                    if (IsPotentialMarkOfTheWebBlock(ex))
                    {
                        logger.LogEvent(
                            "ModuleLoader",
                            $"Assembly '{Path.GetFileName(file)}' appears to be blocked by Windows. Right-click the file, open Properties, check 'Unblock', and restart the application.",
                            Serilog.Events.LogEventLevel.Warning);
                    }
                    logger.LogEvent("ModuleLoad", "Failed to load module " + file + ": " + ex.Message, Serilog.Events.LogEventLevel.Error);
                    bus.Publish(new ModuleLoadFailed(file, ex));
                }
            }

            host.NotifyDiscoveryCompleted();
            logger.LogEvent("ModuleLoader", "Module discovery notifications completed.");
        }

        private static Assembly LoadAssemblySafely(string file, IEventLogger logger)
        {
            try
            {
                return Assembly.LoadFrom(file);
            }
            catch (FileLoadException ex) when (IsPotentialMarkOfTheWebBlock(ex) && TryRemoveMarkOfTheWeb(file, logger))
            {
                logger.LogEvent(
                    "ModuleLoader",
                    $"Removed Mark-of-the-Web from '{Path.GetFileName(file)}' and retrying assembly load.");
                return Assembly.LoadFrom(file);
            }
        }

        private static bool TryRemoveMarkOfTheWeb(string file, IEventLogger logger)
        {
            if (!IsWindows())
            {
                return false;
            }

            var zoneIdentifierStream = file + ":Zone.Identifier";

            try
            {
                if (!DeleteFile(zoneIdentifierStream))
                {
                    var error = Marshal.GetLastWin32Error();

                    // The stream does not exist (2 = ERROR_FILE_NOT_FOUND, 3 = ERROR_PATH_NOT_FOUND).
                    if (error == 2 || error == 3)
                    {
                        return false;
                    }

                    logger.LogEvent(
                        "ModuleLoader",
                        $"Failed to remove Mark-of-the-Web from '{Path.GetFileName(file)}'. Win32 error code: {error}.",
                        Serilog.Events.LogEventLevel.Debug);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.LogEvent(
                    "ModuleLoader",
                    $"Unexpected error while removing Mark-of-the-Web from '{Path.GetFileName(file)}': {ex.Message}",
                    Serilog.Events.LogEventLevel.Debug);
                return false;
            }
        }

        private static bool IsPotentialMarkOfTheWebBlock(Exception ex)
        {
            if (!IsWindows())
            {
                return false;
            }

            if (ex is FileLoadException fileLoadException)
            {
                var hresult = Marshal.GetHRForException(fileLoadException);
                if (hresult == unchecked((int)0x80131515))
                {
                    return true;
                }

                if (fileLoadException.InnerException is NotSupportedException)
                {
                    return true;
                }

                if (fileLoadException.Message.IndexOf("operation is not supported", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteFile(string lpFileName);
    }
}
