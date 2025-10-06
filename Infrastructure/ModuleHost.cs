using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using System.Windows.Forms;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Coordinates lifecycle notifications for extension modules and surfaces
    /// rich events for consumers who want to observe module activity.
    /// </summary>
    public sealed class ModuleHost
    {
        private readonly List<LoadedModule> _modules = new();
        private readonly IEventLogger _logger;
        private readonly IEventBus _bus;
        private readonly SafeModeManager? _safeModeManager;
        private IServiceProvider? _serviceProvider;
        private readonly Dictionary<string, AuxiliaryWindowDescriptor> _auxiliaryWindows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Form>> _activeAuxiliaryWindows = new(StringComparer.OrdinalIgnoreCase);

        public ModuleHost(IEventLogger logger, IEventBus bus, SafeModeManager? safeModeManager = null)
        {
            _logger = logger;
            _bus = bus;
            _safeModeManager = safeModeManager;

            bus.Subscribe<WebSocketConnecting>(HandleWebSocketConnecting);
            bus.Subscribe<WebSocketConnected>(HandleWebSocketConnected);
            bus.Subscribe<WebSocketDisconnected>(HandleWebSocketDisconnected);
            bus.Subscribe<WebSocketReconnecting>(HandleWebSocketReconnecting);
            bus.Subscribe<WebSocketMessageReceived>(HandleWebSocketMessageReceived);
            bus.Subscribe<OscConnecting>(HandleOscConnecting);
            bus.Subscribe<OscConnected>(HandleOscConnected);
            bus.Subscribe<OscDisconnected>(HandleOscDisconnected);
            bus.Subscribe<OscMessageReceived>(HandleOscMessageReceived);
            bus.Subscribe<SettingsValidating>(HandleSettingsValidating);
            bus.Subscribe<SettingsValidated>(HandleSettingsValidated);
            bus.Subscribe<SettingsValidationFailed>(HandleSettingsValidationFailed);
            bus.Subscribe<SettingsLoading>(HandleSettingsLoading);
            bus.Subscribe<SettingsLoaded>(HandleSettingsLoaded);
            bus.Subscribe<SettingsSaving>(HandleSettingsSaving);
            bus.Subscribe<SettingsSaved>(HandleSettingsSaved);
            bus.Subscribe<AutoSuicideScheduled>(HandleAutoSuicideScheduled);
            bus.Subscribe<AutoSuicideCancelled>(HandleAutoSuicideCancelled);
            bus.Subscribe<AutoSuicideTriggered>(HandleAutoSuicideTriggered);
            bus.Subscribe<UnhandledExceptionOccurred>(HandleUnhandledException);
        }

        private void LogHostEvent(string action, string detail)
        {
            _logger.LogEvent("ModuleHost", $"{action}: {detail}");
        }

        /// <summary>
        /// Occurs when module discovery begins.
        /// </summary>
        public event EventHandler<string>? DiscoveryStarted;

        /// <summary>
        /// Occurs after all modules have been discovered.
        /// </summary>
        public event EventHandler<IReadOnlyList<ModuleDiscoveryContext>>? DiscoveryCompleted;

        /// <summary>
        /// Occurs for each module that is discovered in the probing directory.
        /// </summary>
        public event EventHandler<ModuleDiscoveryContext>? ModuleDiscovered;

        /// <summary>
        /// Occurs once a module has acknowledged its discovery via
        /// <see cref="IModule.OnModuleLoaded"/>.
        /// </summary>
        public event EventHandler<ModuleDiscoveryContext>? ModuleLoaded;

        /// <summary>
        /// Occurs immediately before service registration is performed.
        /// </summary>
        public event EventHandler<ModuleServiceRegistrationContext>? BeforeServiceRegistration;

        /// <summary>
        /// Occurs immediately after service registration is performed.
        /// </summary>
        public event EventHandler<ModuleServiceRegistrationContext>? AfterServiceRegistration;

        /// <summary>
        /// Occurs before the application builds the service provider.
        /// </summary>
        public event EventHandler<ModuleServiceProviderBuildContext>? ServiceProviderBuilding;

        /// <summary>
        /// Occurs after the application builds the service provider.
        /// </summary>
        public event EventHandler<ModuleServiceProviderContext>? ServiceProviderBuilt;

        /// <summary>
        /// Occurs before the main window is instantiated.
        /// </summary>
        public event EventHandler<ModuleMainWindowCreationContext>? MainWindowCreating;

        /// <summary>
        /// Occurs after the main window is instantiated.
        /// </summary>
        public event EventHandler<ModuleMainWindowContext>? MainWindowCreated;

        /// <summary>
        /// Occurs when the main window is shown for the first time.
        /// </summary>
        public event EventHandler<ModuleMainWindowLifecycleContext>? MainWindowShown;

        /// <summary>
        /// Occurs when the main window begins closing.
        /// </summary>
        public event EventHandler<ModuleMainWindowLifecycleContext>? MainWindowClosing;

        /// <summary>
        /// Occurs immediately before settings are loaded.
        /// </summary>
        public event EventHandler<ModuleSettingsContext>? SettingsLoading;

        /// <summary>
        /// Occurs after settings have been loaded.
        /// </summary>
        public event EventHandler<ModuleSettingsContext>? SettingsLoaded;

        /// <summary>
        /// Occurs immediately before settings are saved.
        /// </summary>
        public event EventHandler<ModuleSettingsContext>? SettingsSaving;

        /// <summary>
        /// Occurs after settings have been saved.
        /// </summary>
        public event EventHandler<ModuleSettingsContext>? SettingsSaved;

        /// <summary>
        /// Occurs when the settings view is being composed prior to display.
        /// </summary>
        public event EventHandler<ModuleSettingsViewBuildContext>? SettingsViewBuilding;

        /// <summary>
        /// Occurs once the settings view has been shown.
        /// </summary>
        public event EventHandler<ModuleSettingsViewLifecycleContext>? SettingsViewOpened;

        /// <summary>
        /// Occurs when the user confirms the settings view and changes are being applied.
        /// </summary>
        public event EventHandler<ModuleSettingsViewLifecycleContext>? SettingsViewApplying;

        /// <summary>
        /// Occurs when the settings view is closing.
        /// </summary>
        public event EventHandler<ModuleSettingsViewLifecycleContext>? SettingsViewClosing;

        /// <summary>
        /// Occurs after the settings view has closed.
        /// </summary>
        public event EventHandler<ModuleSettingsViewLifecycleContext>? SettingsViewClosed;

        /// <summary>
        /// Occurs before the WinForms message loop starts executing.
        /// </summary>
        public event EventHandler<ModuleAppRunContext>? AppRunStarting;

        /// <summary>
        /// Occurs after the WinForms message loop has terminated.
        /// </summary>
        public event EventHandler<ModuleAppRunContext>? AppRunCompleted;

        /// <summary>
        /// Occurs at the start of the shutdown sequence.
        /// </summary>
        public event EventHandler<ModuleAppShutdownContext>? AppShutdownStarting;

        /// <summary>
        /// Occurs once the shutdown sequence has finished.
        /// </summary>
        public event EventHandler<ModuleAppShutdownContext>? AppShutdownCompleted;

        /// <summary>
        /// Occurs when an unhandled exception is observed.
        /// </summary>
        public event EventHandler<ModuleExceptionContext>? UnhandledExceptionObserved;

        /// <summary>
        /// Occurs as the WebSocket connection lifecycle progresses.
        /// </summary>
        public event EventHandler<ModuleWebSocketConnectionContext>? WebSocketConnecting;

        public event EventHandler<ModuleWebSocketConnectionContext>? WebSocketConnected;

        public event EventHandler<ModuleWebSocketConnectionContext>? WebSocketDisconnected;

        public event EventHandler<ModuleWebSocketConnectionContext>? WebSocketReconnecting;

        /// <summary>
        /// Occurs whenever a WebSocket message is dispatched to subscribers.
        /// </summary>
        public event EventHandler<ModuleWebSocketMessageContext>? WebSocketMessageReceived;

        /// <summary>
        /// Occurs as the OSC listener lifecycle progresses.
        /// </summary>
        public event EventHandler<ModuleOscConnectionContext>? OscConnecting;

        public event EventHandler<ModuleOscConnectionContext>? OscConnected;

        public event EventHandler<ModuleOscConnectionContext>? OscDisconnected;

        public event EventHandler<ModuleOscMessageContext>? OscMessageReceived;

        /// <summary>
        /// Occurs during settings validation.
        /// </summary>
        public event EventHandler<ModuleSettingsValidationContext>? SettingsValidating;

        public event EventHandler<ModuleSettingsValidationContext>? SettingsValidated;

        public event EventHandler<ModuleSettingsValidationContext>? SettingsValidationFailed;

        /// <summary>
        /// Occurs when auto-suicide operations are coordinated.
        /// </summary>
        public event EventHandler<ModuleAutoSuicideRuleContext>? AutoSuicideRulesPrepared;

        public event EventHandler<ModuleAutoSuicideDecisionContext>? AutoSuicideDecisionEvaluated;

        public event EventHandler<ModuleAutoSuicideScheduleContext>? AutoSuicideScheduled;

        public event EventHandler<ModuleAutoSuicideScheduleContext>? AutoSuicideCancelled;

        public event EventHandler<ModuleAutoSuicideTriggerContext>? AutoSuicideTriggered;

        /// <summary>
        /// Occurs when auto-launch operations are coordinated.
        /// </summary>
        public event EventHandler<ModuleAutoLaunchEvaluationContext>? AutoLaunchEvaluating;

        public event EventHandler<ModuleAutoLaunchExecutionContext>? AutoLaunchStarting;

        public event EventHandler<ModuleAutoLaunchFailureContext>? AutoLaunchFailed;

        public event EventHandler<ModuleAutoLaunchExecutionContext>? AutoLaunchCompleted;

        /// <summary>
        /// Occurs when the main window theme or layout changes.
        /// </summary>
        public event EventHandler<ModuleThemeCatalogContext>? ThemeCatalogBuilding;

        public event EventHandler<ModuleMainWindowMenuContext>? MainWindowMenuBuilding;

        public event EventHandler<ModuleMainWindowUiContext>? MainWindowUiComposed;

        public event EventHandler<ModuleMainWindowThemeContext>? MainWindowThemeChanged;

        public event EventHandler<ModuleMainWindowLayoutContext>? MainWindowLayoutUpdated;

        /// <summary>
        /// Occurs when auxiliary windows are coordinated.
        /// </summary>
        public event EventHandler<ModuleAuxiliaryWindowCatalogContext>? AuxiliaryWindowCatalogBuilding;

        public event EventHandler<ModuleAuxiliaryWindowLifecycleContext>? AuxiliaryWindowOpening;

        public event EventHandler<ModuleAuxiliaryWindowLifecycleContext>? AuxiliaryWindowOpened;

        public event EventHandler<ModuleAuxiliaryWindowLifecycleContext>? AuxiliaryWindowClosing;

        public event EventHandler<ModuleAuxiliaryWindowLifecycleContext>? AuxiliaryWindowClosed;

        /// <summary>
        /// Gets a snapshot of the modules known by the host.
        /// </summary>
        public IReadOnlyList<LoadedModule> Modules => _modules.AsReadOnly();

        /// <summary>
        /// Gets the active service provider, if available.
        /// </summary>
        public IServiceProvider? CurrentServiceProvider => _serviceProvider;

        /// <summary>
        /// Gets the auxiliary windows registered with the host.
        /// </summary>
        public IReadOnlyCollection<AuxiliaryWindowDescriptor> AuxiliaryWindows => _auxiliaryWindows.Values;

        public void NotifyDiscoveryStarted(string directory)
        {
            LogHostEvent(nameof(NotifyDiscoveryStarted), $"Scanning directory '{directory}' for modules.");
            DiscoveryStarted?.Invoke(this, directory);
            _bus.Publish(new ModuleDiscoveryStarted(directory));
        }

        public void NotifyDiscoveryCompleted()
        {
            var snapshot = _modules.ConvertAll(m => m.Discovery);
            var readOnly = snapshot.AsReadOnly();
            LogHostEvent(nameof(NotifyDiscoveryCompleted), $"Discovered {snapshot.Count} module(s).");
            DiscoveryCompleted?.Invoke(this, readOnly);
            _bus.Publish(new ModuleDiscoveryCompleted(readOnly));
        }

        public void RegisterModule(IModule module, ModuleDiscoveryContext discovery, IServiceCollection services)
        {
            var loaded = new LoadedModule(module, discovery);
            _modules.Add(loaded);
            LogHostEvent(nameof(RegisterModule), $"Registering module '{discovery.ModuleName}'.");

            ModuleDiscovered?.Invoke(this, discovery);
            _bus.Publish(new ModuleDiscovered(discovery));

            if (InvokeModuleAction(loaded, discovery, static (m, ctx) => m.OnModuleLoaded(ctx), nameof(IModule.OnModuleLoaded)))
            {
                NotifyPeers(loaded, discovery, static (m, ctx) => m.OnPeerModuleLoaded(ctx), nameof(IModule.OnPeerModuleLoaded));
            }

            ModuleLoaded?.Invoke(this, discovery);
            _bus.Publish(new ModuleLoaded(discovery));

            var registrationContext = new ModuleServiceRegistrationContext(discovery, services, _logger, _bus);
            BeforeServiceRegistration?.Invoke(this, registrationContext);
            _bus.Publish(new ModuleServicesRegistering(registrationContext));

            if (InvokeModuleAction(loaded, registrationContext, static (m, ctx) => m.OnBeforeServiceRegistration(ctx), nameof(IModule.OnBeforeServiceRegistration)))
            {
                NotifyPeers(loaded, registrationContext, static (m, ctx) => m.OnPeerModuleBeforeServiceRegistration(ctx), nameof(IModule.OnPeerModuleBeforeServiceRegistration));
            }

            NotifyPeers(loaded, registrationContext, static (m, ctx) => m.OnPeerModuleRegisteringServices(ctx), nameof(IModule.OnPeerModuleRegisteringServices));

            try
            {
                module.RegisterServices(services);
            }
            catch (Exception ex)
            {
                HandleModuleException(loaded, nameof(IModule.RegisterServices), ex);
                _bus.Publish(new ModuleServiceRegistrationFailed(discovery, ex));
                return;
            }

            if (InvokeModuleAction(loaded, registrationContext, static (m, ctx) => m.OnAfterServiceRegistration(ctx), nameof(IModule.OnAfterServiceRegistration)))
            {
                NotifyPeers(loaded, registrationContext, static (m, ctx) => m.OnPeerModuleAfterServiceRegistration(ctx), nameof(IModule.OnPeerModuleAfterServiceRegistration));
            }

            AfterServiceRegistration?.Invoke(this, registrationContext);
            _bus.Publish(new ModuleServicesRegistered(registrationContext));
            LogHostEvent(nameof(RegisterModule), $"Service registration completed for '{discovery.ModuleName}'.");
        }

        public void NotifyServiceProviderBuilding(ModuleServiceProviderBuildContext context)
        {
            LogHostEvent(nameof(NotifyServiceProviderBuilding), "Dispatching service provider building notifications.");
            ServiceProviderBuilding?.Invoke(this, context);
            _bus.Publish(new ServiceProviderBuilding(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnBeforeServiceProviderBuild(ctx), nameof(IModule.OnBeforeServiceProviderBuild)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleBeforeServiceProviderBuild(ctx), nameof(IModule.OnPeerModuleBeforeServiceProviderBuild));
                }
            }
        }

        public void NotifyServiceProviderBuilt(ModuleServiceProviderContext context)
        {
            _serviceProvider = context.ServiceProvider;
            LogHostEvent(nameof(NotifyServiceProviderBuilt), "Service provider available to modules.");
            ServiceProviderBuilt?.Invoke(this, context);
            _bus.Publish(new ServiceProviderBuilt(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnAfterServiceProviderBuild(ctx), nameof(IModule.OnAfterServiceProviderBuild)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleAfterServiceProviderBuild(ctx), nameof(IModule.OnPeerModuleAfterServiceProviderBuild));
                }
            }
        }

        public void NotifyMainWindowCreating(ModuleMainWindowCreationContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowCreating), $"Creating main window '{context.WindowType.FullName}'.");
            MainWindowCreating?.Invoke(this, context);
            _bus.Publish(new MainWindowCreating(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnBeforeMainWindowCreation(ctx), nameof(IModule.OnBeforeMainWindowCreation)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleBeforeMainWindowCreation(ctx), nameof(IModule.OnPeerModuleBeforeMainWindowCreation));
                }
            }
        }

        public void NotifyMainWindowCreated(ModuleMainWindowContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowCreated), $"Main window '{context.MainWindow.GetType().FullName}' created.");
            MainWindowCreated?.Invoke(this, context);
            _bus.Publish(new MainWindowCreated(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnAfterMainWindowCreation(ctx), nameof(IModule.OnAfterMainWindowCreation)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleAfterMainWindowCreation(ctx), nameof(IModule.OnPeerModuleAfterMainWindowCreation));
                }
            }
        }

        public void NotifyMainWindowShown(ModuleMainWindowLifecycleContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowShown), "Main window shown for the first time.");
            MainWindowShown?.Invoke(this, context);
            _bus.Publish(new MainWindowShown(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowShown(ctx), nameof(IModule.OnMainWindowShown)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleMainWindowShown(ctx), nameof(IModule.OnPeerModuleMainWindowShown));
                }
            }
        }

        public void NotifyMainWindowClosing(ModuleMainWindowLifecycleContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowClosing), "Main window closing sequence started.");
            MainWindowClosing?.Invoke(this, context);
            _bus.Publish(new MainWindowClosing(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowClosing(ctx), nameof(IModule.OnMainWindowClosing)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleMainWindowClosing(ctx), nameof(IModule.OnPeerModuleMainWindowClosing));
                }
            }
        }

        public void NotifyAppRunStarting(ModuleAppRunContext context)
        {
            LogHostEvent(nameof(NotifyAppRunStarting), "Application run starting.");
            AppRunStarting?.Invoke(this, context);
            _bus.Publish(new AppRunStarting(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnAppRunStarting(ctx), nameof(IModule.OnAppRunStarting)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleAppRunStarting(ctx), nameof(IModule.OnPeerModuleAppRunStarting));
                }
            }
        }

        public void NotifyAppRunCompleted(ModuleAppRunContext context)
        {
            LogHostEvent(nameof(NotifyAppRunCompleted), "Application run completed.");
            AppRunCompleted?.Invoke(this, context);
            _bus.Publish(new AppRunCompleted(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnAppRunCompleted(ctx), nameof(IModule.OnAppRunCompleted)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleAppRunCompleted(ctx), nameof(IModule.OnPeerModuleAppRunCompleted));
                }
            }
        }

        public void NotifyAppShutdownStarting(ModuleAppShutdownContext context)
        {
            LogHostEvent(nameof(NotifyAppShutdownStarting), "Shutdown sequence beginning.");
            AppShutdownStarting?.Invoke(this, context);
            _bus.Publish(new AppShutdownStarting(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnBeforeAppShutdown(ctx), nameof(IModule.OnBeforeAppShutdown)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleBeforeAppShutdown(ctx), nameof(IModule.OnPeerModuleBeforeAppShutdown));
                }
            }
        }

        public void NotifyAppShutdownCompleted(ModuleAppShutdownContext context)
        {
            LogHostEvent(nameof(NotifyAppShutdownCompleted), "Shutdown sequence completed.");
            AppShutdownCompleted?.Invoke(this, context);
            _bus.Publish(new AppShutdownCompleted(context));
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnAfterAppShutdown(ctx), nameof(IModule.OnAfterAppShutdown)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleAfterAppShutdown(ctx), nameof(IModule.OnPeerModuleAfterAppShutdown));
                }
            }

            _serviceProvider = null;
        }

        public void NotifyAutoSuicideRulesPrepared(ModuleAutoSuicideRuleContext context)
        {
            LogHostEvent(nameof(NotifyAutoSuicideRulesPrepared), $"Coordinating {context.Rules.Count} auto-suicide rule(s).");
            AutoSuicideRulesPrepared?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoSuicideRulesPrepared(ctx), nameof(IModule.OnAutoSuicideRulesPrepared));
            }

            _bus.Publish(new AutoSuicideRulesPrepared(new List<AutoSuicideRule>(context.Rules).AsReadOnly(), context.Settings));
        }

        public void NotifyAutoSuicideDecisionEvaluated(ModuleAutoSuicideDecisionContext context)
        {
            LogHostEvent(nameof(NotifyAutoSuicideDecisionEvaluated), $"Decision evaluated for round '{context.RoundType}' (pending delayed: {context.HasPendingDelayed}).");
            AutoSuicideDecisionEvaluated?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoSuicideDecisionEvaluated(ctx), nameof(IModule.OnAutoSuicideDecisionEvaluated));
            }

            _bus.Publish(new AutoSuicideDecisionEvaluated(context.RoundType, context.TerrorName, context.Decision, context.HasPendingDelayed));
        }

        public void NotifyAutoLaunchEvaluating(ModuleAutoLaunchEvaluationContext context)
        {
            LogHostEvent(nameof(NotifyAutoLaunchEvaluating), $"Evaluating {context.Plans.Count} auto-launch plan(s).");
            AutoLaunchEvaluating?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoLaunchEvaluating(ctx), nameof(IModule.OnAutoLaunchEvaluating));
            }

            _bus.Publish(new AutoLaunchEvaluating(new List<AutoLaunchPlan>(context.Plans).AsReadOnly(), context.Settings));
        }

        public void NotifyAutoLaunchStarting(ModuleAutoLaunchExecutionContext context)
        {
            LogHostEvent(nameof(NotifyAutoLaunchStarting), $"Launching '{context.Plan.Path}' with arguments '{context.Plan.Arguments}'.");
            AutoLaunchStarting?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoLaunchStarting(ctx), nameof(IModule.OnAutoLaunchStarting));
            }

            _bus.Publish(new AutoLaunchStarting(context.Plan));
        }

        public void NotifyAutoLaunchFailed(ModuleAutoLaunchFailureContext context)
        {
            LogHostEvent(nameof(NotifyAutoLaunchFailed), $"Launch failed for '{context.Plan.Path}': {context.Exception?.Message ?? "<none>"}.");
            AutoLaunchFailed?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoLaunchFailed(ctx), nameof(IModule.OnAutoLaunchFailed));
            }

            _bus.Publish(new AutoLaunchFailed(context.Plan, context.Exception));
        }

        public void NotifyAutoLaunchCompleted(ModuleAutoLaunchExecutionContext context)
        {
            LogHostEvent(nameof(NotifyAutoLaunchCompleted), $"Launch completed for '{context.Plan.Path}'.");
            AutoLaunchCompleted?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoLaunchCompleted(ctx), nameof(IModule.OnAutoLaunchCompleted));
            }

            _bus.Publish(new AutoLaunchCompleted(context.Plan));
        }

        public void NotifyThemeCatalogBuilding(ModuleThemeCatalogContext context)
        {
            LogHostEvent(nameof(NotifyThemeCatalogBuilding), "Building theme catalog.");
            ThemeCatalogBuilding?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnThemeCatalogBuilding(ctx), nameof(IModule.OnThemeCatalogBuilding));
            }
        }

        public void NotifyMainWindowMenuBuilding(ModuleMainWindowMenuContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowMenuBuilding), "Building main window menu.");
            MainWindowMenuBuilding?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowMenuBuilding(ctx), nameof(IModule.OnMainWindowMenuBuilding));
            }
        }

        public void NotifyMainWindowUiComposed(ModuleMainWindowUiContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowUiComposed), "Main window UI composed.");
            MainWindowUiComposed?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowUiComposed(ctx), nameof(IModule.OnMainWindowUiComposed));
            }
        }

        public void NotifyMainWindowThemeChanged(ModuleMainWindowThemeContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowThemeChanged), $"Theme changed to '{context.ThemeKey}'.");
            MainWindowThemeChanged?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowThemeChanged(ctx), nameof(IModule.OnMainWindowThemeChanged));
            }

            _bus.Publish(new MainWindowThemeChanged(context.ThemeKey, context.Theme, context.Form));
        }

        public void NotifyMainWindowLayoutUpdated(ModuleMainWindowLayoutContext context)
        {
            LogHostEvent(nameof(NotifyMainWindowLayoutUpdated), "Layout updated.");
            MainWindowLayoutUpdated?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnMainWindowLayoutUpdated(ctx), nameof(IModule.OnMainWindowLayoutUpdated));
            }

            _bus.Publish(new MainWindowLayoutUpdated(context.Form));
        }

        public void NotifySettingsViewBuilding(ModuleSettingsViewBuildContext context)
        {
            LogHostEvent(nameof(NotifySettingsViewBuilding), "Building settings view.");
            SettingsViewBuilding?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsViewBuilding(ctx), nameof(IModule.OnSettingsViewBuilding));
            }
        }

        public void NotifySettingsViewOpened(ModuleSettingsViewLifecycleContext context)
        {
            LogHostEvent(nameof(NotifySettingsViewOpened), "Settings view opened.");
            SettingsViewOpened?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsViewOpened(ctx), nameof(IModule.OnSettingsViewOpened));
            }
        }

        public void NotifySettingsViewApplying(ModuleSettingsViewLifecycleContext context)
        {
            LogHostEvent(nameof(NotifySettingsViewApplying), "Applying settings from view.");
            SettingsViewApplying?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsViewApplying(ctx), nameof(IModule.OnSettingsViewApplying));
            }
        }

        public void NotifySettingsViewClosing(ModuleSettingsViewLifecycleContext context)
        {
            LogHostEvent(nameof(NotifySettingsViewClosing), "Settings view closing.");
            SettingsViewClosing?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsViewClosing(ctx), nameof(IModule.OnSettingsViewClosing));
            }
        }

        public void NotifySettingsViewClosed(ModuleSettingsViewLifecycleContext context)
        {
            LogHostEvent(nameof(NotifySettingsViewClosed), "Settings view closed.");
            SettingsViewClosed?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsViewClosed(ctx), nameof(IModule.OnSettingsViewClosed));
            }
        }

        public ModuleAuxiliaryWindowCatalogContext NotifyAuxiliaryWindowCatalogBuilding()
        {
            var snapshot = new List<AuxiliaryWindowDescriptor>(_auxiliaryWindows.Values).AsReadOnly();
            var context = new ModuleAuxiliaryWindowCatalogContext(snapshot, RegisterAuxiliaryWindow, _serviceProvider);
            LogHostEvent(nameof(NotifyAuxiliaryWindowCatalogBuilding), $"Catalog contains {snapshot.Count} auxiliary window(s).");
            AuxiliaryWindowCatalogBuilding?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAuxiliaryWindowCatalogBuilding(ctx), nameof(IModule.OnAuxiliaryWindowCatalogBuilding));
            }

            return context;
        }

        public Form? ShowAuxiliaryWindow(string id, Form? owner = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                LogHostEvent(nameof(ShowAuxiliaryWindow), "Requested window id was null or whitespace.");
                return null;
            }

            if (!_auxiliaryWindows.TryGetValue(id, out var descriptor))
            {
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Requested auxiliary window '{id}' is not registered.");
                return null;
            }

            if (!descriptor.AllowMultipleInstances && _activeAuxiliaryWindows.TryGetValue(descriptor.Id, out var existingList))
            {
                var existing = existingList.Find(f => f != null && !f.IsDisposed);
                if (existing != null)
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                    {
                        existing.WindowState = FormWindowState.Normal;
                    }

                    existing.BringToFront();
                    existing.Activate();
                    LogHostEvent(nameof(ShowAuxiliaryWindow), $"Activated existing instance of '{descriptor.Id}'.");
                    return existing;
                }
            }

            Form window;
            try
            {
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Creating auxiliary window '{descriptor.Id}'.");
                window = descriptor.Factory(_serviceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("ModuleHost", $"Auxiliary window '{descriptor.Id}' factory failed: {ex}", LogEventLevel.Error);
                return null;
            }

            if (window == null)
            {
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Factory for auxiliary window '{descriptor.Id}' returned null.");
                return null;
            }

            if (!_activeAuxiliaryWindows.TryGetValue(descriptor.Id, out var instances))
            {
                instances = new List<Form>();
                _activeAuxiliaryWindows[descriptor.Id] = instances;
            }

            instances.Add(window);
            LogHostEvent(nameof(ShowAuxiliaryWindow), $"Tracking new instance of '{descriptor.Id}'. Active count: {instances.Count}.");

            var openingContext = new ModuleAuxiliaryWindowLifecycleContext(descriptor, window, ModuleAuxiliaryWindowStage.Opening, _serviceProvider);
            NotifyAuxiliaryWindowLifecycle(openingContext, AuxiliaryWindowOpening, static (m, ctx) => m.OnAuxiliaryWindowOpening(ctx), nameof(IModule.OnAuxiliaryWindowOpening));

            window.Shown += (s, e) =>
            {
                var openedContext = new ModuleAuxiliaryWindowLifecycleContext(descriptor, window, ModuleAuxiliaryWindowStage.Opened, _serviceProvider);
                NotifyAuxiliaryWindowLifecycle(openedContext, AuxiliaryWindowOpened, static (m, ctx) => m.OnAuxiliaryWindowOpened(ctx), nameof(IModule.OnAuxiliaryWindowOpened));
            };

            window.FormClosing += (s, e) =>
            {
                var closingContext = new ModuleAuxiliaryWindowLifecycleContext(descriptor, window, ModuleAuxiliaryWindowStage.Closing, _serviceProvider);
                NotifyAuxiliaryWindowLifecycle(closingContext, AuxiliaryWindowClosing, static (m, ctx) => m.OnAuxiliaryWindowClosing(ctx), nameof(IModule.OnAuxiliaryWindowClosing));
            };

            window.FormClosed += (s, e) =>
            {
                if (_activeAuxiliaryWindows.TryGetValue(descriptor.Id, out var active))
                {
                    active.Remove(window);
                }

                var remaining = _activeAuxiliaryWindows.TryGetValue(descriptor.Id, out var activeList) ? activeList.Count : 0;
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Window '{descriptor.Id}' closed. Remaining active instances: {remaining}.");

                var closedContext = new ModuleAuxiliaryWindowLifecycleContext(descriptor, window, ModuleAuxiliaryWindowStage.Closed, _serviceProvider);
                NotifyAuxiliaryWindowLifecycle(closedContext, AuxiliaryWindowClosed, static (m, ctx) => m.OnAuxiliaryWindowClosed(ctx), nameof(IModule.OnAuxiliaryWindowClosed));
            };

            if (owner != null && !descriptor.ShowModal)
            {
                window.Owner = owner;
            }

            if (descriptor.ShowModal && owner != null)
            {
                window.StartPosition = FormStartPosition.CenterParent;
                window.ShowDialog(owner);
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Displayed modal window '{descriptor.Id}' with owner '{owner?.Name}'.");
            }
            else if (descriptor.ShowModal)
            {
                window.StartPosition = FormStartPosition.CenterScreen;
                window.ShowDialog();
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Displayed modal window '{descriptor.Id}' without owner.");
            }
            else
            {
                window.Show(owner);
                LogHostEvent(nameof(ShowAuxiliaryWindow), $"Displayed modeless window '{descriptor.Id}'.");
            }

            return window;
        }

        private void HandleWebSocketConnecting(WebSocketConnecting message)
        {
            LogHostEvent(nameof(HandleWebSocketConnecting), $"Connecting to {message.Endpoint}.");
            var context = new ModuleWebSocketConnectionContext(message.Endpoint, ModuleWebSocketConnectionPhase.Connecting, _serviceProvider);
            WebSocketConnecting?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnWebSocketConnecting(ctx), nameof(IModule.OnWebSocketConnecting));
            }
        }

        private void HandleWebSocketConnected(WebSocketConnected message)
        {
            LogHostEvent(nameof(HandleWebSocketConnected), $"Connected to {message.Endpoint}.");
            var context = new ModuleWebSocketConnectionContext(message.Endpoint, ModuleWebSocketConnectionPhase.Connected, _serviceProvider);
            WebSocketConnected?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnWebSocketConnected(ctx), nameof(IModule.OnWebSocketConnected));
            }
        }

        private void HandleWebSocketDisconnected(WebSocketDisconnected message)
        {
            LogHostEvent(nameof(HandleWebSocketDisconnected), $"Disconnected from {message.Endpoint}. Exception: {message.Exception?.Message ?? "<none>"}.");
            var context = new ModuleWebSocketConnectionContext(message.Endpoint, ModuleWebSocketConnectionPhase.Disconnected, _serviceProvider, message.Exception);
            WebSocketDisconnected?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnWebSocketDisconnected(ctx), nameof(IModule.OnWebSocketDisconnected));
            }
        }

        private void HandleWebSocketReconnecting(WebSocketReconnecting message)
        {
            LogHostEvent(nameof(HandleWebSocketReconnecting), $"Reconnecting to {message.Endpoint}. Exception: {message.Exception?.Message ?? "<none>"}.");
            var context = new ModuleWebSocketConnectionContext(message.Endpoint, ModuleWebSocketConnectionPhase.Reconnecting, _serviceProvider, message.Exception);
            WebSocketReconnecting?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnWebSocketReconnecting(ctx), nameof(IModule.OnWebSocketReconnecting));
            }
        }

        private void HandleWebSocketMessageReceived(WebSocketMessageReceived message)
        {
            LogHostEvent(nameof(HandleWebSocketMessageReceived), $"Message received ({message.Message?.Length ?? 0} chars).");
            var context = new ModuleWebSocketMessageContext(message.Message!, _serviceProvider);
            WebSocketMessageReceived?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnWebSocketMessageReceived(ctx), nameof(IModule.OnWebSocketMessageReceived));
            }
        }

        private void HandleOscConnecting(OscConnecting message)
        {
            LogHostEvent(nameof(HandleOscConnecting), $"Connecting OSC on port {message.Port}.");
            var context = new ModuleOscConnectionContext(message.Port, ModuleOscConnectionPhase.Connecting, _serviceProvider);
            OscConnecting?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnOscConnecting(ctx), nameof(IModule.OnOscConnecting));
            }
        }

        private void HandleOscConnected(OscConnected message)
        {
            LogHostEvent(nameof(HandleOscConnected), $"OSC connected on port {message.Port}.");
            var context = new ModuleOscConnectionContext(message.Port, ModuleOscConnectionPhase.Connected, _serviceProvider);
            OscConnected?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnOscConnected(ctx), nameof(IModule.OnOscConnected));
            }
        }

        private void HandleOscDisconnected(OscDisconnected message)
        {
            LogHostEvent(nameof(HandleOscDisconnected), $"OSC disconnected on port {message.Port}. Exception: {message.Exception?.Message ?? "<none>"}.");
            var context = new ModuleOscConnectionContext(message.Port, ModuleOscConnectionPhase.Disconnected, _serviceProvider, message.Exception);
            OscDisconnected?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnOscDisconnected(ctx), nameof(IModule.OnOscDisconnected));
            }
        }

        private void HandleOscMessageReceived(OscMessageReceived message)
        {
            LogHostEvent(nameof(HandleOscMessageReceived), $"OSC message received: {message.Message.Address}.");
            var context = new ModuleOscMessageContext(message.Message, _serviceProvider);
            OscMessageReceived?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnOscMessageReceived(ctx), nameof(IModule.OnOscMessageReceived));
            }
        }

        private void HandleSettingsValidating(SettingsValidating message)
        {
            LogHostEvent(nameof(HandleSettingsValidating), "Settings validating notification received.");
            var context = new ModuleSettingsValidationContext(message.Settings, message.Errors, ModuleSettingsValidationStage.Validating, _serviceProvider);
            SettingsValidating?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnBeforeSettingsValidation(ctx), nameof(IModule.OnBeforeSettingsValidation));
            }
        }

        private void HandleSettingsValidated(SettingsValidated message)
        {
            LogHostEvent(nameof(HandleSettingsValidated), "Settings validated successfully.");
            var context = new ModuleSettingsValidationContext(message.Settings, new List<string>(), ModuleSettingsValidationStage.Validated, _serviceProvider);
            SettingsValidated?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsValidated(ctx), nameof(IModule.OnSettingsValidated));
            }
        }

        private void HandleSettingsValidationFailed(SettingsValidationFailed message)
        {
            LogHostEvent(nameof(HandleSettingsValidationFailed), $"Settings validation failed with {message.Errors.Count} error(s).");
            var context = new ModuleSettingsValidationContext(message.Settings, new List<string>(message.Errors), ModuleSettingsValidationStage.Failed, _serviceProvider);
            SettingsValidationFailed?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsValidationFailed(ctx), nameof(IModule.OnSettingsValidationFailed));
            }
        }

        private void HandleSettingsLoading(SettingsLoading message)
        {
            LogHostEvent(nameof(HandleSettingsLoading), "Settings loading initiated.");
            if (_serviceProvider == null)
            {
                return;
            }

            var context = new ModuleSettingsContext(message.Settings, _serviceProvider);
            SettingsLoading?.Invoke(this, context);
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsLoading(ctx), nameof(IModule.OnSettingsLoading)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleSettingsLoading(ctx), nameof(IModule.OnPeerModuleSettingsLoading));
                }
            }
        }

        private void HandleSettingsLoaded(SettingsLoaded message)
        {
            LogHostEvent(nameof(HandleSettingsLoaded), "Settings loaded notification received.");
            if (_serviceProvider == null)
            {
                return;
            }

            var context = new ModuleSettingsContext(message.Settings, _serviceProvider);
            SettingsLoaded?.Invoke(this, context);
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsLoaded(ctx), nameof(IModule.OnSettingsLoaded)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleSettingsLoaded(ctx), nameof(IModule.OnPeerModuleSettingsLoaded));
                }
            }
        }

        private void HandleSettingsSaving(SettingsSaving message)
        {
            LogHostEvent(nameof(HandleSettingsSaving), "Settings saving initiated.");
            if (_serviceProvider == null)
            {
                return;
            }

            var context = new ModuleSettingsContext(message.Settings, _serviceProvider);
            SettingsSaving?.Invoke(this, context);
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsSaving(ctx), nameof(IModule.OnSettingsSaving)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleSettingsSaving(ctx), nameof(IModule.OnPeerModuleSettingsSaving));
                }
            }
        }

        private void HandleSettingsSaved(SettingsSaved message)
        {
            LogHostEvent(nameof(HandleSettingsSaved), "Settings saved notification received.");
            if (_serviceProvider == null)
            {
                return;
            }

            var context = new ModuleSettingsContext(message.Settings, _serviceProvider);
            SettingsSaved?.Invoke(this, context);
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnSettingsSaved(ctx), nameof(IModule.OnSettingsSaved)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleSettingsSaved(ctx), nameof(IModule.OnPeerModuleSettingsSaved));
                }
            }
        }

        private void HandleAutoSuicideScheduled(AutoSuicideScheduled message)
        {
            LogHostEvent(nameof(HandleAutoSuicideScheduled), $"Auto suicide scheduled. Delay: {message.Delay}.");
            var context = new ModuleAutoSuicideScheduleContext(message.Delay, message.ResetStartTime, _serviceProvider);
            AutoSuicideScheduled?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoSuicideScheduled(ctx), nameof(IModule.OnAutoSuicideScheduled));
            }
        }

        private void HandleAutoSuicideCancelled(AutoSuicideCancelled message)
        {
            LogHostEvent(nameof(HandleAutoSuicideCancelled), $"Auto suicide cancelled. Remaining delay: {message.RemainingDelay?.ToString() ?? "<none>"}.");
            var context = new ModuleAutoSuicideScheduleContext(message.RemainingDelay ?? TimeSpan.Zero, resetStartTime: false, _serviceProvider);
            AutoSuicideCancelled?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoSuicideCancelled(ctx), nameof(IModule.OnAutoSuicideCancelled));
            }
        }

        private void HandleAutoSuicideTriggered(AutoSuicideTriggered message)
        {
            LogHostEvent(nameof(HandleAutoSuicideTriggered), "Auto suicide triggered.");
            var context = new ModuleAutoSuicideTriggerContext(_serviceProvider);
            AutoSuicideTriggered?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, static (m, ctx) => m.OnAutoSuicideTriggered(ctx), nameof(IModule.OnAutoSuicideTriggered));
            }
        }

        private void HandleUnhandledException(UnhandledExceptionOccurred message)
        {
            LogHostEvent(nameof(HandleUnhandledException), $"Unhandled exception observed. Terminating: {message.IsTerminating}.");
            var context = new ModuleExceptionContext(message.Exception, message.IsTerminating, _serviceProvider);
            UnhandledExceptionObserved?.Invoke(this, context);
            foreach (var module in _modules)
            {
                if (InvokeModuleAction(module, context, static (m, ctx) => m.OnUnhandledException(ctx), nameof(IModule.OnUnhandledException)))
                {
                    NotifyPeers(module, context, static (m, ctx) => m.OnPeerModuleUnhandledException(ctx), nameof(IModule.OnPeerModuleUnhandledException));
                }
            }
        }

        private bool InvokeModuleAction<TContext>(LoadedModule module, TContext context, Action<IModule, TContext> action, string stage)
        {
            try
            {
                action(module.Instance, context);
                return true;
            }
            catch (Exception ex)
            {
                HandleModuleException(module, stage, ex);
                return false;
            }
        }

        private void NotifyPeers<TContext>(LoadedModule subject, TContext context, Action<IModule, ModulePeerNotificationContext<TContext>> peerAction, string stage)
        {
            if (_modules.Count <= 1)
            {
                return;
            }

            var notification = new ModulePeerNotificationContext<TContext>(subject.Discovery, context);

            foreach (var peer in _modules)
            {
                if (ReferenceEquals(peer, subject))
                {
                    continue;
                }

                try
                {
                    peerAction(peer.Instance, notification);
                }
                catch (Exception ex)
                {
                    HandleModuleException(peer, stage, ex);
                }
            }
        }

        private void HandleModuleException(LoadedModule module, string stage, Exception exception)
        {
            _logger.LogEvent("ModuleHost", $"Module '{module.Discovery.ModuleName}' failed during {stage}: {exception}", LogEventLevel.Error);

            if (_safeModeManager?.TryScheduleAutomaticSafeMode(module.Discovery.ModuleName, stage, exception) == true)
            {
                _logger.LogEvent(
                    "ModuleHost",
                    $"Safe mode scheduled due to repeated failure of module '{module.Discovery.ModuleName}' at stage {stage}.",
                    LogEventLevel.Warning);
            }
            _bus.Publish(new ModuleCallbackFailed(module.Discovery, stage, exception));
        }

        private void RegisterAuxiliaryWindow(AuxiliaryWindowDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            _auxiliaryWindows[descriptor.Id] = descriptor;
            LogHostEvent(nameof(RegisterAuxiliaryWindow), $"Registered auxiliary window '{descriptor.Id}'.");
        }

        private void NotifyAuxiliaryWindowLifecycle(ModuleAuxiliaryWindowLifecycleContext context, EventHandler<ModuleAuxiliaryWindowLifecycleContext>? handler, Action<IModule, ModuleAuxiliaryWindowLifecycleContext> action, string stage)
        {
            LogHostEvent(nameof(NotifyAuxiliaryWindowLifecycle), $"{context.Descriptor.Id} - {context.Stage}.");
            handler?.Invoke(this, context);
            foreach (var module in _modules)
            {
                InvokeModuleAction(module, context, action, stage);
            }
        }

        public sealed record LoadedModule(IModule Instance, ModuleDiscoveryContext Discovery);
    }
}
