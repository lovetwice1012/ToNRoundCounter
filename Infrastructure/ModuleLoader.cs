using System;
using System.Collections.Generic;
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
        public static void LoadModules(IServiceCollection services, ModuleHost host, IEventLogger logger, IEventBus bus, string path = "Modules", bool safeMode = false)
        {
            var modulesDirectory = ResolveModulesDirectory(path);

            logger.LogEvent("ModuleLoader", $"Starting module discovery in '{modulesDirectory}'.");
            host.NotifyDiscoveryStarted(modulesDirectory);

            if (safeMode)
            {
                logger.LogEvent("ModuleLoader", "Safe mode active. Skipping module discovery.", Serilog.Events.LogEventLevel.Warning);
                host.NotifyDiscoveryCompleted();
                return;
            }

            if (!Directory.Exists(modulesDirectory))
            {
                logger.LogEvent("ModuleLoader", $"Module directory '{modulesDirectory}' does not exist. Skipping discovery.");
                host.NotifyDiscoveryCompleted();
                return;
            }

            var discoveredFiles = EnumerateModuleAssemblies(modulesDirectory, logger).ToList();
            logger.LogEvent("ModuleLoader", $"Found {discoveredFiles.Count} candidate assembly file(s).");

            foreach (var file in discoveredFiles)
            {
                try
                {
                    var asm = LoadAssemblySafely(file, logger);
                    IEnumerable<Type> modules;
                    try
                    {
                        modules = asm.GetTypes()
                            .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        LogReflectionTypeLoadException(rtle, file, logger);
                        modules = rtle.Types
                            ?.Where(t => t != null && typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract)
                            ?? Enumerable.Empty<Type>();
                    }
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

        private static IEnumerable<string> EnumerateModuleAssemblies(string rootDirectory, IEventLogger logger)
        {
            var pending = new Stack<string>();
            pending.Push(rootDirectory);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[]? files = null;
                try
                {
                    files = Directory.GetFiles(current, "*.dll", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    logger.LogEvent("ModuleLoader", $"Failed to enumerate assemblies in '{current}': {ex.Message}", Serilog.Events.LogEventLevel.Warning);
                }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        yield return file;
                    }
                }

                string[]? subdirectories = null;
                try
                {
                    subdirectories = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    logger.LogEvent("ModuleLoader", $"Failed to enumerate directories in '{current}': {ex.Message}", Serilog.Events.LogEventLevel.Warning);
                }

                if (subdirectories != null)
                {
                    foreach (var directory in subdirectories)
                    {
                        pending.Push(directory);
                    }
                }
            }
        }

        private static void LogReflectionTypeLoadException(ReflectionTypeLoadException exception, string assemblyPath, IEventLogger logger)
        {
            if (logger == null)
            {
                return;
            }

            var loaderExceptions = exception.LoaderExceptions;
            if (loaderExceptions == null || loaderExceptions.Length == 0)
            {
                logger.LogEvent(
                    "ModuleLoader",
                    () => $"Failed to enumerate types from '{Path.GetFileName(assemblyPath)}': {exception.Message}",
                    Serilog.Events.LogEventLevel.Error);
                return;
            }

            logger.LogEvent(
                "ModuleLoader",
                () =>
                {
                    var details = string.Join(Environment.NewLine, loaderExceptions.Select((ex, index) =>
                        $"[{index + 1}] {ex.GetType().Name}: {ex.Message}"));
                    return $"Failed to enumerate types from '{Path.GetFileName(assemblyPath)}'. Loader exceptions:{Environment.NewLine}{details}";
                },
                Serilog.Events.LogEventLevel.Error);
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

        private static string ResolveModulesDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            }

            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                return Path.GetFullPath(Path.Combine(baseDirectory, path));
            }

            return Path.GetFullPath(path);
        }

        private static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteFile(string lpFileName);
    }
}
