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
            var eventBus = new EventBus();

            services.AddSingleton<ICancellationProvider, CancellationProvider>();
            services.AddSingleton<IEventLogger>(eventLogger);
            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton<IOSCListener>(sp => new OSCListener(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IWebSocketClient>(sp => new WebSocketClient(wsUrl, sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<AutoSuicideService>();
            services.AddSingleton<StateService>();
            services.AddSingleton<IAppSettings>(sp => new AppSettings(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>()));
            services.AddSingleton<IInputSender, NativeInputSender>();
            services.AddSingleton<IErrorReporter>(sp => new ErrorReporter(sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IHttpClient, HttpClientWrapper>();
            services.AddSingleton<IUiDispatcher, WinFormsDispatcher>();
            services.AddSingleton<MainPresenter>(sp => new MainPresenter(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<IHttpClient>()));
            ModuleLoader.LoadModules(services, eventLogger, eventBus);
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
                sp.GetServices<IAfkWarningHandler>()));

            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IErrorReporter>().Register();

            var mainForm = provider.GetRequiredService<MainForm>();
            ((WinFormsDispatcher)provider.GetRequiredService<IUiDispatcher>()).SetMainForm(mainForm);

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

            var launches = new List<(string Path, string Arguments, string Origin)>();

            if (!string.IsNullOrWhiteSpace(autoLaunchPath))
            {
                launches.Add((autoLaunchPath, autoLaunchArguments ?? string.Empty, "command line"));
            }
            else if (appSettings.AutoLaunchEnabled)
            {
                foreach (var entry in appSettings.AutoLaunchEntries ?? Enumerable.Empty<AutoLaunchEntry>())
                {
                    if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.ExecutablePath))
                    {
                        continue;
                    }

                    launches.Add((entry.ExecutablePath, entry.Arguments ?? string.Empty, "settings"));
                }
            }

            if (launches.Count > 0)
            {
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

                            Process.Start(psi);
                            eventLogger.LogEvent("AutoLaunch", $"Started executable ({launch.Origin}): {resolvedPath}");
                        }
                        catch (Exception ex)
                        {
                            eventLogger.LogEvent("AutoLaunch", $"Failed to start executable ({launch.Origin}): {ex.Message}", Serilog.Events.LogEventLevel.Error);
                        }
                    }
                };
            }

            WinFormsApp.Run(mainForm);
            (provider as IDisposable)?.Dispose();
        }
    }
}
