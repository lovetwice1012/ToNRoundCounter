using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rug.Osc;
using Serilog.Events;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Properties;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Application;
using MediaPlayer = System.Windows.Media.MediaPlayer;
using WinFormsApp = System.Windows.Forms.Application;
using ToNRoundCounter.Infrastructure.Interop;

namespace ToNRoundCounter.UI
{
    public partial class MainForm : Form, IMainView
    {
        // 上部固定UI
        private Label lblStatus = null!;           // WebSocket接続状況
        private Label lblOSCStatus = null!;        // OSC通信接続状況
        private Button btnToggleTopMost = null!;   // 画面最前面固定ボタン
        private Button btnSettings = null!;        // 設定変更ボタン
        private MenuStrip mainMenuStrip = null!;
        private ToolStripMenuItem windowsMenuItem = null!;

        // デバッグ情報用ラベル
        private Label lblDebugInfo = null!;

        // 情報表示パネル
        public InfoPanel InfoPanel { get; private set; } = null!;
        private TerrorInfoPanel terrorInfoPanel = null!;
        private JObject? terrorInfoData;

        // 統計情報表示およびラウンドログ表示は SplitContainer で実装（縦に並べる）
        private SplitContainer splitContainerMain = null!;
        private Label lblStatsTitle = null!;
        private Label lblRoundLogTitle = null!;
        private RichTextBox rtbStatsDisplay = null!;  // 統計情報表示欄
        public LogPanel logPanel = null!;             // ラウンドログパネル

        // その他のフィールド
        private readonly ICancellationProvider _cancellation;
        private readonly IWebSocketClient webSocketClient;
        private readonly IOSCListener oscListener;
        private readonly StateService stateService;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly MainPresenter _presenter;
        private readonly IEventBus _eventBus;
        private readonly IInputSender _inputSender;
        private readonly IUiDispatcher _dispatcher;
        private readonly IReadOnlyList<IAfkWarningHandler> _afkWarningHandlers;
        private readonly IReadOnlyList<IOscRepeaterPolicy> _oscRepeaterPolicies;
        private readonly ModuleHost _moduleHost;

        private Action<WebSocketConnected>? _wsConnectedHandler;
        private Action<WebSocketDisconnected>? _wsDisconnectedHandler;
        private Action<OscConnected>? _oscConnectedHandler;
        private Action<OscDisconnected>? _oscDisconnectedHandler;
        private Action<WebSocketMessageReceived>? _wsMessageHandler;
        private Action<OscMessageReceived>? _oscMessageHandler;
        private Action<SettingsValidationFailed>? _settingsValidationFailedHandler;

        private Dictionary<string, Color> terrorColors = new();
        private bool lastOptedIn = true;

        // 次ラウンド予測用：stateService.RoundCycle==0 → 通常ラウンド, ==1 → 「通常ラウンド or 特殊ラウンド」, >=2 → 特殊ラウンド
        private Random randomGenerator = new Random();

        // OSC/Velocity 関連
        private List<AutoSuicideRule> autoSuicideRules = new List<AutoSuicideRule>();
        private static readonly string[] AllRoundTypes = new string[]
        {
            "クラシック", "走れ！", "オルタネイト", "パニッシュ", "狂気", "サボタージュ", "霧", "ブラッドバス", "ダブルトラブル", "EX", "ミッドナイト", "ゴースト", "8ページ", "アンバウンド", "寒い夜", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス"
        };

        private Process? oscRepeaterProcess;

        private bool isNotifyActivated = false;

        private static readonly string[] testerNames = new string[] { "yussy5373", "Kotetsu Wilde", "tofu_shoyu", "ちよ千夜", "Blackpit", "shari_1928", "MitarashiMochi", "Motimotiusa3" };

        private bool isRestarted = false;

        private bool issetAllSelfKillMode = false;
        private bool allRoundsForcedSchedule;

        private string _lastSaveCode = string.Empty;

        private string version = "1.12.2";

        private readonly AutoSuicideService autoSuicideService;

        private MediaPlayer? itemMusicPlayer;
        private bool itemMusicLoopRequested;
        private bool itemMusicActive;
        private DateTime itemMusicMatchStart = DateTime.MinValue;
        private string lastLoadedItemMusicPath = string.Empty;
        private ItemMusicEntry? activeItemMusicEntry;
        private string currentTerrorBaseText = string.Empty;
        private string currentOverlayTerrorBaseText = string.Empty;
        private string currentTerrorCountdownSuffix = string.Empty;
        private string? currentUnboundDisplayName;
        private string? currentUnboundTerrorDetails;
        private readonly List<string> currentTerrorInfoNames = new();
        private bool terrorCountdownActive;
        private DateTime terrorCountdownStart = DateTime.MinValue;
        private int terrorCountdownDurationSeconds;
        private string terrorCountdownTargetName = string.Empty;
        private int terrorCountdownLastDisplayedSeconds = -1;
        private readonly Dictionary<OverlaySection, OverlaySectionForm> overlayForms = new();
        private readonly List<(string Label, string Status)> overlayRoundHistory = new();
        private System.Windows.Forms.Timer? overlayVisibilityTimer;
        private OverlayShortcutForm? shortcutOverlayForm;
        private bool overlayTemporarilyHidden;
        private int activeOverlayInteractions;
        private DateTime lastVrChatForegroundTime = DateTime.MinValue;
        private readonly object instanceTimerSync = new();
        private string currentInstanceId = string.Empty;
        private DateTimeOffset currentInstanceEnteredAt = DateTimeOffset.Now;
        private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");
        private const string NextRoundPredictionUnavailableMessage = "次のラウンドの予測は特殊ラウンドを一回発生させることで利用可能です";
        private bool hasObservedSpecialRound;

        private enum OverlaySection
        {
            Velocity,
            Angle,
            Terror,
            Damage,
            NextRound,
            RoundStatus,
            RoundHistory,
            RoundStats,
            TerrorInfo,
            Shortcuts,
            Clock,
            InstanceTimer
        }

        private void LogUi(string message, LogEventLevel level = LogEventLevel.Information)
        {
            _logger?.LogEvent("MainForm", message, level);
        }


        public MainForm(IWebSocketClient webSocketClient, IOSCListener oscListener, AutoSuicideService autoSuicideService, StateService stateService, IAppSettings settings, IEventLogger logger, MainPresenter presenter, IEventBus eventBus, ICancellationProvider cancellation, IInputSender inputSender, IUiDispatcher dispatcher, IEnumerable<IAfkWarningHandler> afkWarningHandlers, IEnumerable<IOscRepeaterPolicy> oscRepeaterPolicies, ModuleHost moduleHost)
        {
            InitializeSoundPlayers();
            this.Name = "MainForm";
            this.webSocketClient = webSocketClient;
            this.autoSuicideService = autoSuicideService;
            this.oscListener = oscListener;
            this.stateService = stateService;
            _settings = settings;
            _logger = logger;
            LogUi("Constructing main form instance and wiring dependencies.");
            _presenter = presenter;
            _eventBus = eventBus;
            _cancellation = cancellation;
            _inputSender = inputSender;
            _dispatcher = dispatcher;
            _afkWarningHandlers = (afkWarningHandlers ?? Array.Empty<IAfkWarningHandler>()).ToList();
            _oscRepeaterPolicies = (oscRepeaterPolicies ?? Array.Empty<IOscRepeaterPolicy>()).ToList();
            LogUi($"AFK warning handlers resolved: {_afkWarningHandlers.Count}.", LogEventLevel.Debug);
            LogUi($"OSC repeater policies resolved: {_oscRepeaterPolicies.Count}.", LogEventLevel.Debug);
            _moduleHost = moduleHost;
            _presenter.AttachView(this);
            LogUi("Presenter attached to main form view.", LogEventLevel.Debug);

            terrorColors = new Dictionary<string, Color>();
            LoadTerrorInfo();
            _settings.Load();
            _lastSaveCode = _settings.LastSaveCode ?? string.Empty;
            UpdateItemMusicPlayer(null);
            LogUi("Initial settings and resources loaded.", LogEventLevel.Debug);
            _moduleHost.NotifyThemeCatalogBuilding(new ModuleThemeCatalogContext(Theme.RegisteredThemes, Theme.RegisterTheme, _moduleHost.CurrentServiceProvider));
            _moduleHost.NotifyAuxiliaryWindowCatalogBuilding();
            Theme.SetTheme(_settings.ThemeKey, new ThemeApplicationContext(this, _moduleHost.CurrentServiceProvider));
            _moduleHost.NotifyMainWindowThemeChanged(new ModuleMainWindowThemeContext(this, _settings.ThemeKey, Theme.CurrentDescriptor, _moduleHost.CurrentServiceProvider));
            LoadAutoSuicideRules();
            InitializeComponents();
            if (lblStatus == null || lblOSCStatus == null || btnToggleTopMost == null || btnSettings == null ||
                mainMenuStrip == null || windowsMenuItem == null || InfoPanel == null || terrorInfoPanel == null ||
                splitContainerMain == null || rtbStatsDisplay == null || logPanel == null)
            {
                throw new InvalidOperationException("Main form controls failed to initialize.");
            }
            InitializeOverlay();
            _moduleHost.NotifyMainWindowMenuBuilding(new ModuleMainWindowMenuContext(this, mainMenuStrip, _moduleHost.CurrentServiceProvider));
            BuildAuxiliaryWindowsMenu();
            _moduleHost.NotifyMainWindowUiComposed(new ModuleMainWindowUiContext(this, this.Controls, mainMenuStrip, _moduleHost.CurrentServiceProvider));
            ApplyTheme();
            _moduleHost.NotifyMainWindowLayoutUpdated(new ModuleMainWindowLayoutContext(this, _moduleHost.CurrentServiceProvider));
            LogUi("Main window composition and module notifications completed.");
            this.Load += MainForm_Load;
            _wsConnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Connected");
                lblStatus.ForeColor = Color.Green;
            });
            _eventBus.Subscribe(_wsConnectedHandler);

            _wsDisconnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Disconnected");
                lblStatus.ForeColor = Color.Red;
            });
            _eventBus.Subscribe(_wsDisconnectedHandler);

            _oscConnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                lblOSCStatus.Text = "OSC: " + LanguageManager.Translate("Connected");
                lblOSCStatus.ForeColor = Color.Green;
            });
            _eventBus.Subscribe(_oscConnectedHandler);

            _oscDisconnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                lblOSCStatus.Text = "OSC: " + LanguageManager.Translate("Disconnected");
                lblOSCStatus.ForeColor = Color.Red;
            });
            _eventBus.Subscribe(_oscDisconnectedHandler);

            _wsMessageHandler = async e => await HandleEventAsync(e.Message);
            _eventBus.Subscribe(_wsMessageHandler);

            _oscMessageHandler = e => HandleOscMessage(e.Message);
            _eventBus.Subscribe(_oscMessageHandler);

            _settingsValidationFailedHandler = e => _dispatcher.Invoke(() => MessageBox.Show(string.Join("\n", e.Errors), "Settings Error"));
            _eventBus.Subscribe(_settingsValidationFailedHandler);
            _ = webSocketClient.StartAsync();
            _ = oscListener.StartAsync(_settings.OSCPort);

            velocityTimer = new System.Windows.Forms.Timer();
            velocityTimer.Interval = 50;
            velocityTimer.Tick += VelocityTimer_Tick;
            velocityTimer.Start();
            LogUi("Main form construction complete. Background listeners and timers started.");
        }

        private void InitializeOverlay()
        {
            overlayForms.Clear();
            if (shortcutOverlayForm != null)
            {
                shortcutOverlayForm.ShortcutClicked -= ShortcutOverlay_ShortcutClicked;
                shortcutOverlayForm = null;
            }

            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens.FirstOrDefault()?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            int offsetX = 40;
            int offsetY = 80;
            int spacing = 16;

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlaySizes ??= new Dictionary<string, Size>();

            var sections = new (OverlaySection Section, string Title, string InitialValue)[]
            {
                (OverlaySection.Velocity, "速度", currentVelocity.ToString("00.00", CultureInfo.InvariantCulture)),
                (OverlaySection.Terror, "テラー", GetOverlayTerrorDisplayText()),
                (OverlaySection.Damage, "ダメージ", GetDamageOverlayText()),
                (OverlaySection.NextRound, "次ラウンド予測", GetNextRoundOverlayValue()),
                (OverlaySection.RoundStatus, "ラウンド状況", InfoPanel?.RoundTypeValue?.Text ?? string.Empty),
                (OverlaySection.RoundHistory, "ラウンドタイプ推移", string.Empty),
                (OverlaySection.RoundStats, "ラウンド統計", string.Empty),
                (OverlaySection.TerrorInfo, "テラー詳細", BuildTerrorInfoOverlayText()),
                (OverlaySection.Shortcuts, "ショートカット", string.Empty),
                (OverlaySection.Clock, "時計", GetClockOverlayText()),
                (OverlaySection.InstanceTimer, "滞在時間", GetInstanceTimerDisplayText())
            };

            int x = Math.Max(workingArea.Left, workingArea.Right - 260 - offsetX);
            int nextDefaultY = Math.Max(workingArea.Top, workingArea.Top + offsetY);

            foreach (var (section, title, initialValue) in sections)
            {
                OverlaySectionForm form = section switch
                {
                    OverlaySection.Velocity => new OverlayVelocityForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.Clock => new OverlayClockForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.RoundStats => new OverlayRoundStatsForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.RoundHistory => new OverlayRoundHistoryForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.Shortcuts => new OverlayShortcutForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    _ => new OverlaySectionForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    }
                };

                form.SetBackgroundOpacity(GetEffectiveOverlayOpacity());

                string key = GetOverlaySectionKey(section);

                if (section == OverlaySection.RoundHistory && form is OverlayRoundHistoryForm historyForm)
                {
                    historyForm.SetHistory(overlayRoundHistory);
                }
                else if (section == OverlaySection.Shortcuts && form is OverlayShortcutForm shortcuts)
                {
                    shortcutOverlayForm = shortcuts;
                    SetupShortcutOverlay(shortcuts);
                }
                else if (section == OverlaySection.Clock && form is OverlayClockForm clockForm)
                {
                    UpdateClockForm(clockForm);
                }
                else if (section == OverlaySection.Velocity && form is OverlayVelocityForm velocityForm)
                {
                    velocityForm.UpdateReadings(currentVelocity, lastIdleSeconds);
                }
                else
                {
                    form.SetValue(initialValue);
                }

                if (_settings.OverlayScaleFactors.TryGetValue(key, out var savedScale) && savedScale > 0f)
                {
                    form.ScaleFactor = savedScale;
                }

                _settings.OverlayScaleFactors[key] = form.ScaleFactor;

                if (_settings.OverlaySizes.TryGetValue(key, out var savedSize) && savedSize.Width > 0 && savedSize.Height > 0)
                {
                    form.ApplySavedSize(savedSize);
                }

                _settings.OverlaySizes[key] = form.Size;

                Point location;
                if (_settings.OverlayPositions.TryGetValue(key, out var savedLocation))
                {
                    location = ClampOverlayLocation(savedLocation, form.Size, workingArea);
                }
                else
                {
                    location = new Point(x, nextDefaultY);
                    nextDefaultY += form.Height + spacing;
                }

                form.Location = location;
                _settings.OverlayPositions[key] = form.Location;

                form.Hide();
                overlayForms[section] = form;
                form.Move += (_, _) => HandleOverlayMoved(section, form);
                form.SizeChanged += (_, _) => HandleOverlayResized(section, form);
                form.DragInteractionStarted += HandleOverlayInteractionStarted;
                form.DragInteractionEnded += HandleOverlayInteractionEnded;
            }

            ApplyOverlayRoundHistorySettings();
            RefreshRoundStatsOverlay();

            overlayVisibilityTimer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };
            overlayVisibilityTimer.Tick += OverlayVisibilityTimer_Tick;
            overlayVisibilityTimer.Start();
            UpdateShortcutOverlayState();
        }

        private void OverlayVisibilityTimer_Tick(object? sender, EventArgs e)
        {
            UpdateClockOverlay();
            UpdateVelocityOverlay();
            UpdateInstanceTimerOverlay();
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateOverlayVisibility));
                return;
            }

            if (overlayForms.Count == 0)
            {
                return;
            }

            bool isVrChatForeground = WindowUtilities.IsProcessInForeground("VRChat");
            if (isVrChatForeground)
            {
                lastVrChatForegroundTime = DateTime.UtcNow;
            }
            else if (lastVrChatForegroundTime != DateTime.MinValue &&
                     DateTime.UtcNow - lastVrChatForegroundTime < TimeSpan.FromSeconds(2))
            {
                isVrChatForeground = true;
            }

            foreach (var kvp in overlayForms.ToList())
            {
                var section = kvp.Key;
                var form = kvp.Value;
                if (form.IsDisposed)
                {
                    overlayForms.Remove(section);
                    continue;
                }

                bool enabled = IsOverlaySectionEnabled(section);
                bool overlayHasFocus = form.ContainsFocus;
                bool shouldShow = enabled && !overlayTemporarilyHidden &&
                                  (isVrChatForeground || overlayHasFocus || activeOverlayInteractions > 0);

                if (shouldShow)
                {
                    if (!form.Visible)
                    {
                        form.Show();
                    }
                    form.SetTopMostState(true);
                }
                else
                {
                    form.SetTopMostState(false);

                    if (form.Visible)
                    {
                        form.Hide();
                    }
                }
            }
        }

        private void HandleOverlayInteractionStarted(object? sender, EventArgs e)
        {
            activeOverlayInteractions++;

            if (activeOverlayInteractions == 1)
            {
                UpdateOverlayVisibility();
            }
        }

        private void HandleOverlayInteractionEnded(object? sender, EventArgs e)
        {
            if (activeOverlayInteractions > 0)
            {
                activeOverlayInteractions--;
            }

            if (activeOverlayInteractions == 0)
            {
                UpdateOverlayVisibility();
            }
        }

        private void ApplyOverlayBackgroundOpacityToForms()
        {
            double opacity = GetEffectiveOverlayOpacity();

            foreach (var form in overlayForms.Values)
            {
                if (form.IsDisposed)
                {
                    continue;
                }

                form.SetBackgroundOpacity(opacity);
            }
        }

        private double GetEffectiveOverlayOpacity()
        {
            double opacity = _settings.OverlayOpacity;
            if (opacity <= 0d)
            {
                opacity = 0.95d;
            }

            if (opacity < 0.2d)
            {
                opacity = 0.2d;
            }

            if (opacity > 1d)
            {
                opacity = 1d;
            }

            return opacity;
        }

        private void SetupShortcutOverlay(OverlayShortcutForm form)
        {
            form.ShortcutClicked += ShortcutOverlay_ShortcutClicked;
            form.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel, autoSuicideService.HasScheduled);
            form.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay, autoSuicideService.HasScheduled);
        }

        private void UpdateShortcutOverlayState()
        {
            if (shortcutOverlayForm == null)
            {
                return;
            }

            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AutoSuicideToggle, _settings.AutoSuicideEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle, issetAllSelfKillMode);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle, _settings.CoordinatedAutoSuicideBrainEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AfkDetectionToggle, _settings.AfkSoundCancelEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd, overlayTemporarilyHidden);
            shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel, autoSuicideService.HasScheduled);
            shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay, autoSuicideService.HasScheduled);
        }

        private async void ShortcutOverlay_ShortcutClicked(object? sender, OverlayShortcutForm.ShortcutButtonEventArgs e)
        {
            switch (e.Button)
            {
                case OverlayShortcutForm.ShortcutButton.AutoSuicideToggle:
                    _settings.AutoSuicideEnabled = !_settings.AutoSuicideEnabled;
                    LoadAutoSuicideRules();
                    if (!_settings.AutoSuicideEnabled && !issetAllSelfKillMode)
                    {
                        CancelAutoSuicide();
                    }
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.AutoSuicideCancel:
                    bool hadScheduled = autoSuicideService.HasScheduled;
                    if (hadScheduled)
                    {
                        CancelAutoSuicide();
                        shortcutOverlayForm?.PulseButton(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel);
                    }
                    else
                    {
                        UpdateShortcutOverlayState();
                    }
                    break;
                case OverlayShortcutForm.ShortcutButton.AutoSuicideDelay:
                    bool canDelay = autoSuicideService.HasScheduled;
                    if (canDelay)
                    {
                        DelayAutoSuicide();
                        shortcutOverlayForm?.PulseButton(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay);
                    }
                    else
                    {
                        UpdateShortcutOverlayState();
                    }
                    break;
                case OverlayShortcutForm.ShortcutButton.ManualSuicide:
                    _ = Task.Run(PerformAutoSuicide);
                    shortcutOverlayForm?.SetToggleState(OverlayShortcutForm.ShortcutButton.ManualSuicide, true);
                    break;
                case OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle:
                    issetAllSelfKillMode = !issetAllSelfKillMode;
                    if (issetAllSelfKillMode)
                    {
                        EnsureAllRoundsModeAutoSuicide();
                    }
                    else if (allRoundsForcedSchedule)
                    {
                        CancelAutoSuicide();
                    }
                    UpdateShortcutOverlayState();
                    break;
                case OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle:
                    _settings.CoordinatedAutoSuicideBrainEnabled = !_settings.CoordinatedAutoSuicideBrainEnabled;
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.AfkDetectionToggle:
                    _settings.AfkSoundCancelEnabled = !_settings.AfkSoundCancelEnabled;
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd:
                    SetOverlayTemporarilyHidden(!overlayTemporarilyHidden);
                    break;
            }

            WindowUtilities.TryFocusProcessWindowIfAltNotPressed("VRChat");
        }

        private void SetOverlayTemporarilyHidden(bool hidden)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetOverlayTemporarilyHidden(hidden)));
                return;
            }

            overlayTemporarilyHidden = hidden;
            UpdateOverlayVisibility();
            UpdateShortcutOverlayState();
        }

        private void EnsureAllRoundsModeAutoSuicide()
        {
            if (!issetAllSelfKillMode)
            {
                return;
            }

            if (stateService.CurrentRound == null)
            {
                return;
            }

            if (autoSuicideService.HasScheduled)
            {
                CancelAutoSuicide();
            }

            ScheduleAutoSuicide(TimeSpan.FromSeconds(13), true, true);
        }

        private void ResetRoundScopedShortcutButtons()
        {
            shortcutOverlayForm?.ResetButtons(
                OverlayShortcutForm.ShortcutButton.AutoSuicideDelay,
                OverlayShortcutForm.ShortcutButton.ManualSuicide,
                OverlayShortcutForm.ShortcutButton.AutoSuicideCancel);
        }

        private void HandleOverlayMoved(OverlaySection section, OverlaySectionForm form)
        {
            if (form.IsDisposed)
            {
                return;
            }

            string key = GetOverlaySectionKey(section);
            Rectangle workingArea = Screen.FromPoint(form.Location)?.WorkingArea
                ?? Screen.PrimaryScreen?.WorkingArea
                ?? new Rectangle(0, 0, 1920, 1080);
            Point clampedLocation = ClampOverlayLocation(form.Location, form.Size, workingArea);
            if (form.Location != clampedLocation)
            {
                form.Location = clampedLocation;
            }

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayPositions[key] = form.Location;
        }

        private void HandleOverlayResized(OverlaySection section, OverlaySectionForm form)
        {
            if (form.IsDisposed)
            {
                return;
            }

            HandleOverlayMoved(section, form);

            string key = GetOverlaySectionKey(section);
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlayScaleFactors[key] = form.ScaleFactor;
            _settings.OverlaySizes ??= new Dictionary<string, Size>();
            _settings.OverlaySizes[key] = form.Size;
        }

        private static Point ClampOverlayLocation(Point location, Size size, Rectangle workingArea)
        {
            int maxX = Math.Max(workingArea.Left, workingArea.Right - size.Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - size.Height);

            int x = Math.Min(Math.Max(location.X, workingArea.Left), maxX);
            int y = Math.Min(Math.Max(location.Y, workingArea.Top), maxY);

            return new Point(x, y);
        }

        private static string GetOverlaySectionKey(OverlaySection section) => section.ToString();

        private string GetDamageOverlayText()
        {
            string? damageValue = InfoPanel?.DamageValue?.Text;
            if (string.IsNullOrWhiteSpace(damageValue))
            {
                damageValue = "0";
            }

            return $"Damage: {damageValue}";
        }

        private string GetNextRoundOverlayValue()
        {
            if (!hasObservedSpecialRound)
            {
                return NextRoundPredictionUnavailableMessage;
            }

            string nextRoundText = InfoPanel?.NextRoundType?.Text ?? string.Empty;
            return $"次のラウンドは\n{nextRoundText}";
        }

        private void UpdateClockOverlay()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string fallback = FormatClockOverlayText(now);

            UpdateOverlay(OverlaySection.Clock, form =>
            {
                if (form is OverlayClockForm clockForm)
                {
                    clockForm.UpdateTime(now, JapaneseCulture);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        private void UpdateVelocityOverlay()
        {
            string fallback = $"{currentVelocity.ToString("00.00", CultureInfo.InvariantCulture)}\nAFK: {lastIdleSeconds:F1}秒";

            UpdateOverlay(OverlaySection.Velocity, form =>
            {
                if (form is OverlayVelocityForm velocityForm)
                {
                    velocityForm.UpdateReadings(currentVelocity, lastIdleSeconds);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        private void UpdateInstanceTimerOverlay()
        {
            UpdateOverlay(OverlaySection.InstanceTimer, form => form.SetValue(GetInstanceTimerDisplayText()));
        }

        private string GetClockOverlayText()
        {
            return FormatClockOverlayText(DateTimeOffset.Now);
        }

        private string FormatClockOverlayText(DateTimeOffset now)
        {
            string dayName = JapaneseCulture.DateTimeFormat.GetDayName(now.DayOfWeek);
            if (dayName.EndsWith("曜日", StringComparison.Ordinal))
            {
                int trimmedLength = Math.Max(0, dayName.Length - 2);
                dayName = trimmedLength > 0 ? dayName.Substring(0, trimmedLength) : dayName;
            }

            if (string.IsNullOrEmpty(dayName))
            {
                dayName = JapaneseCulture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek);
            }

            return $"{now:yyyy:MM:dd} ({dayName})\n{now:HH:mm:ss}";
        }

        private void UpdateClockForm(OverlayClockForm form)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            form.UpdateTime(now, JapaneseCulture);
        }

        private string GetInstanceTimerDisplayText()
        {
            string instanceId;
            DateTimeOffset enteredAt;

            lock (instanceTimerSync)
            {
                instanceId = currentInstanceId;
                enteredAt = currentInstanceEnteredAt;
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                return "00時間00分00秒";
            }

            TimeSpan elapsed = DateTimeOffset.Now - enteredAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            int totalHours = (int)Math.Floor(elapsed.TotalHours);
            return $"{totalHours:D2}時間{elapsed.Minutes:D2}分{elapsed.Seconds:D2}秒";
        }

        private void CaptureOverlayPositions()
        {
            if (overlayForms.Count == 0)
            {
                return;
            }

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlaySizes ??= new Dictionary<string, Size>();

            foreach (var kvp in overlayForms)
            {
                var section = kvp.Key;
                var form = kvp.Value;
                if (form.IsDisposed)
                {
                    continue;
                }

                string key = GetOverlaySectionKey(section);
                _settings.OverlayPositions[key] = form.Location;
                _settings.OverlayScaleFactors[key] = form.ScaleFactor;
                _settings.OverlaySizes[key] = form.Size;
            }
        }

        private bool IsOverlaySectionEnabled(OverlaySection section)
        {
            return section switch
            {
                OverlaySection.Velocity => _settings.OverlayShowVelocity,
                OverlaySection.Angle => false,
                OverlaySection.Terror => _settings.OverlayShowTerror,
                OverlaySection.Damage => _settings.OverlayShowDamage,
                OverlaySection.NextRound => _settings.OverlayShowNextRound,
                OverlaySection.RoundStatus => _settings.OverlayShowRoundStatus,
                OverlaySection.RoundHistory => _settings.OverlayShowRoundHistory,
                OverlaySection.RoundStats => _settings.OverlayShowRoundStats,
                OverlaySection.TerrorInfo => _settings.OverlayShowTerrorInfo,
                OverlaySection.Shortcuts => _settings.OverlayShowShortcuts,
                OverlaySection.Clock => _settings.OverlayShowClock,
                OverlaySection.InstanceTimer => _settings.OverlayShowInstanceTimer,
                _ => false
            };
        }

        private void UpdateOverlay(OverlaySection section, Action<OverlaySectionForm> updater)
        {
            if (!overlayForms.TryGetValue(section, out var form) || form.IsDisposed)
            {
                return;
            }

            if (form.InvokeRequired)
            {
                form.Invoke(new Action(() => updater(form)));
            }
            else
            {
                updater(form);
            }
        }

        private void InitializeComponents()
        {
            LogUi("Initializing main form visual components.", LogEventLevel.Debug);
            this.Text = LanguageManager.Translate("ToNRoundCouter");
            this.Size = new Size(600, 800);
            this.MinimumSize = new Size(300, 400);
            this.BackColor = Theme.Current.Background;
            this.Resize += MainForm_Resize;

            mainMenuStrip = new MenuStrip();
            mainMenuStrip.Name = "mainMenuStrip";
            mainMenuStrip.Dock = DockStyle.Top;
            this.MainMenuStrip = mainMenuStrip;
            this.Controls.Add(mainMenuStrip);

            var fileMenu = new ToolStripMenuItem(LanguageManager.Translate("ファイル"));
            var settingsMenuItem = new ToolStripMenuItem(LanguageManager.Translate("設定..."));
            settingsMenuItem.Click += BtnSettings_Click;
            var exitMenuItem = new ToolStripMenuItem(LanguageManager.Translate("終了"));
            exitMenuItem.Click += (s, e) => Close();
            fileMenu.DropDownItems.Add(settingsMenuItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitMenuItem);
            mainMenuStrip.Items.Add(fileMenu);

            windowsMenuItem = new ToolStripMenuItem(LanguageManager.Translate("ウィンドウ"));
            mainMenuStrip.Items.Add(windowsMenuItem);

            int margin = 10;
            int currentY = mainMenuStrip.Bottom + margin;
            int contentWidth = this.ClientSize.Width - 2 * margin;
            bool useCustomPanelColors = string.Equals(_settings.ThemeKey, Theme.DefaultThemeKey, StringComparison.OrdinalIgnoreCase);

            // WebSocket接続状況
            lblStatus = new Label();
            lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Disconnected");
            lblStatus.ForeColor = Color.Red;
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(margin, currentY);
            lblStatus.Width = contentWidth / 2 - 5;
            this.Controls.Add(lblStatus);

            // OSC接続状況
            lblOSCStatus = new Label();
            lblOSCStatus.Text = "OSC: " + LanguageManager.Translate("Disconnected");
            lblOSCStatus.ForeColor = Color.Red;
            lblOSCStatus.AutoSize = true;
            lblOSCStatus.Location = new Point(lblStatus.Right + 10, currentY);
            this.Controls.Add(lblOSCStatus);

            // デバッグ情報用ラベル
            lblDebugInfo = new Label();
            lblDebugInfo.Text = "";
            lblDebugInfo.ForeColor = Color.Blue;
            lblDebugInfo.AutoSize = true;
            lblDebugInfo.Location = new Point(lblOSCStatus.Right + 10, currentY);
            this.Controls.Add(lblDebugInfo);

            currentY += lblStatus.Height + margin;


            // 画面最前面固定ボタン
            btnToggleTopMost = new Button();
            btnToggleTopMost.Text = LanguageManager.Translate("固定する");
            btnToggleTopMost.AutoSize = true;
            btnToggleTopMost.Location = new Point(margin, currentY);
            btnToggleTopMost.Width = contentWidth / 2 - 5;
            btnToggleTopMost.Click += BtnToggleTopMost_Click;
            this.Controls.Add(btnToggleTopMost);

            // 設定変更ボタン
            btnSettings = new Button();
            btnSettings.Text = LanguageManager.Translate("設定変更");
            btnSettings.AutoSize = true;
            btnSettings.Location = new Point(btnToggleTopMost.Right + 10, currentY);
            btnSettings.Width = contentWidth / 2 - 5;
            btnSettings.Click += BtnSettings_Click;
            this.Controls.Add(btnSettings);
            currentY += btnToggleTopMost.Height + margin;

            // 情報表示パネル
            InfoPanel = new InfoPanel();
            InfoPanel.BackColor = useCustomPanelColors ? _settings.BackgroundColor_InfoPanel : Theme.Current.PanelBackground;
            InfoPanel.Location = new Point(margin, currentY);
            InfoPanel.Width = contentWidth;
            InfoPanel.NextRoundType.Text = NextRoundPredictionUnavailableMessage;
            InfoPanel.NextRoundType.ForeColor = Color.White;
            this.Controls.Add(InfoPanel);
            currentY += InfoPanel.Height + margin;

            terrorInfoPanel = new TerrorInfoPanel();
            terrorInfoPanel.Location = new Point(margin, currentY);
            terrorInfoPanel.Width = contentWidth;
            terrorInfoPanel.Height = 0;
            this.Controls.Add(terrorInfoPanel);
            currentY += terrorInfoPanel.Height + margin;

            // SplitContainer（統計情報表示欄とラウンドログを縦に並べる）
            splitContainerMain = new SplitContainer();
            splitContainerMain.Orientation = Orientation.Horizontal;
            splitContainerMain.Location = new Point(margin, currentY);
            splitContainerMain.Width = contentWidth;
            splitContainerMain.Height = this.ClientSize.Height - currentY - margin;
            splitContainerMain.SplitterDistance = splitContainerMain.Height / 2;
            splitContainerMain.IsSplitterFixed = false;

            // 統計情報表示欄
            lblStatsTitle = new Label();
            lblStatsTitle.Text = LanguageManager.Translate("統計情報表示欄");
            lblStatsTitle.Dock = DockStyle.None;
            lblStatsTitle.Location = new Point(0, 0);
            lblStatsTitle.Height = 20;
            lblStatsTitle.ForeColor = Theme.Current.Foreground;
            splitContainerMain.Panel1.Controls.Add(lblStatsTitle);

            rtbStatsDisplay = new RichTextBox();
            rtbStatsDisplay.ReadOnly = true;
            rtbStatsDisplay.BorderStyle = BorderStyle.FixedSingle;
            rtbStatsDisplay.Font = new Font("Arial", 10);
            rtbStatsDisplay.BackColor = useCustomPanelColors ? Color.White : Theme.Current.PanelBackground;
            rtbStatsDisplay.ForeColor = Theme.Current.Foreground;
            rtbStatsDisplay.Location = new Point(0, lblStatsTitle.Height);
            rtbStatsDisplay.Size = new Size(splitContainerMain.Panel1.Width, splitContainerMain.Panel1.Height - lblStatsTitle.Height);
            rtbStatsDisplay.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitContainerMain.Panel1.Controls.Add(rtbStatsDisplay);

            // ラウンドログ表示欄
            lblRoundLogTitle = new Label();
            lblRoundLogTitle.Text = LanguageManager.Translate("ラウンドログ");
            lblRoundLogTitle.Dock = DockStyle.None;
            lblRoundLogTitle.Location = new Point(0, 0);
            lblRoundLogTitle.Height = 20;
            lblRoundLogTitle.ForeColor = Theme.Current.Foreground;
            splitContainerMain.Panel2.Controls.Add(lblRoundLogTitle);

            logPanel = new LogPanel();
            logPanel.RoundLogTextBox.Location = new Point(0, lblRoundLogTitle.Height);
            logPanel.RoundLogTextBox.Size = new Size(splitContainerMain.Panel2.Width, splitContainerMain.Panel2.Height - lblRoundLogTitle.Height);
            logPanel.RoundLogTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitContainerMain.Panel2.Controls.Add(logPanel.RoundLogTextBox);

            this.Controls.Add(splitContainerMain);
        }

        private void ReinitializeInfoPanel()
        {
            string previousNextRoundText = InfoPanel?.NextRoundType?.Text ?? NextRoundPredictionUnavailableMessage;
            Color previousNextRoundColor = InfoPanel?.NextRoundType?.ForeColor ?? Color.White;

            InfoPanel = new InfoPanel();
            InfoPanel.BackColor = _settings.BackgroundColor_InfoPanel;
            if (hasObservedSpecialRound)
            {
                InfoPanel.NextRoundType.Text = previousNextRoundText;
                InfoPanel.NextRoundType.ForeColor = previousNextRoundColor;
            }
            else
            {
                InfoPanel.NextRoundType.Text = NextRoundPredictionUnavailableMessage;
                InfoPanel.NextRoundType.ForeColor = Color.White;
            }
            this.Controls.Add(InfoPanel);
            if (terrorInfoPanel != null)
            {
                this.Controls.SetChildIndex(InfoPanel, this.Controls.GetChildIndex(terrorInfoPanel));
            }
            ApplyTheme();
            MainForm_Resize(this, EventArgs.Empty);
            UpdateOverlay(OverlaySection.NextRound, form => form.SetValue(GetNextRoundOverlayValue()));
        }

        private void ApplyTheme()
        {
            var themeColors = Theme.Current;
            this.BackColor = themeColors.Background;
            lblDebugInfo.ForeColor = Color.Blue;
            InfoPanel.ApplyTheme();
            bool useCustomPanelColors = string.Equals(_settings.ThemeKey, Theme.DefaultThemeKey, StringComparison.OrdinalIgnoreCase);
            InfoPanel.BackColor = useCustomPanelColors ? _settings.BackgroundColor_InfoPanel : themeColors.PanelBackground;
            rtbStatsDisplay.ForeColor = themeColors.Foreground;
            rtbStatsDisplay.BackColor = useCustomPanelColors ? Color.White : themeColors.PanelBackground;
            logPanel.ApplyTheme();
            logPanel.AggregateStatsTextBox.BackColor = useCustomPanelColors ? Color.White : themeColors.PanelBackground;
            logPanel.RoundLogTextBox.BackColor = useCustomPanelColors ? Color.White : themeColors.PanelBackground;
            terrorInfoPanel.ApplyTheme();
            LogUi("Theme applied to main form components.", LogEventLevel.Debug);
        }

        private void MainForm_Resize(object? sender, EventArgs? e)
        {
            LogUi($"Main form resized to {this.ClientSize.Width}x{this.ClientSize.Height}.", LogEventLevel.Debug);
            int margin = 10;
            int contentWidth = this.ClientSize.Width - 2 * margin;
            int currentY = (mainMenuStrip != null ? mainMenuStrip.Bottom : 0) + margin;
            lblStatus.Location = new Point(margin, currentY);
            lblStatus.Width = contentWidth / 2 - 5;
            lblOSCStatus.Location = new Point(lblStatus.Right + 10, currentY);
            lblDebugInfo.Location = new Point(lblOSCStatus.Right + 10, currentY);
            currentY += lblStatus.Height + margin;
            btnToggleTopMost.Location = new Point(margin, currentY);
            btnToggleTopMost.Width = contentWidth / 2 - 5;
            btnSettings.Location = new Point(btnToggleTopMost.Right + 10, currentY);
            btnSettings.Width = contentWidth / 2 - 5;
            currentY += btnToggleTopMost.Height + margin;
            InfoPanel.Location = new Point(margin, currentY);
            InfoPanel.Width = contentWidth;
            currentY += InfoPanel.Height + margin;
            terrorInfoPanel.Location = new Point(margin, currentY);
            terrorInfoPanel.Width = contentWidth;
            currentY += terrorInfoPanel.Height + margin;
            splitContainerMain.Location = new Point(margin, currentY);
            splitContainerMain.Width = contentWidth;
            splitContainerMain.Height = this.ClientSize.Height - currentY - margin;
        }

        private void BtnToggleTopMost_Click(object sender, EventArgs e)
        {
            this.TopMost = !this.TopMost;
            btnToggleTopMost.Text = this.TopMost ? LanguageManager.Translate("固定解除") : LanguageManager.Translate("固定する");
            LogUi($"TopMost toggled. New state: {this.TopMost}.", LogEventLevel.Debug);
        }

        private async void BtnSettings_Click(object sender, EventArgs e)
        {
            LogUi("Settings dialog requested by user.");
            using (SettingsForm settingsForm = new SettingsForm(_settings))
            {
                var buildContext = new ModuleSettingsViewBuildContext(settingsForm, settingsForm.SettingsPanel, _settings, _moduleHost.CurrentServiceProvider);
                _moduleHost.NotifySettingsViewBuilding(buildContext);

                _moduleHost.NotifyThemeCatalogBuilding(new ModuleThemeCatalogContext(Theme.RegisteredThemes, Theme.RegisterTheme, _moduleHost.CurrentServiceProvider));
                settingsForm.SettingsPanel.LoadThemeOptions(Theme.RegisteredThemes, _settings.ThemeKey);
                settingsForm.SettingsPanel.ShowStatsCheckBox.Checked = _settings.ShowStats;
                settingsForm.SettingsPanel.DebugInfoCheckBox.Checked = _settings.ShowDebug;
                settingsForm.SettingsPanel.ToggleRoundLogCheckBox.Checked = _settings.ShowRoundLog;
                settingsForm.SettingsPanel.RoundTypeCheckBox.Checked = _settings.Filter_RoundType;
                settingsForm.SettingsPanel.TerrorCheckBox.Checked = _settings.Filter_Terror;
                settingsForm.SettingsPanel.AppearanceCountCheckBox.Checked = _settings.Filter_Appearance;
                settingsForm.SettingsPanel.SurvivalCountCheckBox.Checked = _settings.Filter_Survival;
                settingsForm.SettingsPanel.DeathCountCheckBox.Checked = _settings.Filter_Death;
                settingsForm.SettingsPanel.SurvivalRateCheckBox.Checked = _settings.Filter_SurvivalRate;
                settingsForm.SettingsPanel.OverlayVelocityCheckBox.Checked = _settings.OverlayShowVelocity;
                settingsForm.SettingsPanel.OverlayAngleCheckBox.Checked = false;
                settingsForm.SettingsPanel.OverlayAngleCheckBox.Enabled = false;
                settingsForm.SettingsPanel.OverlayTerrorCheckBox.Checked = _settings.OverlayShowTerror;
                settingsForm.SettingsPanel.OverlayUnboundTerrorDetailsCheckBox.Checked = _settings.OverlayShowUnboundTerrorDetails;
                settingsForm.SettingsPanel.OverlayDamageCheckBox.Checked = _settings.OverlayShowDamage;
                settingsForm.SettingsPanel.OverlayNextRoundCheckBox.Checked = _settings.OverlayShowNextRound;
                settingsForm.SettingsPanel.OverlayRoundStatusCheckBox.Checked = _settings.OverlayShowRoundStatus;
                settingsForm.SettingsPanel.OverlayRoundHistoryCheckBox.Checked = _settings.OverlayShowRoundHistory;
                settingsForm.SettingsPanel.OverlayRoundStatsCheckBox.Checked = _settings.OverlayShowRoundStats;
                settingsForm.SettingsPanel.OverlayTerrorInfoCheckBox.Checked = _settings.OverlayShowTerrorInfo;
                settingsForm.SettingsPanel.OverlayShortcutsCheckBox.Checked = _settings.OverlayShowShortcuts;
                settingsForm.SettingsPanel.OverlayClockCheckBox.Checked = _settings.OverlayShowClock;
                settingsForm.SettingsPanel.OverlayInstanceTimerCheckBox.Checked = _settings.OverlayShowInstanceTimer;
                settingsForm.SettingsPanel.SetOverlayOpacity(_settings.OverlayOpacity);
                int historyLength = _settings.OverlayRoundHistoryLength <= 0 ? 3 : _settings.OverlayRoundHistoryLength;
                historyLength = Math.Max((int)settingsForm.SettingsPanel.OverlayRoundHistoryCountNumeric.Minimum, Math.Min((int)settingsForm.SettingsPanel.OverlayRoundHistoryCountNumeric.Maximum, historyLength));
                settingsForm.SettingsPanel.OverlayRoundHistoryCountNumeric.Value = historyLength;
                settingsForm.SettingsPanel.InfoPanelBgLabel.BackColor = _settings.BackgroundColor_InfoPanel;
                settingsForm.SettingsPanel.StatsBgLabel.BackColor = _settings.BackgroundColor_Stats;
                settingsForm.SettingsPanel.LogBgLabel.BackColor = _settings.BackgroundColor_Log;
                settingsForm.SettingsPanel.FixedTerrorColorLabel.BackColor = _settings.FixedTerrorColor;
                for (int i = 0; i < settingsForm.SettingsPanel.RoundTypeStatsListBox.Items.Count; i++)
                {
                    string item = settingsForm.SettingsPanel.RoundTypeStatsListBox.Items[i].ToString();
                    settingsForm.SettingsPanel.RoundTypeStatsListBox.SetItemChecked(i, _settings.RoundTypeStats.Contains(item));
                }
                settingsForm.SettingsPanel.autoSuicideCheckBox.Checked = _settings.AutoSuicideEnabled;
                settingsForm.SettingsPanel.autoSuicideUseDetailCheckBox.Checked = _settings.AutoSuicideUseDetail;
                for (int i = 0; i < settingsForm.SettingsPanel.autoSuicideRoundListBox.Items.Count; i++)
                {
                    string item = settingsForm.SettingsPanel.autoSuicideRoundListBox.Items[i].ToString();
                    settingsForm.SettingsPanel.autoSuicideRoundListBox.SetItemChecked(i, _settings.AutoSuicideRoundTypes.Contains(item));
                }
                settingsForm.SettingsPanel.oscPortNumericUpDown.Value = _settings.OSCPort;
                settingsForm.SettingsPanel.webSocketIpTextBox.Text = _settings.WebSocketIp;
                settingsForm.SettingsPanel.AutoLaunchEnabledCheckBox.Checked = _settings.AutoLaunchEnabled;
                settingsForm.SettingsPanel.LoadAutoLaunchEntries(_settings.AutoLaunchEntries);
                settingsForm.SettingsPanel.ItemMusicEnabledCheckBox.Checked = _settings.ItemMusicEnabled;
                settingsForm.SettingsPanel.LoadItemMusicEntries(_settings.ItemMusicEntries);
                settingsForm.SettingsPanel.DiscordWebhookUrlTextBox.Text = _settings.DiscordWebhookUrl;

                var openedContext = new ModuleSettingsViewLifecycleContext(settingsForm, settingsForm.SettingsPanel, _settings, ModuleSettingsViewStage.Opened, null, _moduleHost.CurrentServiceProvider);
                _moduleHost.NotifySettingsViewOpened(openedContext);

                var dialogResult = settingsForm.ShowDialog();

                var closingContext = new ModuleSettingsViewLifecycleContext(settingsForm, settingsForm.SettingsPanel, _settings, ModuleSettingsViewStage.Closing, dialogResult, _moduleHost.CurrentServiceProvider);
                _moduleHost.NotifySettingsViewClosing(closingContext);

                if (dialogResult == DialogResult.OK)
                {
                    var applyingContext = new ModuleSettingsViewLifecycleContext(settingsForm, settingsForm.SettingsPanel, _settings, ModuleSettingsViewStage.Applying, dialogResult, _moduleHost.CurrentServiceProvider);
                    _moduleHost.NotifySettingsViewApplying(applyingContext);

                    _settings.OSCPort = (int)settingsForm.SettingsPanel.oscPortNumericUpDown.Value;
                    _settings.WebSocketIp = settingsForm.SettingsPanel.webSocketIpTextBox.Text;
                    _settings.ShowStats = settingsForm.SettingsPanel.ShowStatsCheckBox.Checked;
                    _settings.ShowDebug = settingsForm.SettingsPanel.DebugInfoCheckBox.Checked;
                    _settings.ShowRoundLog = settingsForm.SettingsPanel.ToggleRoundLogCheckBox.Checked;
                    _settings.Filter_RoundType = settingsForm.SettingsPanel.RoundTypeCheckBox.Checked;
                    _settings.Filter_Terror = settingsForm.SettingsPanel.TerrorCheckBox.Checked;
                    _settings.Filter_Appearance = settingsForm.SettingsPanel.AppearanceCountCheckBox.Checked;
                    _settings.Filter_Survival = settingsForm.SettingsPanel.SurvivalCountCheckBox.Checked;
                    _settings.Filter_Death = settingsForm.SettingsPanel.DeathCountCheckBox.Checked;
                    _settings.Filter_SurvivalRate = settingsForm.SettingsPanel.SurvivalRateCheckBox.Checked;
                    _settings.OverlayShowVelocity = settingsForm.SettingsPanel.OverlayVelocityCheckBox.Checked;
                    _settings.OverlayShowAngle = false;
                    _settings.OverlayShowTerror = settingsForm.SettingsPanel.OverlayTerrorCheckBox.Checked;
                    _settings.OverlayShowUnboundTerrorDetails = settingsForm.SettingsPanel.OverlayUnboundTerrorDetailsCheckBox.Checked;
                    _settings.OverlayShowDamage = settingsForm.SettingsPanel.OverlayDamageCheckBox.Checked;
                    _settings.OverlayShowNextRound = settingsForm.SettingsPanel.OverlayNextRoundCheckBox.Checked;
                    _settings.OverlayShowRoundStatus = settingsForm.SettingsPanel.OverlayRoundStatusCheckBox.Checked;
                    _settings.OverlayShowRoundHistory = settingsForm.SettingsPanel.OverlayRoundHistoryCheckBox.Checked;
                    _settings.OverlayShowRoundStats = settingsForm.SettingsPanel.OverlayRoundStatsCheckBox.Checked;
                    _settings.OverlayShowTerrorInfo = settingsForm.SettingsPanel.OverlayTerrorInfoCheckBox.Checked;
                    _settings.OverlayShowShortcuts = settingsForm.SettingsPanel.OverlayShortcutsCheckBox.Checked;
                    _settings.OverlayShowClock = settingsForm.SettingsPanel.OverlayClockCheckBox.Checked;
                    _settings.OverlayShowInstanceTimer = settingsForm.SettingsPanel.OverlayInstanceTimerCheckBox.Checked;
                    _settings.OverlayOpacity = settingsForm.SettingsPanel.GetOverlayOpacity();
                    _settings.OverlayRoundHistoryLength = (int)settingsForm.SettingsPanel.OverlayRoundHistoryCountNumeric.Value;
                    _settings.BackgroundColor_InfoPanel = settingsForm.SettingsPanel.InfoPanelBgLabel.BackColor;
                    _settings.BackgroundColor_Stats = settingsForm.SettingsPanel.StatsBgLabel.BackColor;
                    _settings.BackgroundColor_Log = settingsForm.SettingsPanel.LogBgLabel.BackColor;
                    _settings.FixedTerrorColor = settingsForm.SettingsPanel.FixedTerrorColorLabel.BackColor;
                    _settings.RoundTypeStats.Clear();
                    foreach (object item in settingsForm.SettingsPanel.RoundTypeStatsListBox.CheckedItems)
                    {
                        _settings.RoundTypeStats.Add(item.ToString());
                    }
                    _settings.AutoSuicideEnabled = settingsForm.SettingsPanel.autoSuicideCheckBox.Checked;
                    _settings.AutoSuicideUseDetail = settingsForm.SettingsPanel.autoSuicideUseDetailCheckBox.Checked;
                    _settings.AutoSuicideRoundTypes.Clear();
                    foreach (object item in settingsForm.SettingsPanel.autoSuicideRoundListBox.CheckedItems)
                    {
                        _settings.AutoSuicideRoundTypes.Add(item.ToString());
                    }
                    settingsForm.SettingsPanel.CleanAutoSuicideDetailRules();
                    _settings.AutoSuicideDetailCustom = settingsForm.SettingsPanel.GetCustomAutoSuicideLines();
                    _settings.AutoSuicideFuzzyMatch = settingsForm.SettingsPanel.autoSuicideFuzzyCheckBox.Checked;
                    _settings.AutoLaunchEnabled = settingsForm.SettingsPanel.AutoLaunchEnabledCheckBox.Checked;
                    _settings.AutoLaunchEntries = settingsForm.SettingsPanel.GetAutoLaunchEntries();
                    _settings.ItemMusicEnabled = settingsForm.SettingsPanel.ItemMusicEnabledCheckBox.Checked;
                    _settings.ItemMusicEntries = settingsForm.SettingsPanel.GetItemMusicEntries();
                    _settings.AutoLaunchExecutablePath = string.Empty;
                    _settings.AutoLaunchArguments = string.Empty;
                    _settings.ItemMusicItemName = string.Empty;
                    _settings.ItemMusicSoundPath = string.Empty;
                    _settings.ItemMusicMinSpeed = 0;
                    _settings.ItemMusicMaxSpeed = 0;
                    _settings.DiscordWebhookUrl = settingsForm.SettingsPanel.DiscordWebhookUrlTextBox.Text.Trim();
                    _settings.ThemeKey = settingsForm.SettingsPanel.SelectedThemeKey;
                    LoadAutoSuicideRules();
                    if (!_settings.AutoSuicideEnabled && !issetAllSelfKillMode)
                    {
                        CancelAutoSuicide();
                    }
                    UpdateItemMusicPlayer(null);
                    ResetItemMusicTracking();

                    _settings.apikey = settingsForm.SettingsPanel.apiKeyTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(_settings.apikey))
                    {
                        _settings.apikey = string.Empty;
                    }
                    else if (_settings.apikey.Length < 32)
                    {
                        MessageBox.Show(LanguageManager.Translate("APIキーは32文字以上である必要があります。"), LanguageManager.Translate("エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    RecomputeOverlayTerrorBase();
                    RefreshTerrorInfoOverlay();

                    Theme.SetTheme(_settings.ThemeKey, new ThemeApplicationContext(this, _moduleHost.CurrentServiceProvider));
                    _moduleHost.NotifyMainWindowThemeChanged(new ModuleMainWindowThemeContext(this, _settings.ThemeKey, Theme.CurrentDescriptor, _moduleHost.CurrentServiceProvider));
                    ApplyTheme();
                    _moduleHost.NotifyMainWindowLayoutUpdated(new ModuleMainWindowLayoutContext(this, _moduleHost.CurrentServiceProvider));
                    InfoPanel.TerrorValue.ForeColor = _settings.FixedTerrorColor;
                    UpdateAggregateStatsDisplay();
                    UpdateDisplayVisibility();
                    ApplyOverlayBackgroundOpacityToForms();
                    UpdateOverlayVisibility();
                    UpdateShortcutOverlayState();
                    ApplyOverlayRoundHistorySettings();
                    _moduleHost.NotifyAuxiliaryWindowCatalogBuilding();
                    BuildAuxiliaryWindowsMenu();
                    await _settings.SaveAsync();
                }

                var closedContext = new ModuleSettingsViewLifecycleContext(settingsForm, settingsForm.SettingsPanel, _settings, ModuleSettingsViewStage.Closed, dialogResult, _moduleHost.CurrentServiceProvider);
                _moduleHost.NotifySettingsViewClosed(closedContext);
            }
        }

        private void BuildAuxiliaryWindowsMenu()
        {
            if (windowsMenuItem == null)
            {
                return;
            }

            windowsMenuItem.DropDownItems.Clear();

            if (_moduleHost.AuxiliaryWindows == null || _moduleHost.AuxiliaryWindows.Count == 0)
            {
                windowsMenuItem.Visible = false;
                return;
            }

            windowsMenuItem.Visible = true;

            foreach (var descriptor in _moduleHost.AuxiliaryWindows.OrderBy(d => d.DisplayName, StringComparer.CurrentCulture))
            {
                if (descriptor == null)
                {
                    continue;
                }

                var descriptorId = descriptor.Id;
                var menuItem = new ToolStripMenuItem(descriptor.DisplayName)
                {
                    Tag = descriptorId
                };

                menuItem.Click += (s, e) => _moduleHost.ShowAuxiliaryWindow(descriptorId, this);
                windowsMenuItem.DropDownItems.Add(menuItem);
            }
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            LogUi("Main form load sequence starting.");
            MainForm_Resize(null, null);
            UpdateDisplayVisibility();
            var shouldStartOscRepeater = true;
            if (_oscRepeaterPolicies.Count > 0)
            {
                foreach (var policy in _oscRepeaterPolicies)
                {
                    if (policy == null)
                    {
                        continue;
                    }

                    bool allowStartup = true;
                    try
                    {
                        allowStartup = policy.ShouldStartOscRepeater(_settings);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"OSC repeater policy {policy.GetType().FullName} threw an exception: {ex.Message}", LogEventLevel.Warning);
                        continue;
                    }

                    if (!allowStartup)
                    {
                        LogUi($"OSC repeater startup vetoed by {policy.GetType().FullName}.", LogEventLevel.Information);
                        shouldStartOscRepeater = false;
                        break;
                    }
                }
            }

            if (shouldStartOscRepeater)
            {
                await InitializeOSCRepeater();
            }
            else
            {
                LogUi("OSC repeater startup skipped by policy.", LogEventLevel.Information);
            }
            await CheckForUpdatesAsync();
            LogUi("Main form load sequence completed.", LogEventLevel.Debug);
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                LogUi("Checking for application updates.", LogEventLevel.Debug);
                using (var client = new HttpClient())
                {
                    var json = await client.GetStringAsync("https://raw.githubusercontent.com/lovetwice1012/ToNRoundCounter/refs/heads/master/version.json");
                    var data = JObject.Parse(json);
                    var latest = data["latest"]?.ToString();
                    var url = data["url"]?.ToString();
                    if (!string.IsNullOrEmpty(latest) && !string.IsNullOrEmpty(url) && IsOlderVersion(version, latest))
                    {
                        LogUi($"Update available. Current: {version}, Latest: {latest}.");
                        var result = MessageBox.Show($"新しいバージョン {latest} が利用可能です。\n更新をダウンロードして適用しますか？", "アップデート", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes)
                        {
                            LogUi("User accepted update download.");
                            var zipPath = Path.Combine(Path.GetTempPath(), "ToNRoundCounter_update.zip");
                            var bytes = await client.GetByteArrayAsync(url);
                            File.WriteAllBytes(zipPath, bytes);

                            var updaterExe = Path.Combine(Directory.GetCurrentDirectory(), "Updater.exe");
                            if (File.Exists(updaterExe))
                            {
                                LogUi($"Launching updater from '{updaterExe}' with package '{zipPath}'.");
                                Process.Start(new ProcessStartInfo(updaterExe)
                                {
                                    Arguments = $"\"{zipPath}\" \"{WinFormsApp.ExecutablePath}\"",
                                    UseShellExecute = false
                                });
                                WinFormsApp.Exit();
                            }
                            else
                            {
                                LogUi("Updater executable not found during update attempt.", LogEventLevel.Error);
                                MessageBox.Show("Updater.exe が見つかりません。", "アップデート", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            LogUi("User declined update installation.", LogEventLevel.Debug);
                        }
                    }
                    else
                    {
                        LogUi("Application is up to date.", LogEventLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUi($"Failed to check for updates: {ex.Message}", LogEventLevel.Warning);
            }
        }

        private bool IsOlderVersion(string current, string? latest)
        {
            if (string.IsNullOrEmpty(latest))
            {
                return false;
            }

            try
            {
                return new Version(current) < new Version(latest);
            }
            catch
            {
                return false;
            }
        }

        private void UnsubscribeEventBus()
        {
            LogUi("Unsubscribing event bus handlers.", LogEventLevel.Debug);
            if (_wsConnectedHandler != null) _eventBus.Unsubscribe(_wsConnectedHandler);
            if (_wsDisconnectedHandler != null) _eventBus.Unsubscribe(_wsDisconnectedHandler);
            if (_oscConnectedHandler != null) _eventBus.Unsubscribe(_oscConnectedHandler);
            if (_oscDisconnectedHandler != null) _eventBus.Unsubscribe(_oscDisconnectedHandler);
            if (_wsMessageHandler != null) _eventBus.Unsubscribe(_wsMessageHandler);
            if (_oscMessageHandler != null) _eventBus.Unsubscribe(_oscMessageHandler);
            if (_settingsValidationFailedHandler != null) _eventBus.Unsubscribe(_settingsValidationFailedHandler);
        }


        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            LogUi("Main form closing initiated.");
            SaveRoundLogsToFile();
            CaptureOverlayPositions();
            try
            {
                await _settings.SaveAsync();
            }
            catch (Exception ex)
            {
                LogUi($"Failed to save settings on close: {ex.Message}", LogEventLevel.Warning);
            }
            _cancellation.Cancel();
            await webSocketClient.StopAsync();
            oscListener.Stop();
            if (oscRepeaterProcess != null && !oscRepeaterProcess.HasExited)
            {
                try
                {
                    oscRepeaterProcess.Kill();
                    oscRepeaterProcess.WaitForExit();
                }
                catch { }
            }
            LogUi("Main form closing sequence finished. Base closing invoked.", LogEventLevel.Debug);
            base.OnFormClosing(e);
        }

        private void SaveRoundLogsToFile()
        {
            try
            {
                LogUi("Persisting round logs to disk.", LogEventLevel.Debug);
                var history = stateService.GetRoundLogHistory();
                if (history == null)
                {
                    LogUi("No round log history available; skipping save.", LogEventLevel.Debug);
                    return;
                }

                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                string roundLogsDirectory = Path.Combine(baseDirectory, "roundLogs");
                Directory.CreateDirectory(roundLogsDirectory);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                string filePath = Path.Combine(roundLogsDirectory, $"{timestamp}.log");

                var logLines = history.Select(entry => entry.Item2).ToList();

                if (logLines.Count == 0)
                {
                    File.WriteAllText(filePath, "ラウンドログは記録されませんでした。");
                }
                else
                {
                    File.WriteAllLines(filePath, logLines);
                }

                _logger?.LogEvent("RoundLog", $"ラウンドログをファイルに保存しました: {filePath}");
                LogUi($"Round log history saved to '{filePath}'.", LogEventLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("RoundLogError", $"ラウンドログの保存に失敗しました: {ex.Message}", LogEventLevel.Error);
                LogUi($"Failed to save round logs: {ex.Message}", LogEventLevel.Error);
            }
        }

        private async Task HandleEventAsync(string message)
        {
            LogUi($"Processing inbound WebSocket payload ({message.Length} chars).", LogEventLevel.Debug);
            try
            {
                var json = JObject.Parse(message);
                string eventType = json.Value<string>("Type") ?? json.Value<string>("TYPE") ?? "Unknown";
                _logger.LogEvent(eventType, message);
                int command = -1;
                if (json.TryGetValue("Command", out JToken? commandToken))
                {
                    command = commandToken.Value<int>();
                }
                LogUi($"WebSocket event '{eventType}' received with command {command}.", LogEventLevel.Debug);
                if (eventType == "CONNECTED")
                {
                    stateService.PlayerDisplayName = json.Value<string>("DisplayName") ?? "";
                    if (isRestarted == false)
                    {
                        UpdateAndRestart();
                        isRestarted = true;
                    }
                }
                else if (eventType == "ROUND_ACTIVE")
                {
                    ProcessRoundActive(json);
                }
                else if (eventType == "ROUND_TYPE" && command == 1)
                {
                    string roundType = json.Value<string>("DisplayName") ?? "Default";
                    int displayColorInt = json.Value<int>("DisplayColor");
                    var currentRound = new Round();
                    stateService.UpdateCurrentRound(currentRound);
                    currentRound.RoundType = roundType;
                    currentRound.IsDeath = false;
                    currentRound.TerrorKey = string.Empty;
                    currentRound.RoundColor = displayColorInt;
                    string mapName = string.Empty;
                    string itemName = string.Empty;
                    _dispatcher.Invoke(() =>
                    {
                        mapName = InfoPanel.MapValue.Text;
                        itemName = InfoPanel.ItemValue.Text;
                    });
                    currentRound.MapName = mapName;
                    currentRound.Damage = 0;
                    currentRound.PageCount = 0;
                    if (!string.IsNullOrEmpty(itemName))
                        currentRound.ItemNames.Add(itemName);
                    _dispatcher.Invoke(() =>
                    {
                        UpdateRoundTypeLabel();
                        InfoPanel.RoundTypeValue.ForeColor = ConvertColorFromInt(displayColorInt);
                    });
                    //もしtesterNamesに含まれているかつオルタネイトなら、オルタネイトラウンド開始の音を鳴らす
                    if (testerNames.Contains(stateService.PlayerDisplayName) && roundType == "オルタネイト")
                    {
                        PlayFromStart(tester_roundStartAlternatePlayer);
                    }
                    //issetAllSelfKillModeがtrueなら13秒後に自殺入力をする
                    int autoAction = ShouldAutoSuicide(roundType, null, out var hasPendingDelayed);
                    if (issetAllSelfKillMode)
                    {
                        ScheduleAutoSuicide(TimeSpan.FromSeconds(13), true, true);
                    }
                    else if (autoAction == 1)
                    {
                        var delay = hasPendingDelayed ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(13);
                        ScheduleAutoSuicide(delay, true);
                    }
                    else if (autoAction == 2)
                    {
                        ScheduleAutoSuicide(TimeSpan.FromSeconds(40), true);
                    }

                }
                else if (eventType == "TRACKER")
                {
                    string trackerEvent = json.Value<string>("event") ?? "";
                    if (trackerEvent == "round_start")
                    {
                        if (lastOptedIn != false)
                        {
                            var trackerRound = new Round();
                            stateService.UpdateCurrentRound(trackerRound);
                            trackerRound.RoundType = string.Empty;
                            trackerRound.IsDeath = false;
                            trackerRound.TerrorKey = string.Empty;
                            trackerRound.RoundColor = 0xFFFFFF;
                            string mapName = string.Empty;
                            _dispatcher.Invoke(() => mapName = InfoPanel.MapValue.Text);
                            trackerRound.MapName = mapName;
                            trackerRound.Damage = 0;
                        }
                    }
                    else if (trackerEvent == "round_won")
                    {
                        var existingRound = stateService.CurrentRound;
                        if (existingRound != null)
                        {
                            FinalizeCurrentRound(existingRound.IsDeath ? "☠" : "✅");
                        }
                    }
                    else if (trackerEvent == "round_lost")
                    {
                        var existingRound = stateService.CurrentRound;
                        if (existingRound != null && !existingRound.IsDeath)
                        {
                            existingRound.IsDeath = true;
                            FinalizeCurrentRound("☠");
                        }
                    }
                }
                else if (eventType == "LOCATION" && command == 1)
                {
                    _dispatcher.Invoke(() =>
                    {
                        InfoPanel.MapValue.Text = json.Value<string>("Name") ?? "";
                    });
                    var existingRound = stateService.CurrentRound;
                    if (existingRound != null)
                    {
                        existingRound.MapName = json.Value<string>("Name") ?? string.Empty;
                    }
                }
                else if (eventType == "TERRORS" && (command == 0 || command == 1))
                {
                    string displayName = json.Value<string>("DisplayName") ?? "";
                    int displayColorInt = json.Value<int>("DisplayColor");
                    Color color = ConvertColorFromInt(displayColorInt);

                    var activeRound = stateService.CurrentRound;
                    if (activeRound != null && !activeRound.RoundColor.HasValue)
                    {
                        activeRound.RoundColor = displayColorInt;
                    }

                    List<(string name, int count)>? terrors = null;
                    var namesArray = json.Value<JArray>("Names");
                    if (namesArray != null && namesArray.Count > 0)
                    {
                        var arr = namesArray.Select(token => token.ToString()).ToList();
                        terrors = arr.Select(n => (n, 1)).ToList();
                    }

                    var roundType = stateService.CurrentRound?.RoundType ?? string.Empty;
                    if ((terrors == null || terrors.Count == 0) && roundType == "アンバウンド")
                    {
                        var lookup = UnboundRoundDefinitions.GetTerrors(displayName);
                        if (lookup != null)
                        {
                            terrors = lookup.ToList();
                        }
                    }

                    var namesForLogic = terrors?.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
                    var activeRoundForNames = stateService.CurrentRound;
                    if (activeRoundForNames != null && namesForLogic != null && namesForLogic.Count > 0)
                    {
                        string joinedNames = string.Join(" & ", namesForLogic);
                        activeRoundForNames.TerrorKey = joinedNames;
                    }

                    _dispatcher.Invoke(() => { UpdateTerrorDisplay(displayName, color, terrors); });

                    var activeRoundForAuto = stateService.CurrentRound;
                    if (activeRoundForAuto != null)
                    {
                        if (roundType == "ブラッドバス" && namesForLogic != null && namesForLogic.Any(n => n.Contains("LVL 3")))
                        {
                            roundType = "EX";
                        }
                        //もしroundTypeが自動自殺ラウンド対象なら自動自殺
                        int terrorAction = ShouldAutoSuicide(roundType, activeRoundForAuto.TerrorKey);
                        if (terrorAction == 0 && autoSuicideService.HasScheduled && !issetAllSelfKillMode)
                        {
                            CancelAutoSuicide();
                        }
                        if (issetAllSelfKillMode)
                        {
                            _ = Task.Run(() => PerformAutoSuicide());
                        }
                        else if (terrorAction == 1)
                        {
                            _ = Task.Run(() => PerformAutoSuicide());
                        }
                        else if (terrorAction == 2)
                        {
                            TimeSpan remaining = TimeSpan.FromSeconds(40) - (DateTime.UtcNow - autoSuicideService.RoundStartTime);
                            if (remaining > TimeSpan.Zero)
                            {
                                ScheduleAutoSuicide(remaining, false, allRoundsForcedSchedule);
                            }
                        }
                    }
                }
                else if (eventType == "ITEM")
                {
                    if (command == 1)
                        _dispatcher.Invoke(() => { UpdateItemDisplay(json); });
                    else if (command == 0)
                        _dispatcher.Invoke(() => { ClearItemDisplay(); });
                }
                else if (eventType == "DAMAGED")
                {
                    int damageValue = json.Value<int>("Value");
                    var currentRound = stateService.CurrentRound;
                    if (currentRound != null)
                    {
                        currentRound.Damage += damageValue;
                        _dispatcher.Invoke(() =>
                        {
                            if (InfoPanel?.DamageValue != null)
                            {
                                InfoPanel.DamageValue.Text = currentRound.Damage.ToString();
                                UpdateOverlay(OverlaySection.Damage, form => form.SetValue(GetDamageOverlayText()));
                            }
                        });
                    }
                }
                else if (eventType == "DEATH")
                {
                    string deathName = json.Value<string>("Name") ?? "";
                    bool isLocal = json.Value<bool?>("IsLocal") ?? false;
                    if (stateService.CurrentRound != null && (deathName == stateService.PlayerDisplayName || isLocal))
                    {
                        stateService.CurrentRound.IsDeath = true;
                        FinalizeCurrentRound("☠");
                        if (stateService.PlayerDisplayName == "Kotetsu Wilde")
                        //if (stateService.PlayerDisplayName == "yussy5373")
                        {
                            int randomNum = randomGenerator.Next(1, 4);
                            if (randomNum == 1)
                            {
                                PlayFromStart(tester_BATOU_01Player);
                            }
                            else if (randomNum == 2)
                            {
                                PlayFromStart(tester_BATOU_02Player);
                            }
                            else if (randomNum == 3)
                            {
                                PlayFromStart(tester_BATOU_03Player);
                            }
                        }
                    }
                }
                else if (eventType == "STATS")
                {
                    /*
                    string statName = json.Value<string>("Name") ?? string.Empty;
                    JToken valueToken = json["Value"];
                    if (!string.IsNullOrEmpty(statName) && valueToken != null)
                    {
                        stateService.UpdateStat(statName, valueToken.ToObject<object>());
                    }
                    */
                }
                else if (eventType == "ALIVE")
                {
                    /*
                    bool isAlive = json.Value<bool?>("Value") ?? true;
                    if (stateService.CurrentRound != null)
                    {
                        if (!isAlive && !stateService.CurrentRound.IsDeath)
                        {
                            stateService.CurrentRound.IsDeath = true;
                            FinalizeCurrentRound("☠");
                        }
                        else if (isAlive)
                        {
                            stateService.CurrentRound.IsDeath = false;
                        }
                    }
                    */
                }
                else if (eventType == "REBORN")
                {
                    /*
                    bool reborn = json.Value<bool?>("Value") ?? false;
                    if (stateService.CurrentRound != null && reborn)
                    {
                        stateService.CurrentRound.IsDeath = false;
                    }
                    */
                }
                else if (eventType == "PAGE_COUNT")
                {
                    int pages = json.Value<int>("Value");
                    if (stateService.CurrentRound != null)
                    {
                        stateService.CurrentRound.PageCount = pages;
                        _dispatcher.Invoke(UpdateRoundTypeLabel);
                    }
                }
                else if (eventType == "PLAYER_JOIN")
                {
                    /*
                    if (stateService.CurrentRound != null)
                    {
                        stateService.CurrentRound.InstancePlayersCount++;
                    }
                    */
                }
                else if (eventType == "PLAYER_LEAVE")
                {
                    /*
                    if (stateService.CurrentRound != null && stateService.CurrentRound.InstancePlayersCount > 0)
                    {
                        stateService.CurrentRound.InstancePlayersCount--;
                    }
                    */
                }
                else if (eventType == "OPTED_IN")
                {
                    if (!isNotifyActivated)
                    {
                        _ = Task.Run(() => SendAlertOscMessagesAsync(0.9f, false));
                        isNotifyActivated = true;
                    }
                    bool optedIn = json.Value<bool?>("Value") ?? true;
                    lastOptedIn = optedIn;
                    if (stateService.CurrentRound != null && optedIn == false)
                        stateService.CurrentRound.IsDeath = true;
                }
                else if (eventType == "INSTANCE")
                {
                    // "INSTANCE" タイプの接続を受けたら、メッセージ内の "Value" フィールドを使ってインスタンス接続を開始する
                    string instanceValue = json.Value<string>("Value") ?? string.Empty;
                    if (!string.IsNullOrEmpty(instanceValue))
                    {
                        bool instanceChanged;
                        DateTimeOffset now = DateTimeOffset.Now;
                        lock (instanceTimerSync)
                        {
                            instanceChanged = !string.Equals(instanceValue, currentInstanceId, StringComparison.Ordinal);
                            if (instanceChanged)
                            {
                                currentInstanceId = instanceValue;
                                currentInstanceEnteredAt = now;
                            }
                        }

                        if (instanceChanged)
                        {
                            UpdateInstanceTimerOverlay();
                        }

                        _ = Task.Run(() => ConnectToInstance(instanceValue));
                        isNotifyActivated = false;
                    }
                    else
                    {
                        bool hadInstance;
                        DateTimeOffset now = DateTimeOffset.Now;
                        lock (instanceTimerSync)
                        {
                            hadInstance = !string.IsNullOrEmpty(currentInstanceId);
                            currentInstanceId = string.Empty;
                            currentInstanceEnteredAt = now;
                        }

                        if (hadInstance)
                        {
                            UpdateInstanceTimerOverlay();
                        }
                    }
                    followAutoSelfKill = false;
                    _dispatcher.Invoke(async () =>
                    {
                        await disableAutoFollofSelfKillOscMessagesAsync();
                    });
                }
                else if (eventType == "MASTER_CHANGE")
                {
                    // インスタンスオーナー交代直後は特殊ラウンドが確定
                    stateService.SetRoundCycle(2);
                    _dispatcher.Invoke(() =>
                    {
                        UpdateNextRoundPrediction();
                    });

                }
                else if (eventType == "IS_SABOTEUR")
                {
                    bool isSaboteur = json.Value<bool?>("Value") ?? false;
                    //もしtesterNamesに含まれている場合、サボタージュの音を鳴らす
                    if (stateService.CurrentRound != null && isSaboteur)
                    {
                        if (testerNames.Contains(stateService.PlayerDisplayName) && !punishSoundPlayed)
                        {
                            PlayFromStart(tester_IDICIDEDKILLALLPlayer);
                            punishSoundPlayed = true;
                        }
                    }
                }
                else if (eventType == "SAVED")
                {
                    string savecode = json.Value<string>("Value") ?? String.Empty;
                    if (!string.IsNullOrEmpty(savecode))
                    {
                        await PersistLastSaveCodeAsync(savecode).ConfigureAwait(false);
                    }
                    if (savecode != String.Empty && _settings.apikey != String.Empty)
                    {
                        // https://toncloud.sprink.cloud/api/savecode/create/{apikey} にPOSTリクエストを送信(savecodeを送信)
                        using (var client = new HttpClient())
                        {
                            client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/savecode/create/" + _settings.apikey);
                            var content = new StringContent("{\"savecode\":\"" + savecode + "\"}", Encoding.UTF8, "application/json");
                            try
                            {
                                var response = await client.PostAsync("", content);
                                if (response.IsSuccessStatusCode)
                                {
                                    _logger.LogEvent("SaveCode", "Save code sent successfully.");
                                }
                                else
                                {
                                    _logger.LogEvent("SaveCodeError", $"Failed to send save code: {response.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogEvent("SaveCodeError", $"Exception occurred: {ex.Message}");
                            }
                        }
                    }
                }
                else if (eventType == "CUSTOM")
                {
                    string customEvent = json.Value<string>("Name") ?? "";
                    switch (customEvent)
                    {
                        case "InstancePlayersCount":
                            int playerCount = json.Value<int>("Value");
                            _dispatcher.Invoke(() =>
                            {
                                if (stateService.CurrentRound != null)
                                {
                                    stateService.CurrentRound.InstancePlayersCount = playerCount;

                                    _dispatcher.Invoke(() =>
                                    {
                                        //await SendPieSizeOscMessagesAsync(playerCount);
                                    });
                                }

                            });
                            break;
                        default:
                            _logger.LogEvent("CustomEvent", $"Unknown custom event: {customEvent}");
                            break;
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent(LanguageManager.Translate("ParseError"), message);
                LogUi($"Failed to process WebSocket payload: {ex.Message}", LogEventLevel.Error);
            }
        }

        /// <summary>
        /// 共通のラウンド終了処理
        /// </summary>
        /// <param name="status">"☠" または "✅"</param>
        private void FinalizeCurrentRound(string status)
        {
            if (stateService.CurrentRound != null)
            {
                string roundType = stateService.CurrentRound.RoundType ?? string.Empty;
                stateService.SetRoundMapName(roundType, stateService.CurrentRound.MapName ?? "");
                if (!string.IsNullOrEmpty(stateService.CurrentRound.TerrorKey))
                {
                    string terrorKey = stateService.CurrentRound.TerrorKey!;
                    bool survived = lastOptedIn && !stateService.CurrentRound.IsDeath;
                    stateService.RecordRoundResult(roundType, terrorKey, survived);
                    stateService.SetTerrorMapName(roundType, terrorKey, stateService.CurrentRound.MapName ?? "");
                }
                else
                {
                    stateService.RecordRoundResult(roundType, null, !stateService.CurrentRound.IsDeath);
                }
                if (!string.IsNullOrEmpty(stateService.CurrentRound.MapName))
                    stateService.SetRoundMapName(stateService.CurrentRound.RoundType ?? string.Empty, stateService.CurrentRound.MapName);

                // 次ラウンド予測ロジック
                var normalTypes = new[] { "クラシック", "Classic", "RUN", "走れ！" };
                var overrideTypes = new HashSet<string> { "アンバウンド", "8ページ", "ゴースト", "オルタネイト" };
                string current = stateService.CurrentRound.RoundType ?? string.Empty;

                string? historyStatusOverride = null;
                bool isNormalRound = normalTypes.Any(type => current.Contains(type));
                bool isOverrideRound = overrideTypes.Contains(current);

                if (isNormalRound)
                {
                    // 通常ラウンド
                    if (stateService.RoundCycle == 0)
                        stateService.SetRoundCycle(1); // 次は通常 or 特殊
                    else if (stateService.RoundCycle == 1)
                        stateService.SetRoundCycle(2); // 次は特殊
                    else
                        stateService.SetRoundCycle(1); // 想定外: 状態を不確定へ
                }
                else if (isOverrideRound)
                {
                    // 8ページ・アンバウンド・ゴースト・オルタネイトによる上書き
                    if (stateService.RoundCycle >= 2)
                        stateService.SetRoundCycle(0); // 特殊として扱いリセット
                    else
                        stateService.SetRoundCycle(1); // 通常扱いだが次は不確定
                    historyStatusOverride = "置き換え";
                    hasObservedSpecialRound = true;
                }
                else
                {
                    // 確定特殊ラウンド
                    stateService.SetRoundCycle(0);
                    hasObservedSpecialRound = true;
                }

                var round = stateService.CurrentRound;
                _dispatcher.Invoke(() =>
                {
                    UpdateNextRoundPrediction(historyStatusOverride);
                    UpdateAggregateStatsDisplay();
                    _presenter.AppendRoundLog(round, status);
                    ClearEventDisplays();
                    ClearItemDisplay();
                    lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}";
                });
                _ = _presenter.UploadRoundLogAsync(round, status);
                stateService.UpdateCurrentRound(null);
                ResetRoundScopedShortcutButtons();
            }
            SetOverlayTemporarilyHidden(false);
        }


        private async Task<bool> InvokeAfkWarningHandlersAsync(double idleSeconds)
        {
            if (_afkWarningHandlers.Count == 0)
            {
                return false;
            }

            var context = new AfkWarningContext(idleSeconds);
            foreach (var handler in _afkWarningHandlers)
            {
                try
                {
                    if (await handler.HandleAsync(context, _cancellation.Token))
                    {
                        _logger.LogEvent("AfkWarningHandled", $"Handled by {handler.GetType().FullName}");
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogEvent("AfkWarningCancelled", "AFK warning handling cancelled.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("AfkWarningHandlerError", ex.ToString(), LogEventLevel.Error);
                }
            }

            return false;
        }

        private async void VelocityTimer_Tick(object sender, EventArgs e)
        {
            // 無操作判定：VelocityMagnitudeの絶対値が1未満の場合、最低1秒連続してidleと判定する
            double idleSecondsForDisplay = 0d;
            if (stateService.CurrentRound != null && currentVelocity < 1)
            {
                if (idleStartTime == DateTime.MinValue)
                {
                    idleStartTime = DateTime.Now;
                }
                else
                {
                    double idleSeconds = (DateTime.Now - idleStartTime).TotalSeconds;
                    idleSecondsForDisplay = idleSeconds;
                    // 70秒以上無操作の場合、70秒時点で音声再生（まだ再生していなければ）
                    if (idleSeconds >= 70 && !afkSoundPlayed)
                    {
                        bool handled = await InvokeAfkWarningHandlersAsync(idleSeconds);
                        if (!handled)
                        {
                            PlayFromStart(afkPlayer);
                            _ = Task.Run(() => SendAlertOscMessagesAsync(0.1f));
                        }
                        afkSoundPlayed = true;
                    }
                    // 無操作時間に応じた文字色と点滅の制御
                    if (idleSeconds > 75)
                    {
                        bool blink = ((int)((DateTime.Now - idleStartTime).TotalMilliseconds / 250)) % 2 == 0;
                        InfoPanel.IdleTimeLabel.ForeColor = blink ? Color.Red : Color.Black;
                        InfoPanel.IdleTimeLabel.Text = $"無操作時間: {idleSeconds:F1}秒";
                    }
                    else if (idleSeconds > 70)
                    {
                        InfoPanel.IdleTimeLabel.ForeColor = Color.Red;
                        InfoPanel.IdleTimeLabel.Text = $"無操作時間: {idleSeconds:F1}秒";
                    }
                    else if (idleSeconds > 60)
                    {
                        InfoPanel.IdleTimeLabel.ForeColor = Color.Orange;
                        InfoPanel.IdleTimeLabel.Text = $"無操作時間: {idleSeconds:F1}秒";
                    }
                    else
                    {
                        InfoPanel.IdleTimeLabel.ForeColor = Color.White;
                        InfoPanel.IdleTimeLabel.Text = $"無操作時間: {idleSeconds:F1}秒";
                    }
                }
            }
            else
            {
                idleStartTime = DateTime.MinValue;
                InfoPanel.IdleTimeLabel.Text = string.Empty;
                InfoPanel.IdleTimeLabel.ForeColor = Color.White;
                afkSoundPlayed = false;
            }

            lastIdleSeconds = idleSecondsForDisplay;
            UpdateVelocityOverlay();

            string currentItemText = InfoPanel.ItemValue.Text ?? string.Empty;

            if (_settings.ItemMusicEnabled)
            {
                var matchingEntry = FindMatchingItemMusicEntry(currentItemText, currentVelocity);
                if (matchingEntry != null)
                {
                    if (!ReferenceEquals(activeItemMusicEntry, matchingEntry))
                    {
                        itemMusicMatchStart = DateTime.Now;
                        UpdateItemMusicPlayer(matchingEntry);
                    }
                    else
                    {
                        EnsureItemMusicPlayer(matchingEntry);
                    }

                    if (itemMusicMatchStart == DateTime.MinValue)
                    {
                        itemMusicMatchStart = DateTime.Now;
                    }
                    else if ((DateTime.Now - itemMusicMatchStart).TotalSeconds >= 0.5)
                    {
                        if (!itemMusicActive)
                        {
                            StartItemMusic(matchingEntry);
                        }
                    }
                }
                else
                {
                    ResetItemMusicTracking();
                }
            }
            else
            {
                ResetItemMusicTracking();
            }

            // パニッシュ・8ページ検出条件の更新
            if (stateService.CurrentRound == null)
            {
                string itemText = currentItemText;
                bool hasCoil = itemText.IndexOf("Coil", StringComparison.OrdinalIgnoreCase) >= 0;
                // 既存条件：6～7の範囲
                bool condition1 = hasCoil && (currentVelocity > 6.4 && currentVelocity < 6.6);
                // 追加条件：アイテム未所持 または "Emerald Coil" 所持時、6.4～6.6の範囲
                bool condition2 = ((string.IsNullOrEmpty(itemText)) ||
                                   (itemText.IndexOf("Emerald Coil", StringComparison.OrdinalIgnoreCase) >= 0))
                                  && (currentVelocity == 6.5);
                if (condition1 || condition2)
                {
                    if (velocityInRangeStart == DateTime.MinValue)
                    {
                        velocityInRangeStart = DateTime.Now;
                    }
                    else if ((DateTime.Now - velocityInRangeStart).TotalSeconds >= 0.5)
                    {
                        InfoPanel.RoundTypeValue.Text = "パニッシュ or 8ページの可能性あり";
                        InfoPanel.RoundTypeValue.ForeColor = Color.Red;
                        UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(InfoPanel.RoundTypeValue.Text ?? string.Empty));
                        if (!punishSoundPlayed)
                        {
                            // パニッシュ・8ページ検出時の音声再生
                            PlayFromStart(punishPlayer);
                            punishSoundPlayed = true;
                        }
                    }
                }
                else
                {
                    velocityInRangeStart = DateTime.MinValue;
                    punishSoundPlayed = false;
                    UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(InfoPanel.RoundTypeValue.Text ?? string.Empty));
                }
            }

            if (terrorCountdownActive)
            {
                UpdateTerrorCountdownDisplay();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        private void UpdateNextRoundPrediction(string? historyStatusOverride = null)
        {
            if (!hasObservedSpecialRound)
            {
                InfoPanel.NextRoundType.Text = NextRoundPredictionUnavailableMessage;
                InfoPanel.NextRoundType.ForeColor = Color.White;
                UpdateOverlay(OverlaySection.NextRound, form => form.SetValue(GetNextRoundOverlayValue()));
                if (overlayRoundHistory.Count > 0)
                {
                    overlayRoundHistory.Clear();
                }
                RefreshRoundHistoryOverlay();
                return;
            }

            // stateService.RoundCycle == 0: 次は通常ラウンド
            // stateService.RoundCycle == 1: 「通常ラウンド or 特殊ラウンド」と表示（50/50の抽選結果によるため不明）
            // stateService.RoundCycle >= 2: 次は特殊ラウンド
            if (stateService.RoundCycle == 0)
            {
                InfoPanel.NextRoundType.Text = "通常ラウンド";
                InfoPanel.NextRoundType.ForeColor = Color.White;
            }
            else if (stateService.RoundCycle == 1)
            {
                InfoPanel.NextRoundType.Text = "通常ラウンド or 特殊ラウンド";
                InfoPanel.NextRoundType.ForeColor = Color.Orange;
            }
            else
            {
                InfoPanel.NextRoundType.Text = "特殊ラウンド";
                InfoPanel.NextRoundType.ForeColor = Color.Red;
            }

            UpdateOverlay(OverlaySection.NextRound, form => form.SetValue(GetNextRoundOverlayValue()));
            RecordRoundHistory(historyStatusOverride);
        }

        private void RecordRoundHistory(string? statusOverride)
        {
            string label = InfoPanel?.NextRoundType?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(statusOverride))
            {
                return;
            }

            string status = statusOverride ?? GetDefaultRoundHistoryStatus();

            if (overlayRoundHistory.Count > 0)
            {
                var last = overlayRoundHistory[overlayRoundHistory.Count - 1];
                if (last.Label == label && last.Status == status)
                {
                    return;
                }
            }

            int maxEntries = _settings.OverlayRoundHistoryLength;
            if (maxEntries <= 0)
            {
                maxEntries = 3;
            }
            maxEntries = Math.Max(1, maxEntries);

            overlayRoundHistory.Add((label, status));
            while (overlayRoundHistory.Count > maxEntries)
            {
                overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        private string GetDefaultRoundHistoryStatus()
        {
            if (stateService.RoundCycle <= 0)
            {
                return "クラシック確定";
            }

            if (stateService.RoundCycle == 1)
            {
                return "50/50";
            }

            return "特殊確定";
        }

        private void ApplyOverlayRoundHistorySettings()
        {
            int maxEntries = _settings.OverlayRoundHistoryLength;
            if (maxEntries <= 0)
            {
                maxEntries = 3;
            }
            maxEntries = Math.Max(1, maxEntries);

            while (overlayRoundHistory.Count > maxEntries)
            {
                overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        private void RefreshRoundHistoryOverlay()
        {
            UpdateOverlay(OverlaySection.RoundHistory, form =>
            {
                if (form is OverlayRoundHistoryForm historyForm)
                {
                    historyForm.SetHistory(overlayRoundHistory);
                }
                else
                {
                    form.SetValue(string.Join(" > ", overlayRoundHistory.Select(h => h.Label)));
                }
            });
        }

        private (List<OverlayRoundStatsForm.RoundStatEntry> Entries, int TotalRounds) BuildRoundStatsEntries()
        {
            var aggregates = stateService.GetRoundAggregates();
            int totalRounds = aggregates.Values.Sum(r => r.Total);

            bool hasRoundFilter = _settings.RoundTypeStats != null && _settings.RoundTypeStats.Count > 0;
            HashSet<string>? filterSet = null;
            if (hasRoundFilter)
            {
                filterSet = new HashSet<string>(_settings.RoundTypeStats, StringComparer.OrdinalIgnoreCase);
            }
            var entries = aggregates
                .Where(kvp => !hasRoundFilter || (filterSet != null && filterSet.Contains(kvp.Key)))
                .Select(kvp => new OverlayRoundStatsForm.RoundStatEntry(kvp.Key, kvp.Value.Total, kvp.Value.Survival, kvp.Value.Death))
                .OrderByDescending(entry => entry.Total)
                .ToList();

            return (entries, totalRounds);
        }

        private void RefreshRoundStatsOverlay()
        {
            var (entries, totalRounds) = BuildRoundStatsEntries();

            UpdateOverlay(OverlaySection.RoundStats, form =>
            {
                if (form is OverlayRoundStatsForm statsForm)
                {
                    statsForm.SetStats(entries, totalRounds);
                }
                else
                {
                    var builder = new StringBuilder();
                    foreach (var entry in entries)
                    {
                        builder.AppendLine($"{entry.RoundName}: {entry.Total} (生存 {entry.Survival}, 死亡 {entry.Death}, {entry.SurvivalRate:F1}% )");
                    }

                    form.SetValue(builder.ToString().Trim());
                }
            });
        }

        private void UpdateAggregateStatsDisplay()
        {
            static string TranslateSafe(string key)
            {
                return LanguageManager.Translate(key) ?? key;
            }

            rtbStatsDisplay.Clear();
            var roundAggregates = stateService.GetRoundAggregates();
            int overallTotal = roundAggregates.Values.Sum(r => r.Total);
            foreach (var kvp in roundAggregates)
            {
                string roundType = kvp.Key;
                // ラウンドタイプごとのフィルターが有効なら対象のラウンドタイプのみ表示
                if (_settings.RoundTypeStats != null && _settings.RoundTypeStats.Count > 0 && !_settings.RoundTypeStats.Contains(roundType))
                    continue;

                RoundAggregate agg = kvp.Value;
                var parts = new List<string>();
                parts.Add(roundType);
                if (_settings.Filter_Appearance)
                    parts.Add(TranslateSafe("出現回数") + "=" + agg.Total);
                if (_settings.Filter_Survival)
                    parts.Add(TranslateSafe("生存回数") + "=" + agg.Survival);
                if (_settings.Filter_Death)
                    parts.Add(TranslateSafe("死亡回数") + "=" + agg.Death);
                if (_settings.Filter_SurvivalRate)
                    parts.Add(string.Format(TranslateSafe("生存率") + "={0:F1}%", agg.SurvivalRate));
                if (overallTotal > 0 && _settings.Filter_Appearance)
                {
                    double occurrenceRate = agg.Total * 100.0 / overallTotal;
                    parts.Add(string.Format(TranslateSafe("出現率") + "={0:F1}%", occurrenceRate));
                }
                string roundLine = string.Join(" ", parts);
                AppendLine(rtbStatsDisplay, roundLine, Theme.Current.Foreground);

                // テラーのフィルター
                if (_settings.Filter_Terror && stateService.TryGetTerrorAggregates(roundType, out var terrorDict) && terrorDict != null)
                {
                    foreach (var terrorKvp in terrorDict)
                    {
                        string terrorKey = terrorKvp.Key;
                        TerrorAggregate tAgg = terrorKvp.Value;
                        var terrorParts = new List<string>();
                        terrorParts.Add(terrorKey);
                        if (_settings.Filter_Appearance)
                            terrorParts.Add(TranslateSafe("出現回数") + "=" + tAgg.Total);
                        if (_settings.Filter_Survival)
                            terrorParts.Add(TranslateSafe("生存回数") + "=" + tAgg.Survival);
                        if (_settings.Filter_Death)
                            terrorParts.Add(TranslateSafe("死亡回数") + "=" + tAgg.Death);
                        if (_settings.Filter_SurvivalRate)
                            terrorParts.Add(string.Format(TranslateSafe("生存率") + "={0:F1}%", tAgg.SurvivalRate));
                        string terrorLine = string.Join(" ", terrorParts);
                        Color rawColor = terrorColors.ContainsKey(terrorKey) ? terrorColors[terrorKey] : Color.Black;
                        Color terrorColor = (_settings.FixedTerrorColor != Color.Empty && _settings.FixedTerrorColor != Color.White)
                            ? _settings.FixedTerrorColor
                            : AdjustColorForVisibility(rawColor);
                        AppendIndentedLine(rtbStatsDisplay, terrorLine, terrorColor);
                    }
                }
                AppendLine(rtbStatsDisplay, "", Theme.Current.Foreground);
            }
            if (_settings.ShowDebug)
            {
                AppendLine(rtbStatsDisplay, "VelocityMagnitude: " + currentVelocity.ToString("F2"), Color.Blue);
                if (idleStartTime != DateTime.MinValue)
                {
                    double idleSeconds = (DateTime.Now - idleStartTime).TotalSeconds;
                    AppendLine(rtbStatsDisplay, "Idle Time: " + idleSeconds.ToString("F2") + " sec", Color.Blue);
                }
            }

            RefreshRoundStatsOverlay();
        }

        private void AppendLine(RichTextBox rtb, string text, Color color)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.AppendText(text + Environment.NewLine);
            rtb.SelectionColor = rtb.ForeColor;
        }

        private void AppendIndentedLine(RichTextBox rtb, string text, Color color)
        {
            string prefix = "    ";
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionColor = Color.Black;
            rtb.AppendText(prefix);
            rtb.SelectionColor = color;
            rtb.AppendText(text);
            rtb.SelectionColor = Color.Black;
            rtb.AppendText(Environment.NewLine);
        }

        private void UpdateDisplayVisibility()
        {
            LogUi($"Updating display visibility. Stats: {_settings.ShowStats}, RoundLog: {_settings.ShowRoundLog}.", LogEventLevel.Debug);
            lblStatsTitle.Visible = _settings.ShowStats;
            rtbStatsDisplay.Visible = _settings.ShowStats;
            lblRoundLogTitle.Visible = _settings.ShowRoundLog;
            logPanel.RoundLogTextBox.Visible = _settings.ShowRoundLog;
        }

        public void UpdateRoundLog(IEnumerable<string> logEntries)
        {
            _dispatcher.Invoke(() =>
            {
                logPanel.RoundLogTextBox.Clear();
                foreach (var entry in logEntries)
                {
                    logPanel.RoundLogTextBox.AppendText(entry + Environment.NewLine);
                }
            });
        }

        private void ClearEventDisplays()
        {
            InfoPanel.RoundTypeValue.Text = "";
            InfoPanel.MapValue.Text = "";
            currentUnboundDisplayName = null;
            currentUnboundTerrorDetails = null;
            SetTerrorBaseText(string.Empty);
            UpdateTerrorInfoPanel(null);
            ResetTerrorCountdown();
            InfoPanel.DamageValue.Text = "";
            InfoPanel.ItemValue.Text = "";
            UpdateOverlay(OverlaySection.Damage, form => form.SetValue(GetDamageOverlayText()));
            UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(InfoPanel.RoundTypeValue.Text ?? string.Empty));
        }

        private void UpdateItemDisplay(JObject json)
        {
            string itemName = json.Value<string>("Name") ?? "";
            InfoPanel.ItemValue.Text = itemName;
            if (stateService.CurrentRound != null)
            {
                if (!stateService.CurrentRound.ItemNames.Contains(itemName))
                    stateService.CurrentRound.ItemNames.Add(itemName);
            }
        }

        private void ClearItemDisplay()
        {
            InfoPanel.ItemValue.Text = "";
        }

        private void LoadTerrorInfo()
        {
            string path = "./terrorsInfo.json";



            if (File.Exists(path))
            {
                try
                {
                    LogUi($"Loading terror info from '{path}'.", LogEventLevel.Debug);
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    terrorInfoData = JObject.Parse(json);
                    LogUi("Terror info loaded successfully.", LogEventLevel.Debug);
                }
                catch
                {
                    LogUi("Failed to parse terror info file. Terror info disabled.", LogEventLevel.Warning);
                    terrorInfoData = null;
                }
            }
            else
            {
                LogUi($"Terror info file '{path}' not found.", LogEventLevel.Debug);
            }

            RefreshTerrorInfoOverlay();
        }

        private void UpdateTerrorInfoPanel(List<string>? names)
        {
            currentTerrorInfoNames.Clear();
            if (names != null)
            {
                currentTerrorInfoNames.AddRange(names);
            }
            RefreshTerrorInfoOverlay();

            if (terrorInfoPanel == null)
                return;

            int margin = 10;
            int width = this.ClientSize.Width - 2 * margin;
            var nameList = names ?? new List<string>();
            terrorInfoPanel.UpdateInfo(nameList, terrorInfoData, width);

            // Re-layout controls when height changes
            MainForm_Resize(this, EventArgs.Empty);
        }

        private Color AdjustColorForVisibility(Color color)
        {
            if (color.GetBrightness() > 0.8f)
                return Color.Black;
            return color;
        }
        private void ProcessRoundActive(JObject json)
        {
            bool? active = json["Value"]?.ToObject<bool?>();
            if (active == null)
                return;

            if (active == true)
            {
                if (stateService.CurrentRound == null)
                {
                    string mapName = string.Empty;
                    string itemName = string.Empty;
                    _dispatcher.Invoke(() =>
                    {
                        mapName = InfoPanel.MapValue.Text;
                        itemName = InfoPanel.ItemValue.Text;
                    });
                    var activeRound = new Round
                    {
                        RoundType = "Active Round",
                        IsDeath = false,
                        TerrorKey = string.Empty,
                        MapName = mapName,
                        Damage = 0,
                        PageCount = 0,
                        RoundColor = 0xFFFFFF
                    };
                    stateService.UpdateCurrentRound(activeRound);
                    if (!string.IsNullOrEmpty(itemName))
                        activeRound.ItemNames.Add(itemName);
                    _dispatcher.Invoke(() =>
                    {
                        UpdateRoundTypeLabel();
                        InfoPanel.RoundTypeValue.ForeColor = Color.White;
                        InfoPanel.DamageValue.Text = "0";
                        UpdateOverlay(OverlaySection.Damage, form => form.SetValue(GetDamageOverlayText()));
                    });
                }

                var roundForAutoCheck = stateService.CurrentRound;
                if (roundForAutoCheck != null)
                {
                    string checkType = roundForAutoCheck.RoundType ?? string.Empty;
                    string? terror = roundForAutoCheck.TerrorKey;
                    if (checkType == "ブラッドバス" && !string.IsNullOrEmpty(terror) && terror!.Contains("LVL 3"))
                    {
                        checkType = "EX";
                    }
                    if (!autoSuicideService.HasScheduled)
                    {
                        int action = ShouldAutoSuicide(checkType, terror, out var hasPendingDelayed);
                        if (issetAllSelfKillMode)
                        {
                            ScheduleAutoSuicide(TimeSpan.FromSeconds(13), true, true);
                        }
                        else if (action == 1)
                        {
                            var delay = hasPendingDelayed ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(13);
                            ScheduleAutoSuicide(delay, true);
                        }
                        else if (action == 2)
                        {
                            ScheduleAutoSuicide(TimeSpan.FromSeconds(40), true);
                        }
                    }
                }
            }
            else
            {
                if (stateService.CurrentRound != null)
                {
                    FinalizeCurrentRound(stateService.CurrentRound.IsDeath ? "☠" : "✅");
                }
                CancelAutoSuicide();
            }
        }

        private void PerformAutoSuicide()
        {
            _logger.LogEvent("Suicide", "Performing Suside");
            LaunchSuicideInputIfExists();
            _logger.LogEvent("Suicide", "finish");
            allRoundsForcedSchedule = false;
            UpdateShortcutOverlayState();
        }

        private void ScheduleAutoSuicide(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false)
        {
            autoSuicideService.Schedule(delay, resetStartTime, PerformAutoSuicide);
            allRoundsForcedSchedule = fromAllRoundsMode;
            UpdateShortcutOverlayState();
        }

        private void CancelAutoSuicide()
        {
            if (autoSuicideService.HasScheduled)
            {
                autoSuicideService.Cancel();
            }
            allRoundsForcedSchedule = false;
            UpdateShortcutOverlayState();
        }

        private void DelayAutoSuicide()
        {
            if (!autoSuicideService.HasScheduled)
            {
                UpdateShortcutOverlayState();
                return;
            }

            TimeSpan elapsed = DateTime.UtcNow - autoSuicideService.RoundStartTime;
            TimeSpan remaining = TimeSpan.FromSeconds(40) - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.FromSeconds(40);
            }

            ScheduleAutoSuicide(remaining, false, allRoundsForcedSchedule);
        }

        private void LoadAutoSuicideRules()
        {
            autoSuicideRules = new List<AutoSuicideRule>();
            var lines = new List<string>();
            if (!_settings.AutoSuicideUseDetail)
            {
                foreach (var round in AllRoundTypes)
                {
                    bool enabled = _settings.AutoSuicideRoundTypes.Contains(round);
                    lines.Add($"{round}::{(enabled ? 1 : 0)}");
                }
            }
            if (_settings.AutoSuicideDetailCustom != null)
                lines.AddRange(_settings.AutoSuicideDetailCustom);
            var temp = new List<AutoSuicideRule>();
            foreach (var line in lines)
            {
                if (AutoSuicideRule.TryParse(line, out var r) && r != null)
                    temp.Add(r);
            }
            var cleaned = new List<AutoSuicideRule>();
            for (int i = temp.Count - 1; i >= 0; i--)
            {
                var r = temp[i];
                bool redundant = cleaned.Any(c => c.Covers(r));
                if (!redundant)
                    cleaned.Add(r);
            }
            cleaned.Reverse();
            autoSuicideRules = cleaned;
            if (_settings.CoordinatedAutoSuicideBrainEnabled)
            {
                _moduleHost.NotifyAutoSuicideRulesPrepared(new ModuleAutoSuicideRuleContext(autoSuicideRules, _settings, _moduleHost.CurrentServiceProvider));
            }
            UpdateShortcutOverlayState();
        }

        private int ShouldAutoSuicide(string roundType, string? terrorName, out bool hasPendingDelayed)
        {
            hasPendingDelayed = false;
            if (!_settings.AutoSuicideEnabled) return 0;
            Func<string, string, bool> comparer;
            if (_settings.AutoSuicideFuzzyMatch)
                comparer = (a, b) => MatchWithTypoTolerance(a, b).result;
            else
                comparer = (a, b) => a == b;

            if (string.IsNullOrWhiteSpace(terrorName))
            {
                terrorName = null;
                hasPendingDelayed = autoSuicideRules.Any(r =>
                    r.Value == 2 &&
                    r.MatchesRound(roundType, comparer) &&
                    !r.Matches(roundType, null, comparer));
            }

            int decision = 0;
            for (int i = autoSuicideRules.Count - 1; i >= 0; i--)
            {
                var r = autoSuicideRules[i];
                if (r.Matches(roundType, terrorName, comparer))
                {
                    decision = r.Value;
                    break;
                }
            }

            var decisionContext = new ModuleAutoSuicideDecisionContext(roundType, terrorName, decision, hasPendingDelayed, _moduleHost.CurrentServiceProvider);
            if (_settings.CoordinatedAutoSuicideBrainEnabled)
            {
                _moduleHost.NotifyAutoSuicideDecisionEvaluated(decisionContext);
            }
            hasPendingDelayed = decisionContext.HasPendingDelayed;
            return decisionContext.Decision;
        }

        private int ShouldAutoSuicide(string roundType, string? terrorName)
        {
            return ShouldAutoSuicide(roundType, terrorName, out _);
        }

        private void UpdateRoundTypeLabel()
        {
            if (InfoPanel == null)
            {
                _logger.LogEvent("Error regen Infopanel", "InfoPanel disposed");
                ReinitializeInfoPanel();
                if (InfoPanel == null)
                    return;
            }

            var round = stateService.CurrentRound;
            if (round == null)
            {
                InfoPanel.RoundTypeValue.Text = string.Empty;
                UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(InfoPanel.RoundTypeValue.Text ?? string.Empty));
                return;
            }

            if (round.RoundType == "8ページ")
            {
                InfoPanel.RoundTypeValue.Text = $"{round.RoundType} ({round.PageCount}/8)";
            }
            else
            {
                InfoPanel.RoundTypeValue.Text = round.RoundType;
            }

            UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(InfoPanel.RoundTypeValue.Text ?? string.Empty));
        }

        private Color ConvertColorFromInt(int colorInt)
        {
            int r = (colorInt >> 16) & 0xFF;
            int g = (colorInt >> 8) & 0xFF;
            int b = colorInt & 0xFF;
            return Color.FromArgb(r, g, b);
        }

        private void ResetTerrorCountdown()
        {
            terrorCountdownActive = false;
            terrorCountdownTargetName = string.Empty;
            terrorCountdownDurationSeconds = 0;
            terrorCountdownStart = DateTime.MinValue;
            terrorCountdownLastDisplayedSeconds = -1;
            currentTerrorCountdownSuffix = string.Empty;
            RefreshTerrorDisplays();
        }

        private void UpdateTerrorCountdownState(string primaryDisplayName)
        {
            bool isSpaceName = primaryDisplayName == " ";
            bool isSm64Name = string.Equals(primaryDisplayName, "sm64.z64", StringComparison.OrdinalIgnoreCase);

            if (!isSpaceName && !isSm64Name)
            {
                if (terrorCountdownActive)
                {
                    ResetTerrorCountdown();
                }
                return;
            }

            int baseSeconds = isSpaceName ? 20 : 33;
            if (string.Equals(stateService.CurrentRound?.RoundType, "ミッドナイト", StringComparison.Ordinal))
            {
                baseSeconds += 10;
            }

            bool restartCountdown = !terrorCountdownActive
                                     || !string.Equals(terrorCountdownTargetName, primaryDisplayName, StringComparison.Ordinal)
                                     || terrorCountdownDurationSeconds != baseSeconds;

            terrorCountdownTargetName = primaryDisplayName;
            terrorCountdownDurationSeconds = baseSeconds;

            if (restartCountdown)
            {
                terrorCountdownStart = DateTime.Now;
                terrorCountdownLastDisplayedSeconds = -1;
            }

            terrorCountdownActive = true;
            UpdateTerrorCountdownDisplay();
        }

        private void UpdateTerrorCountdownDisplay()
        {
            if (!terrorCountdownActive)
            {
                return;
            }

            double remaining = terrorCountdownDurationSeconds - (DateTime.Now - terrorCountdownStart).TotalSeconds;
            if (remaining < 0)
            {
                remaining = 0;
            }

            int seconds = (int)Math.Ceiling(remaining);
            if (seconds < 0)
            {
                seconds = 0;
            }

            string suffix = $"(出現まで {seconds} 秒)";

            if (seconds != terrorCountdownLastDisplayedSeconds || !string.Equals(currentTerrorCountdownSuffix, suffix, StringComparison.Ordinal))
            {
                currentTerrorCountdownSuffix = suffix;
                terrorCountdownLastDisplayedSeconds = seconds;
                RefreshTerrorDisplays();
            }
        }

        private void UpdateTerrorDisplay(string displayName, Color color, List<(string name, int count)>? terrors)
        {
            var currentRound = stateService.CurrentRound;
            string? roundType = currentRound?.RoundType;

            if (roundType == "アンバウンド")
            {
                // Unbound rounds sometimes lack explicit terror names in the event data.
                // If they are missing, resolve them via the predefined lookup so the
                // information panel can display the appropriate terror details.
                if (terrors == null || terrors.Count == 0)
                {
                    var lookup = UnboundRoundDefinitions.GetTerrors(displayName);
                    if (lookup != null)
                    {
                        terrors = lookup.ToList();
                    }
                }
            }

            List<string>? expanded = terrors?.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
            if (expanded != null && expanded.Count == 0)
            {
                expanded = null;
            }

            if (roundType == "アンバウンド")
            {
                currentUnboundDisplayName = displayName;
                if (terrors != null && terrors.Count > 0)
                {
                    string terrorText = string.Join(", ", terrors.Select(t => $"{t.name} x{t.count}"));
                    currentUnboundTerrorDetails = terrorText;
                    SetTerrorBaseText($"{displayName} ({terrorText})");

                    var terrorKeyValue = currentRound?.TerrorKey;
                    if (!string.IsNullOrEmpty(terrorKeyValue))
                    {
                        terrorColors[terrorKeyValue!] = color;
                    }
                }
                else
                {
                    currentUnboundTerrorDetails = null;
                    SetTerrorBaseText(displayName);
                }
            }
            else
            {
                currentUnboundDisplayName = null;
                currentUnboundTerrorDetails = null;

                if (expanded != null && expanded.Count > 0)
                {
                    string joinedNames = string.Join(" & ", expanded);
                    string infoText = joinedNames != displayName
                        ? displayName + Environment.NewLine + string.Join(Environment.NewLine, expanded)
                        : displayName;

                    SetTerrorBaseText(infoText);

                    if (!string.IsNullOrEmpty(joinedNames))
                    {
                        terrorColors[joinedNames] = color;
                    }
                }
                else
                {
                    SetTerrorBaseText(displayName);
                }
            }

            InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;

            UpdateTerrorInfoPanel(expanded);
            UpdateTerrorCountdownState(displayName);
        }

        private void SetTerrorBaseText(string text)
        {
            currentTerrorBaseText = text ?? string.Empty;
            RecomputeOverlayTerrorBase();
        }

        private void RecomputeOverlayTerrorBase()
        {
            RecomputeOverlayTerrorBase(true);
        }

        private void RecomputeOverlayTerrorBase(bool refreshDisplay)
        {
            bool isUnbound = string.Equals(stateService.CurrentRound?.RoundType, "アンバウンド", StringComparison.Ordinal);
            if (isUnbound && !string.IsNullOrEmpty(currentUnboundDisplayName))
            {
                if (_settings.OverlayShowUnboundTerrorDetails && !string.IsNullOrEmpty(currentUnboundTerrorDetails))
                {
                    currentOverlayTerrorBaseText = $"{currentUnboundDisplayName} ({currentUnboundTerrorDetails})";
                }
                else
                {
                    currentOverlayTerrorBaseText = currentUnboundDisplayName ?? string.Empty;
                }
            }
            else
            {
                currentOverlayTerrorBaseText = currentTerrorBaseText;
            }

            if (refreshDisplay)
            {
                RefreshTerrorDisplays();
            }
        }

        private void RefreshTerrorDisplays()
        {
            string overlayBase = string.IsNullOrEmpty(currentOverlayTerrorBaseText)
                ? currentTerrorBaseText
                : currentOverlayTerrorBaseText;
            string overlayText = CombineBaseAndSuffix(overlayBase, currentTerrorCountdownSuffix);

            if (InfoPanel?.TerrorValue != null)
            {
                string infoText = CombineBaseAndSuffix(currentTerrorBaseText, currentTerrorCountdownSuffix);
                InfoPanel.TerrorValue.Text = infoText;
            }

            UpdateOverlay(OverlaySection.Terror, form => form.SetValue(overlayText));
        }

        private static string CombineBaseAndSuffix(string? baseText, string? suffix)
        {
            string baseValue = baseText ?? string.Empty;
            string suffixValue = suffix ?? string.Empty;
            if (string.IsNullOrEmpty(suffixValue))
            {
                return baseValue;
            }

            if (string.IsNullOrEmpty(baseValue))
            {
                return suffixValue;
            }

            if (!char.IsWhiteSpace(baseValue[baseValue.Length - 1]))
            {
                return baseValue + " " + suffixValue;
            }

            return baseValue + suffixValue;
        }

        private string GetOverlayTerrorDisplayText()
        {
            if (string.IsNullOrEmpty(currentOverlayTerrorBaseText) && !string.IsNullOrEmpty(currentTerrorBaseText))
            {
                RecomputeOverlayTerrorBase(false);
            }

            string overlayBase = string.IsNullOrEmpty(currentOverlayTerrorBaseText)
                ? currentTerrorBaseText
                : currentOverlayTerrorBaseText;
            return CombineBaseAndSuffix(overlayBase, currentTerrorCountdownSuffix);
        }

        private string GetOverlayAngleDisplayText()
        {
            if (!hasFacingAngleMeasurement)
            {
                return "―";
            }

            return $"{lastKnownFacingAngle:0.#}°";
        }

        private void RefreshTerrorInfoOverlay()
        {
            UpdateOverlay(OverlaySection.TerrorInfo, form => form.SetValue(BuildTerrorInfoOverlayText()));
        }

        private string BuildTerrorInfoOverlayText()
        {
            if (currentTerrorInfoNames.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var groups = currentTerrorInfoNames
                .GroupBy(name => name)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                string header = group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key;
                sb.AppendLine(header);

                if (terrorInfoData != null && terrorInfoData[group.Key] is JArray infoArray)
                {
                    foreach (JObject obj in infoArray.OfType<JObject>())
                    {
                        var prop = obj.Properties().FirstOrDefault();
                        if (prop == null)
                        {
                            continue;
                        }

                        sb.Append("  • ");
                        sb.Append(prop.Name);
                        sb.Append(": ");
                        sb.AppendLine(prop.Value.ToString());
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        private void LaunchSuicideInputIfExists()
        {
            _inputSender.PressKeys();

        }

        public static void UpdateAndRestart()
        {
            try
            {
                // 実行中のToNSaveManagerプロセスを取得
                var process = Process.GetProcessesByName("ToNSaveManager").FirstOrDefault();
                if (process == null)
                {
                    throw new InvalidOperationException("ToNSaveManager.exe が実行されていません。");
                }

                // 実行ファイルのパスとディレクトリ取得
                string exePath;
                try
                {
                    exePath = process.MainModule.FileName;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("プロセスの実行ファイルパスを取得できませんでした。", ex);
                }

                string exeDirectory = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(exeDirectory))
                {
                    throw new InvalidOperationException("実行ファイルのディレクトリが不正です。");
                }

                // Scriptsフォルダのパス
                string scriptsDir = Path.Combine(exeDirectory, "scripts");
                // フォルダが存在しない場合は作成
                if (!Directory.Exists(scriptsDir))
                {
                    try
                    {
                        Directory.CreateDirectory(scriptsDir);
                    }
                    catch (Exception ex)
                    {
                        throw new IOException($"Scriptsフォルダの作成に失敗しました: {scriptsDir}", ex);
                    }
                }

                // コピー元のJSファイルパス
                string sourceJs = Path.Combine(Environment.CurrentDirectory, "ToNRoundCounter.js");
                if (!File.Exists(sourceJs))
                {
                    throw new FileNotFoundException("コピー元のToNRoundCounter.jsが見つかりません。", sourceJs);
                }

                // コピー先のJSファイルパス
                string destJs = Path.Combine(scriptsDir, "ToNRoundCounter.js");
                if (!File.Exists(destJs))
                {
                    try
                    {
                        File.Copy(sourceJs, destJs);
                    }
                    catch (Exception ex)
                    {
                        throw new IOException($"JSファイルのコピーに失敗しました: {sourceJs} -> {destJs}", ex);
                    }
                }
                else
                {
                    return;
                }

                // プロセスの再起動
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("ToNSaveManagerプロセスの停止に失敗しました。", ex);
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = exeDirectory,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("ToNSaveManagerプロセスの起動に失敗しました。", ex);
                }
            }
            catch (Exception ex)
            {
                // エラー内容を標準出力に表示
                Console.Error.WriteLine(ex.Message);
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(ex.InnerException.Message);
                }
                throw;
            }
        }

        private ItemMusicEntry? FindMatchingItemMusicEntry(string text, double velocity)
        {
            if (!_settings.ItemMusicEnabled || _settings.ItemMusicEntries == null)
            {
                return null;
            }

            foreach (var entry in _settings.ItemMusicEntries)
            {
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                string itemName = entry.ItemName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                if (text.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                double minSpeed = entry.MinSpeed;
                if (double.IsNaN(minSpeed) || double.IsInfinity(minSpeed) || minSpeed < 0)
                {
                    minSpeed = 0;
                }

                double maxSpeed = entry.MaxSpeed;
                if (double.IsNaN(maxSpeed) || double.IsInfinity(maxSpeed) || maxSpeed < minSpeed)
                {
                    maxSpeed = minSpeed;
                }

                if (velocity >= minSpeed && velocity <= maxSpeed)
                {
                    return entry;
                }
            }

            return null;
        }

        private class TypoMatchResult
        {
            public bool result;
            public bool typomatch;
        }

        private TypoMatchResult MatchWithTypoTolerance(string text, string target)
        {
            if (text == null || target == null)
            {
                return new TypoMatchResult { result = false, typomatch = false };
            }

            if (text.Equals(target))
            {
                return new TypoMatchResult { result = true, typomatch = false };
            }

            int allowedDistance = (int)Math.Floor(target.Length * 0.25);
            int distance = LevenshteinDistance(text, target);

            if (distance <= allowedDistance)
            {
                return new TypoMatchResult { result = true, typomatch = true };
            }

            return new TypoMatchResult { result = false, typomatch = false };
        }

        private int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return target?.Length ?? 0;
            }
            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            int[,] d = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
            {
                d[i, 0] = i;
            }
            for (int j = 0; j <= target.Length; j++)
            {
                d[0, j] = j;
            }

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[source.Length, target.Length];
        }
    }
}
