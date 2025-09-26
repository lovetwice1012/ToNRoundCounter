using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;
using WinFormsApp = System.Windows.Forms.Application;

namespace ToNRoundCounter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            WinFormsApp.EnableVisualStyles();
            WinFormsApp.SetCompatibleTextRenderingDefault(false);

            var bootstrap = new AppSettingsData();
            try
            {
                if (File.Exists("appsettings.json"))
                {
                    var json = File.ReadAllText("appsettings.json");
                    bootstrap = JsonConvert.DeserializeObject<AppSettingsData>(json) ?? new AppSettingsData();
                }
            }
            catch { }

            var logPath = string.IsNullOrWhiteSpace(bootstrap.LogFilePath) ? "logs/log-.txt" : bootstrap.LogFilePath;
            var wsIp = string.IsNullOrWhiteSpace(bootstrap.WebSocketIp) ? "127.0.0.1" : bootstrap.WebSocketIp;
            var wsUrl = $"ws://{wsIp}:11398";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();

            var eventLogger = new EventLogger();
            eventLogger.LogEvent("Bootstrap", $"Application starting. Args: {(args.Length == 0 ? "<none>" : string.Join(" ", args))}");
            eventLogger.LogEvent("Bootstrap", $"Resolved log path: {Path.GetFullPath(logPath)}");
            eventLogger.LogEvent("Bootstrap", $"Resolved WebSocket endpoint: {wsUrl}");

            var eventBus = new EventBus(eventLogger);
            var moduleHost = new ModuleHost(eventLogger, eventBus);

            services.AddSingleton<ICancellationProvider, CancellationProvider>();
            services.AddSingleton<IEventLogger>(eventLogger);
            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton(moduleHost);
            services.AddSingleton<IOSCListener>(sp => new OSCListener(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IWebSocketClient>(sp => new WebSocketClient(wsUrl, sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton(sp => new AutoSuicideService(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<StateService>();
            services.AddSingleton<IAppSettings>(sp => new AppSettings(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>()));
            services.AddSingleton<IInputSender, NativeInputSender>();
            services.AddSingleton<IErrorReporter>(sp => new ErrorReporter(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>()));
            services.AddSingleton<IHttpClient, HttpClientWrapper>();
            services.AddSingleton<IUiDispatcher, WinFormsDispatcher>();
            services.AddSingleton<MainPresenter>(sp => new MainPresenter(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<IHttpClient>()));
            ModuleLoader.LoadModules(services, moduleHost, eventLogger, eventBus);
            eventLogger.LogEvent("Bootstrap", $"Module discovery complete. Discovered modules: {moduleHost.Modules.Count}");
            services.AddSingleton<MainForm>(sp => new MainForm(
                sp.GetRequiredService<IWebSocketClient>(),
                sp.GetRequiredService<IOSCListener>(),
                sp.GetRequiredService<AutoSuicideService>(),
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<MainPresenter>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<ICancellationProvider>(),
                sp.GetRequiredService<IInputSender>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetServices<IAfkWarningHandler>(),
                sp.GetServices<IOscRepeaterPolicy>(),
                sp.GetRequiredService<ModuleHost>()));

            eventLogger.LogEvent("Bootstrap", "Building service provider (pre-build notifications).");
            moduleHost.NotifyServiceProviderBuilding(new ModuleServiceProviderBuildContext(services, eventLogger, eventBus));

            var provider = services.BuildServiceProvider();
            eventLogger.LogEvent("Bootstrap", "Service provider built successfully.");
            moduleHost.NotifyServiceProviderBuilt(new ModuleServiceProviderContext(provider, eventLogger, eventBus));
            provider.GetRequiredService<IErrorReporter>().Register();
            eventLogger.LogEvent("Bootstrap", "Core services registered and error reporter attached.");

            moduleHost.NotifyMainWindowCreating(new ModuleMainWindowCreationContext(provider, typeof(MainForm)));
            var mainForm = provider.GetRequiredService<MainForm>();
            moduleHost.NotifyMainWindowCreated(new ModuleMainWindowContext(mainForm, provider));
            mainForm.Shown += (s, e) => moduleHost.NotifyMainWindowShown(new ModuleMainWindowLifecycleContext(mainForm, provider));
            mainForm.FormClosing += (s, e) => moduleHost.NotifyMainWindowClosing(new ModuleMainWindowLifecycleContext(mainForm, provider));
            ((WinFormsDispatcher)provider.GetRequiredService<IUiDispatcher>()).SetMainForm(mainForm);
            eventLogger.LogEvent("Bootstrap", "Main window constructed and lifecycle hooks registered.");

            var appSettings = provider.GetRequiredService<IAppSettings>();

            string? autoLaunchPath = null;
            string? autoLaunchArguments = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--debug")
                {
                    continue;
                }

                if (args[i] == "--launch" && i + 1 < args.Length)
                {
                    autoLaunchPath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--launch-args" && i + 1 < args.Length)
                {
                    autoLaunchArguments = args[i + 1];
                    i++;
                }
            }

            if (args.Contains("--debug") &&
                args.SkipWhile(a => a != "--test").Skip(1).FirstOrDefault() == "crashreporting")
            {
                throw new InvalidOperationException("Crash report test triggered");
            }

            var launches = new List<AutoLaunchPlan>();

            if (!string.IsNullOrWhiteSpace(autoLaunchPath))
            {
                launches.Add(new AutoLaunchPlan(autoLaunchPath, autoLaunchArguments ?? string.Empty, "command line"));
            }
            else if (appSettings.AutoLaunchEnabled)
            {
                foreach (var entry in appSettings.AutoLaunchEntries ?? Enumerable.Empty<AutoLaunchEntry>())
                {
                    if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.ExecutablePath))
                    {
                        continue;
                    }

                    launches.Add(new AutoLaunchPlan(entry.ExecutablePath, entry.Arguments ?? string.Empty, "settings"));
                }
            }

            if (launches.Count > 0)
            {
                moduleHost.NotifyAutoLaunchEvaluating(new ModuleAutoLaunchEvaluationContext(launches, appSettings, provider));
                mainForm.Shown += (s, e) =>
                {
                    foreach (var launch in launches)
                    {
                        try
                        {
                            string resolvedPath = launch.Path;
                            if (!Path.IsPathRooted(resolvedPath))
                            {
                                resolvedPath = Path.GetFullPath(resolvedPath);
                            }

                            if (!File.Exists(resolvedPath))
                            {
                                var failureContext = new ModuleAutoLaunchFailureContext(launch, new FileNotFoundException("Executable not found", resolvedPath), provider);
                                moduleHost.NotifyAutoLaunchFailed(failureContext);
                                eventLogger.LogEvent("AutoLaunch", $"Executable not found ({launch.Origin}): {resolvedPath}", Serilog.Events.LogEventLevel.Error);
                                continue;
                            }

                            var psi = new ProcessStartInfo
                            {
                                FileName = resolvedPath,
                                UseShellExecute = true,
                                Arguments = launch.Arguments ?? string.Empty,
                            };

                            var workingDirectory = Path.GetDirectoryName(resolvedPath);
                            if (!string.IsNullOrEmpty(workingDirectory))
                            {
                                psi.WorkingDirectory = workingDirectory;
                            }

                            var executionContext = new ModuleAutoLaunchExecutionContext(launch, provider);
                            moduleHost.NotifyAutoLaunchStarting(executionContext);
                            Process.Start(psi);
                            moduleHost.NotifyAutoLaunchCompleted(executionContext);
                            eventLogger.LogEvent("AutoLaunch", $"Started executable ({launch.Origin}): {resolvedPath}");
                        }
                        catch (Exception ex)
                        {
                            var failureContext = new ModuleAutoLaunchFailureContext(launch, ex, provider);
                            moduleHost.NotifyAutoLaunchFailed(failureContext);
                            eventLogger.LogEvent("AutoLaunch", $"Failed to start executable ({launch.Origin}): {ex.Message}", Serilog.Events.LogEventLevel.Error);
                        }
                    }
                };
            }

            moduleHost.NotifyAppRunStarting(new ModuleAppRunContext(mainForm, provider));
            eventLogger.LogEvent("Bootstrap", "Starting WinForms message loop.");
            WinFormsApp.Run(mainForm);
            eventLogger.LogEvent("Bootstrap", "WinForms message loop exited.");
            moduleHost.NotifyAppRunCompleted(new ModuleAppRunContext(mainForm, provider));
            moduleHost.NotifyAppShutdownStarting(new ModuleAppShutdownContext(provider));
            eventLogger.LogEvent("Bootstrap", "Disposing service provider and shutting down.");
            (provider as IDisposable)?.Dispose();
            moduleHost.NotifyAppShutdownCompleted(new ModuleAppShutdownContext(provider));
            eventLogger.LogEvent("Bootstrap", "Application shutdown complete.");
        }
    }
}
