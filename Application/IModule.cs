using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Rug.Osc;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Represents an extension module that can register its own services and
    /// participate in application lifecycle events.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Called immediately after the module has been discovered.
        /// </summary>
        /// <param name="context">Contextual information about the module discovery.</param>
        void OnModuleLoaded(ModuleDiscoveryContext context);

        /// <summary>
        /// Called before dependency injection registrations occur.
        /// </summary>
        /// <param name="context">Contextual information about the registration operation.</param>
        void OnBeforeServiceRegistration(ModuleServiceRegistrationContext context);

        /// <summary>
        /// Registers module specific services with the provided <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">Service collection used during application composition.</param>
        void RegisterServices(IServiceCollection services);

        /// <summary>
        /// Called after dependency injection registrations have completed.
        /// </summary>
        /// <param name="context">Contextual information about the registration operation.</param>
        void OnAfterServiceRegistration(ModuleServiceRegistrationContext context);

        /// <summary>
        /// Called before the <see cref="IServiceProvider"/> is built.
        /// </summary>
        /// <param name="context">Contextual information about the service provider build.</param>
        void OnBeforeServiceProviderBuild(ModuleServiceProviderBuildContext context);

        /// <summary>
        /// Called after the <see cref="IServiceProvider"/> has been created.
        /// </summary>
        /// <param name="context">Contextual information about the created provider.</param>
        void OnAfterServiceProviderBuild(ModuleServiceProviderContext context);

        /// <summary>
        /// Called before the main window is instantiated.
        /// </summary>
        /// <param name="context">Contextual information about the window creation.</param>
        void OnBeforeMainWindowCreation(ModuleMainWindowCreationContext context);

        /// <summary>
        /// Called after the main window has been instantiated.
        /// </summary>
        /// <param name="context">Contextual information about the instantiated window.</param>
        void OnAfterMainWindowCreation(ModuleMainWindowContext context);

        /// <summary>
        /// Called when the main window has been shown to the user.
        /// </summary>
        /// <param name="context">Contextual information about the window lifecycle.</param>
        void OnMainWindowShown(ModuleMainWindowLifecycleContext context);

        /// <summary>
        /// Called when the main window is about to close.
        /// </summary>
        /// <param name="context">Contextual information about the window lifecycle.</param>
        void OnMainWindowClosing(ModuleMainWindowLifecycleContext context);

        /// <summary>
        /// Called before settings are loaded from storage.
        /// </summary>
        /// <param name="context">Contextual information about the settings lifecycle.</param>
        void OnSettingsLoading(ModuleSettingsContext context);

        /// <summary>
        /// Called after settings have been loaded from storage.
        /// </summary>
        /// <param name="context">Contextual information about the settings lifecycle.</param>
        void OnSettingsLoaded(ModuleSettingsContext context);

        /// <summary>
        /// Called before settings are persisted.
        /// </summary>
        /// <param name="context">Contextual information about the settings lifecycle.</param>
        void OnSettingsSaving(ModuleSettingsContext context);

        /// <summary>
        /// Called after settings have been persisted.
        /// </summary>
        /// <param name="context">Contextual information about the settings lifecycle.</param>
        void OnSettingsSaved(ModuleSettingsContext context);

        /// <summary>
        /// Called when the settings view is being composed prior to display.
        /// </summary>
        /// <param name="context">Contextual information about the settings UI.</param>
        void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context);

        /// <summary>
        /// Called once the settings view has been shown.
        /// </summary>
        /// <param name="context">Contextual information about the settings UI lifecycle.</param>
        void OnSettingsViewOpened(ModuleSettingsViewLifecycleContext context);

        /// <summary>
        /// Called when the user confirms the settings view and changes are about to be applied.
        /// </summary>
        /// <param name="context">Contextual information about the settings UI lifecycle.</param>
        void OnSettingsViewApplying(ModuleSettingsViewLifecycleContext context);

        /// <summary>
        /// Called when the settings view is closing.
        /// </summary>
        /// <param name="context">Contextual information about the settings UI lifecycle.</param>
        void OnSettingsViewClosing(ModuleSettingsViewLifecycleContext context);

        /// <summary>
        /// Called after the settings view has closed.
        /// </summary>
        /// <param name="context">Contextual information about the settings UI lifecycle.</param>
        void OnSettingsViewClosed(ModuleSettingsViewLifecycleContext context);

        /// <summary>
        /// Called immediately before the WinForms message loop begins.
        /// </summary>
        /// <param name="context">Contextual information about the application run.</param>
        void OnAppRunStarting(ModuleAppRunContext context);

        /// <summary>
        /// Called once the WinForms message loop has finished executing.
        /// </summary>
        /// <param name="context">Contextual information about the application run.</param>
        void OnAppRunCompleted(ModuleAppRunContext context);

        /// <summary>
        /// Called prior to application shutdown.
        /// </summary>
        /// <param name="context">Contextual information about the shutdown sequence.</param>
        void OnBeforeAppShutdown(ModuleAppShutdownContext context);

        /// <summary>
        /// Called after application shutdown has completed.
        /// </summary>
        /// <param name="context">Contextual information about the shutdown sequence.</param>
        void OnAfterAppShutdown(ModuleAppShutdownContext context);

        /// <summary>
        /// Called whenever an unhandled exception is observed by the host.
        /// </summary>
        /// <param name="context">Contextual information about the exception.</param>
        void OnUnhandledException(ModuleExceptionContext context);

        /// <summary>
        /// Called when the application begins establishing the WebSocket connection.
        /// </summary>
        /// <param name="context">Contextual information about the WebSocket lifecycle.</param>
        void OnWebSocketConnecting(ModuleWebSocketConnectionContext context);

        /// <summary>
        /// Called when the WebSocket connection has been established.
        /// </summary>
        /// <param name="context">Contextual information about the WebSocket lifecycle.</param>
        void OnWebSocketConnected(ModuleWebSocketConnectionContext context);

        /// <summary>
        /// Called when the WebSocket connection has been closed.
        /// </summary>
        /// <param name="context">Contextual information about the WebSocket lifecycle.</param>
        void OnWebSocketDisconnected(ModuleWebSocketConnectionContext context);

        /// <summary>
        /// Called when the WebSocket client schedules a reconnection attempt.
        /// </summary>
        /// <param name="context">Contextual information about the WebSocket lifecycle.</param>
        void OnWebSocketReconnecting(ModuleWebSocketConnectionContext context);

        /// <summary>
        /// Called whenever a WebSocket message is received.
        /// </summary>
        /// <param name="context">Information about the received message.</param>
        void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context);

        /// <summary>
        /// Called when the OSC listener is preparing to connect.
        /// </summary>
        /// <param name="context">Contextual information about the OSC lifecycle.</param>
        void OnOscConnecting(ModuleOscConnectionContext context);

        /// <summary>
        /// Called when the OSC listener has connected successfully.
        /// </summary>
        /// <param name="context">Contextual information about the OSC lifecycle.</param>
        void OnOscConnected(ModuleOscConnectionContext context);

        /// <summary>
        /// Called when the OSC listener disconnects.
        /// </summary>
        /// <param name="context">Contextual information about the OSC lifecycle.</param>
        void OnOscDisconnected(ModuleOscConnectionContext context);

        /// <summary>
        /// Called whenever an OSC message is received.
        /// </summary>
        /// <param name="context">Information about the received message.</param>
        void OnOscMessageReceived(ModuleOscMessageContext context);

        /// <summary>
        /// Called before built-in validation runs for application settings.
        /// </summary>
        /// <param name="context">Contextual information about the validation pipeline.</param>
        void OnBeforeSettingsValidation(ModuleSettingsValidationContext context);

        /// <summary>
        /// Called after settings have been validated without critical failures.
        /// </summary>
        /// <param name="context">Contextual information about the validation pipeline.</param>
        void OnSettingsValidated(ModuleSettingsValidationContext context);

        /// <summary>
        /// Called when settings validation reports one or more failures.
        /// </summary>
        /// <param name="context">Contextual information about the validation pipeline.</param>
        void OnSettingsValidationFailed(ModuleSettingsValidationContext context);

        /// <summary>
        /// Called after auto-suicide rules have been composed from application settings.
        /// </summary>
        /// <param name="context">Contextual information about the rule set.</param>
        void OnAutoSuicideRulesPrepared(ModuleAutoSuicideRuleContext context);

        /// <summary>
        /// Called after the auto-suicide decision logic has evaluated the current round.
        /// </summary>
        /// <param name="context">Contextual information about the decision.</param>
        void OnAutoSuicideDecisionEvaluated(ModuleAutoSuicideDecisionContext context);

        /// <summary>
        /// Called when a delayed auto-suicide action has been scheduled.
        /// </summary>
        /// <param name="context">Contextual information about the scheduled action.</param>
        void OnAutoSuicideScheduled(ModuleAutoSuicideScheduleContext context);

        /// <summary>
        /// Called when a pending auto-suicide action has been cancelled.
        /// </summary>
        /// <param name="context">Contextual information about the scheduled action.</param>
        void OnAutoSuicideCancelled(ModuleAutoSuicideScheduleContext context);

        /// <summary>
        /// Called when the scheduled auto-suicide callback is about to be executed.
        /// </summary>
        /// <param name="context">Contextual information about the execution.</param>
        void OnAutoSuicideTriggered(ModuleAutoSuicideTriggerContext context);

        /// <summary>
        /// Called when the application determines which executables should be launched automatically.
        /// </summary>
        /// <param name="context">Contextual information about the auto-launch evaluation.</param>
        void OnAutoLaunchEvaluating(ModuleAutoLaunchEvaluationContext context);

        /// <summary>
        /// Called immediately before an auto-launch process is started.
        /// </summary>
        /// <param name="context">Contextual information about the auto-launch execution.</param>
        void OnAutoLaunchStarting(ModuleAutoLaunchExecutionContext context);

        /// <summary>
        /// Called when an auto-launch attempt fails.
        /// </summary>
        /// <param name="context">Contextual information about the auto-launch failure.</param>
        void OnAutoLaunchFailed(ModuleAutoLaunchFailureContext context);

        /// <summary>
        /// Called after an auto-launch process has started successfully.
        /// </summary>
        /// <param name="context">Contextual information about the auto-launch execution.</param>
        void OnAutoLaunchCompleted(ModuleAutoLaunchExecutionContext context);

        /// <summary>
        /// Called while the available UI themes are being collected.
        /// </summary>
        /// <param name="context">Contextual information about the theme catalog.</param>
        void OnThemeCatalogBuilding(ModuleThemeCatalogContext context);

        /// <summary>
        /// Called when the main window menu is being constructed.
        /// </summary>
        /// <param name="context">Contextual information about the main menu.</param>
        void OnMainWindowMenuBuilding(ModuleMainWindowMenuContext context);

        /// <summary>
        /// Called after the main window has constructed its core controls.
        /// </summary>
        /// <param name="context">Contextual information about the UI composition.</param>
        void OnMainWindowUiComposed(ModuleMainWindowUiContext context);

        /// <summary>
        /// Called while auxiliary (tool) windows are being registered with the host.
        /// </summary>
        /// <param name="context">Contextual information about the auxiliary window catalog.</param>
        void OnAuxiliaryWindowCatalogBuilding(ModuleAuxiliaryWindowCatalogContext context);

        /// <summary>
        /// Called immediately before an auxiliary window is shown.
        /// </summary>
        /// <param name="context">Contextual information about the auxiliary window lifecycle.</param>
        void OnAuxiliaryWindowOpening(ModuleAuxiliaryWindowLifecycleContext context);

        /// <summary>
        /// Called after an auxiliary window has been shown.
        /// </summary>
        /// <param name="context">Contextual information about the auxiliary window lifecycle.</param>
        void OnAuxiliaryWindowOpened(ModuleAuxiliaryWindowLifecycleContext context);

        /// <summary>
        /// Called when an auxiliary window begins closing.
        /// </summary>
        /// <param name="context">Contextual information about the auxiliary window lifecycle.</param>
        void OnAuxiliaryWindowClosing(ModuleAuxiliaryWindowLifecycleContext context);

        /// <summary>
        /// Called after an auxiliary window has closed.
        /// </summary>
        /// <param name="context">Contextual information about the auxiliary window lifecycle.</param>
        void OnAuxiliaryWindowClosed(ModuleAuxiliaryWindowLifecycleContext context);

        /// <summary>
        /// Called whenever the main window theme changes.
        /// </summary>
        /// <param name="context">Contextual information about the theme change.</param>
        void OnMainWindowThemeChanged(ModuleMainWindowThemeContext context);

        /// <summary>
        /// Called when the main window layout has been (re)constructed.
        /// </summary>
        /// <param name="context">Contextual information about the layout.</param>
        void OnMainWindowLayoutUpdated(ModuleMainWindowLayoutContext context);

        /// <summary>
        /// Called when another module reports that it has completed loading.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleLoaded(ModulePeerNotificationContext<ModuleDiscoveryContext> context);

        /// <summary>
        /// Called when another module begins its service registration sequence.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleBeforeServiceRegistration(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context);

        /// <summary>
        /// Called while another module is registering services.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleRegisteringServices(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context);

        /// <summary>
        /// Called after another module completes its service registration sequence.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAfterServiceRegistration(ModulePeerNotificationContext<ModuleServiceRegistrationContext> context);

        /// <summary>
        /// Called when another module begins participating in the service provider build.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleBeforeServiceProviderBuild(ModulePeerNotificationContext<ModuleServiceProviderBuildContext> context);

        /// <summary>
        /// Called when another module has completed its service provider build callbacks.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAfterServiceProviderBuild(ModulePeerNotificationContext<ModuleServiceProviderContext> context);

        /// <summary>
        /// Called when another module is about to create the main window.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleBeforeMainWindowCreation(ModulePeerNotificationContext<ModuleMainWindowCreationContext> context);

        /// <summary>
        /// Called when another module has finished creating the main window.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAfterMainWindowCreation(ModulePeerNotificationContext<ModuleMainWindowContext> context);

        /// <summary>
        /// Called when another module reports that the main window is shown.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleMainWindowShown(ModulePeerNotificationContext<ModuleMainWindowLifecycleContext> context);

        /// <summary>
        /// Called when another module reports that the main window is closing.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleMainWindowClosing(ModulePeerNotificationContext<ModuleMainWindowLifecycleContext> context);

        /// <summary>
        /// Called when another module participates in settings loading.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleSettingsLoading(ModulePeerNotificationContext<ModuleSettingsContext> context);

        /// <summary>
        /// Called when another module completes settings loading.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleSettingsLoaded(ModulePeerNotificationContext<ModuleSettingsContext> context);

        /// <summary>
        /// Called when another module participates in settings saving.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleSettingsSaving(ModulePeerNotificationContext<ModuleSettingsContext> context);

        /// <summary>
        /// Called when another module completes settings saving.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleSettingsSaved(ModulePeerNotificationContext<ModuleSettingsContext> context);

        /// <summary>
        /// Called when another module begins the application run sequence.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAppRunStarting(ModulePeerNotificationContext<ModuleAppRunContext> context);

        /// <summary>
        /// Called when another module completes the application run sequence.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAppRunCompleted(ModulePeerNotificationContext<ModuleAppRunContext> context);

        /// <summary>
        /// Called when another module is about to shut the application down.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleBeforeAppShutdown(ModulePeerNotificationContext<ModuleAppShutdownContext> context);

        /// <summary>
        /// Called when another module has completed the shutdown sequence.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleAfterAppShutdown(ModulePeerNotificationContext<ModuleAppShutdownContext> context);

        /// <summary>
        /// Called when another module observes an unhandled exception.
        /// </summary>
        /// <param name="context">Contextual information about the peer module.</param>
        void OnPeerModuleUnhandledException(ModulePeerNotificationContext<ModuleExceptionContext> context);
    }

    /// <summary>
    /// Provides information about the discovery of a module.
    /// </summary>
    public sealed class ModuleDiscoveryContext
    {
        public ModuleDiscoveryContext(string moduleName, Assembly assembly, string? sourcePath)
        {
            ModuleName = moduleName;
            Assembly = assembly;
            SourcePath = sourcePath;
        }

        public string ModuleName { get; }

        public Assembly Assembly { get; }

        public string? SourcePath { get; }
    }

    /// <summary>
    /// Provides information related to service registration operations.
    /// </summary>
    public sealed class ModuleServiceRegistrationContext
    {
        public ModuleServiceRegistrationContext(ModuleDiscoveryContext module, IServiceCollection services, IEventLogger logger, IEventBus bus)
        {
            Module = module;
            Services = services;
            Logger = logger;
            Bus = bus;
        }

        public ModuleDiscoveryContext Module { get; }

        public IServiceCollection Services { get; }

        public IEventLogger Logger { get; }

        public IEventBus Bus { get; }
    }

    /// <summary>
    /// Provides information about the imminent service provider build.
    /// </summary>
    public sealed class ModuleServiceProviderBuildContext
    {
        public ModuleServiceProviderBuildContext(IServiceCollection services, IEventLogger logger, IEventBus bus)
        {
            Services = services;
            Logger = logger;
            Bus = bus;
        }

        public IServiceCollection Services { get; }

        public IEventLogger Logger { get; }

        public IEventBus Bus { get; }
    }

    /// <summary>
    /// Provides information about the constructed service provider.
    /// </summary>
    public sealed class ModuleServiceProviderContext
    {
        public ModuleServiceProviderContext(IServiceProvider serviceProvider, IEventLogger logger, IEventBus bus)
        {
            ServiceProvider = serviceProvider;
            Logger = logger;
            Bus = bus;
        }

        public IServiceProvider ServiceProvider { get; }

        public IEventLogger Logger { get; }

        public IEventBus Bus { get; }
    }

    /// <summary>
    /// Provides information about the creation of the main window.
    /// </summary>
    public sealed class ModuleMainWindowCreationContext
    {
        public ModuleMainWindowCreationContext(IServiceProvider serviceProvider, Type windowType)
        {
            ServiceProvider = serviceProvider;
            WindowType = windowType;
        }

        public IServiceProvider ServiceProvider { get; }

        public Type WindowType { get; }
    }

    /// <summary>
    /// Provides information about the instantiated main window.
    /// </summary>
    public sealed class ModuleMainWindowContext
    {
        public ModuleMainWindowContext(Form mainWindow, IServiceProvider serviceProvider)
        {
            MainWindow = mainWindow;
            ServiceProvider = serviceProvider;
        }

        public Form MainWindow { get; }

        public IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about the lifecycle of the main window.
    /// </summary>
    public sealed class ModuleMainWindowLifecycleContext
    {
        public ModuleMainWindowLifecycleContext(Form mainWindow, IServiceProvider serviceProvider)
        {
            MainWindow = mainWindow;
            ServiceProvider = serviceProvider;
        }

        public Form MainWindow { get; }

        public IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about settings lifecycle events.
    /// </summary>
    public sealed class ModuleSettingsContext
    {
        public ModuleSettingsContext(IAppSettings settings, IServiceProvider serviceProvider)
        {
            Settings = settings;
            ServiceProvider = serviceProvider;
        }

        public IAppSettings Settings { get; }

        public IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about the WinForms application run lifecycle.
    /// </summary>
    public sealed class ModuleAppRunContext
    {
        public ModuleAppRunContext(Form mainWindow, IServiceProvider serviceProvider)
        {
            MainWindow = mainWindow;
            ServiceProvider = serviceProvider;
        }

        public Form MainWindow { get; }

        public IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about application shutdown operations.
    /// </summary>
    public sealed class ModuleAppShutdownContext
    {
        public ModuleAppShutdownContext(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about unhandled exceptions observed by the host.
    /// </summary>
    public sealed class ModuleExceptionContext
    {
        public ModuleExceptionContext(Exception exception, bool isTerminating, IServiceProvider? serviceProvider)
        {
            Exception = exception;
            IsTerminating = isTerminating;
            ServiceProvider = serviceProvider;
        }

        public Exception Exception { get; }

        public bool IsTerminating { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Describes the phase of the WebSocket connection lifecycle.
    /// </summary>
    public enum ModuleWebSocketConnectionPhase
    {
        Connecting,
        Connected,
        Disconnected,
        Reconnecting
    }

    /// <summary>
    /// Provides information about WebSocket connection lifecycle events.
    /// </summary>
    public sealed class ModuleWebSocketConnectionContext
    {
        public ModuleWebSocketConnectionContext(Uri endpoint, ModuleWebSocketConnectionPhase phase, IServiceProvider? serviceProvider, Exception? exception = null)
        {
            Endpoint = endpoint;
            Phase = phase;
            ServiceProvider = serviceProvider;
            Exception = exception;
        }

        public Uri Endpoint { get; }

        public ModuleWebSocketConnectionPhase Phase { get; }

        public Exception? Exception { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about WebSocket messages dispatched by the host.
    /// </summary>
    public sealed class ModuleWebSocketMessageContext
    {
        public ModuleWebSocketMessageContext(string message, IServiceProvider? serviceProvider)
        {
            Message = message;
            ServiceProvider = serviceProvider;
        }

        public string Message { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Describes the phase of the OSC connection lifecycle.
    /// </summary>
    public enum ModuleOscConnectionPhase
    {
        Connecting,
        Connected,
        Disconnected
    }

    /// <summary>
    /// Provides information about OSC listener lifecycle events.
    /// </summary>
    public sealed class ModuleOscConnectionContext
    {
        public ModuleOscConnectionContext(int port, ModuleOscConnectionPhase phase, IServiceProvider? serviceProvider, Exception? exception = null)
        {
            Port = port;
            Phase = phase;
            ServiceProvider = serviceProvider;
            Exception = exception;
        }

        public int Port { get; }

        public ModuleOscConnectionPhase Phase { get; }

        public Exception? Exception { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about OSC messages received by the host.
    /// </summary>
    public sealed class ModuleOscMessageContext
    {
        public ModuleOscMessageContext(OscMessage message, IServiceProvider? serviceProvider)
        {
            Message = message;
            ServiceProvider = serviceProvider;
        }

        public OscMessage Message { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Represents the stage of application settings validation.
    /// </summary>
    public enum ModuleSettingsValidationStage
    {
        Validating,
        Validated,
        Failed
    }

    /// <summary>
    /// Provides information about the application settings validation pipeline.
    /// </summary>
    public sealed class ModuleSettingsValidationContext
    {
        public ModuleSettingsValidationContext(IAppSettings settings, IList<string> errors, ModuleSettingsValidationStage stage, IServiceProvider? serviceProvider)
        {
            Settings = settings;
            Errors = errors;
            Stage = stage;
            ServiceProvider = serviceProvider;
        }

        public IAppSettings Settings { get; }

        public IList<string> Errors { get; }

        public ModuleSettingsValidationStage Stage { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about the composed auto-suicide rule set.
    /// </summary>
    public sealed class ModuleAutoSuicideRuleContext
    {
        public ModuleAutoSuicideRuleContext(IList<AutoSuicideRule> rules, IAppSettings settings, IServiceProvider? serviceProvider)
        {
            Rules = rules;
            Settings = settings;
            ServiceProvider = serviceProvider;
        }

        public IList<AutoSuicideRule> Rules { get; }

        public IAppSettings Settings { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about an auto-suicide decision.
    /// </summary>
    public sealed class ModuleAutoSuicideDecisionContext
    {
        public ModuleAutoSuicideDecisionContext(string roundType, string? terrorName, int decision, bool hasPendingDelayed, IServiceProvider? serviceProvider)
        {
            RoundType = roundType;
            TerrorName = terrorName;
            Decision = decision;
            HasPendingDelayed = hasPendingDelayed;
            ServiceProvider = serviceProvider;
        }

        public string RoundType { get; }

        public string? TerrorName { get; }

        public int Decision { get; private set; }

        public bool HasPendingDelayed { get; private set; }

        public bool IsOverridden { get; private set; }

        public IServiceProvider? ServiceProvider { get; }

        public void OverrideDecision(int decision, bool? hasPendingDelayed = null)
        {
            Decision = decision;
            if (hasPendingDelayed.HasValue)
            {
                HasPendingDelayed = hasPendingDelayed.Value;
            }

            IsOverridden = true;
        }
    }

    /// <summary>
    /// Provides information about scheduled auto-suicide actions.
    /// </summary>
    public sealed class ModuleAutoSuicideScheduleContext
    {
        public ModuleAutoSuicideScheduleContext(TimeSpan delay, bool resetStartTime, IServiceProvider? serviceProvider)
        {
            Delay = delay;
            ResetStartTime = resetStartTime;
            ServiceProvider = serviceProvider;
        }

        public TimeSpan Delay { get; }

        public bool ResetStartTime { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about the execution of a scheduled auto-suicide action.
    /// </summary>
    public sealed class ModuleAutoSuicideTriggerContext
    {
        public ModuleAutoSuicideTriggerContext(IServiceProvider? serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Represents a pending auto-launch request.
    /// </summary>
    public sealed record AutoLaunchPlan(string Path, string Arguments, string Origin);

    /// <summary>
    /// Provides information about auto-launch evaluation.
    /// </summary>
    public sealed class ModuleAutoLaunchEvaluationContext
    {
        public ModuleAutoLaunchEvaluationContext(IList<AutoLaunchPlan> plans, IAppSettings settings, IServiceProvider? serviceProvider)
        {
            Plans = plans;
            Settings = settings;
            ServiceProvider = serviceProvider;
        }

        public IList<AutoLaunchPlan> Plans { get; }

        public IAppSettings Settings { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about an individual auto-launch execution.
    /// </summary>
    public class ModuleAutoLaunchExecutionContext
    {
        public ModuleAutoLaunchExecutionContext(AutoLaunchPlan plan, IServiceProvider? serviceProvider)
        {
            Plan = plan;
            ServiceProvider = serviceProvider;
        }

        public AutoLaunchPlan Plan { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about a failed auto-launch attempt.
    /// </summary>
    public sealed class ModuleAutoLaunchFailureContext : ModuleAutoLaunchExecutionContext
    {
        public ModuleAutoLaunchFailureContext(AutoLaunchPlan plan, Exception exception, IServiceProvider? serviceProvider)
            : base(plan, serviceProvider)
        {
            Exception = exception;
        }

        public Exception Exception { get; }
    }

    /// <summary>
    /// Provides information about main window theme changes.
    /// </summary>
    public sealed class ModuleMainWindowThemeContext
    {
        public ModuleMainWindowThemeContext(Form form, string themeKey, ThemeDescriptor descriptor, IServiceProvider? serviceProvider)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            ThemeKey = string.IsNullOrWhiteSpace(themeKey) ? descriptor?.Key ?? string.Empty : themeKey;
            Theme = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            ServiceProvider = serviceProvider;
        }

        public Form Form { get; }

        public string ThemeKey { get; }

        public ThemeDescriptor Theme { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about main window layout updates.
    /// </summary>
    public sealed class ModuleMainWindowLayoutContext
    {
        public ModuleMainWindowLayoutContext(Form form, IServiceProvider? serviceProvider)
        {
            Form = form;
            ServiceProvider = serviceProvider;
        }

        public Form Form { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Indicates the stage of the settings view lifecycle.
    /// </summary>
    public enum ModuleSettingsViewStage
    {
        Opened,
        Applying,
        Closing,
        Closed
    }

    /// <summary>
    /// Provides information while the settings view is being composed.
    /// </summary>
    public sealed class ModuleSettingsViewBuildContext
    {
        public ModuleSettingsViewBuildContext(SettingsForm form, SettingsPanel panel, IAppSettings settings, IServiceProvider? serviceProvider)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ServiceProvider = serviceProvider;
        }

        public SettingsForm Form { get; }

        public SettingsPanel Panel { get; }

        public IAppSettings Settings { get; }

        public IServiceProvider? ServiceProvider { get; }

        public T AddExtensionControl<T>(T control) where T : Control
        {
            return Panel.AddModuleExtensionControl(control);
        }

        public GroupBox AddSettingsGroup(string title)
        {
            return Panel.AddModuleSettingsGroup(title);
        }
    }

    /// <summary>
    /// Provides information about the lifecycle of the settings view dialog.
    /// </summary>
    public sealed class ModuleSettingsViewLifecycleContext
    {
        public ModuleSettingsViewLifecycleContext(SettingsForm form, SettingsPanel panel, IAppSettings settings, ModuleSettingsViewStage stage, DialogResult? dialogResult, IServiceProvider? serviceProvider)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Stage = stage;
            DialogResult = dialogResult;
            ServiceProvider = serviceProvider;
        }

        public SettingsForm Form { get; }

        public SettingsPanel Panel { get; }

        public IAppSettings Settings { get; }

        public ModuleSettingsViewStage Stage { get; }

        public DialogResult? DialogResult { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides theme catalog information to modules.
    /// </summary>
    public sealed class ModuleThemeCatalogContext
    {
        private readonly Func<ThemeDescriptor, ThemeDescriptor> _register;

        public ModuleThemeCatalogContext(IReadOnlyCollection<ThemeDescriptor> themes, Func<ThemeDescriptor, ThemeDescriptor> register, IServiceProvider? serviceProvider)
        {
            Themes = themes ?? throw new ArgumentNullException(nameof(themes));
            _register = register ?? throw new ArgumentNullException(nameof(register));
            ServiceProvider = serviceProvider;
        }

        public IReadOnlyCollection<ThemeDescriptor> Themes { get; }

        public IServiceProvider? ServiceProvider { get; }

        public ThemeDescriptor RegisterTheme(string key, string displayName, ThemeColors colors, Action<ThemeApplicationContext>? applyAction = null)
        {
            return _register(new ThemeDescriptor(key, displayName, colors, applyAction));
        }

        public ThemeDescriptor RegisterTheme(ThemeDescriptor descriptor)
        {
            return _register(descriptor);
        }
    }

    /// <summary>
    /// Provides information about the main window menu during construction.
    /// </summary>
    public sealed class ModuleMainWindowMenuContext
    {
        public ModuleMainWindowMenuContext(Form form, MenuStrip menu, IServiceProvider? serviceProvider)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            MenuStrip = menu ?? throw new ArgumentNullException(nameof(menu));
            ServiceProvider = serviceProvider;
        }

        public Form Form { get; }

        public MenuStrip MenuStrip { get; }

        public ToolStripItemCollection Items => MenuStrip.Items;

        public IServiceProvider? ServiceProvider { get; }

        public ToolStripMenuItem AddMenu(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Menu text must be provided", nameof(text));
            }

            var item = new ToolStripMenuItem(text);
            MenuStrip.Items.Add(item);
            return item;
        }
    }

    /// <summary>
    /// Provides information about the main window UI composition.
    /// </summary>
    public sealed class ModuleMainWindowUiContext
    {
        public ModuleMainWindowUiContext(Form form, Control.ControlCollection controls, MenuStrip? menuStrip, IServiceProvider? serviceProvider)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            Controls = controls ?? throw new ArgumentNullException(nameof(controls));
            MenuStrip = menuStrip;
            ServiceProvider = serviceProvider;
        }

        public Form Form { get; }

        public Control.ControlCollection Controls { get; }

        public MenuStrip? MenuStrip { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Describes an auxiliary window that can be launched by the host.
    /// </summary>
    public sealed class AuxiliaryWindowDescriptor
    {
        public AuxiliaryWindowDescriptor(string id, string displayName, Func<IServiceProvider?, Form> factory, bool allowMultipleInstances, bool showModal)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Auxiliary window id must be provided", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Auxiliary window display name must be provided", nameof(displayName));
            }

            Id = id;
            DisplayName = displayName;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            AllowMultipleInstances = allowMultipleInstances;
            ShowModal = showModal;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public Func<IServiceProvider?, Form> Factory { get; }

        public bool AllowMultipleInstances { get; }

        public bool ShowModal { get; }
    }

    /// <summary>
    /// Coordinates auxiliary window registration.
    /// </summary>
    public sealed class ModuleAuxiliaryWindowCatalogContext
    {
        private readonly Action<AuxiliaryWindowDescriptor> _register;

        public ModuleAuxiliaryWindowCatalogContext(IReadOnlyCollection<AuxiliaryWindowDescriptor> registeredWindows, Action<AuxiliaryWindowDescriptor> register, IServiceProvider? serviceProvider)
        {
            RegisteredWindows = registeredWindows ?? throw new ArgumentNullException(nameof(registeredWindows));
            _register = register ?? throw new ArgumentNullException(nameof(register));
            ServiceProvider = serviceProvider;
        }

        public IReadOnlyCollection<AuxiliaryWindowDescriptor> RegisteredWindows { get; }

        public IServiceProvider? ServiceProvider { get; }

        public AuxiliaryWindowDescriptor RegisterWindow(string id, string displayName, Func<IServiceProvider?, Form> factory, bool allowMultipleInstances = false, bool showModal = false)
        {
            var descriptor = new AuxiliaryWindowDescriptor(id, displayName, factory, allowMultipleInstances, showModal);
            _register(descriptor);
            return descriptor;
        }

        public void RegisterWindow(AuxiliaryWindowDescriptor descriptor)
        {
            _register(descriptor);
        }
    }

    /// <summary>
    /// Indicates the lifecycle stage for an auxiliary window notification.
    /// </summary>
    public enum ModuleAuxiliaryWindowStage
    {
        Opening,
        Opened,
        Closing,
        Closed
    }

    /// <summary>
    /// Provides information about an auxiliary window lifecycle event.
    /// </summary>
    public sealed class ModuleAuxiliaryWindowLifecycleContext
    {
        public ModuleAuxiliaryWindowLifecycleContext(AuxiliaryWindowDescriptor descriptor, Form window, ModuleAuxiliaryWindowStage stage, IServiceProvider? serviceProvider)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Window = window ?? throw new ArgumentNullException(nameof(window));
            Stage = stage;
            ServiceProvider = serviceProvider;
        }

        public AuxiliaryWindowDescriptor Descriptor { get; }

        public Form Window { get; }

        public ModuleAuxiliaryWindowStage Stage { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    /// <summary>
    /// Provides information about the activity of another module.
    /// </summary>
    /// <typeparam name="TContext">The type of event context being observed.</typeparam>
    public sealed class ModulePeerNotificationContext<TContext>
    {
        public ModulePeerNotificationContext(ModuleDiscoveryContext observedModule, TContext eventContext)
        {
            ObservedModule = observedModule;
            EventContext = eventContext;
        }

        /// <summary>
        /// Gets the module whose activity is being observed.
        /// </summary>
        public ModuleDiscoveryContext ObservedModule { get; }

        /// <summary>
        /// Gets the context associated with the observed module activity.
        /// </summary>
        public TContext EventContext { get; }
    }
}
