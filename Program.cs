using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using ToNRoundCounter.UI;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
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
                sp.GetRequiredService<IUiDispatcher>()));

            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IErrorReporter>().Register();

            if (args.Contains("--debug") &&
                args.SkipWhile(a => a != "--test").Skip(1).FirstOrDefault() == "crashreporting")
            {
                throw new InvalidOperationException("Crash report test triggered");
            }

            WinFormsApp.Run(provider.GetRequiredService<MainForm>());
            (provider as IDisposable)?.Dispose();
        }
    }
}
