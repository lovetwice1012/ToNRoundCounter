using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Infrastructure.Services;
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
            if (TryRunUpdateInstallerMode(args))
            {
                return;
            }

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
                    exporter.ExportAsync(exportOptions!).GetAwaiter().GetResult();
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

            var launchTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logPath;
            if (string.IsNullOrWhiteSpace(bootstrap.LogFilePath))
            {
                logPath = Path.Combine("logs", $"log-{launchTimestamp}.txt");
            }
            else
            {
                var logDir = Path.GetDirectoryName(bootstrap.LogFilePath!) ?? "logs";
                var logBase = Path.GetFileNameWithoutExtension(bootstrap.LogFilePath!);
                var logExt = Path.GetExtension(bootstrap.LogFilePath!);
                if (string.IsNullOrEmpty(logExt)) logExt = ".txt";
                logPath = Path.Combine(logDir, $"{logBase}{launchTimestamp}{logExt}");
            }
            var wsIp = string.IsNullOrWhiteSpace(bootstrap.WebSocketIp) ? "127.0.0.1" : bootstrap.WebSocketIp;
            var wsUrl = $"ws://{wsIp}:11398";

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logPath);

            Log.Logger = loggerConfiguration.CreateLogger();

            // Compress old logs in the background to avoid blocking startup.
            // Directory scans + GZip compression can take 50-500ms on large log folders.
            var compressLogDir = Path.GetDirectoryName(logPath) ?? "logs";
            _ = Task.Run(() =>
            {
                try
                {
                    CompressOldLogs(compressLogDir, TimeSpan.FromDays(3));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Background log compression failed.");
                }
            });

            var dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            Directory.CreateDirectory(dataDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
            var roundDataPath = Path.Combine(dataDirectory, "rounds", $"{timestamp}.sqlite");
            var statisticsPath = Path.Combine(dataDirectory, "statistics", $"{timestamp}.sqlite");
            var settingsPath = Path.Combine(dataDirectory, "settings", $"{timestamp}.sqlite");

            using var roundDataRepository = new SqliteRoundDataRepository(roundDataPath);
            using var eventLogRepository = new SqliteEventLogRepository(statisticsPath);
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
            
            // Cloud WebSocket Client for ToNRoundCounter Cloud integration
            var cloudWsUrl = string.IsNullOrWhiteSpace(bootstrap.CloudWebSocketUrl) ? AppSettings.DefaultCloudWebSocketUrl : bootstrap.CloudWebSocketUrl;
            var cloudApiKey = bootstrap.CloudApiKey;
            var cloudPlayerName = bootstrap.CloudPlayerName;
            eventLogger.LogEvent("Bootstrap", $"Cloud WebSocket endpoint: {cloudWsUrl}");
            eventLogger.LogEvent("Bootstrap", $"Cloud API Key configured: {!string.IsNullOrWhiteSpace(cloudApiKey)}");
            eventLogger.LogEvent("Bootstrap", $"Cloud Player Name: {cloudPlayerName}");
            services.AddSingleton(sp => new CloudWebSocketClient(
                cloudWsUrl,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<ICancellationProvider>(),
                sp.GetRequiredService<IEventLogger>(),
                cloudApiKey,      // Pass API key from bootstrap settings
                null));           // Player identity is accepted only from the local WS CONNECTED event
            
            services.AddSingleton(sp => new AutoSuicideService(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<StateService>();
            services.AddSingleton<IAppSettings>(sp => new AppSettings(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ISettingsRepository>()));
            services.AddSingleton(sp => new AutoRecordingService(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IInputSender, NativeInputSender>();
            services.AddSingleton<IOscRepeaterService>(sp => new OscRepeaterService(
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<ICancellationProvider>()));
            services.AddSingleton<IErrorReporter>(sp => new ErrorReporter(sp.GetRequiredService<IEventLogger>(), sp.GetRequiredService<IEventBus>()));
            services.AddSingleton<IHttpClient, HttpClientWrapper>();
            services.AddSingleton<IUiDispatcher, WinFormsDispatcher>();
            services.AddSingleton<ToNRoundCounter.Infrastructure.Services.YoutubeAudioCache>(sp =>
                new ToNRoundCounter.Infrastructure.Services.YoutubeAudioCache(sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<ISoundManager>(sp => new SoundManager(
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<ToNRoundCounter.Infrastructure.Services.YoutubeAudioCache>()));
            services.AddSingleton<IModuleSoundApi>(sp => new ToNRoundCounter.Infrastructure.Services.ModuleSoundApi(
                sp.GetRequiredService<ISoundManager>(),
                sp.GetRequiredService<IAppSettings>()));
            services.AddSingleton<IOverlayManager>(sp => new OverlayManager(
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IUiDispatcher>()));
            services.AddSingleton<IAutoSuicideCoordinator>(sp => new AutoSuicideCoordinator(
                sp.GetRequiredService<AutoSuicideService>(),
                sp.GetRequiredService<IInputSender>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<IOverlayManager>(),
                sp.GetRequiredService<ModuleHost>()));
            services.AddSingleton<MainPresenter>(sp => new MainPresenter(
                sp.GetRequiredService<StateService>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<IEventLogger>(),
                sp.GetRequiredService<IHttpClient>(),
                sp.GetRequiredService<CloudWebSocketClient>(),
                sp.GetRequiredService<ICancellationProvider>()));
            ModuleLoader.LoadModules(services, moduleHost, eventLogger, eventBus, safeMode: safeModeActive);
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
                sp.GetRequiredService<ModuleHost>(),
                sp.GetRequiredService<CloudWebSocketClient>(),
                sp.GetRequiredService<ISoundManager>(),
                sp.GetRequiredService<IOverlayManager>(),
                sp.GetRequiredService<IAutoSuicideCoordinator>(),
                sp.GetRequiredService<IOscRepeaterService>()));

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

            var launches = BuildAutoLaunchPlans(autoLaunchPath, autoLaunchArguments, appSettings);

            if (launches.Count > 0)
            {
                moduleHost.NotifyAutoLaunchEvaluating(new ModuleAutoLaunchEvaluationContext(launches, appSettings, provider));
                mainForm.Shown += async (s, e) => await Task.Run(() => ExecuteAutoLaunchPlans(launches, moduleHost, eventLogger, provider));
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

        internal static bool TryLaunchUpdateInstaller(string zipPath, string targetExe, out string? error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                {
                    error = "Update package was not found.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
                {
                    error = "Target executable was not found.";
                    return false;
                }

                string currentExe = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? WinFormsApp.ExecutablePath;
                if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                {
                    error = "Current executable path could not be resolved.";
                    return false;
                }

                string entryAssemblyPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
                if (!string.IsNullOrEmpty(entryAssemblyPath) && File.Exists(entryAssemblyPath))
                {
                    error = "Self-update mode requires a single-file build.";
                    return false;
                }

                string updaterDirectory = Path.Combine(Path.GetTempPath(), "ToNRoundCounter", "updaters");
                Directory.CreateDirectory(updaterDirectory);
                string updaterExe = Path.Combine(updaterDirectory, $"ToNRoundCounter.Update.{Guid.NewGuid():N}.exe");
                File.Copy(currentExe, updaterExe, overwrite: true);

                var startInfo = new ProcessStartInfo(updaterExe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(targetExe) ?? Environment.CurrentDirectory,
                };
                startInfo.ArgumentList.Add("--apply-update");
                startInfo.ArgumentList.Add(zipPath);
                startInfo.ArgumentList.Add(targetExe);

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryRunUpdateInstallerMode(string[] args)
        {
            if (args.Length == 0 || !string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: ToNRoundCounter --apply-update <zipPath> <targetExe>");
                Environment.ExitCode = 1;
                return true;
            }

            Environment.ExitCode = RunUpdateInstaller(args[1], args[2]);
            return true;
        }

        private static int RunUpdateInstaller(string zipPath, string targetExe)
        {
            string targetDir = Path.GetDirectoryName(targetExe) ?? ".";
            string targetRoot = Path.GetFullPath(targetDir);

            for (int i = 0; i < 30 && IsFileLocked(targetExe); i++)
            {
                Thread.Sleep(1000);
            }

            var backupDirectory = Path.Combine(targetRoot, $".update-backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            var backedUpEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string destinationPath = ResolveUpdateEntryPath(targetRoot, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            var destinationDirectory = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destinationDirectory))
                            {
                                Directory.CreateDirectory(destinationDirectory);
                            }

                            if (IsAudioEntry(entry) && File.Exists(destinationPath) && HasFileChanged(entry, destinationPath))
                            {
                                Console.WriteLine($"Skipping modified audio file: {entry.FullName}");
                                continue;
                            }

                            string backupRelativePath = Path.GetRelativePath(targetRoot, destinationPath);
                            BackupExistingFile(destinationPath, backupRelativePath, backupDirectory, backedUpEntries);
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }

                File.Delete(zipPath);

                if (Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update failed: " + ex.Message);
                TryRollback(targetRoot, backupDirectory, backedUpEntries);
                return 1;
            }

            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            return 0;
        }

        private static string ResolveUpdateEntryPath(string targetRoot, string entryFullName)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(targetRoot, entryFullName));
            string normalizedRoot = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Update package entry escapes the target directory: {entryFullName}");
            }

            return destinationPath;
        }

        private static void BackupExistingFile(string destinationPath, string relativeEntryPath, string backupDirectory, HashSet<string> backedUpEntries)
        {
            if (!File.Exists(destinationPath) || backedUpEntries.Contains(relativeEntryPath))
            {
                return;
            }

            var backupPath = Path.Combine(backupDirectory, relativeEntryPath);
            var backupDir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            Directory.CreateDirectory(backupDirectory);
            File.Copy(destinationPath, backupPath, true);
            backedUpEntries.Add(relativeEntryPath);
        }

        private static void TryRollback(string targetDir, string backupDirectory, HashSet<string> backedUpEntries)
        {
            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            try
            {
                foreach (var relativePath in backedUpEntries)
                {
                    var backupPath = Path.Combine(backupDirectory, relativePath);
                    var restorePath = Path.Combine(targetDir, relativePath);
                    var restoreDir = Path.GetDirectoryName(restorePath);

                    if (!string.IsNullOrEmpty(restoreDir))
                    {
                        Directory.CreateDirectory(restoreDir);
                    }

                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, restorePath, true);
                    }
                }
            }
            catch (Exception rollbackEx)
            {
                Console.WriteLine("Rollback failed: " + rollbackEx.Message);
            }
            finally
            {
                try
                {
                    Directory.Delete(backupDirectory, true);
                }
                catch
                {
                }
            }
        }

        private static bool IsFileLocked(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
        }

        private static bool IsAudioEntry(ZipArchiveEntry entry)
        {
            var normalizedPath = entry.FullName.Replace('\\', '/');
            return normalizedPath.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasFileChanged(ZipArchiveEntry entry, string destinationPath)
        {
            try
            {
                using var entryStream = entry.Open();
                using var destinationStream = File.OpenRead(destinationPath);

                if (entry.Length != destinationStream.Length)
                {
                    return true;
                }

                const int bufferSize = 81920;
                var entryBuffer = new byte[bufferSize];
                var destinationBuffer = new byte[bufferSize];

                int entryRead;
                while ((entryRead = entryStream.Read(entryBuffer, 0, bufferSize)) > 0)
                {
                    var destinationRead = destinationStream.Read(destinationBuffer, 0, bufferSize);
                    if (entryRead != destinationRead)
                    {
                        return true;
                    }

                    for (int i = 0; i < entryRead; i++)
                    {
                        if (entryBuffer[i] != destinationBuffer[i])
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static async Task<AppSettingsData> LoadBootstrapAsync()
        {
            try
            {
                if (!File.Exists("appsettings.json"))
                {
                    return new AppSettingsData();
                }

                var json = await File.ReadAllTextAsync("appsettings.json").ConfigureAwait(false);
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

        /// <summary>
        /// Validates that the executable path is safe to launch.
        /// </summary>
        private static bool IsExecutablePathSafe(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            // Check for valid executable extensions
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var allowedExtensions = new[] { ".exe", ".bat", ".cmd", ".com", ".msi" };
            if (!allowedExtensions.Contains(extension))
            {
                errorMessage = $"File extension '{extension}' is not allowed for auto-launch. Allowed extensions: {string.Join(", ", allowedExtensions)}";
                return false;
            }

            // Warn if trying to launch from Windows system directories
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant();
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            var lowerPath = filePath.ToLowerInvariant();

            if (lowerPath.StartsWith(systemDir) || lowerPath.StartsWith(windowsDir))
            {
                // Allow but log warning
                errorMessage = null; // Not an error, but we'll log it
                return true;
            }

            return true;
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

                    // Validate executable path for security
                    if (!IsExecutablePathSafe(resolvedPath, out string? securityError))
                    {
                        var failureContext = new ModuleAutoLaunchFailureContext(launch, new InvalidOperationException(securityError), provider);
                        moduleHost.NotifyAutoLaunchFailed(failureContext);
                        eventLogger.LogEvent("AutoLaunch", $"Security validation failed ({launch.Origin}): {securityError}", Serilog.Events.LogEventLevel.Error);
                        continue;
                    }

                    // Log warning if launching from system directory
                    var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant();
                    var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
                    var lowerPath = resolvedPath.ToLowerInvariant();
                    if (lowerPath.StartsWith(systemDir) || lowerPath.StartsWith(windowsDir))
                    {
                        eventLogger.LogEvent("AutoLaunch", $"WARNING: Launching executable from system directory ({launch.Origin}): {resolvedPath}", Serilog.Events.LogEventLevel.Warning);
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

        private static void CompressOldLogs(string logDirectory, TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(logDirectory)) return;

                var cutoff = DateTime.Now - maxAge;
                foreach (var file in Directory.EnumerateFiles(logDirectory, "*.txt"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) >= cutoff) continue;

                        var gzPath = file + ".gz";
                        if (File.Exists(gzPath)) continue;

                        using (var source = File.OpenRead(file))
                        using (var destination = File.Create(gzPath))
                        using (var gz = new GZipStream(destination, CompressionLevel.Optimal))
                        {
                            source.CopyTo(gz);
                        }

                        File.Delete(file);
                    }
                    catch
                    {
                        // Skip files that are locked or otherwise inaccessible.
                    }
                }
            }
            catch
            {
                // Non-critical: do not let cleanup errors block startup.
            }
        }
    }
}
