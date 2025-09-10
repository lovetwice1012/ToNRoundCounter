using System;
using System.Windows.Forms;
using ToNRoundCounter.Application;
using WinFormsApp = System.Windows.Forms.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Reports unhandled errors to the logger and user.
    /// </summary>
    public class ErrorReporter : IErrorReporter
    {
        private readonly IEventLogger _logger;

        public ErrorReporter(IEventLogger logger)
        {
            _logger = logger;
        }

        public void Register()
        {
            WinFormsApp.ThreadException += (s, e) => Handle(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Handle(e.ExceptionObject as Exception);
        }

        public void Handle(Exception ex)
        {
            if (ex == null) return;
            _logger.LogEvent("Unhandled", ex.ToString(), Serilog.Events.LogEventLevel.Error);
            try
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // ignore UI errors
            }
        }
    }
}
