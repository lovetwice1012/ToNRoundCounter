using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Security.Principal;
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

#nullable enable

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
        private ToolStripMenuItem fileMenuItem = null!;
        private ToolStripMenuItem settingsMenuItem = null!;
        private ToolStripMenuItem exitMenuItem = null!;
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
        private readonly BlockingCollection<(string Message, LogEventLevel Level)> _uiLogQueue;
        private readonly Task _logWorker;
        private readonly MainPresenter _presenter;
        private readonly IEventBus _eventBus;
        private readonly IInputSender _inputSender;
        private readonly IUiDispatcher _dispatcher;
        private readonly IReadOnlyList<IAfkWarningHandler> _afkWarningHandlers;
        private readonly IReadOnlyList<IOscRepeaterPolicy> _oscRepeaterPolicies;
        private readonly ModuleHost _moduleHost;
        private readonly AutoRecordingService autoRecordingService;
        private readonly CloudWebSocketClient? _cloudClient;

        private Action<WebSocketConnected>? _wsConnectedHandler;
        private Action<WebSocketDisconnected>? _wsDisconnectedHandler;
        private Action<OscConnected>? _oscConnectedHandler;
        private Action<OscDisconnected>? _oscDisconnectedHandler;
        private Action<WebSocketMessageReceived>? _wsMessageHandler;
        private Action<OscMessageReceived>? _oscMessageHandler;
        private Action<SettingsValidationFailed>? _settingsValidationFailedHandler;

        private Dictionary<string, Color> terrorColors = new();
        private bool lastOptedIn = true;
        private bool _isWebSocketConnected;
        private bool _isOscConnected;

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
        private bool autoSuicideManualCancelRequested;
        private DateTime? autoSuicideManualDelayUntil;

        private string _lastSaveCode = string.Empty;

        private string version = "1.14.1";

        private readonly AutoSuicideService autoSuicideService;

        private MediaPlayer? itemMusicPlayer;
        private bool itemMusicLoopRequested;
        private bool itemMusicActive;
        private DateTime itemMusicMatchStart = DateTime.MinValue;
        private string lastLoadedItemMusicPath = string.Empty;
        private ItemMusicEntry? activeItemMusicEntry;
        private MediaPlayer? roundBgmPlayer;
        private bool roundBgmLoopRequested;
        private bool roundBgmActive;
        private DateTime roundBgmMatchStart = DateTime.MinValue;
        private string lastLoadedRoundBgmPath = string.Empty;
        private RoundBgmEntry? activeRoundBgmEntry;
        private string? roundBgmSelectionRoundType;
        private string? roundBgmSelectionTerrorType;
        private readonly Random roundBgmRandom = new Random(Guid.NewGuid().GetHashCode());
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
        private double terrorCountdownLastDisplayedValue = double.NaN;
        private readonly object instanceTimerSync = new();
        private string currentInstanceId = string.Empty;
        private DateTimeOffset currentInstanceEnteredAt = DateTimeOffset.Now;
        private List<InstanceMemberInfo> currentInstanceMembers = new List<InstanceMemberInfo>();
        private List<string> currentDesirePlayers = new List<string>();
        private System.Windows.Forms.Timer? instanceMemberUpdateTimer;
        
        // Cloud state update tracking
        private DateTime lastCloudStateUpdate = DateTime.MinValue;
        private const double CloudStateUpdateIntervalSeconds = 2.0;
        private List<string> currentPlayerItems = new List<string>();
        
        // Cloud monitoring status tracking
        private DateTime lastMonitoringStatusUpdate = DateTime.MinValue;
        private const double MonitoringStatusUpdateIntervalSeconds = 30.0; // Every 30 seconds
        
        private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");
        private const string NextRoundPredictionUnavailableMessage = "次のラウンドの予測は特殊ラウンドを一回発生させることで利用可能です";
        private bool hasObservedSpecialRound;

        private void LogUi(string message, LogEventLevel level = LogEventLevel.Information)
        {
            if (_logger == null)
            {
                return;
            }

            try
            {
                if (!_uiLogQueue.IsAddingCompleted)
                {
                    _uiLogQueue.Add((message, level));
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // Queue has been marked as complete; fall back to direct logging.
            }

            _logger.LogEvent("MainForm", message, level);
        }

        private void EvaluateAutoRecording(string reason)
        {
            try
            {
                autoRecordingService?.EvaluateRecordingState(reason);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("AutoRecordingError", () => $"Failed to evaluate auto recording after '{reason}': {ex}", LogEventLevel.Error);
            }
        }

        private void ProcessUiLogQueue()
        {
            try
            {
                foreach (var entry in _uiLogQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        _logger?.LogEvent("MainForm", entry.Message, entry.Level);
                    }
                    catch
                    {
                        // Ignore logging failures in the background pipeline.
                    }
                }
            }
            catch
            {
                // Swallow exceptions to keep background logging from crashing the app.
            }
        }


        public MainForm(IWebSocketClient webSocketClient, IOSCListener oscListener, AutoSuicideService autoSuicideService, StateService stateService, IAppSettings settings, IEventLogger logger, MainPresenter presenter, IEventBus eventBus, ICancellationProvider cancellation, IInputSender inputSender, IUiDispatcher dispatcher, IEnumerable<IAfkWarningHandler> afkWarningHandlers, IEnumerable<IOscRepeaterPolicy> oscRepeaterPolicies, AutoRecordingService autoRecordingService, ModuleHost moduleHost, CloudWebSocketClient cloudClient)
        {
            InitializeSoundPlayers();
            this.Name = "MainForm";
            this.webSocketClient = webSocketClient;
            this.autoSuicideService = autoSuicideService;
            this.oscListener = oscListener;
            this.stateService = stateService;
            _cloudClient = cloudClient;
            _settings = settings;
            _logger = logger;
            _uiLogQueue = new BlockingCollection<(string Message, LogEventLevel Level)>(
                new ConcurrentQueue<(string Message, LogEventLevel Level)>());
            _logWorker = Task.Factory.StartNew(
                () => ProcessUiLogQueue(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            this.autoRecordingService = autoRecordingService;
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
            EvaluateAutoRecording("InitialLoad");
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
                _isWebSocketConnected = true;
                UpdateWebSocketStatusLabel();
            });
            _eventBus.Subscribe(_wsConnectedHandler);

            _wsDisconnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                _isWebSocketConnected = false;
                UpdateWebSocketStatusLabel();
            });
            _eventBus.Subscribe(_wsDisconnectedHandler);

            _oscConnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                _isOscConnected = true;
                UpdateOscStatusLabel();
            });
            _eventBus.Subscribe(_oscConnectedHandler);

            _oscDisconnectedHandler = _ => _dispatcher.Invoke(() =>
            {
                _isOscConnected = false;
                UpdateOscStatusLabel();
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

            // Cloud WebSocketクライアントの起動
            if (_settings.CloudSyncEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cloudClient.StartAsync();
                        _logger?.LogEvent("CloudSync", "Cloud WebSocket client started successfully.");
                        
                        // Auto-login after connection
                        if (!string.IsNullOrWhiteSpace(_settings.CloudPlayerName))
                        {
                            try
                            {
                                var loginResult = await _cloudClient.LoginAsync(
                                    _settings.CloudPlayerName,
                                    "1.0.0",
                                    System.Threading.CancellationToken.None
                                );
                                _logger?.LogEvent("CloudSync", $"Logged in as: {_settings.CloudPlayerName}");
                            }
                            catch (Exception loginEx)
                            {
                                _logger?.LogEvent("CloudSync", $"Auto-login failed: {loginEx.Message}", LogEventLevel.Warning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogEvent("CloudSync", $"Failed to start Cloud WebSocket client: {ex.Message}", LogEventLevel.Warning);
                    }
                });

                // Subscribe to Cloud stream events
                if (_cloudClient != null)
                {
                    _cloudClient.MessageReceived += OnCloudMessageReceived;
                }
            }

            velocityTimer = new System.Windows.Forms.Timer();
            velocityTimer.Interval = 50;
            velocityTimer.Tick += VelocityTimer_Tick;
            velocityTimer.Start();

            // Initialize instance member update timer
            instanceMemberUpdateTimer = new System.Windows.Forms.Timer();
            instanceMemberUpdateTimer.Interval = 2000; // Update every 2 seconds
            instanceMemberUpdateTimer.Tick += InstanceMemberUpdateTimer_Tick;
            instanceMemberUpdateTimer.Start();

            LogUi("Main form construction complete. Background listeners and timers started.");
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

            fileMenuItem = new ToolStripMenuItem(LanguageManager.Translate("ファイル"));
            settingsMenuItem = new ToolStripMenuItem(LanguageManager.Translate("設定..."));
            settingsMenuItem.Click += BtnSettings_Click;
            exitMenuItem = new ToolStripMenuItem(LanguageManager.Translate("終了"));
            exitMenuItem.Click += (s, e) => Close();
            fileMenuItem.DropDownItems.Add(settingsMenuItem);
            fileMenuItem.DropDownItems.Add(new ToolStripSeparator());
            fileMenuItem.DropDownItems.Add(exitMenuItem);
            mainMenuStrip.Items.Add(fileMenuItem);

            windowsMenuItem = new ToolStripMenuItem(LanguageManager.Translate("ウィンドウ"));
            mainMenuStrip.Items.Add(windowsMenuItem);

            int margin = 10;
            int currentY = mainMenuStrip.Bottom + margin;
            int contentWidth = this.ClientSize.Width - 2 * margin;
            bool useCustomPanelColors = string.Equals(_settings.ThemeKey, Theme.DefaultThemeKey, StringComparison.OrdinalIgnoreCase);

            // WebSocket接続状況
            lblStatus = new Label();
            _isWebSocketConnected = false;
            UpdateWebSocketStatusLabel();
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(margin, currentY);
            lblStatus.Width = contentWidth / 2 - 5;
            this.Controls.Add(lblStatus);

            // OSC接続状況
            lblOSCStatus = new Label();
            _isOscConnected = false;
            UpdateOscStatusLabel();
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

        private void ApplyLanguage()
        {
            this.Text = LanguageManager.Translate("ToNRoundCouter");
            fileMenuItem.Text = LanguageManager.Translate("ファイル");
            settingsMenuItem.Text = LanguageManager.Translate("設定...");
            exitMenuItem.Text = LanguageManager.Translate("終了");
            windowsMenuItem.Text = LanguageManager.Translate("ウィンドウ");
            btnSettings.Text = LanguageManager.Translate("設定変更");
            btnToggleTopMost.Text = this.TopMost ? LanguageManager.Translate("固定解除") : LanguageManager.Translate("固定する");
            lblStatsTitle.Text = LanguageManager.Translate("統計情報表示欄");
            lblRoundLogTitle.Text = LanguageManager.Translate("ラウンドログ");
            UpdateWebSocketStatusLabel();
            UpdateOscStatusLabel();
            InfoPanel?.ApplyLanguage();
        }

        private void UpdateWebSocketStatusLabel()
        {
            if (lblStatus == null)
            {
                return;
            }

            var statusText = LanguageManager.Translate(_isWebSocketConnected ? "Connected" : "Disconnected");
            lblStatus.Text = $"WebSocket: {statusText}";
            lblStatus.ForeColor = _isWebSocketConnected ? Color.Green : Color.Red;
        }

        private void UpdateOscStatusLabel()
        {
            if (lblOSCStatus == null)
            {
                return;
            }

            var statusText = LanguageManager.Translate(_isOscConnected ? "Connected" : "Disconnected");
            lblOSCStatus.Text = $"OSC: {statusText}";
            lblOSCStatus.ForeColor = _isOscConnected ? Color.Green : Color.Red;
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
            try
            {
                await BtnSettings_ClickAsync();
            }
            catch (Exception ex)
            {
                LogUi($"Unhandled error in settings dialog: {ex.Message}", LogEventLevel.Error);
                MessageBox.Show($"設定ダイアログでエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task BtnSettings_ClickAsync()
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
                settingsForm.SettingsPanel.RoundBgmEnabledCheckBox.Checked = _settings.RoundBgmEnabled;
                settingsForm.SettingsPanel.LoadRoundBgmEntries(_settings.RoundBgmEntries);
                settingsForm.SettingsPanel.SetRoundBgmItemConflictBehavior(_settings.RoundBgmItemConflictBehavior);
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

                    var previousLanguage = LanguageManager.NormalizeCulture(_settings.Language);
                    var selectedLanguage = LanguageManager.NormalizeCulture(settingsForm.SettingsPanel.SelectedLanguage);
                    bool languageChanged = !string.Equals(previousLanguage, selectedLanguage, StringComparison.OrdinalIgnoreCase);
                    _settings.Language = selectedLanguage;
                    if (languageChanged)
                    {
                        LanguageManager.SetLanguage(_settings.Language);
                    }

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
                    bool autoRecordingPreviouslyEnabled = _settings.AutoRecordingEnabled;

                    _settings.AutoLaunchEnabled = settingsForm.SettingsPanel.AutoLaunchEnabledCheckBox.Checked;
                    _settings.AutoLaunchEntries = settingsForm.SettingsPanel.GetAutoLaunchEntries();
                    _settings.AutoRecordingEnabled = settingsForm.SettingsPanel.AutoRecordingEnabledCheckBox.Checked;
                    _settings.AutoRecordingWindowTitle = settingsForm.SettingsPanel.AutoRecordingWindowTitleTextBox.Text?.Trim() ?? string.Empty;
                    _settings.AutoRecordingFrameRate = (int)settingsForm.SettingsPanel.AutoRecordingFrameRateNumeric.Value;
                    _settings.AutoRecordingResolution = settingsForm.SettingsPanel.GetAutoRecordingResolution();
                    _settings.AutoRecordingIncludeOverlay = settingsForm.SettingsPanel.AutoRecordingIncludeOverlayCheckBox.Checked;
                    _settings.AutoRecordingOutputDirectory = settingsForm.SettingsPanel.AutoRecordingOutputDirectoryTextBox.Text?.Trim() ?? string.Empty;
                    _settings.AutoRecordingOutputExtension = settingsForm.SettingsPanel.GetAutoRecordingOutputExtension();
                    _settings.AutoRecordingVideoCodec = settingsForm.SettingsPanel.GetAutoRecordingVideoCodec();
                    _settings.AutoRecordingVideoBitrate = settingsForm.SettingsPanel.GetAutoRecordingVideoBitrate();
                    _settings.AutoRecordingAudioBitrate = settingsForm.SettingsPanel.GetAutoRecordingAudioBitrate();
                    _settings.AutoRecordingHardwareEncoder = settingsForm.SettingsPanel.GetAutoRecordingHardwareEncoder();
                    _settings.AutoRecordingRoundTypes = settingsForm.SettingsPanel.GetAutoRecordingRoundTypes();
                    _settings.AutoRecordingTerrors = settingsForm.SettingsPanel.GetAutoRecordingTerrors();

                    bool autoRecordingEnabledNow = _settings.AutoRecordingEnabled;
                    if (!autoRecordingPreviouslyEnabled && autoRecordingEnabledNow && !IsRunningAsAdministrator())
                    {
                        if (PromptForElevatedRestart())
                        {
                            await _settings.SaveAsync();
                            RestartAsAdministratorForRecording();
                            return;
                        }
                    }
                    _settings.ItemMusicEnabled = settingsForm.SettingsPanel.ItemMusicEnabledCheckBox.Checked;
                    _settings.ItemMusicEntries = settingsForm.SettingsPanel.GetItemMusicEntries();
                    _settings.RoundBgmEnabled = settingsForm.SettingsPanel.RoundBgmEnabledCheckBox.Checked;
                    _settings.RoundBgmEntries = settingsForm.SettingsPanel.GetRoundBgmEntries();
                    _settings.RoundBgmItemConflictBehavior = settingsForm.SettingsPanel.GetRoundBgmItemConflictBehavior();
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
                    UpdateRoundBgmPlayer(null);
                    ResetRoundBgmTracking();

                    _settings.ApiKey = settingsForm.SettingsPanel.apiKeyTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(_settings.ApiKey))
                    {
                        _settings.ApiKey = string.Empty;
                    }
                    else if (_settings.ApiKey.Length < 32)
                    {
                        MessageBox.Show(LanguageManager.Translate("APIキーは32文字以上である必要があります。"), LanguageManager.Translate("エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Cloud settings
                    bool previousCloudEnabled = _settings.CloudSyncEnabled;
                    string previousCloudUrl = _settings.CloudWebSocketUrl ?? string.Empty;
                    string previousCloudPlayerName = _settings.CloudPlayerName ?? string.Empty;

                    _settings.CloudSyncEnabled = settingsForm.SettingsPanel.CloudSyncEnabledCheckBox.Checked;
                    _settings.CloudPlayerName = settingsForm.SettingsPanel.CloudPlayerNameTextBox.Text?.Trim() ?? string.Empty;
                    _settings.CloudWebSocketUrl = settingsForm.SettingsPanel.CloudWebSocketUrlTextBox.Text?.Trim() ?? string.Empty;

                    bool cloudNeedsRestart = previousCloudEnabled != _settings.CloudSyncEnabled
                        || !string.Equals(previousCloudUrl, _settings.CloudWebSocketUrl, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(previousCloudPlayerName, _settings.CloudPlayerName, StringComparison.OrdinalIgnoreCase);

                    EvaluateAutoRecording("SettingsChanged");
                    RecomputeOverlayTerrorBase();
                    RefreshTerrorInfoOverlay();

                    Theme.SetTheme(_settings.ThemeKey, new ThemeApplicationContext(this, _moduleHost.CurrentServiceProvider));
                    _moduleHost.NotifyMainWindowThemeChanged(new ModuleMainWindowThemeContext(this, _settings.ThemeKey, Theme.CurrentDescriptor, _moduleHost.CurrentServiceProvider));
                    ApplyTheme();
                    if (languageChanged)
                    {
                        ApplyLanguage();
                    }
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

                    // Restart Cloud client if needed
                    if (cloudNeedsRestart && _cloudClient != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _cloudClient.StopAsync();
                                _logger?.LogEvent("CloudSync", "Cloud client stopped for restart.");

                                if (_settings.CloudSyncEnabled)
                                {
                                    if (!string.IsNullOrWhiteSpace(_settings.CloudWebSocketUrl))
                                    {
                                        _cloudClient.UpdateEndpoint(_settings.CloudWebSocketUrl);
                                    }

                                    await _cloudClient.StartAsync();
                                    _logger?.LogEvent("CloudSync", "Cloud client restarted successfully.");

                                    // Auto-login after restart
                                    if (!string.IsNullOrWhiteSpace(_settings.CloudPlayerName))
                                    {
                                        try
                                        {
                                            await _cloudClient.LoginAsync(
                                                _settings.CloudPlayerName,
                                                "1.0.0",
                                                System.Threading.CancellationToken.None
                                            );
                                            _logger?.LogEvent("CloudSync", $"Logged in as: {_settings.CloudPlayerName}");
                                        }
                                        catch (Exception loginEx)
                                        {
                                            _logger?.LogEvent("CloudSync", $"Failed to login after restart: {loginEx.Message}", LogEventLevel.Warning);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogEvent("CloudSync", $"Failed to restart Cloud client: {ex.Message}", LogEventLevel.Warning);
                            }
                        });
                    }
                }

                var closedContext = new ModuleSettingsViewLifecycleContext(settingsForm, settingsForm.SettingsPanel, _settings, ModuleSettingsViewStage.Closed, dialogResult, _moduleHost.CurrentServiceProvider);
                _moduleHost.NotifySettingsViewClosed(closedContext);
            }
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    return false;
                }

                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private bool PromptForElevatedRestart()
        {
            string message = LanguageManager.Translate("AutoRecording_AdminElevationPrompt");
            string title = LanguageManager.Translate("AutoRecording_AdminElevationTitle");
            var result = MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                _logger?.LogEvent("AutoRecording", "Administrator restart accepted for audio capture.");
                return true;
            }

            _logger?.LogEvent("AutoRecording", "Administrator restart declined by user.", LogEventLevel.Warning);
            return false;
        }

        private void RestartAsAdministratorForRecording()
        {
            try
            {
                string exePath = WinFormsApp.ExecutablePath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    throw new InvalidOperationException("Unable to determine executable path for restart.");
                }

                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = BuildRestartArguments()
                };

                Process.Start(startInfo);
                _logger?.LogEvent("AutoRecording", "Restarting ToNRoundCounter with administrator privileges for audio capture.");
                WinFormsApp.Exit();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                _logger?.LogEvent("AutoRecording", () => $"Administrator restart canceled by user: {ex.Message}", LogEventLevel.Warning);
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("AutoRecording", () => $"Failed to restart with administrator privileges: {ex.Message}", LogEventLevel.Error);
            }
        }

        private static string BuildRestartArguments()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length <= 1)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                var argument = args[i];
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append('"');
                builder.Append(argument.Replace("\"", "\"\""));
                builder.Append('"');
            }

            return builder.ToString();
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

            // Add Cloud features menu items
            if (_cloudClient != null && _settings.CloudSyncEnabled)
            {
                var votingMenuItem = new ToolStripMenuItem("投票システム (Voting)")
                {
                    Tag = "CloudVotingPanel"
                };
                votingMenuItem.Click += (s, e) =>
                {
                    var votingForm = new VotingPanelForm(_cloudClient, currentInstanceId, _settings.CloudPlayerName ?? Environment.UserName);
                    votingForm.ShowDialog(this);
                };
                windowsMenuItem.DropDownItems.Add(votingMenuItem);

                var profileMenuItem = new ToolStripMenuItem("プロフィール管理 (Profile)")
                {
                    Tag = "CloudProfileManager"
                };
                profileMenuItem.Click += (s, e) =>
                {
                    var profileForm = new ProfileManagerForm(_cloudClient, _settings.CloudPlayerName ?? Environment.UserName);
                    profileForm.ShowDialog(this);
                };
                windowsMenuItem.DropDownItems.Add(profileMenuItem);

                var settingsSyncMenuItem = new ToolStripMenuItem("設定同期 (Settings Sync)")
                {
                    Tag = "CloudSettingsSync"
                };
                settingsSyncMenuItem.Click += (s, e) =>
                {
                    var syncForm = new SettingsSyncForm(_cloudClient, _settings.CloudPlayerName ?? Environment.UserName, _settings);
                    syncForm.ShowDialog(this);
                };
                windowsMenuItem.DropDownItems.Add(settingsSyncMenuItem);

                var backupMenuItem = new ToolStripMenuItem("バックアップ管理 (Backup)")
                {
                    Tag = "CloudBackupManager"
                };
                backupMenuItem.Click += (s, e) =>
                {
                    var backupForm = new BackupManagerForm(_cloudClient, _settings.CloudPlayerName ?? Environment.UserName);
                    backupForm.ShowDialog(this);
                };
                windowsMenuItem.DropDownItems.Add(backupMenuItem);

                windowsMenuItem.DropDownItems.Add(new ToolStripSeparator());
            }

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
            try
            {
                await MainForm_LoadAsync();
            }
            catch (Exception ex)
            {
                LogUi($"Critical error during form load: {ex.Message}", LogEventLevel.Error);
                MessageBox.Show($"フォームの初期化中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task MainForm_LoadAsync()
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
            try
            {
                await OnFormClosingAsync(e);
            }
            catch (Exception ex)
            {
                LogUi($"Critical error during form closing: {ex.Message}", LogEventLevel.Error);
            }
            finally
            {
                base.OnFormClosing(e);
            }
        }

        private async Task OnFormClosingAsync(FormClosingEventArgs e)
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
            try
            {
                await webSocketClient.StopAsync();
            }
            catch (Exception ex)
            {
                LogUi($"Failed to stop WebSocket client: {ex.Message}", LogEventLevel.Warning);
            }

            try
            {
                oscListener.Stop();
            }
            catch (Exception ex)
            {
                LogUi($"Failed to stop OSC listener: {ex.Message}", LogEventLevel.Warning);
            }

            // Cloud WebSocketクライアントの停止
            if (_settings.CloudSyncEnabled && _cloudClient != null)
            {
                try
                {
                    // NOTE: InstanceLeaveAsync removed due to VRChat platform constraints
                    // Simply disconnect from cloud sync server
                    await _cloudClient.StopAsync();
                    LogUi("Cloud WebSocket client stopped successfully.", LogEventLevel.Debug);
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to stop Cloud WebSocket client: {ex.Message}", LogEventLevel.Warning);
                }
            }

            _cancellation.Cancel();
            if (oscRepeaterProcess != null && !oscRepeaterProcess.HasExited)
            {
                try
                {
                    oscRepeaterProcess.Kill();
                    oscRepeaterProcess.WaitForExit();
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to kill OSC repeater process: {ex.Message}", LogEventLevel.Warning);
                }
            }
            LogUi("Main form closing sequence finished. Base closing invoked.", LogEventLevel.Debug);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeEventBus();
                velocityTimer?.Stop();
                velocityTimer?.Dispose();

                try
                {
                    webSocketClient?.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to stop WebSocket client during dispose: {ex.Message}", LogEventLevel.Warning);
                }

                try
                {
                    oscListener?.Stop();
                }
                catch (Exception ex)
                {
                    LogUi($"Failed to stop OSC listener during dispose: {ex.Message}", LogEventLevel.Warning);
                }

                _cancellation.Cancel();
                overlayVisibilityTimer?.Stop();
                overlayVisibilityTimer?.Dispose();

                foreach (var form in overlayForms.Values)
                {
                    if (form == null)
                    {
                        continue;
                    }

                    if (!form.IsDisposed)
                    {
                        form.Hide();
                        form.Close();
                    }

                    form.Dispose();
                }

                overlayForms.Clear();
                components?.Dispose();

                try
                {
                    _uiLogQueue.CompleteAdding();
                }
                catch
                {
                    // Ignored
                }

                try
                {
                    _logWorker.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Ignored
                }

                _uiLogQueue.Dispose();
            }

            base.Dispose(disposing);
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

                    // Cloud同期: ラウンド開始
                    if (_settings.CloudSyncEnabled)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _presenter.OnRoundStartAsync(currentRound, currentInstanceId);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogEvent("CloudSync", $"Failed to sync round start: {ex.Message}", LogEventLevel.Warning);
                            }
                        });
                    }

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

                    EvaluateAutoRecording("RoundTypeUpdated");
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
                    else if (trackerEvent == "round_killers")
                    {
                        var args = json.Value<JArray>("args");
                        var activeRound = stateService.CurrentRound;
                        if (args != null && activeRound != null)
                        {
                            if (activeRound.TerrorIds == null || activeRound.TerrorIds.Length < 3)
                            {
                                activeRound.TerrorIds = new int[3];
                            }

                            for (int index = 0; index < activeRound.TerrorIds.Length; index++)
                            {
                                activeRound.TerrorIds[index] = 0;
                            }

                            for (int i = 0; i < Math.Min(3, args.Count); i++)
                            {
                                int? terrorId = TryConvertToInt(args[i]);
                                activeRound.TerrorIds[i] = terrorId ?? 0;
                            }
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
                    string updatedMapName = json.Value<string>("Name") ?? string.Empty;
                    _dispatcher.Invoke(() =>
                    {
                        InfoPanel.MapValue.Text = updatedMapName;
                    });

                    var existingRound = stateService.CurrentRound;
                    string? roundTypeForStorage = existingRound?.RoundType;
                    string? terrorKeyForStorage = existingRound?.TerrorKey;

                    if (existingRound != null)
                    {
                        existingRound.MapName = updatedMapName;
                    }

                    if (string.IsNullOrWhiteSpace(roundTypeForStorage))
                    {
                        _dispatcher.Invoke(() =>
                        {
                            var currentRoundType = InfoPanel.RoundTypeValue.Text;
                            if (!string.IsNullOrWhiteSpace(currentRoundType))
                            {
                                roundTypeForStorage = currentRoundType;
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(updatedMapName) && !string.IsNullOrWhiteSpace(roundTypeForStorage))
                    {
                        stateService.SetRoundMapName(roundTypeForStorage!, updatedMapName);
                        if (!string.IsNullOrWhiteSpace(terrorKeyForStorage))
                        {
                            stateService.SetTerrorMapName(roundTypeForStorage!, terrorKeyForStorage!, updatedMapName);
                        }
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
                        EvaluateAutoRecording("TerrorUpdated");
                    }

                    _dispatcher.Invoke(() => { UpdateTerrorDisplay(displayName, color, terrors); });

                    var activeRoundForAuto = stateService.CurrentRound;
                    if (activeRoundForAuto != null)
                    {
                        // Announce threat to Cloud
                        if (_settings.CloudSyncEnabled && _cloudClient != null && _cloudClient.IsConnected && !string.IsNullOrEmpty(currentInstanceId))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _cloudClient.AnnounceThreatAsync(
                                        currentInstanceId,
                                        activeRoundForAuto.TerrorKey ?? displayName,
                                        roundType
                                    );
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogEvent("CloudThreat", $"Failed to announce threat: {ex.Message}", LogEventLevel.Debug);
                                }
                            });
                        }

                        // Check for desire players
                        _ = Task.Run(async () =>
                        {
                            await CheckDesirePlayersForRoundAsync(roundType, activeRoundForAuto.TerrorKey);
                        });

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
                            // Non-delayed auto suicide - check for desire players
                            ScheduleAutoSuicideWithDesireCheck(TimeSpan.FromSeconds(3), true, allRoundsForcedSchedule);
                        }
                        else if (terrorAction == 2)
                        {
                            var roundStart = autoSuicideService.RoundStartTime;
                            bool resetStartTime = roundStart == default;
                            TimeSpan remaining;
                            if (resetStartTime)
                            {
                                remaining = TimeSpan.FromSeconds(40);
                            }
                            else
                            {
                                remaining = TimeSpan.FromSeconds(40) - (DateTime.UtcNow - roundStart);
                                if (remaining < TimeSpan.Zero)
                                {
                                    remaining = TimeSpan.Zero;
                                }
                            }

                            ScheduleAutoSuicide(remaining, resetStartTime, allRoundsForcedSchedule);
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
                        
                        // Send damage update to Cloud
                        _ = Task.Run(async () => await UpdateCloudPlayerState());
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
                    string statName = json.Value<string>("Name") ?? string.Empty;
                    JToken? valueToken = json["Value"];

                    if (string.IsNullOrWhiteSpace(statName) || valueToken == null)
                    {
                        return;
                    }

                    var statValue = valueToken.Type == JTokenType.Null ? null : valueToken.ToObject<object>();
                    if (statValue != null)
                    {
                        stateService.UpdateStat(statName, statValue);
                    }

                    int? numericValue = TryConvertToInt(valueToken);
                    if (numericValue.HasValue && stateService.CurrentRound != null)
                    {
                        if (string.Equals(statName, "RoundInt", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(statName, "RoundID", StringComparison.OrdinalIgnoreCase))
                        {
                            stateService.CurrentRound.RoundNumber = numericValue.Value;
                        }
                        else if (string.Equals(statName, "MapInt", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(statName, "MapID", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(statName, "MapId", StringComparison.OrdinalIgnoreCase))
                        {
                            stateService.CurrentRound.MapId = numericValue.Value;
                        }
                    }
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
                        string previousInstanceId;
                        DateTimeOffset now = DateTimeOffset.Now;
                        lock (instanceTimerSync)
                        {
                            hadInstance = !string.IsNullOrEmpty(currentInstanceId);
                            previousInstanceId = currentInstanceId;
                            currentInstanceId = string.Empty;
                            currentInstanceEnteredAt = now;
                        }

                        if (hadInstance)
                        {
                            UpdateInstanceTimerOverlay();
                            
                            // NOTE: Cloud instance leave removed due to VRChat platform constraints
                            // Instance tracking is now handled server-side based on active connections
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
                    if (savecode != String.Empty && _settings.ApiKey != String.Empty)
                    {
                        // https://toncloud.sprink.cloud/api/savecode/create/{apikey} にPOSTリクエストを送信(savecodeを送信)
                        using (var client = new HttpClient())
                        {
                            client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/savecode/create/" + _settings.ApiKey);
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
            var round = stateService.CurrentRound;
            if (round != null)
            {
                ResetRoundBgmTracking();
                string roundType = round.RoundType ?? string.Empty;

                if (string.IsNullOrWhiteSpace(round.MapName))
                {
                    string latestMapName = string.Empty;
                    _dispatcher.Invoke(() => latestMapName = InfoPanel.MapValue.Text);
                    if (!string.IsNullOrWhiteSpace(latestMapName))
                    {
                        round.MapName = latestMapName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(round.MapName))
                {
                    stateService.SetRoundMapName(roundType, round.MapName);
                }
                if (!string.IsNullOrEmpty(round.TerrorKey))
                {
                    string terrorKey = round.TerrorKey!;
                    bool survived = lastOptedIn && !round.IsDeath;
                    stateService.RecordRoundResult(roundType, terrorKey, survived);
                    if (!string.IsNullOrWhiteSpace(round.MapName))
                    {
                        stateService.SetTerrorMapName(roundType, terrorKey, round.MapName);
                    }
                }
                else
                {
                    stateService.RecordRoundResult(roundType, null, !round.IsDeath);
                }

                // 次ラウンド予測ロジック
                var normalTypes = new[] { "クラシック", "Classic", "RUN", "走れ！" };
                var overrideTypes = new HashSet<string> { "アンバウンド", "8ページ", "ゴースト", "オルタネイト" };
                string current = round.RoundType ?? string.Empty;
                int roundCycleForHistory = stateService.RoundCycle;

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

                stateService.UpdateCurrentRound(null);
                EvaluateAutoRecording("RoundFinalized");
                var roundForHistory = stateService.PreviousRound ?? round;
                lastRoundTypeForHistory = roundForHistory?.RoundType ?? string.Empty;

                // Cloud同期: ラウンド終了
                if (_settings.CloudSyncEnabled && roundForHistory != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _presenter.OnRoundEndAsync(roundForHistory, status);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogEvent("CloudSync", $"Failed to sync round end: {ex.Message}", LogEventLevel.Warning);
                        }
                    });
                }

                _dispatcher.Invoke(() =>
                {
                    UpdateNextRoundPrediction(historyStatusOverride, roundCycleForHistory);
                    UpdateAggregateStatsDisplay();
                    if (roundForHistory != null)
                    {
                        _presenter.AppendRoundLog(roundForHistory, status);
                    }
                    ClearEventDisplays();
                    ClearItemDisplay();
                    lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}";
                });
                if (roundForHistory != null)
                {
                    _ = _presenter.UploadRoundLogAsync(roundForHistory, status);
                }
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
            try
            {
                await VelocityTimer_TickAsync();
            }
            catch (Exception ex)
            {
                LogUi($"Error in velocity timer tick: {ex.Message}", LogEventLevel.Error);
            }
        }

        private async Task VelocityTimer_TickAsync()
        {
            if (Interlocked.Exchange(ref oscUiUpdatePending, 0) == 1)
            {
                lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}  Members: {connected}";
            }

            // Send state update to Cloud (throttled)
            _ = Task.Run(async () => await UpdateCloudPlayerState());

            // Send monitoring status to Cloud (throttled)
            _ = Task.Run(async () =>
            {
                var now = DateTime.Now;
                if ((now - lastMonitoringStatusUpdate).TotalSeconds >= MonitoringStatusUpdateIntervalSeconds)
                {
                    lastMonitoringStatusUpdate = now;
                    await ReportMonitoringStatusAsync();
                }
            });

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

            UpdateRoundBgmState();

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

        private void UpdateNextRoundPrediction(string? historyStatusOverride = null, int? roundCycleForHistory = null)
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
                lastRoundTypeForHistory = string.Empty;
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
            RecordRoundHistory(historyStatusOverride, roundCycleForHistory);
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
                    EvaluateAutoRecording("RoundActiveStarted");
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

        private void ScheduleAutoSuicide(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false, bool isManualAction = false)
        {
            if (!isManualAction && autoSuicideManualCancelRequested)
            {
                autoSuicideManualCancelRequested = false;
                autoSuicideManualDelayUntil = null;
                UpdateShortcutOverlayState();
                return;
            }

            if (resetStartTime)
            {
                autoSuicideManualCancelRequested = false;
                autoSuicideManualDelayUntil = null;
            }

            if (!isManualAction)
            {
                if (autoSuicideManualDelayUntil.HasValue)
                {
                    DateTime now = DateTime.UtcNow;
                    if (autoSuicideManualDelayUntil.Value > now)
                    {
                        TimeSpan manualRemaining = autoSuicideManualDelayUntil.Value - now;
                        if (manualRemaining > delay)
                        {
                            delay = manualRemaining;
                        }
                    }
                    else
                    {
                        autoSuicideManualDelayUntil = null;
                    }
                }
            }
            else
            {
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                autoSuicideManualCancelRequested = false;
                autoSuicideManualDelayUntil = DateTime.UtcNow + delay;
            }

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            autoSuicideService.Schedule(delay, resetStartTime, PerformAutoSuicide);
            allRoundsForcedSchedule = fromAllRoundsMode;
            UpdateShortcutOverlayState();
        }

        private void CancelAutoSuicide(bool manualOverride = false)
        {
            if (autoSuicideService.HasScheduled)
            {
                autoSuicideService.Cancel();
            }

            if (manualOverride)
            {
                autoSuicideManualCancelRequested = true;
            }

            autoSuicideManualDelayUntil = null;
            allRoundsForcedSchedule = false;
            UpdateShortcutOverlayState();
        }

        private TimeSpan? DelayAutoSuicide(bool manualOverride = false)
        {
            if (!autoSuicideService.HasScheduled)
            {
                UpdateShortcutOverlayState();
                return null;
            }

            TimeSpan elapsed = DateTime.UtcNow - autoSuicideService.RoundStartTime;
            TimeSpan remaining = TimeSpan.FromSeconds(40) - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.FromSeconds(40);
            }

            if (manualOverride)
            {
                autoSuicideManualCancelRequested = false;
            }

            ScheduleAutoSuicide(remaining, false, allRoundsForcedSchedule, manualOverride);
            return remaining;
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
            terrorCountdownLastDisplayedValue = double.NaN;
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
                terrorCountdownLastDisplayedValue = double.NaN;
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

            double elapsed = (DateTime.Now - terrorCountdownStart).TotalSeconds;
            double remaining = terrorCountdownDurationSeconds - elapsed;
            bool shouldCountUp = string.Equals(terrorCountdownTargetName, " ", StringComparison.Ordinal)
                                 || string.Equals(terrorCountdownTargetName, "sm64.z64", StringComparison.OrdinalIgnoreCase);

            string suffix;
            double displayValue;

            if (remaining > 0 || !shouldCountUp)
            {
                if (remaining < 0)
                {
                    remaining = 0;
                }

                int seconds = (int)Math.Ceiling(remaining);
                if (seconds < 0)
                {
                    seconds = 0;
                }

                suffix = $"(出現まで {seconds} 秒)";
                displayValue = seconds;
            }
            else
            {
                double overtime = elapsed - terrorCountdownDurationSeconds;
                if (overtime < 0)
                {
                    overtime = 0;
                }

                double rounded = Math.Round(overtime, 1, MidpointRounding.AwayFromZero);
                suffix = $"{rounded.ToString("F1", CultureInfo.InvariantCulture)}秒経過...";
                displayValue = rounded;
            }

            if (!displayValue.Equals(terrorCountdownLastDisplayedValue) || !string.Equals(currentTerrorCountdownSuffix, suffix, StringComparison.Ordinal))
            {
                currentTerrorCountdownSuffix = suffix;
                terrorCountdownLastDisplayedValue = displayValue;
                RefreshTerrorDisplays();
            }
        }

        private static int? TryConvertToInt(JToken? token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            if (token.Type == JTokenType.Float)
            {
                return (int)Math.Round(token.Value<double>(), MidpointRounding.AwayFromZero);
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>() ? 1 : 0;
            }

            if (token.Type == JTokenType.String)
            {
                var value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value) &&
                    int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
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

        private void UpdateRoundBgmState()
        {
            if (!_settings.RoundBgmEnabled)
            {
                ResetRoundBgmTracking();
                return;
            }

            var currentRound = stateService.CurrentRound;
            if (currentRound == null)
            {
                ResetRoundBgmTracking();
                return;
            }

            var matchingEntry = FindMatchingRoundBgmEntry(currentRound.RoundType, currentRound.TerrorKey);
            if (matchingEntry != null)
            {
                if (!ReferenceEquals(activeRoundBgmEntry, matchingEntry))
                {
                    roundBgmMatchStart = DateTime.Now;
                    UpdateRoundBgmPlayer(matchingEntry);
                }
                else
                {
                    EnsureRoundBgmPlayer(matchingEntry);
                }

                if (roundBgmMatchStart == DateTime.MinValue)
                {
                    roundBgmMatchStart = DateTime.Now;
                }
                else if ((DateTime.Now - roundBgmMatchStart).TotalSeconds >= 0.5)
                {
                    if (!roundBgmActive)
                    {
                        StartRoundBgm(matchingEntry);
                    }
                }
            }
            else
            {
                ResetRoundBgmTracking();
            }
        }

        private RoundBgmEntry? FindMatchingRoundBgmEntry(string? roundType, string? terrorType)
        {
            if (!_settings.RoundBgmEnabled || _settings.RoundBgmEntries == null || _settings.RoundBgmEntries.Count == 0)
            {
                return null;
            }

            var roundBgmEntries = _settings.RoundBgmEntries;
            if (roundBgmEntries == null)
            {
                return null;
            }

            static string? NormalizeKey(string? value)
            {
                if (value is null)
                {
                    return null;
                }

                var trimmed = value.Trim();
                return trimmed.Length == 0 ? null : trimmed;
            }

            var comparer = StringComparer.OrdinalIgnoreCase;
            string? normalizedRound = NormalizeKey(roundType);
            string? normalizedTerror = NormalizeKey(terrorType);

            bool StringsEqual(string? left, string? right)
            {
                if (left == null && right == null)
                {
                    return true;
                }

                if (left == null || right == null)
                {
                    return false;
                }

                return comparer.Equals(left, right);
            }

            bool EntryMatches(RoundBgmEntry entry)
            {
                string? entryRound = NormalizeKey(entry.RoundType);
                string? entryTerror = NormalizeKey(entry.TerrorType);
                bool hasRound = entryRound != null;
                bool hasTerror = entryTerror != null;

                bool matchesRound = hasRound && normalizedRound != null && comparer.Equals(entryRound, normalizedRound);
                bool matchesTerror = hasTerror && normalizedTerror != null && comparer.Equals(entryTerror, normalizedTerror);

                if (hasRound && hasTerror)
                {
                    return matchesRound && matchesTerror;
                }

                if (hasRound && !hasTerror)
                {
                    return matchesRound;
                }

                if (!hasRound && hasTerror)
                {
                    return matchesTerror;
                }

                return !hasRound && !hasTerror;
            }

            bool combinationChanged = !StringsEqual(roundBgmSelectionRoundType, normalizedRound) ||
                                      !StringsEqual(roundBgmSelectionTerrorType, normalizedTerror);

            if (!combinationChanged && activeRoundBgmEntry != null && EntryMatches(activeRoundBgmEntry))
            {
                return activeRoundBgmEntry;
            }

            var matchesRoundAndTerror = new List<RoundBgmEntry>();
            var matchesRoundOnly = new List<RoundBgmEntry>();
            var matchesTerrorOnly = new List<RoundBgmEntry>();
            var matchesWildcard = new List<RoundBgmEntry>();

            foreach (var entry in roundBgmEntries)
            {
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                string? entryRound = NormalizeKey(entry.RoundType);
                string? entryTerror = NormalizeKey(entry.TerrorType);
                bool hasRound = entryRound != null;
                bool hasTerror = entryTerror != null;

                bool matchesRound = hasRound && normalizedRound != null && comparer.Equals(entryRound, normalizedRound);
                bool matchesTerror = hasTerror && normalizedTerror != null && comparer.Equals(entryTerror, normalizedTerror);

                if (hasRound && hasTerror)
                {
                    if (matchesRound && matchesTerror)
                    {
                        matchesRoundAndTerror.Add(entry);
                    }
                }
                else if (hasRound)
                {
                    if (matchesRound)
                    {
                        matchesRoundOnly.Add(entry);
                    }
                }
                else if (hasTerror)
                {
                    if (matchesTerror)
                    {
                        matchesTerrorOnly.Add(entry);
                    }
                }
                else
                {
                    matchesWildcard.Add(entry);
                }
            }

            RoundBgmEntry? SelectEntry(List<RoundBgmEntry> candidates)
            {
                if (candidates.Count == 0)
                {
                    return null;
                }

                var selected = candidates[roundBgmRandom.Next(candidates.Count)];
                roundBgmSelectionRoundType = normalizedRound;
                roundBgmSelectionTerrorType = normalizedTerror;
                return selected;
            }

            var selection = SelectEntry(matchesRoundAndTerror)
                            ?? SelectEntry(matchesRoundOnly)
                            ?? SelectEntry(matchesTerrorOnly)
                            ?? SelectEntry(matchesWildcard);

            if (selection == null)
            {
                roundBgmSelectionRoundType = normalizedRound;
                roundBgmSelectionTerrorType = normalizedTerror;
            }

            return selection;
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

        /// <summary>
        /// Handle Cloud WebSocket stream messages
        /// </summary>
        private void OnCloudMessageReceived(object? sender, CloudMessage message)
        {
            if (message.Type != "stream" || string.IsNullOrEmpty(message.Event))
            {
                return;
            }

            try
            {
                switch (message.Event)
                {
                    case "player.state.updated":
                        // プレイヤー状態が更新された
                        _logger?.LogEvent("CloudStream", $"Player state updated: {message.Data}", LogEventLevel.Debug);
                        break;

                    case "instance.member.joined":
                        // メンバーが参加した
                        _dispatcher.Invoke(() =>
                        {
                            _logger?.LogEvent("CloudStream", "Instance member joined");
                            // 次回のupdateで自動的に反映される
                        });
                        break;

                    case "instance.member.left":
                        // メンバーが退出した
                        _dispatcher.Invoke(() =>
                        {
                            _logger?.LogEvent("CloudStream", "Instance member left");
                            // 次回のupdateで自動的に反映される
                        });
                        break;

                    case "threat.announced":
                        // 脅威がアナウンスされた
                        _logger?.LogEvent("CloudStream", $"Threat announced: {message.Data}", LogEventLevel.Information);
                        break;

                    default:
                        _logger?.LogEvent("CloudStream", $"Unknown stream event: {message.Event}", LogEventLevel.Debug);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogEvent("CloudStream", $"Failed to handle stream message: {ex.Message}", LogEventLevel.Warning);
            }
        }
    }
}
