using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rug.Osc;
using Serilog.Events;
using System.Windows.Forms;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.AfkJump
{
    public sealed class AfkJumpModule : IModule
    {
        public void OnModuleLoaded(ModuleDiscoveryContext context)
        {
        }

        public void OnBeforeServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<IAfkWarningHandler>(sp => new AfkJumpHandler(sp.GetRequiredService<IEventLogger>()));
        }

        public void OnAfterServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void OnBeforeServiceProviderBuild(ModuleServiceProviderBuildContext context)
        {
        }

        public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context)
        {
        }

        public void OnBeforeMainWindowCreation(ModuleMainWindowCreationContext context)
        {
        }

        public void OnAfterMainWindowCreation(ModuleMainWindowContext context)
        {
        }

        public void OnMainWindowShown(ModuleMainWindowLifecycleContext context)
        {
        }

        public void OnMainWindowClosing(ModuleMainWindowLifecycleContext context)
        {
        }

        public void OnSettingsLoading(ModuleSettingsContext context)
        {
        }

        public void OnSettingsLoaded(ModuleSettingsContext context)
        {
        }

        public void OnSettingsSaving(ModuleSettingsContext context)
        {
        }

        public void OnSettingsSaved(ModuleSettingsContext context)
        {
        }

        public void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context)
        {
            if (context == null)
            {
                return;
            }

            var group = context.AddSettingsGroup("AFK Jump");
            var description = new Label
            {
                AutoSize = true,
                Text = "AFKジャンプモジュールには設定項目はありません。"
            };

            group.Controls.Add(description);
        }

        public void OnSettingsViewOpened(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnSettingsViewApplying(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnSettingsViewClosing(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnSettingsViewClosed(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnAppRunStarting(ModuleAppRunContext context)
        {
        }

        public void OnAppRunCompleted(ModuleAppRunContext context)
        {
        }

        public void OnBeforeAppShutdown(ModuleAppShutdownContext context)
        {
        }

        public void OnAfterAppShutdown(ModuleAppShutdownContext context)
        {
        }

        public void OnUnhandledException(ModuleExceptionContext context)
        {
        }

        public void OnWebSocketConnecting(ModuleWebSocketConnectionContext context)
        {
        }

        public void OnWebSocketConnected(ModuleWebSocketConnectionContext context)
        {
        }

        public void OnWebSocketDisconnected(ModuleWebSocketConnectionContext context)
        {
        }

        public void OnWebSocketReconnecting(ModuleWebSocketConnectionContext context)
        {
        }

        public void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context)
        {
        }

        public void OnOscConnecting(ModuleOscConnectionContext context)
        {
        }

        public void OnOscConnected(ModuleOscConnectionContext context)
        {
        }

        public void OnOscDisconnected(ModuleOscConnectionContext context)
        {
        }

        public void OnOscMessageReceived(ModuleOscMessageContext context)
        {
        }

        public void OnBeforeSettingsValidation(ModuleSettingsValidationContext context)
        {
        }

        public void OnSettingsValidated(ModuleSettingsValidationContext context)
        {
        }

        public void OnSettingsValidationFailed(ModuleSettingsValidationContext context)
        {
        }

        public void OnAutoSuicideRulesPrepared(ModuleAutoSuicideRuleContext context)
        {
        }

        public void OnAutoSuicideDecisionEvaluated(ModuleAutoSuicideDecisionContext context)
        {
        }

        public void OnAutoSuicideScheduled(ModuleAutoSuicideScheduleContext context)
        {
        }

        public void OnAutoSuicideCancelled(ModuleAutoSuicideScheduleContext context)
        {
        }

        public void OnAutoSuicideTriggered(ModuleAutoSuicideTriggerContext context)
        {
        }

        public void OnAutoLaunchEvaluating(ModuleAutoLaunchEvaluationContext context)
        {
        }

        public void OnAutoLaunchStarting(ModuleAutoLaunchExecutionContext context)
        {
        }

        public void OnAutoLaunchFailed(ModuleAutoLaunchFailureContext context)
        {
        }

        public void OnAutoLaunchCompleted(ModuleAutoLaunchExecutionContext context)
        {
        }

        public void OnThemeCatalogBuilding(ModuleThemeCatalogContext context)
        {
        }

        public void OnMainWindowMenuBuilding(ModuleMainWindowMenuContext context)
        {
        }

        public void OnMainWindowUiComposed(ModuleMainWindowUiContext context)
        {
        }

        public void OnMainWindowThemeChanged(ModuleMainWindowThemeContext context)
        {
        }

        public void OnMainWindowLayoutUpdated(ModuleMainWindowLayoutContext context)
        {
        }

        public void OnAuxiliaryWindowCatalogBuilding(ModuleAuxiliaryWindowCatalogContext context)
        {
        }

        public void OnAuxiliaryWindowOpening(ModuleAuxiliaryWindowLifecycleContext context)
        {
        }

        public void OnAuxiliaryWindowOpened(ModuleAuxiliaryWindowLifecycleContext context)
        {
        }

        public void OnAuxiliaryWindowClosing(ModuleAuxiliaryWindowLifecycleContext context)
        {
        }

        public void OnAuxiliaryWindowClosed(ModuleAuxiliaryWindowLifecycleContext context)
        {
        }

        public void OnPeerModuleLoaded(ModulePeerNotificationContext<ModuleDiscoveryContext> context)
        {
        }

        public void OnPeerModuleBeforeServiceRegistration(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context)
        {
        }

        public void OnPeerModuleRegisteringServices(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context)
        {
        }

        public void OnPeerModuleAfterServiceRegistration(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context)
        {
        }

        public void OnPeerModuleBeforeServiceProviderBuild(ModulePeerNotificationContext<ModuleServiceProviderBuildContext> context)
        {
        }

        public void OnPeerModuleAfterServiceProviderBuild(ModulePeerNotificationContext<ModuleServiceProviderContext> context)
        {
        }

        public void OnPeerModuleBeforeMainWindowCreation(ModulePeerNotificationContext<ModuleMainWindowCreationContext> context)
        {
        }

        public void OnPeerModuleAfterMainWindowCreation(ModulePeerNotificationContext<ModuleMainWindowContext> context)
        {
        }

        public void OnPeerModuleMainWindowShown(ModulePeerNotificationContext<ModuleMainWindowLifecycleContext> context)
        {
        }

        public void OnPeerModuleMainWindowClosing(ModulePeerNotificationContext<ModuleMainWindowLifecycleContext> context)
        {
        }

        public void OnPeerModuleSettingsLoading(ModulePeerNotificationContext<ModuleSettingsContext> context)
        {
        }

        public void OnPeerModuleSettingsLoaded(ModulePeerNotificationContext<ModuleSettingsContext> context)
        {
        }

        public void OnPeerModuleSettingsSaving(ModulePeerNotificationContext<ModuleSettingsContext> context)
        {
        }

        public void OnPeerModuleSettingsSaved(ModulePeerNotificationContext<ModuleSettingsContext> context)
        {
        }

        public void OnPeerModuleAppRunStarting(ModulePeerNotificationContext<ModuleAppRunContext> context)
        {
        }

        public void OnPeerModuleAppRunCompleted(ModulePeerNotificationContext<ModuleAppRunContext> context)
        {
        }

        public void OnPeerModuleBeforeAppShutdown(ModulePeerNotificationContext<ModuleAppShutdownContext> context)
        {
        }

        public void OnPeerModuleAfterAppShutdown(ModulePeerNotificationContext<ModuleAppShutdownContext> context)
        {
        }

        public void OnPeerModuleUnhandledException(ModulePeerNotificationContext<ModuleExceptionContext> context)
        {
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
