using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using ToNRoundCounter.UI;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddSingleton<ICancellationProvider, CancellationProvider>();
            services.AddSingleton<IEventLogger, EventLogger>();
            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<IOSCListener>(sp => new OSCListener(sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<IWebSocketClient>(sp => new WebSocketClient("ws://127.0.0.1:11398", sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ICancellationProvider>(), sp.GetRequiredService<IEventLogger>()));
            services.AddSingleton<AutoSuicideService>();
            services.AddSingleton<StateService>();
            services.AddSingleton<IAppSettings, AppSettings>();
            services.AddSingleton<IInputSender, NativeInputSender>();
            services.AddSingleton<IErrorReporter, ErrorReporter>();
            services.AddSingleton<IHttpClient, HttpClientWrapper>();
            services.AddSingleton<IUiDispatcher, WinFormsDispatcher>();
            services.AddSingleton<MainPresenter>();
            ModuleLoader.LoadModules(services);
            services.AddSingleton<MainForm>();

            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IErrorReporter>().Register();
                Application.Run(provider.GetRequiredService<MainForm>());
            }
        }
    }
}
