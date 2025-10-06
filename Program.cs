using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;
using ToNRoundCounter.Infrastructure.Sqlite;
using WinFormsApp = System.Windows.Forms.Application;

namespace ToNRoundCounter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (RoundLogExportOptions.TryCreate(args, out var exportOptions, out var exportError))
            {
                if (exportError != null)
                {
                    Console.Error.WriteLine(exportError);
                    Environment.ExitCode = 1;
                    return;
                }

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();

                try
                {
                    var exporter = new RoundLogExporter(Log.Logger);
                    Task.Run(() => exporter.ExportAsync(exportOptions!)).GetAwaiter().GetResult();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Logger = Log.Logger ?? new LoggerConfiguration().WriteTo.Console().CreateLogger();
                    Log.Logger.Error(ex, "Failed to export round logs.");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            WinFormsApp.EnableVisualStyles();
            WinFormsApp.SetCompatibleTextRenderingDefault(false);

            LanguageAssemblyResolver.Initialize();

            var bootstrap = LoadBootstrapAsync().GetAwaiter().GetResult();

            var useDefaultLogPath = string.IsNullOrWhiteSpace(bootstrap.LogFilePath);
            var defaultLogPath = Path.Combine("logs", $"log-{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var logPath = useDefaultLogPath ? defaultLogPath : bootstrap.LogFilePath!;
            var wsIp = string.IsNullOrWhiteSpace(bootstrap.WebSocketIp) ? "127.0.0.1" : bootstrap.WebSocketIp;
            var wsUrl = $"ws://{wsIp}:11398";

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console();

            loggerConfiguration = useDefaultLogPath
                ? loggerConfiguration.WriteTo.File(logPath)
                : loggerConfiguration.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

            Log.Logger = loggerConfiguration.CreateLogger();

            var dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            Directory.CreateDirectory(dataDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
            var roundDataPath = Path.Combine(dataDirectory, "rounds", $"{timestamp}.sqlite");
            var statisticsPath = Path.Combine(dataDirectory, "statistics", $"{timestamp}.sqlite");
            var settingsPath = Path.Combine(dataDirectory, "settings", $"{timestamp}.sqlite");

            using var roundDataRepository = new SqliteRoundDataRepository(roundDataPath);
            var eventLogRepository = new SqliteEventLogRepository(statisticsPath);
            var settingsRepository = new SqliteSettingsRepository(settingsPath);

            var services = new ServiceCollection();

            var eventLogger = new EventLogger(eventLogRepository);
            eventLogger.LogEvent("Bootstrap", $"Application starting. Args: {(args.Length == 0 ? "<none>" : string.Join(" ", args))}");
            eventLogger.LogEvent("Bootstrap", $"Resolved log path: {Path.GetFullPath(logPath)}");
            eventLogger.LogEvent("Bootstrap", $"Resolved WebSocket endpoint: {wsUrl}");
            eventLogger.LogEvent("Bootstrap", $"Round data SQLite path: {Path.GetFullPath(roundDataPath)}");
            eventLogger.LogEvent("Bootstrap", $"Statistics SQLite path: {Path.GetFullPath(statisticsPath)}");
            eventLogger.LogEvent("Bootstrap", $"Settings SQLite path: {Path.GetFullPath(settingsPath)}");

            var safeModePath = Path.Combine(dataDirectory, "safe-mode.json");
            var safeModeManager = new SafeModeManager(safeModePath, eventLogger);
            var manualSafeMode = args.Contains("--safe-mode");
            var disableSafeMode = args.Contains("--disable-safe-mode");

            if (manualSafeMode)
            {
                safeModeManager.RecordManualActivation("Command line flag '--safe-mode'.");
            }

            if (disableSafeMode)
            {
                eventLogger.LogEvent("Bootstrap", "Safe mode override requested via '--disable-safe-mode'. Clearing scheduled safe mode.");
                safeModeManager.ClearScheduledSafeMode();
            }

            var safeModeActive = !disableSafeMode && (manualSafeMode || safeModeManager.IsSafeModeRequested);

            if (safeModeActive)
            {
                var description = manualSafeMode ? "Command line request (--safe-mode)." : safeModeManager.DescribeCurrentState();
                eventLogger.LogEvent("Bootstrap", $"Safe mode active. {description}");
            }

            var eventBus = new EventBus(eventLogger);
            var moduleHost = new ModuleHost(eventLogger, eventBus, safeModeManager);

            services.AddSingleton<ICancellationProvider, CancellationProvider>();
            services.AddSingleton<IEventLogger>(eventLogger);
            services.AddSingleton<IEventLogRepository>(eventLogRepository);
            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton<IRoundDataRepository>(roundDataRepository);
            services.AddSingleton<ISettingsRepository>(settingsRepository);
            services.AddSingleton(safeModeManager);
            services.AddSingleton(moduleHost);
            services.AddSingleton<IOSCListener>(sp => new OSCListener(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IWebSocketClient>(sp => new WebSocketClient(wsUrl, sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton(sp => new AutoSuicideService(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<StateService>();
            services.AddSingleton<IAppSettings>(sp => new AppSettings(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ISettingsRepository>()));
            services.AddSingleton(sp => new AutoRecordingService(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IInputSender, NativeInputSender>();
            services.AddSingleton<IErrorReporter>(sp => new ErrorReporter(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>()));
            services.AddSingleton<IHttpClient, HttpClientWrapper>();
            services.AddSingleton<IUiDispatcher, WinFormsDispatcher>();
            services.AddSingleton<MainPresenter>(sp => new MainPresenter(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<IHttpClient>()));
            Task.Run(() => ModuleLoader.LoadModules(services, moduleHost, eventLogger, eventBus, safeMode: safeModeActive)).GetAwaiter().GetResult();
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
                sp.GetRequiredService<AutoRecordingService>(),
                sp.GetRequiredService<ModuleHost>()));

            eventLogger.LogEvent("Bootstrap", "Building service provider (pre-build notifications).");
            moduleHost.NotifyServiceProviderBuilding(new ModuleServiceProviderBuildContext(services, eventLogger, eventBus));

            var provider = Task.Run(services.BuildServiceProvider).GetAwaiter().GetResult();
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
                if (args[i] == "--debug" || args[i] == "--safe-mode" || args[i] == "--disable-safe-mode")
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

            var launches = Task.Run(() => BuildAutoLaunchPlans(autoLaunchPath, autoLaunchArguments, appSettings)).GetAwaiter().GetResult();

            if (launches.Count > 0)
            {
                Task.Run(() => moduleHost.NotifyAutoLaunchEvaluating(new ModuleAutoLaunchEvaluationContext(launches, appSettings, provider))).GetAwaiter().GetResult();
                mainForm.Shown += async (s, e) => await Task.Run(() => ExecuteAutoLaunchPlans(launches, moduleHost, eventLogger, provider));
            }

            moduleHost.NotifyAppRunStarting(new ModuleAppRunContext(mainForm, provider));
            eventLogger.LogEvent("Bootstrap", "Starting WinForms message loop.");
            WinFormsApp.Run(mainForm);
            eventLogger.LogEvent("Bootstrap", "WinForms message loop exited.");
            moduleHost.NotifyAppRunCompleted(new ModuleAppRunContext(mainForm, provider));
            moduleHost.NotifyAppShutdownStarting(new ModuleAppShutdownContext(provider));
            eventLogger.LogEvent("Bootstrap", "Disposing service provider and shutting down.");
            Task.Run(() => (provider as IDisposable)?.Dispose()).GetAwaiter().GetResult();
            moduleHost.NotifyAppShutdownCompleted(new ModuleAppShutdownContext(provider));
            eventLogger.LogEvent("Bootstrap", "Application shutdown complete.");
        }

        private static async Task<AppSettingsData> LoadBootstrapAsync()
        {
            try
            {
                if (!File.Exists("appsettings.json"))
                {
                    return new AppSettingsData();
                }

                var json = await Task.Run(() => File.ReadAllText("appsettings.json")).ConfigureAwait(false);
                return JsonConvert.DeserializeObject<AppSettingsData>(json) ?? new AppSettingsData();
            }
            catch
            {
                return new AppSettingsData();
            }
        }

        private static List<AutoLaunchPlan> BuildAutoLaunchPlans(string? autoLaunchPath, string? autoLaunchArguments, IAppSettings appSettings)
        {
            var launches = new List<AutoLaunchPlan>();

            if (!string.IsNullOrWhiteSpace(autoLaunchPath))
            {
                launches.Add(new AutoLaunchPlan(autoLaunchPath!, autoLaunchArguments ?? string.Empty, "command line"));
            }
            else if (appSettings.AutoLaunchEnabled)
            {
                foreach (var entry in appSettings.AutoLaunchEntries ?? Enumerable.Empty<AutoLaunchEntry>())
                {
                    if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.ExecutablePath))
                    {
                        continue;
                    }

                    launches.Add(new AutoLaunchPlan(entry.ExecutablePath!, entry.Arguments ?? string.Empty, "settings"));
                }
            }

            return launches;
        }

        private static void ExecuteAutoLaunchPlans(IEnumerable<AutoLaunchPlan> launches, ModuleHost moduleHost, IEventLogger eventLogger, IServiceProvider provider)
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
        }
    }
}
