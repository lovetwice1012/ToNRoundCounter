using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rug.Osc;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;

namespace ToNRoundCounter.Application
{
    public record WebSocketConnecting(Uri Endpoint);
    public record WebSocketConnected(Uri Endpoint);
    public record WebSocketDisconnected(Uri Endpoint, Exception? Exception = null);
    public record WebSocketReconnecting(Uri Endpoint, Exception Exception);
    public record WebSocketMessageReceived(string Message);
    public record OscConnecting(int Port);
    public record OscConnected(int Port);
    public record OscDisconnected(int Port, Exception? Exception = null);
    public record OscMessageReceived(OscMessage Message);
    public record SettingsValidating(IAppSettings Settings, IList<string> Errors);
    public record SettingsValidated(IAppSettings Settings);
    public record SettingsValidationFailed(IAppSettings Settings, IReadOnlyList<string> Errors);
    public record ModuleLoadFailed(string File, Exception Exception);

    public record ModuleDiscoveryStarted(string Directory);
    public record ModuleDiscoveryCompleted(IReadOnlyList<ModuleDiscoveryContext> Modules);
    public record ModuleDiscovered(ModuleDiscoveryContext Context);
    public record ModuleLoaded(ModuleDiscoveryContext Context);
    public record ModuleServicesRegistering(ModuleServiceRegistrationContext Context);
    public record ModuleServicesRegistered(ModuleServiceRegistrationContext Context);
    public record ModuleServiceRegistrationFailed(ModuleDiscoveryContext Context, Exception Exception);
    public record ModuleCallbackFailed(ModuleDiscoveryContext Context, string Stage, Exception Exception);
    public record ServiceProviderBuilding(ModuleServiceProviderBuildContext Context);
    public record ServiceProviderBuilt(ModuleServiceProviderContext Context);
    public record MainWindowCreating(ModuleMainWindowCreationContext Context);
    public record MainWindowCreated(ModuleMainWindowContext Context);
    public record MainWindowShown(ModuleMainWindowLifecycleContext Context);
    public record MainWindowClosing(ModuleMainWindowLifecycleContext Context);
    public record SettingsLoading(IAppSettings Settings);
    public record SettingsLoaded(IAppSettings Settings);
    public record SettingsSaving(IAppSettings Settings);
    public record SettingsSaved(IAppSettings Settings);
    public record AutoSuicideRulesPrepared(IReadOnlyList<AutoSuicideRule> Rules, IAppSettings Settings);
    public record AutoSuicideDecisionEvaluated(string RoundType, string? TerrorName, int Decision, bool HasPendingDelayed);
    public record AutoSuicideScheduled(TimeSpan Delay, bool ResetStartTime);
    public record AutoSuicideCancelled(TimeSpan? RemainingDelay);
    public record AutoSuicideTriggered();
    public record AutoLaunchEvaluating(IReadOnlyList<AutoLaunchPlan> Plans, IAppSettings Settings);
    public record AutoLaunchStarting(AutoLaunchPlan Plan);
    public record AutoLaunchFailed(AutoLaunchPlan Plan, Exception? Exception);
    public record AutoLaunchCompleted(AutoLaunchPlan Plan);
    public record MainWindowThemeChanged(string ThemeKey, ThemeDescriptor Theme, Form Form);
    public record MainWindowLayoutUpdated(Form Form);
    public record AppRunStarting(ModuleAppRunContext Context);
    public record AppRunCompleted(ModuleAppRunContext Context);
    public record AppShutdownStarting(ModuleAppShutdownContext Context);
    public record AppShutdownCompleted(ModuleAppShutdownContext Context);
    public record UnhandledExceptionOccurred(Exception Exception, bool IsTerminating);
}
