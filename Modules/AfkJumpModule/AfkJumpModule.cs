using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rug.Osc;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.AfkJump
{
    public sealed class AfkJumpModule : IModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<IAfkWarningHandler>(sp => new AfkJumpHandler(sp.GetRequiredService<IEventLogger>()));
        }
    }

    internal sealed class AfkJumpHandler : IAfkWarningHandler
    {
        private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(0.2);
        private static readonly TimeSpan SecondDelay = TimeSpan.FromSeconds(0.8);
        private readonly IEventLogger _logger;

        public AfkJumpHandler(IEventLogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> HandleAsync(AfkWarningContext context, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogEvent("AfkJumpModule", "Suppressing AFK sound and sending jump input sequence.");
                using (var sender = new OscSender(IPAddress.Loopback, 0, 9000))
                {
                    sender.Connect();
                    SendJumpValue(sender, 0);
                    await Task.Delay(FirstDelay, cancellationToken);
                    SendJumpValue(sender, 1);
                    await Task.Delay(SecondDelay, cancellationToken);
                    SendJumpValue(sender, 0);
                    sender.Close();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogEvent("AfkJumpModule", "Jump input sequence cancelled.", LogEventLevel.Warning);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("AfkJumpModule", $"Failed to send jump input sequence: {ex}", LogEventLevel.Error);
                return false;
            }
        }

        private static void SendJumpValue(OscSender sender, int value)
        {
            var message = new OscMessage("/input/Jump", value);
            sender.Send(message);
        }
    }
}
