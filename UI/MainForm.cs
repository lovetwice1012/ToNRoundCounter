using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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

namespace ToNRoundCounter.UI
{
    public partial class MainForm : Form, IMainView
    {
        // 上部固定UI
        private Label lblStatus;           // WebSocket接続状況
        private Label lblOSCStatus;        // OSC通信接続状況
        private Button btnToggleTopMost;   // 画面最前面固定ボタン
        private Button btnSettings;        // 設定変更ボタン
        private MenuStrip mainMenuStrip;
        private ToolStripMenuItem windowsMenuItem;

        // デバッグ情報用ラベル
        private Label lblDebugInfo;

        // 情報表示パネル
        public InfoPanel InfoPanel { get; private set; }
        private TerrorInfoPanel terrorInfoPanel;
        private JObject terrorInfoData;

        // 統計情報表示およびラウンドログ表示は SplitContainer で実装（縦に並べる）
        private SplitContainer splitContainerMain;
        private Label lblStatsTitle;
        private Label lblRoundLogTitle;
        private RichTextBox rtbStatsDisplay;  // 統計情報表示欄
        public LogPanel logPanel;             // ラウンドログパネル

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

        private Action<WebSocketConnected> _wsConnectedHandler;
        private Action<WebSocketDisconnected> _wsDisconnectedHandler;
        private Action<OscConnected> _oscConnectedHandler;
        private Action<OscDisconnected> _oscDisconnectedHandler;
        private Action<WebSocketMessageReceived> _wsMessageHandler;
        private Action<OscMessageReceived> _oscMessageHandler;
        private Action<SettingsValidationFailed> _settingsValidationFailedHandler;

        private Dictionary<string, Color> terrorColors;
        private bool lastOptedIn = true;

        // 次ラウンド予測用：stateService.RoundCycle==0 → 通常ラウンド, ==1 → 「通常ラウンド or 特殊ラウンド」, >=2 → 特殊ラウンド
        private Random randomGenerator = new Random();

        // OSC/Velocity 関連
        private List<AutoSuicideRule> autoSuicideRules = new List<AutoSuicideRule>();
        private static readonly string[] AllRoundTypes = new string[]
        {
            "クラシック", "走れ！", "オルタネイト", "パニッシュ", "狂気", "サボタージュ", "霧", "ブラッドバス", "ダブルトラブル", "EX", "ミッドナイト", "ゴースト", "8ページ", "アンバウンド", "寒い夜", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス"
        };

        private Process oscRepeaterProcess = null;

        private bool isNotifyActivated = false;

        private static readonly string[] testerNames = new string[] { "yussy5373", "Kotetsu Wilde", "tofu_shoyu", "ちよ千夜", "Blackpit", "shari_1928", "MitarashiMochi" };

        private bool isRestarted = false;

        private bool issetAllSelfKillMode = false;

        private string _lastSaveCode = string.Empty;

        private string version = "1.12.0";

        private readonly AutoSuicideService autoSuicideService;

        private MediaPlayer itemMusicPlayer;
        private bool itemMusicLoopRequested;
        private bool itemMusicActive;
        private DateTime itemMusicMatchStart = DateTime.MinValue;
        private string lastLoadedItemMusicPath = string.Empty;
        private ItemMusicEntry activeItemMusicEntry;
        private string currentTerrorBaseText = string.Empty;
        private bool terrorCountdownActive;
        private DateTime terrorCountdownStart = DateTime.MinValue;
        private int terrorCountdownDurationSeconds;
        private string terrorCountdownTargetName = string.Empty;
        private int terrorCountdownLastDisplayedSeconds = -1;

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
            InfoPanel.BackColor = _settings.BackgroundColor_InfoPanel;
            InfoPanel.Location = new Point(margin, currentY);
            InfoPanel.Width = contentWidth;
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
            InfoPanel = new InfoPanel();
            InfoPanel.BackColor = _settings.BackgroundColor_InfoPanel;
            this.Controls.Add(InfoPanel);
            if (terrorInfoPanel != null)
            {
                this.Controls.SetChildIndex(InfoPanel, this.Controls.GetChildIndex(terrorInfoPanel));
            }
            ApplyTheme();
            MainForm_Resize(this, EventArgs.Empty);
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

        private void MainForm_Resize(object sender, EventArgs e)
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

                    Theme.SetTheme(_settings.ThemeKey, new ThemeApplicationContext(this, _moduleHost.CurrentServiceProvider));
                    _moduleHost.NotifyMainWindowThemeChanged(new ModuleMainWindowThemeContext(this, _settings.ThemeKey, Theme.CurrentDescriptor, _moduleHost.CurrentServiceProvider));
                    ApplyTheme();
                    _moduleHost.NotifyMainWindowLayoutUpdated(new ModuleMainWindowLayoutContext(this, _moduleHost.CurrentServiceProvider));
                    InfoPanel.TerrorValue.ForeColor = _settings.FixedTerrorColor;
                    UpdateAggregateStatsDisplay();
                    UpdateDisplayVisibility();
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

        private bool IsOlderVersion(string current, string latest)
        {
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
                if (json.TryGetValue("Command", out JToken commandToken))
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
                    stateService.UpdateCurrentRound(new Round());
                    stateService.CurrentRound.RoundType = roundType;
                    stateService.CurrentRound.IsDeath = false;
                    stateService.CurrentRound.TerrorKey = "";
                    stateService.CurrentRound.RoundColor = displayColorInt;
                    string mapName = string.Empty;
                    string itemName = string.Empty;
                    _dispatcher.Invoke(() =>
                    {
                        mapName = InfoPanel.MapValue.Text;
                        itemName = InfoPanel.ItemValue.Text;
                    });
                    stateService.CurrentRound.MapName = mapName;
                    stateService.CurrentRound.Damage = 0;
                    stateService.CurrentRound.PageCount = 0;
                    if (!string.IsNullOrEmpty(itemName))
                        stateService.CurrentRound.ItemNames.Add(itemName);
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
                        autoSuicideService.Schedule(TimeSpan.FromSeconds(13), true, PerformAutoSuicide);
                    }
                    else if (autoAction == 1)
                    {
                        var delay = hasPendingDelayed ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(13);
                        autoSuicideService.Schedule(delay, true, PerformAutoSuicide);
                    }
                    else if (autoAction == 2)
                    {
                        autoSuicideService.Schedule(TimeSpan.FromSeconds(40), true, PerformAutoSuicide);
                    }

                }
                else if (eventType == "TRACKER")
                {
                    string trackerEvent = json.Value<string>("event") ?? "";
                    if (trackerEvent == "round_start")
                    {
                        if (lastOptedIn != false)
                        {
                            stateService.UpdateCurrentRound(new Round());
                            stateService.CurrentRound.RoundType = "";
                            stateService.CurrentRound.IsDeath = false;
                            stateService.CurrentRound.TerrorKey = "";
                            stateService.CurrentRound.RoundColor = 0xFFFFFF;
                            string mapName = string.Empty;
                            _dispatcher.Invoke(() => mapName = InfoPanel.MapValue.Text);
                            stateService.CurrentRound.MapName = mapName;
                            stateService.CurrentRound.Damage = 0;
                        }
                    }
                    else if (trackerEvent == "round_won")
                    {
                        if (stateService.CurrentRound != null)
                        {
                            FinalizeCurrentRound(stateService.CurrentRound.IsDeath ? "☠" : "✅");
                        }
                    }
                    else if (trackerEvent == "round_lost")
                    {
                        if (stateService.CurrentRound != null && !stateService.CurrentRound.IsDeath)
                        {
                            stateService.CurrentRound.IsDeath = true;
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
                    if (stateService.CurrentRound != null)
                    {
                        stateService.CurrentRound.MapName = json.Value<string>("Name") ?? "";
                    }
                }
                else if (eventType == "TERRORS" && (command == 0 || command == 1))
                {
                    string displayName = json.Value<string>("DisplayName") ?? "";
                    int displayColorInt = json.Value<int>("DisplayColor");
                    Color color = ConvertColorFromInt(displayColorInt);

                    if (stateService.CurrentRound != null && !stateService.CurrentRound.RoundColor.HasValue)
                    {
                        stateService.CurrentRound.RoundColor = displayColorInt;
                    }

                    List<(string name, int count)> terrors = null;
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
                    if (stateService.CurrentRound != null && namesForLogic != null && namesForLogic.Count > 0)
                    {
                        string joinedNames = string.Join(" & ", namesForLogic);
                        stateService.CurrentRound.TerrorKey = joinedNames;
                    }

                    _dispatcher.Invoke(() => { UpdateTerrorDisplay(displayName, color, terrors); });

                    if (stateService.CurrentRound != null)
                    {
                        if (roundType == "ブラッドバス" && namesForLogic != null && namesForLogic.Any(n => n.Contains("LVL 3")))
                        {
                            roundType = "EX";
                        }
                        //もしroundTypeが自動自殺ラウンド対象なら自動自殺
                        int terrorAction = ShouldAutoSuicide(roundType, stateService.CurrentRound.TerrorKey);
                        if (terrorAction == 0 && autoSuicideService.HasScheduled)
                        {
                            autoSuicideService.Cancel();
                        }
                        if (issetAllSelfKillMode || terrorAction == 1)
                        {
                            _ = Task.Run(() => PerformAutoSuicide());
                        }
                        else if (terrorAction == 2)
                        {
                            TimeSpan remaining = TimeSpan.FromSeconds(40) - (DateTime.UtcNow - autoSuicideService.RoundStartTime);
                            if (remaining > TimeSpan.Zero)
                            {
                                autoSuicideService.Schedule(remaining, false, PerformAutoSuicide);
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
                    string instanceValue = json.Value<string>("Value") ?? "";
                    if (!string.IsNullOrEmpty(instanceValue))
                    {
                        _ = Task.Run(() => ConnectToInstance(instanceValue));
                        isNotifyActivated = false; ;
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
                string roundType = stateService.CurrentRound.RoundType;
                stateService.SetRoundMapName(roundType, stateService.CurrentRound.MapName ?? "");
                if (!string.IsNullOrEmpty(stateService.CurrentRound.TerrorKey))
                {
                    string terrorKey = stateService.CurrentRound.TerrorKey;
                    bool survived = lastOptedIn && !stateService.CurrentRound.IsDeath;
                    stateService.RecordRoundResult(roundType, terrorKey, survived);
                    stateService.SetTerrorMapName(roundType, terrorKey, stateService.CurrentRound.MapName ?? "");
                }
                else
                {
                    stateService.RecordRoundResult(roundType, null, !stateService.CurrentRound.IsDeath);
                }
                if (!string.IsNullOrEmpty(stateService.CurrentRound.MapName))
                    stateService.SetRoundMapName(stateService.CurrentRound.RoundType, stateService.CurrentRound.MapName);

                // 次ラウンド予測ロジック
                var normalTypes = new[] { "クラシック", "Classic", "RUN", "走れ！" };
                var overrideTypes = new HashSet<string> { "アンバウンド", "8ページ", "ゴースト", "オルタネイト" };
                string current = stateService.CurrentRound.RoundType;

                if (normalTypes.Any(type => current.Contains(type)))
                {
                    // 通常ラウンド
                    if (stateService.RoundCycle == 0)
                        stateService.SetRoundCycle(1); // 次は通常 or 特殊
                    else if (stateService.RoundCycle == 1)
                        stateService.SetRoundCycle(2); // 次は特殊
                    else
                        stateService.SetRoundCycle(1); // 想定外: 状態を不確定へ
                }
                else if (overrideTypes.Contains(current))
                {
                    // 8ページ・アンバウンド・ゴースト・オルタネイトによる上書き
                    if (stateService.RoundCycle >= 2)
                        stateService.SetRoundCycle(0); // 特殊として扱いリセット
                    else
                        stateService.SetRoundCycle(1); // 通常扱いだが次は不確定
                }
                else
                {
                    // 確定特殊ラウンド
                    stateService.SetRoundCycle(0);
                }

                var round = stateService.CurrentRound;
                _dispatcher.Invoke(() =>
                {
                    UpdateNextRoundPrediction();
                    UpdateAggregateStatsDisplay();
                    _presenter.AppendRoundLog(round, status);
                    ClearEventDisplays();
                    ClearItemDisplay();
                    lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}";
                });
                _ = _presenter.UploadRoundLogAsync(round, status);
                stateService.UpdateCurrentRound(null);
            }
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
            if (stateService.CurrentRound != null && currentVelocity < 1)
            {
                if (idleStartTime == DateTime.MinValue)
                {
                    idleStartTime = DateTime.Now;
                }
                else
                {
                    double idleSeconds = (DateTime.Now - idleStartTime).TotalSeconds;
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
                InfoPanel.IdleTimeLabel.Text = "";
                afkSoundPlayed = false;
            }

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

        private void UpdateNextRoundPrediction()
        {
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
        }

        private void UpdateAggregateStatsDisplay()
        {
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
                    parts.Add(LanguageManager.Translate("出現回数") + "=" + agg.Total);
                if (_settings.Filter_Survival)
                    parts.Add(LanguageManager.Translate("生存回数") + "=" + agg.Survival);
                if (_settings.Filter_Death)
                    parts.Add(LanguageManager.Translate("死亡回数") + "=" + agg.Death);
                if (_settings.Filter_SurvivalRate)
                    parts.Add(string.Format(LanguageManager.Translate("生存率") + "={0:F1}%", agg.SurvivalRate));
                if (overallTotal > 0 && _settings.Filter_Appearance)
                {
                    double occurrenceRate = agg.Total * 100.0 / overallTotal;
                    parts.Add(string.Format(LanguageManager.Translate("出現率") + "={0:F1}%", occurrenceRate));
                }
                string roundLine = string.Join(" ", parts);
                AppendLine(rtbStatsDisplay, roundLine, Theme.Current.Foreground);

                // テラーのフィルター
                if (_settings.Filter_Terror && stateService.TryGetTerrorAggregates(roundType, out var terrorDict))
                {
                    foreach (var terrorKvp in terrorDict)
                    {
                        string terrorKey = terrorKvp.Key;
                        TerrorAggregate tAgg = terrorKvp.Value;
                        var terrorParts = new List<string>();
                        terrorParts.Add(terrorKey);
                        if (_settings.Filter_Appearance)
                            terrorParts.Add(LanguageManager.Translate("出現回数") + "=" + tAgg.Total);
                        if (_settings.Filter_Survival)
                            terrorParts.Add(LanguageManager.Translate("生存回数") + "=" + tAgg.Survival);
                        if (_settings.Filter_Death)
                            terrorParts.Add(LanguageManager.Translate("死亡回数") + "=" + tAgg.Death);
                        if (_settings.Filter_SurvivalRate)
                            terrorParts.Add(string.Format(LanguageManager.Translate("生存率") + "={0:F1}%", tAgg.SurvivalRate));
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
            currentTerrorBaseText = "";
            InfoPanel.TerrorValue.Text = currentTerrorBaseText;
            ResetTerrorCountdown();
            InfoPanel.DamageValue.Text = "";
            InfoPanel.ItemValue.Text = "";
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
        }

        private void UpdateTerrorInfoPanel(List<string> names)
        {
            if (terrorInfoPanel == null)
                return;

            int margin = 10;
            int width = this.ClientSize.Width - 2 * margin;
            terrorInfoPanel.UpdateInfo(names, terrorInfoData, width);

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
                    stateService.UpdateCurrentRound(new Round
                    {
                        RoundType = "Active Round",
                        IsDeath = false,
                        TerrorKey = "",
                        MapName = mapName,
                        Damage = 0,
                        PageCount = 0,
                        RoundColor = 0xFFFFFF
                    });
                    if (!string.IsNullOrEmpty(itemName))
                        stateService.CurrentRound.ItemNames.Add(itemName);
                    _dispatcher.Invoke(() =>
                    {
                        UpdateRoundTypeLabel();
                        InfoPanel.RoundTypeValue.ForeColor = Color.White;
                        InfoPanel.DamageValue.Text = "0";
                    });
                }

                if (stateService.CurrentRound != null)
                {
                    string checkType = stateService.CurrentRound.RoundType;
                    string terror = stateService.CurrentRound.TerrorKey;
                    if (checkType == "ブラッドバス" && !string.IsNullOrEmpty(terror) && terror.Contains("LVL 3"))
                    {
                        checkType = "EX";
                    }
                    if (!autoSuicideService.HasScheduled)
                    {
                        int action = ShouldAutoSuicide(checkType, terror, out var hasPendingDelayed);
                        if (issetAllSelfKillMode)
                        {
                            autoSuicideService.Schedule(TimeSpan.FromSeconds(13), true, PerformAutoSuicide);
                        }
                        else if (action == 1)
                        {
                            var delay = hasPendingDelayed ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(13);
                            autoSuicideService.Schedule(delay, true, PerformAutoSuicide);
                        }
                        else if (action == 2)
                        {
                            autoSuicideService.Schedule(TimeSpan.FromSeconds(40), true, PerformAutoSuicide);
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
                autoSuicideService.Cancel();
            }
        }

        private void PerformAutoSuicide()
        {
            _logger.LogEvent("Suicide", "Performing Suside");
            LaunchSuicideInputIfExists();
            _logger.LogEvent("Suicide", "finish");
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
                if (AutoSuicideRule.TryParse(line, out var r))
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
            _moduleHost.NotifyAutoSuicideRulesPrepared(new ModuleAutoSuicideRuleContext(autoSuicideRules, _settings, _moduleHost.CurrentServiceProvider));
        }

        private int ShouldAutoSuicide(string roundType, string terrorName, out bool hasPendingDelayed)
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
            _moduleHost.NotifyAutoSuicideDecisionEvaluated(decisionContext);
            hasPendingDelayed = decisionContext.HasPendingDelayed;
            return decisionContext.Decision;
        }

        private int ShouldAutoSuicide(string roundType, string terrorName)
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
                    InfoPanel.TerrorValue.Text = currentTerrorBaseText;
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

            string currentText = InfoPanel.TerrorValue.Text ?? string.Empty;

            if (seconds != terrorCountdownLastDisplayedSeconds || !currentText.Contains("(出現まで"))
            {
                string baseText = currentTerrorBaseText ?? string.Empty;
                string suffix = $"(出現まで {seconds} 秒)";
                if (!string.IsNullOrEmpty(baseText) && !char.IsWhiteSpace(baseText[baseText.Length - 1]))
                {
                    baseText += " ";
                }
                InfoPanel.TerrorValue.Text = baseText + suffix;
                terrorCountdownLastDisplayedSeconds = seconds;
            }
        }

        private void UpdateTerrorDisplay(string displayName, Color color, List<(string name, int count)> terrors)
        {
            string roundType = stateService.CurrentRound?.RoundType;

            if (roundType == "アンバウンド")
            {
                // Unbound rounds sometimes lack explicit terror names in the event data.
                // If they are missing, resolve them via the predefined lookup so the
                // information panel can display the appropriate terror details.
                if (terrors == null || terrors.Count == 0)
                {
                    var lookup = UnboundRoundDefinitions.GetTerrors(displayName);
                    if (lookup != null)
                        terrors = lookup.ToList();
                }

                if (terrors != null && terrors.Count > 0)
                {
                    string terrorText = string.Join(", ", terrors.Select(t => $"{t.name} x{t.count}"));
                    InfoPanel.TerrorValue.Text = $"{displayName} ({terrorText})";
                    InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                    var expanded = terrors.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
                    if (!string.IsNullOrEmpty(stateService.CurrentRound.TerrorKey))
                        terrorColors[stateService.CurrentRound.TerrorKey] = color;
                    UpdateTerrorInfoPanel(expanded);
                    currentTerrorBaseText = InfoPanel.TerrorValue.Text;
                    UpdateTerrorCountdownState(displayName);
                    return;
                }
            }

            if (terrors != null && terrors.Count > 0)
            {
                var expanded = terrors.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
                string joinedNames = string.Join(" & ", expanded);
                if (joinedNames != displayName)
                    InfoPanel.TerrorValue.Text = displayName + Environment.NewLine + string.Join(Environment.NewLine, expanded);
                else
                    InfoPanel.TerrorValue.Text = displayName;
                InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                if (!string.IsNullOrEmpty(joinedNames))
                    terrorColors[joinedNames] = color;
                UpdateTerrorInfoPanel(expanded);
                currentTerrorBaseText = InfoPanel.TerrorValue.Text;
                UpdateTerrorCountdownState(displayName);
            }
            else
            {
                InfoPanel.TerrorValue.Text = displayName;
                InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                UpdateTerrorInfoPanel(null);
                currentTerrorBaseText = InfoPanel.TerrorValue.Text;
                UpdateTerrorCountdownState(displayName);
            }
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

        private ItemMusicEntry FindMatchingItemMusicEntry(string text, double velocity)
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
