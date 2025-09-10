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
using ToNRoundCounter.Domain;
using ToNRoundCounter.Properties;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Application;
using MediaPlayer = System.Windows.Media.MediaPlayer;

namespace ToNRoundCounter.UI
{
    public partial class MainForm : Form, IMainView
    {
        // 上部固定UI
        private Label lblStatus;           // WebSocket接続状況
        private Label lblOSCStatus;        // OSC通信接続状況
        private Button btnToggleTopMost;   // 画面最前面固定ボタン
        private Button btnSettings;        // 設定変更ボタン

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

        private Dictionary<string, Color> terrorColors;
        private bool lastOptedIn = true;

        // 次ラウンド予測用：stateService.RoundCycle==1 → 通常ラウンド, ==2 → 「通常ラウンド or 特殊ラウンド」, >=3 → 特殊ラウンド
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

        private string version = "1.10.0";

        private readonly AutoSuicideService autoSuicideService;


        public MainForm(IWebSocketClient webSocketClient, IOSCListener oscListener, AutoSuicideService autoSuicideService, StateService stateService, IAppSettings settings, IEventLogger logger, MainPresenter presenter, IEventBus eventBus, ICancellationProvider cancellation, IInputSender inputSender, IUiDispatcher dispatcher)
        {
            InitializeSoundPlayers();
            this.Name = "MainForm";
            this.webSocketClient = webSocketClient;
            this.autoSuicideService = autoSuicideService;
            this.oscListener = oscListener;
            this.stateService = stateService;
            _settings = settings;
            _logger = logger;
            _presenter = presenter;
            _eventBus = eventBus;
            _cancellation = cancellation;
            _inputSender = inputSender;
            _dispatcher = dispatcher;
            _presenter.AttachView(this);

            terrorColors = new Dictionary<string, Color>();
            LoadTerrorInfo();
            _settings.Load();
            LoadAutoSuicideRules();
            InitializeComponents();
            this.Load += MainForm_Load;

            _eventBus.Subscribe<WebSocketConnected>(_ => _dispatcher.Invoke(() =>
            {
                lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Connected");
                lblStatus.ForeColor = Color.Green;
            }));
            _eventBus.Subscribe<WebSocketDisconnected>(_ => _dispatcher.Invoke(() =>
            {
                lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Disconnected");
                lblStatus.ForeColor = Color.Red;
            }));
            _eventBus.Subscribe<WebSocketMessageReceived>(async e => await HandleEventAsync(e.Message));
            _eventBus.Subscribe<OscMessageReceived>(e => HandleOscMessage(e.Message));
            _eventBus.Subscribe<SettingsValidationFailed>(e => _dispatcher.Invoke(() => MessageBox.Show(string.Join("\n", e.Errors), "Settings Error")));
            _ = webSocketClient.StartAsync();
            _ = oscListener.StartAsync(_settings.OSCPort);

            velocityTimer = new System.Windows.Forms.Timer();
            velocityTimer.Interval = 50;
            velocityTimer.Tick += VelocityTimer_Tick;
            velocityTimer.Start();
        }

        private void InitializeComponents()
        {
            this.Text = LanguageManager.Translate("ToNRoundCouter");
            this.Size = new Size(600, 800);
            this.MinimumSize = new Size(300, 400);
            this.BackColor = Theme.Current.Background;
            this.Resize += MainForm_Resize;

            int margin = 10;
            int currentY = margin;
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
            lblDebugInfo.ForeColor = Theme.Current.Foreground;
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
            rtbStatsDisplay.BackColor = Theme.Current.Background;
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

        private void MainForm_Resize(object sender, EventArgs e)
        {
            int margin = 10;
            int contentWidth = this.ClientSize.Width - 2 * margin;
            int currentY = margin;
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
        }

        private async void BtnSettings_Click(object sender, EventArgs e)
        {
            using (SettingsForm settingsForm = new SettingsForm(_settings))
            {
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
                    _logger.LogEvent("AutoSuicideRoundListBox", item);
                    // 修正箇所: foreach の構文エラーを修正し、AppSettings の名前空間を正しいものに修正
                    foreach (var ditem in _settings.AutoSuicideRoundTypes)
                    {
                        _logger.LogEvent("AutoSuicideRoundListBox_debug", ditem);
                    }
                    _logger.LogEvent("AutoSuicideRoundListBox", _settings.AutoSuicideRoundTypes.Contains(item).ToString());
                    settingsForm.SettingsPanel.autoSuicideRoundListBox.SetItemChecked(i, _settings.AutoSuicideRoundTypes.Contains(item) != false);
                }
                settingsForm.SettingsPanel.oscPortNumericUpDown.Value = _settings.OSCPort;

                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    _settings.OSCPort = (int)settingsForm.SettingsPanel.oscPortNumericUpDown.Value;
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
                    LoadAutoSuicideRules();

                    _settings.apikey = settingsForm.SettingsPanel.apiKeyTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(_settings.apikey))
                    {
                        _settings.apikey = string.Empty; // 空文字列に設定
                    }
                    else if (_settings.apikey.Length < 32)
                    {
                        MessageBox.Show(LanguageManager.Translate("APIキーは32文字以上である必要があります。"), LanguageManager.Translate("エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    InfoPanel.BackColor = _settings.BackgroundColor_InfoPanel;
                    rtbStatsDisplay.BackColor = _settings.BackgroundColor_Stats;
                    logPanel.RoundLogTextBox.BackColor = _settings.BackgroundColor_Log;
                    InfoPanel.TerrorValue.ForeColor = _settings.FixedTerrorColor;
                    UpdateAggregateStatsDisplay();
                    UpdateDisplayVisibility();
                    await _settings.SaveAsync();
                }
            }
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            MainForm_Resize(null, null);
            UpdateDisplayVisibility();
            await InitializeOSCRepeater();
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var json = await client.GetStringAsync("https://raw.githubusercontent.com/lovetwice1012/ToNRoundCounter/refs/heads/master/version.json");
                    var data = JObject.Parse(json);
                    var latest = data["latest"]?.ToString();
                    var url = data["url"]?.ToString();
                    if (!string.IsNullOrEmpty(latest) && !string.IsNullOrEmpty(url) && IsOlderVersion(version, latest))
                    {
                        var result = MessageBox.Show($"新しいバージョン {latest} が利用可能です。\n更新をダウンロードして適用しますか？", "アップデート", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes)
                        {
                            var zipPath = Path.Combine(Path.GetTempPath(), "ToNRoundCounter_update.zip");
                            var bytes = await client.GetByteArrayAsync(url);
                            File.WriteAllBytes(zipPath, bytes);

                            var updaterExe = Path.Combine(Directory.GetCurrentDirectory(), "Updater.exe");
                            if (File.Exists(updaterExe))
                            {
                                Process.Start(new ProcessStartInfo(updaterExe)
                                {
                                    Arguments = $"\"{zipPath}\" \"{Application.ExecutablePath}\"",
                                    UseShellExecute = false
                                });
                                Application.Exit();
                            }
                            else
                            {
                                MessageBox.Show("Updater.exe が見つかりません。", "アップデート", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore errors while checking for updates
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


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancellation.Cancel();
            webSocketClient.Stop();
            if (oscRepeaterProcess != null && !oscRepeaterProcess.HasExited)
            {
                try
                {
                    oscRepeaterProcess.Kill();
                    oscRepeaterProcess.WaitForExit();
                }
                catch { }
            }
            base.OnFormClosing(e);
        }

        private async Task HandleEventAsync(string message)
        {
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
                    stateService.UpdateCurrentRound(new Round());
                    stateService.CurrentRound.RoundType = roundType;
                    stateService.CurrentRound.IsDeath = false;
                    stateService.CurrentRound.TerrorKey = "";
                    stateService.CurrentRound.MapName = InfoPanel.MapValue.Text;
                    stateService.CurrentRound.Damage = 0;
                    if (!string.IsNullOrEmpty(InfoPanel.ItemValue.Text))
                        stateService.CurrentRound.ItemNames.Add(InfoPanel.ItemValue.Text);
                    _dispatcher.Invoke(() =>
                    {
                        InfoPanel.RoundTypeValue.Text = roundType;
                        InfoPanel.RoundTypeValue.ForeColor = ConvertColorFromInt(json.Value<int>("DisplayColor"));
                    });
                    //もしtesterNamesに含まれているかつオルタネイトなら、オルタネイトラウンド開始の音を鳴らす
                    if (testerNames.Contains(stateService.PlayerDisplayName) && roundType == "オルタネイト")
                    {
                        PlayFromStart(tester_roundStartAlternatePlayer);
                    }
                    //issetAllSelfKillModeがtrueなら13秒後に自殺入力をする
                    int autoAction = ShouldAutoSuicide(roundType, null);
                    if (issetAllSelfKillMode || autoAction == 1)
                    {
                        autoSuicideService.Schedule(TimeSpan.FromSeconds(13), true, PerformAutoSuicide);
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
                            stateService.CurrentRound.MapName = InfoPanel.MapValue.Text;
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

                    List<(string name, int count)> terrors = null;
                    var namesArray = json.Value<JArray>("Names");
                    if (namesArray != null && namesArray.Count > 0)
                    {
                        var arr = namesArray.Select(token => token.ToString()).ToList();
                        terrors = arr.Select(n => (n, 1)).ToList();
                    }

                    var roundType = InfoPanel.RoundTypeValue.Text;
                    if ((terrors == null || terrors.Count == 0) && roundType == RoundTypeExtensions.GetDisplayName(RoundType.Unbound))
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
                    if (stateService.CurrentRound != null)
                    {
                        stateService.CurrentRound.Damage += damageValue;
                        _dispatcher.Invoke(() =>
                        {
                            InfoPanel.DamageValue.Text = stateService.CurrentRound.Damage.ToString();
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
                    stateService.SetRoundCycle(1); // 確定特殊カウントをリセット
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
            catch (Exception)
            {
                _logger.LogEvent(LanguageManager.Translate("ParseError"), message);
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
                stateService.RoundMapNames[roundType] = stateService.CurrentRound.MapName ?? "";
                if (!string.IsNullOrEmpty(stateService.CurrentRound.TerrorKey))
                {
                    string terrorKey = stateService.CurrentRound.TerrorKey;
                    bool survived = lastOptedIn && !stateService.CurrentRound.IsDeath;
                    stateService.RecordRoundResult(roundType, terrorKey, survived);
                    stateService.TerrorMapNames.Set(roundType, terrorKey, stateService.CurrentRound.MapName ?? "");
                }
                else
                {
                    stateService.RecordRoundResult(roundType, null, !stateService.CurrentRound.IsDeath);
                }
                if (!string.IsNullOrEmpty(stateService.CurrentRound.MapName))
                    stateService.RoundMapNames[stateService.CurrentRound.RoundType] = stateService.CurrentRound.MapName;

                // 新規追加：特殊ラウンド出現方式のロジック
                var normalTypes = new List<string> { "クラシック", "Classic", "RUN", "走れ！" };
                var overrideSpecialTypes = new List<string> { "アンバウンド", "8ページ", "ゴースト" };
                var confirmedSpecialTypes = new List<string> { "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "狂気", "ダブルトラブル", "EX" };

                // 既存のロジックを以下のように変更
                if (normalTypes.Any(type => stateService.CurrentRound.RoundType.Contains(type)))
                {
                    // 通常ラウンドの場合
                    if (stateService.RoundCycle >= 3)
                    {
                        // 次ラウンド特殊の可能性を維持
                    }
                }
                else if (overrideSpecialTypes.Contains(stateService.CurrentRound.RoundType))
                {
                    // "アンバウンド", "8ページ", "ゴースト"の場合：通常を上書き（特殊として確定しない）
                    // stateService.RoundCycle はそのまま維持（特殊カウントはリセットしない）
                }
                else if (confirmedSpecialTypes.Contains(stateService.CurrentRound.RoundType))
                {
                    // "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "狂気", "ダブルトラブル", "EX"の場合：特殊抽選が確定
                    stateService.SetRoundCycle(1);  // 確定特殊カウントをリセット
                }
                else
                {
                    // その他の場合は特殊ラウンドとみなし、確定特殊カウントをリセット
                    stateService.SetRoundCycle(1);
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


        private void VelocityTimer_Tick(object sender, EventArgs e)
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
                        PlayFromStart(afkPlayer);
                        afkSoundPlayed = true;
                        _ = Task.Run(() => SendAlertOscMessagesAsync(0.1f));
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

            // パニッシュ・8ページ検出条件の更新
            if (stateService.CurrentRound == null)
            {
                string itemText = InfoPanel.ItemValue.Text;
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
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        private void UpdateNextRoundPrediction()
        {
            // stateService.RoundCycle == 1: 次は通常ラウンド
            // stateService.RoundCycle == 2: 「通常ラウンド or 特殊ラウンド」と表示（50/50の抽選結果によるため不明）
            // stateService.RoundCycle >= 3: 次は特殊ラウンド
            if (stateService.RoundCycle == 1)
            {
                InfoPanel.NextRoundType.Text = "通常ラウンド";
                InfoPanel.NextRoundType.ForeColor = Color.White;
            }
            else if (stateService.RoundCycle == 2)
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
            int overallTotal = stateService.RoundAggregates.Values.Sum(r => r.Total);
            foreach (var kvp in stateService.RoundAggregates)
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
                if (_settings.Filter_Terror && stateService.TerrorAggregates.TryGetRound(roundType, out var terrorDict))
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
            InfoPanel.TerrorValue.Text = "";
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
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    terrorInfoData = JObject.Parse(json);
                }
                catch
                {
                    terrorInfoData = null;
                }
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
            bool active = json["Value"] != null && json["Value"].ToObject<bool>();
            if (active)
            {
                if (stateService.CurrentRound == null)
                {
                    stateService.UpdateCurrentRound(new Round
                    {
                        RoundType = "Active Round",
                        IsDeath = false,
                        TerrorKey = "",
                        MapName = InfoPanel.MapValue.Text,
                        Damage = 0
                    });
                    if (!string.IsNullOrEmpty(InfoPanel.ItemValue.Text))
                        stateService.CurrentRound.ItemNames.Add(InfoPanel.ItemValue.Text);
                    _dispatcher.Invoke(() =>
                    {
                        InfoPanel.RoundTypeValue.Text = stateService.CurrentRound.RoundType;
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
                        int action = ShouldAutoSuicide(checkType, terror);
                        if (issetAllSelfKillMode || action == 1)
                        {
                            autoSuicideService.Schedule(TimeSpan.FromSeconds(13), true, PerformAutoSuicide);
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
            foreach (var round in AllRoundTypes)
            {
                bool enabled = _settings.AutoSuicideRoundTypes.Contains(round);
                lines.Add($"{round}::{(enabled ? 1 : 0)}");
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
        }

        private int ShouldAutoSuicide(string roundType, string terrorName)
        {
            if (!_settings.AutoSuicideEnabled) return 0;
            Func<string, string, bool> comparer;
            if (_settings.AutoSuicideFuzzyMatch)
                comparer = (a, b) => MatchWithTypoTolerance(a, b).result;
            else
                comparer = (a, b) => a == b;
            for (int i = autoSuicideRules.Count - 1; i >= 0; i--)
            {
                var r = autoSuicideRules[i];
                if (r.Matches(roundType, terrorName, comparer))
                    return r.Value;
            }
            return 0;
        }

        private Color ConvertColorFromInt(int colorInt)
        {
            int r = (colorInt >> 16) & 0xFF;
            int g = (colorInt >> 8) & 0xFF;
            int b = colorInt & 0xFF;
            return Color.FromArgb(r, g, b);
        }

        private void UpdateTerrorDisplay(string displayName, Color color, List<(string name, int count)> terrors)
        {
            string roundType = stateService.CurrentRound?.RoundType;
            if (roundType == RoundTypeExtensions.GetDisplayName(RoundType.Unbound) && terrors != null)
            {
                string terrorText = string.Join(", ", terrors.Select(t => $"{t.name} x{t.count}"));
                InfoPanel.TerrorValue.Text = $"{displayName} ({terrorText})";
                InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                var expanded = terrors.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
                if (!string.IsNullOrEmpty(stateService.CurrentRound.TerrorKey))
                {
                    terrorColors[stateService.CurrentRound.TerrorKey] = color;
                }
                UpdateTerrorInfoPanel(expanded);
            }
            else if (terrors != null && terrors.Count > 0)
            {
                var expanded = terrors.SelectMany(t => Enumerable.Repeat(t.name, t.count)).ToList();
                string joinedNames = string.Join(" & ", expanded);
                if (joinedNames != displayName)
                    InfoPanel.TerrorValue.Text = displayName + Environment.NewLine + string.Join(Environment.NewLine, expanded);
                else
                    InfoPanel.TerrorValue.Text = displayName;
                InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                if (!string.IsNullOrEmpty(joinedNames))
                {
                    terrorColors[joinedNames] = color;
                }
                UpdateTerrorInfoPanel(expanded);
            }
            else
            {
                InfoPanel.TerrorValue.Text = displayName;
                InfoPanel.TerrorValue.ForeColor = (_settings.FixedTerrorColor != Color.Empty) ? _settings.FixedTerrorColor : color;
                UpdateTerrorInfoPanel(null);
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
