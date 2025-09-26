using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.OscRepeaterDisabler
{
    public sealed class OscRepeaterDisablerModule : IModule
    {
        public void OnModuleLoaded(ModuleDiscoveryContext context)
        {
        }

        public void OnBeforeServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<IOscRepeaterPolicy, OscRepeaterDisablePolicy>();
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

            var group = context.AddSettingsGroup("OSCRepeater無効化");
            var description = new Label
            {
                AutoSize = true,
                Text = "OSCRepeater.exe を起動しません。OSCポートは未変更なら 9001 を使用します。"
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

    internal sealed class OscRepeaterDisablePolicy : IOscRepeaterPolicy
    {
        private readonly IEventLogger _logger;

        public OscRepeaterDisablePolicy(IEventLogger logger)
        {
            _logger = logger;
        }

        public bool ShouldStartOscRepeater(IAppSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (!settings.OSCPortChanged)
            {
                settings.OSCPort = 9001;
            }

            _logger.TryLogEvent("OscRepeaterDisabler", "Preventing OSC repeater startup.");
            return false;
        }
    }

    internal static class EventLoggerCompatibilityExtensions
    {
        private static readonly Type[] SignatureWithLevel =
        {
            typeof(string),
            typeof(string),
            typeof(LogEventLevel)
        };

        private static readonly Type[] SignatureWithoutLevel =
        {
            typeof(string),
            typeof(string)
        };

        public static void TryLogEvent(this IEventLogger? logger, string eventType, string message, LogEventLevel level = LogEventLevel.Information)
        {
            if (logger == null)
            {
                return;
            }

            var loggerType = logger.GetType();

            var withLevel = loggerType.GetMethod(nameof(IEventLogger.LogEvent), SignatureWithLevel);
            if (withLevel != null)
            {
                withLevel.Invoke(logger, new object[] { eventType, message, level });
                return;
            }

            var withoutLevel = loggerType.GetMethod(nameof(IEventLogger.LogEvent), SignatureWithoutLevel);
            withoutLevel?.Invoke(logger, new object[] { eventType, message });
        }
    }
}
