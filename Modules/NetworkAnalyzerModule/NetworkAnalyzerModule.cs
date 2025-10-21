using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    public sealed class NetworkAnalyzerModule : IModule
    {
        private IServiceProvider? _serviceProvider;
        private NetworkAnalyzerProxy? _proxy;
        private Label? _statusLabel;
        private NumericUpDown? _portInput;
        private Button? _openLogButton;
        private bool _consentChecked;

        public void OnModuleLoaded(ModuleDiscoveryContext context)
        {
        }

        public void OnBeforeServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<NetworkAnalyzerProxy>();
        }

        public void OnAfterServiceRegistration(ModuleServiceRegistrationContext context)
        {
        }

        public void OnBeforeServiceProviderBuild(ModuleServiceProviderBuildContext context)
        {
        }

        public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context)
        {
            if (context?.ServiceProvider == null)
            {
                return;
            }

            _serviceProvider = context.ServiceProvider;
            _proxy = context.ServiceProvider.GetService<NetworkAnalyzerProxy>();
        }

        public void OnBeforeMainWindowCreation(ModuleMainWindowCreationContext context)
        {
        }

        public void OnAfterMainWindowCreation(ModuleMainWindowContext context)
        {
        }

        public void OnMainWindowShown(ModuleMainWindowLifecycleContext context)
        {
            if (context?.ServiceProvider == null || context.MainWindow == null)
            {
                return;
            }

            var settings = context.ServiceProvider.GetService<IAppSettings>();
            var logger = ResolveLogger(context.ServiceProvider) ?? ResolveLogger(_serviceProvider);
            if (settings == null)
            {
                return;
            }

            NormalizeSettings(settings);
            UpdateStatusControls(settings);

            if (!_consentChecked)
            {
                _consentChecked = true;
                if (!settings.NetworkAnalyzerConsentGranted)
                {
                    using var consentForm = new NetworkAnalyzerConsentForm();
                    var result = consentForm.ShowDialog(context.MainWindow);
                    if (result == DialogResult.OK && consentForm.ConsentConfirmed)
                    {
                        settings.NetworkAnalyzerConsentGranted = true;
                        settings.NetworkAnalyzerConsentTimestamp = DateTimeOffset.Now;
                        logger?.LogEvent("NetworkAnalyzer", "User granted consent for the network analyzer proxy.");
                        _ = Task.Run(async () => await settings.SaveAsync().ConfigureAwait(false));
                    }
                    else
                    {
                        logger?.LogEvent("NetworkAnalyzer", "User declined consent. Proxy remains disabled.", LogEventLevel.Warning);
                        _proxy?.Stop();
                        UpdateStatusControls(settings);
                        return;
                    }
                }
            }

            StartProxyAsync(settings, logger, context.MainWindow);
        }

        public void OnMainWindowClosing(ModuleMainWindowLifecycleContext context)
        {
            _proxy?.Stop();
            var settings = ResolveSettings(context?.ServiceProvider) ?? ResolveSettings(_serviceProvider);
            if (settings != null)
            {
                UpdateStatusControls(settings);
            }
        }

        public void OnSettingsLoading(ModuleSettingsContext context)
        {
        }

        public void OnSettingsLoaded(ModuleSettingsContext context)
        {
            if (context?.Settings == null)
            {
                return;
            }

            int normalized = NormalizePort(context.Settings.NetworkAnalyzerProxyPort);
            if (normalized != context.Settings.NetworkAnalyzerProxyPort)
            {
                context.Settings.NetworkAnalyzerProxyPort = normalized;
            }
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

            var group = context.AddSettingsGroup("Network Analyzer");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(6)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var description = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = "NetworkAnalyzer はローカル専用のプロキシを起動し、HTTPS/WSS 通信を解析・記録します。"
            };

            _statusLabel = new Label
            {
                AutoSize = true,
                Text = "ステータス: 未同意のため無効"
            };

            var portLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = "待ち受けポート番号"
            };

            _portInput = new NumericUpDown
            {
                Minimum = 1025,
                Maximum = 65535,
                Increment = 1,
                Value = NormalizePort(context.Settings.NetworkAnalyzerProxyPort),
                Anchor = AnchorStyles.Left,
                Width = 100
            };

            _openLogButton = new Button
            {
                Text = "ログフォルダーを開く",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Enabled = false
            };
            _openLogButton.Click += OpenLogButtonOnClick;

            layout.Controls.Add(description, 0, 0);
            layout.SetColumnSpan(description, 2);
            layout.Controls.Add(_statusLabel, 0, 1);
            layout.SetColumnSpan(_statusLabel, 2);
            layout.Controls.Add(portLabel, 0, 2);
            layout.Controls.Add(_portInput, 1, 2);
            layout.Controls.Add(_openLogButton, 1, 3);

            group.Controls.Add(layout);
        }

        public void OnSettingsViewOpened(ModuleSettingsViewLifecycleContext context)
        {
            if (context?.Settings == null)
            {
                return;
            }

            if (_portInput != null)
            {
                _portInput.Value = NormalizePort(context.Settings.NetworkAnalyzerProxyPort);
            }

            UpdateStatusControls(context.Settings);
        }

        public void OnSettingsViewApplying(ModuleSettingsViewLifecycleContext context)
        {
            if (context == null || context.Settings == null || context.Stage != ModuleSettingsViewStage.Applying)
            {
                return;
            }

            var settings = context.Settings;
            int originalPort = settings.NetworkAnalyzerProxyPort;
            if (_portInput != null)
            {
                settings.NetworkAnalyzerProxyPort = NormalizePort((int)_portInput.Value);
            }

            if (settings.NetworkAnalyzerProxyPort != originalPort && settings.NetworkAnalyzerConsentGranted)
            {
                var logger = ResolveLogger(context.ServiceProvider) ?? ResolveLogger(_serviceProvider);
                var owner = context.Form;
                if (owner != null)
                {
                    StartProxyAsync(settings, logger, owner);
                }
            }

            UpdateStatusControls(settings);
        }

        public void OnSettingsViewClosing(ModuleSettingsViewLifecycleContext context)
        {
        }

        public void OnSettingsViewClosed(ModuleSettingsViewLifecycleContext context)
        {
            if (_openLogButton != null)
            {
                _openLogButton.Click -= OpenLogButtonOnClick;
            }

            _statusLabel = null;
            _portInput = null;
            _openLogButton = null;
        }

        public void OnAppRunStarting(ModuleAppRunContext context)
        {
        }

        public void OnAppRunCompleted(ModuleAppRunContext context)
        {
        }

        public void OnBeforeAppShutdown(ModuleAppShutdownContext context)
        {
            _proxy?.Stop();
        }

        public void OnAfterAppShutdown(ModuleAppShutdownContext context)
        {
            _proxy?.Dispose();
            _proxy = null;
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

        private void StartProxyAsync(IAppSettings settings, IEventLogger? logger, Form owner)
        {
            if (_proxy == null || settings == null || owner == null)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                bool started = await _proxy.EnsureRunningAsync(settings).ConfigureAwait(false);
                try
                {
                    if (!owner.IsDisposed && owner.IsHandleCreated)
                    {
                        owner.BeginInvoke(new Action(() =>
                        {
                            UpdateStatusControls(settings);
                            if (!started)
                            {
                                MessageBox.Show(owner,
                                    "NetworkAnalyzer のプロキシを開始できませんでした。ログを確認してください。",
                                    "NetworkAnalyzer",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                        }));
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore shutdown race conditions.
                }

                if (!started)
                {
                    logger?.LogEvent("NetworkAnalyzer", "Failed to start the proxy. See network logs for details.", LogEventLevel.Error);
                }
            });
        }

        private void NormalizeSettings(IAppSettings settings)
        {
            settings.NetworkAnalyzerProxyPort = NormalizePort(settings.NetworkAnalyzerProxyPort);
        }

        private void UpdateStatusControls(IAppSettings? settings)
        {
            if (settings == null)
            {
                return;
            }

            if (_statusLabel == null)
            {
                return;
            }

            if (_statusLabel.InvokeRequired)
            {
                _statusLabel.BeginInvoke(new Action(() => ApplyStatus(settings)));
            }
            else
            {
                ApplyStatus(settings);
            }
        }

        private void ApplyStatus(IAppSettings settings)
        {
            if (_statusLabel == null)
            {
                return;
            }

            var running = _proxy?.IsRunning ?? false;
            string status;
            if (!settings.NetworkAnalyzerConsentGranted)
            {
                status = "ステータス: 未同意のため無効";
            }
            else if (running)
            {
                var port = _proxy?.CurrentPort > 0 ? _proxy!.CurrentPort : settings.NetworkAnalyzerProxyPort;
                status = $"ステータス: 稼働中 (127.0.0.1:{port})";
            }
            else
            {
                status = "ステータス: 同意済み (待機中)";
            }

            if (settings.NetworkAnalyzerConsentGranted && settings.NetworkAnalyzerConsentTimestamp.HasValue)
            {
                var localTime = settings.NetworkAnalyzerConsentTimestamp.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
                status += $"\n同意日時: {localTime}";
            }

            _statusLabel.Text = status;
            if (_openLogButton != null)
            {
                _openLogButton.Enabled = running && !string.IsNullOrEmpty(_proxy?.CurrentLogFilePath);
            }
        }

        private void OpenLogButtonOnClick(object? sender, EventArgs e)
        {
            var path = _proxy?.CurrentLogFilePath;
            if (string.IsNullOrEmpty(path))
            {
                path = _proxy?.LogDirectory;
            }

            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("ログファイルはまだ作成されていません。", "NetworkAnalyzer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = directory,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show("ログフォルダーが見つかりません。", "NetworkAnalyzer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                var logger = ResolveLogger(_serviceProvider);
                logger?.LogEvent("NetworkAnalyzer", $"Failed to open log location: {ex}", LogEventLevel.Error);
                MessageBox.Show($"ログを開けませんでした: {ex.Message}", "NetworkAnalyzer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static int NormalizePort(int port)
        {
            if (port < 1025 || port > 65535)
            {
                return 8890;
            }

            return port;
        }

        private IEventLogger? ResolveLogger(IServiceProvider? provider)
        {
            return provider?.GetService<IEventLogger>();
        }

        private IAppSettings? ResolveSettings(IServiceProvider? provider)
        {
            return provider?.GetService<IAppSettings>();
        }
    }
}
