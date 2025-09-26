using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using ToNRoundCounter.Application;

#nullable enable

namespace ToNRoundCounter.Modules.AfkSoundCancel
{
    public sealed class AfkSoundCancelModule : IModule
    {
        private CheckBox? _enableCheckBox;

        public void OnModuleLoaded(ModuleDiscoveryContext context)
        {
        }

        public void OnBeforeServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<IAfkWarningHandler, AfkSoundCancelHandler>();
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

            var group = context.AddSettingsGroup("AFK音キャンセル");
            var checkbox = new CheckBox
            {
                AutoSize = true,
                Text = "AFK警告音を再生しない",
                Checked = context.Settings.AfkSoundCancelEnabled
            };

            group.Controls.Add(checkbox);
            _enableCheckBox = checkbox;
        }

        public void OnSettingsViewOpened(ModuleSettingsViewLifecycleContext context)
        {
            if (context?.Settings == null || _enableCheckBox == null)
            {
                return;
            }

            _enableCheckBox.Checked = context.Settings.AfkSoundCancelEnabled;
        }

        public void OnSettingsViewApplying(ModuleSettingsViewLifecycleContext context)
        {
            if (context == null || context.Stage != ModuleSettingsViewStage.Applying)
            {
                return;
            }

            if (_enableCheckBox != null)
            {
                context.Settings.AfkSoundCancelEnabled = _enableCheckBox.Checked;
            }
        }

        public void OnSettingsViewClosing(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnSettingsViewClosed(ModuleSettingsViewLifecycleContext context)
        {
            _enableCheckBox = null;
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

    internal sealed class AfkSoundCancelHandler : IAfkWarningHandler
    {
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;

        public AfkSoundCancelHandler(IAppSettings settings, IEventLogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public Task<bool> HandleAsync(AfkWarningContext context, CancellationToken cancellationToken)
        {
            if (!_settings.AfkSoundCancelEnabled)
            {
                return Task.FromResult(false);
            }

            var idleSeconds = context?.IdleSeconds ?? 0d;
            _logger.LogEvent("AfkSoundCancel", $"AFK sound suppressed at {idleSeconds:F1} seconds.", LogEventLevel.Information);
            return Task.FromResult(true);
        }
    }
}
