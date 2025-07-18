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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rug.Osc;
using ToNRoundCounter.Models;
using ToNRoundCounter.Properties;
using ToNRoundCounter.UI;
using ToNRoundCounter.Utils;
using WMPLib;

namespace ToNRoundCounter
{
    public partial class MainForm : Form
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
        private CancellationTokenSource cancellationTokenSource;
        private ClientWebSocket webSocket;

        private string playerDisplayName = "";
        private RoundData currentRound = null;
        private Dictionary<string, RoundAggregate> roundAggregates;
        private Dictionary<string, Dictionary<string, TerrorAggregate>> terrorAggregates;
        private Dictionary<string, Color> terrorColors;
        private Dictionary<string, string> roundMapNames;
        private Dictionary<string, Dictionary<string, string>> terrorMapNames;
        private bool lastOptedIn = true;

        // 次ラウンド予測用：roundCycle==1 → 通常ラウンド, ==2 → 「通常ラウンド or 特殊ラウンド」, >=3 → 特殊ラウンド
        private int roundCycle = 0;
        private bool isNextRoundSpecial = false;
        private Random randomGenerator = new Random();

        // ラウンドログ内部リスト
        private List<Tuple<RoundData, string>> roundLogHistory;

        // OSC/Velocity 関連
        private float currentVelocity = 0;
        private DateTime velocityInRangeStart = DateTime.MinValue;
        private DateTime idleStartTime = DateTime.MinValue;
        private System.Windows.Forms.Timer velocityTimer; // Windows.Forms.Timer
        private float receivedVelocityMagnitude = 0;
        private float receivedVelocityY = 0;

        private bool afkSoundPlayed = false;
        private bool punishSoundPlayed = false;

        private ClientWebSocket instanceWsConnection = null;

        private Process oscRepeaterProcess = null;

        private static readonly SemaphoreSlim sendAlertSemaphore = new SemaphoreSlim(1, 1);

        private int connected = 0;

        private bool isNotifyActivated = false;

        private static readonly string[] testerNames = new string[] { "yussy5373", "Kotetsu Wilde", "tofu_shoyu", "ちよ千夜", "Blackpit", "shari_1928", "MitarashiMochi" };

        private bool isRestarted = false;



        // P/Invoke 宣言
        [StructLayout(LayoutKind.Sequential)]
        public struct Win32Point
        {
            public Int32 X;
            public Int32 Y;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public short wVk;
            public short wScan;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public int uMsg;
            public short wParamL;
            public short wParamH;
        };

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT_UNION
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public INPUT_UNION ui;
        };
        internal static unsafe partial class NativeMethods
        {
            [DllImport("ton-self-kill", EntryPoint = "press_keys", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern void press_keys();
        }


        private WindowsMediaPlayer notifyPlayer = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/notify.mp3"
        };

        private WindowsMediaPlayer afkPlayer = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/afk70.mp3"
        };

        private WindowsMediaPlayer punishPlayer = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/punish_8page.mp3"
        };

        private WindowsMediaPlayer tester_roundStartAlternatePlayer = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/testerOnly/RoundStart/alternate.mp3"
        };

        private WindowsMediaPlayer tester_IDICIDEDKILLALLPlayer = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/testerOnly/RoundStart/IDICIDEDKILLALL.mp3"
        };

        private WindowsMediaPlayer tester_BATOU_01Player = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/testerOnly/Batou/Batou-01.mp3"
        };

        private WindowsMediaPlayer tester_BATOU_02Player = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/testerOnly/Batou/Batou-02.mp3"
        };

        private WindowsMediaPlayer tester_BATOU_03Player = new WMPLib.WindowsMediaPlayer
        {
            URL = "./audio/testerOnly/Batou/Batou-03.mp3"
        };

        public MainForm()
        {
            notifyPlayer.controls.stop();
            afkPlayer.controls.stop();
            punishPlayer.controls.stop();
            tester_roundStartAlternatePlayer.controls.stop();
            tester_IDICIDEDKILLALLPlayer.controls.stop();
            tester_BATOU_01Player.controls.stop();
            tester_BATOU_02Player.controls.stop();
            tester_BATOU_03Player.controls.stop();

            this.Name = "MainForm";
            roundAggregates = new Dictionary<string, RoundAggregate>();
            terrorAggregates = new Dictionary<string, Dictionary<string, TerrorAggregate>>();
            terrorColors = new Dictionary<string, Color>();
            roundMapNames = new Dictionary<string, string>();
            terrorMapNames = new Dictionary<string, Dictionary<string, string>>();
            roundLogHistory = new List<Tuple<RoundData, string>>();
            LoadTerrorInfo();
            AppSettings.Load();
            InitializeComponents();
            this.Load += MainForm_Load;
            cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => ConnectWebSocketAsync(cancellationTokenSource.Token));
            Task.Run(() => StartOSCListenerAsync(cancellationTokenSource.Token));

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
            this.BackColor = Color.LightGray;
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
            InfoPanel.BackColor = AppSettings.BackgroundColor_InfoPanel;
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
            splitContainerMain.Panel1.Controls.Add(lblStatsTitle);

            rtbStatsDisplay = new RichTextBox();
            rtbStatsDisplay.ReadOnly = true;
            rtbStatsDisplay.BorderStyle = BorderStyle.FixedSingle;
            rtbStatsDisplay.Font = new Font("Arial", 10);
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

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using (SettingsForm settingsForm = new SettingsForm())
            {
                settingsForm.SettingsPanel.ShowStatsCheckBox.Checked = AppSettings.ShowStats;
                settingsForm.SettingsPanel.DebugInfoCheckBox.Checked = AppSettings.ShowDebug;
                settingsForm.SettingsPanel.ToggleRoundLogCheckBox.Checked = AppSettings.ShowRoundLog;
                settingsForm.SettingsPanel.RoundTypeCheckBox.Checked = AppSettings.Filter_RoundType;
                settingsForm.SettingsPanel.TerrorCheckBox.Checked = AppSettings.Filter_Terror;
                settingsForm.SettingsPanel.AppearanceCountCheckBox.Checked = AppSettings.Filter_Appearance;
                settingsForm.SettingsPanel.SurvivalCountCheckBox.Checked = AppSettings.Filter_Survival;
                settingsForm.SettingsPanel.DeathCountCheckBox.Checked = AppSettings.Filter_Death;
                settingsForm.SettingsPanel.SurvivalRateCheckBox.Checked = AppSettings.Filter_SurvivalRate;
                settingsForm.SettingsPanel.InfoPanelBgLabel.BackColor = AppSettings.BackgroundColor_InfoPanel;
                settingsForm.SettingsPanel.StatsBgLabel.BackColor = AppSettings.BackgroundColor_Stats;
                settingsForm.SettingsPanel.LogBgLabel.BackColor = AppSettings.BackgroundColor_Log;
                settingsForm.SettingsPanel.FixedTerrorColorLabel.BackColor = AppSettings.FixedTerrorColor;
                for (int i = 0; i < settingsForm.SettingsPanel.RoundTypeStatsListBox.Items.Count; i++)
                {
                    string item = settingsForm.SettingsPanel.RoundTypeStatsListBox.Items[i].ToString();
                    settingsForm.SettingsPanel.RoundTypeStatsListBox.SetItemChecked(i, AppSettings.RoundTypeStats.Contains(item));
                }
                settingsForm.SettingsPanel.autoSuicideCheckBox.Checked = AppSettings.AutoSuicideEnabled;
                for (int i = 0; i < settingsForm.SettingsPanel.autoSuicideRoundListBox.Items.Count; i++)
                {
                    string item = settingsForm.SettingsPanel.autoSuicideRoundListBox.Items[i].ToString();
                    EventLogger.LogEvent("AutoSuicideRoundListBox", item);
                    // 修正箇所: foreach の構文エラーを修正し、AppSettings の名前空間を正しいものに修正
                    foreach (var ditem in AppSettings.AutoSuicideRoundTypes)
                    {
                        EventLogger.LogEvent("AutoSuicideRoundListBox_debug", ditem);
                    }
                    EventLogger.LogEvent("AutoSuicideRoundListBox", AppSettings.AutoSuicideRoundTypes.Contains(item).ToString());
                    settingsForm.SettingsPanel.autoSuicideRoundListBox.SetItemChecked(i, AppSettings.AutoSuicideRoundTypes.Contains(item) != false);
                }
                settingsForm.SettingsPanel.oscPortNumericUpDown.Value = AppSettings.OSCPort;

                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    AppSettings.OSCPort = (int)settingsForm.SettingsPanel.oscPortNumericUpDown.Value;
                    AppSettings.ShowStats = settingsForm.SettingsPanel.ShowStatsCheckBox.Checked;
                    AppSettings.ShowDebug = settingsForm.SettingsPanel.DebugInfoCheckBox.Checked;
                    AppSettings.ShowRoundLog = settingsForm.SettingsPanel.ToggleRoundLogCheckBox.Checked;
                    AppSettings.Filter_RoundType = settingsForm.SettingsPanel.RoundTypeCheckBox.Checked;
                    AppSettings.Filter_Terror = settingsForm.SettingsPanel.TerrorCheckBox.Checked;
                    AppSettings.Filter_Appearance = settingsForm.SettingsPanel.AppearanceCountCheckBox.Checked;
                    AppSettings.Filter_Survival = settingsForm.SettingsPanel.SurvivalCountCheckBox.Checked;
                    AppSettings.Filter_Death = settingsForm.SettingsPanel.DeathCountCheckBox.Checked;
                    AppSettings.Filter_SurvivalRate = settingsForm.SettingsPanel.SurvivalRateCheckBox.Checked;
                    AppSettings.BackgroundColor_InfoPanel = settingsForm.SettingsPanel.InfoPanelBgLabel.BackColor;
                    AppSettings.BackgroundColor_Stats = settingsForm.SettingsPanel.StatsBgLabel.BackColor;
                    AppSettings.BackgroundColor_Log = settingsForm.SettingsPanel.LogBgLabel.BackColor;
                    AppSettings.FixedTerrorColor = settingsForm.SettingsPanel.FixedTerrorColorLabel.BackColor;
                    AppSettings.RoundTypeStats.Clear();
                    foreach (object item in settingsForm.SettingsPanel.RoundTypeStatsListBox.CheckedItems)
                    {
                        AppSettings.RoundTypeStats.Add(item.ToString());
                    }
                    AppSettings.AutoSuicideEnabled = settingsForm.SettingsPanel.autoSuicideCheckBox.Checked;
                    AppSettings.AutoSuicideRoundTypes.Clear();
                    foreach (object item in settingsForm.SettingsPanel.autoSuicideRoundListBox.CheckedItems)
                    {
                        AppSettings.AutoSuicideRoundTypes.Add(item.ToString());
                    }

                    AppSettings.apikey = settingsForm.SettingsPanel.apiKeyTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(AppSettings.apikey))
                    {
                        AppSettings.apikey = string.Empty; // 空文字列に設定
                    }
                    else if (AppSettings.apikey.Length < 32)
                    {
                        MessageBox.Show(LanguageManager.Translate("APIキーは32文字以上である必要があります。"), LanguageManager.Translate("エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    InfoPanel.BackColor = AppSettings.BackgroundColor_InfoPanel;
                    rtbStatsDisplay.BackColor = AppSettings.BackgroundColor_Stats;
                    logPanel.RoundLogTextBox.BackColor = AppSettings.BackgroundColor_Log;
                    InfoPanel.TerrorValue.ForeColor = AppSettings.FixedTerrorColor;
                    UpdateAggregateStatsDisplay();
                    UpdateDisplayVisibility();
                    AppSettings.Save();
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            MainForm_Resize(null, null);
            UpdateDisplayVisibility();
            InitializeOSCRepeater();
        }

        private void InitializeOSCRepeater()
        {
            // OSCRepeater.exeが存在し、接続先ポート設定が一度も変更されていない場合のみ実行
            if (File.Exists("./OscRepeater.exe"))
            {
                if (!AppSettings.OSCPortChanged)
                {
                    int port = 30000;
                    bool portFound = false;
                    while (!portFound)
                    {
                        try
                        {
                            // 指定ポートが利用可能か確認するため、TcpListenerを起動してすぐ停止する
                            TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                            listener.Start();
                            listener.Stop();
                            portFound = true;
                        }
                        catch (SocketException)
                        {
                            port++; // 利用中の場合、ポート番号を1増やして再試行
                        }
                    }
                    // 利用可能なポートをAppSettingsに保存し、自動設定済みとマークする
                    AppSettings.OSCPort = port;
                    AppSettings.OSCPortChanged = true;
                    AppSettings.Save();
                }

                // OSCRepeater.exeを --autostart --autoconfig 127.0.0.1:(設定ポート) の引数で起動する
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "./OscRepeater.exe";
                psi.Arguments = $"--autostart --autoconfig 127.0.0.1:{AppSettings.OSCPort} --minimized";
                psi.UseShellExecute = false;
                oscRepeaterProcess = Process.Start(psi);
                // OSCRepeater.exeが起動している場合、終了時に一緒に終了するように設定
                if (oscRepeaterProcess != null)
                {
                    oscRepeaterProcess.EnableRaisingEvents = true;
                    oscRepeaterProcess.Exited += (s, ev) =>
                    {
                        this.Invoke(new Action(() =>
                        {
                            this.Close();
                        }));
                    };
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancellationTokenSource.Cancel();
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

        private async Task ConnectWebSocketAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                webSocket = new ClientWebSocket();
                try
                {
                    await webSocket.ConnectAsync(new Uri("ws://127.0.0.1:11398"), token);
                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Connected");
                        lblStatus.ForeColor = Color.Green;
                    }));
                    await ReceiveMessagesAsync(webSocket, token);
                }
                catch (Exception)
                {
                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = "WebSocket: " + LanguageManager.Translate("Disconnected");
                        lblStatus.ForeColor = Color.Red;
                    }));
                    await Task.Delay(300, token);
                }
            }
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[8192];
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var segment = new ArraySegment<byte>(buffer);
                WebSocketReceiveResult result = null;
                try
                {
                    result = await ws.ReceiveAsync(segment, token);
                }
                catch (Exception)
                {
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                    break;
                }
                else
                {
                    var messageBytes = new List<byte>();
                    messageBytes.AddRange(buffer.Take(result.Count));
                    while (!result.EndOfMessage)
                    {
                        result = await ws.ReceiveAsync(segment, token);
                        messageBytes.AddRange(buffer.Take(result.Count));
                    }
                    var message = Encoding.UTF8.GetString(messageBytes.ToArray());
                    HandleEventAsync(message);
                }
            }
        }

        private async Task HandleEventAsync(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                string eventType = json.Value<string>("Type") ?? json.Value<string>("TYPE") ?? "Unknown";
                EventLogger.LogEvent(eventType, message);
                int command = -1;
                if (json.TryGetValue("Command", out JToken commandToken))
                {
                    command = commandToken.Value<int>();
                }
                if (eventType == "CONNECTED")
                {
                    playerDisplayName = json.Value<string>("DisplayName") ?? "";
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
                    currentRound = new RoundData();
                    currentRound.RoundType = roundType;
                    currentRound.IsDeath = false;
                    currentRound.TerrorKey = "";
                    currentRound.MapName = InfoPanel.MapValue.Text;
                    currentRound.Damage = 0;
                    if (!string.IsNullOrEmpty(InfoPanel.ItemValue.Text))
                        currentRound.ItemNames.Add(InfoPanel.ItemValue.Text);
                    this.Invoke(new Action(() =>
                    {
                        InfoPanel.RoundTypeValue.Text = roundType;
                        InfoPanel.RoundTypeValue.ForeColor = ConvertColorFromInt(json.Value<int>("DisplayColor"));
                    }));
                    //もしtesterNamesに含まれているかつオルタネイトなら、オルタネイトラウンド開始の音を鳴らす
                    if (testerNames.Contains(playerDisplayName) && roundType == "オルタネイト")
                    {
                        tester_roundStartAlternatePlayer.controls.play();
                    }
                }
                else if (eventType == "TRACKER")
                {
                    string trackerEvent = json.Value<string>("event") ?? "";
                    if (trackerEvent == "round_start")
                    {
                        if (lastOptedIn != false)
                        {
                            currentRound = new RoundData();
                            currentRound.RoundType = "";
                            currentRound.IsDeath = false;
                            currentRound.TerrorKey = "";
                            currentRound.MapName = InfoPanel.MapValue.Text;
                            currentRound.Damage = 0;
                        }
                    }
                    else if (trackerEvent == "round_won")
                    {
                        if (currentRound != null)
                        {
                            FinalizeCurrentRound(currentRound.IsDeath ? "☠" : "✅");
                        }
                    }
                    else if (trackerEvent == "round_lost")
                    {
                        if (currentRound != null && !currentRound.IsDeath)
                        {
                            currentRound.IsDeath = true;
                            FinalizeCurrentRound("☠");
                        }
                    }
                }
                else if (eventType == "LOCATION" && command == 1)
                {
                    this.Invoke(new Action(() =>
                    {
                        InfoPanel.MapValue.Text = json.Value<string>("Name") ?? "";
                    }));
                    if (currentRound != null)
                    {
                        currentRound.MapName = json.Value<string>("Name") ?? "";
                    }
                }
                else if (eventType == "TERRORS" && (command == 0 || command == 1))
                {
                    this.Invoke(new Action(() => { UpdateTerrorDisplay(json); }));
                    if (currentRound != null)
                    {
                        var names = json.Value<JArray>("Names")?.Select(token => token.ToString()).ToList();
                        if (names != null && names.Count > 0)
                        {
                            string joinedNames = string.Join(" & ", names);
                            currentRound.TerrorKey = joinedNames;
                        }

                        var roundType = InfoPanel.RoundTypeValue.Text;
                        //もしroundTypeが自動自殺ラウンド対象なら自動自殺
                        if (AppSettings.AutoSuicideEnabled && AppSettings.AutoSuicideRoundTypes.Contains(roundType))
                        {

                            Task.Run(() => PerformAutoSuicide());

                        }
                    }
                }
                else if (eventType == "ITEM")
                {
                    if (command == 1)
                        this.Invoke(new Action(() => { UpdateItemDisplay(json); }));
                    else if (command == 0)
                        this.Invoke(new Action(() => { ClearItemDisplay(); }));
                }
                else if (eventType == "DAMAGED")
                {
                    int damageValue = json.Value<int>("Value");
                    if (currentRound != null)
                    {
                        currentRound.Damage += damageValue;
                        this.Invoke(new Action(() =>
                        {
                            InfoPanel.DamageValue.Text = currentRound.Damage.ToString();
                        }));
                    }
                }
                else if (eventType == "DEATH")
                {
                    string deathName = json.Value<string>("Name") ?? "";
                    bool isLocal = json.Value<bool?>("IsLocal") ?? false;
                    if (currentRound != null && (deathName == playerDisplayName || isLocal))
                    {
                        currentRound.IsDeath = true;
                        FinalizeCurrentRound("☠");
                        if (playerDisplayName == "Kotetsu Wilde")
                        //if (playerDisplayName == "yussy5373")
                        {
                            int randomNum = randomGenerator.Next(1, 4);
                            if (randomNum == 1)
                            {
                                tester_BATOU_01Player.controls.play();
                            }
                            else if (randomNum == 2)
                            {
                                tester_BATOU_02Player.controls.play();
                            }
                            else if (randomNum == 3)
                            {
                                tester_BATOU_03Player.controls.play();
                            }
                        }
                    }
                }
                else if (eventType == "OPTED_IN")
                {
                    if (!isNotifyActivated)
                    {
                        Task.Run(() => SendAlertOscMessagesAsync(0.9f, false));
                        isNotifyActivated = true;
                    }
                    bool optedIn = json.Value<bool?>("Value") ?? true;
                    lastOptedIn = optedIn;
                    if (currentRound != null && optedIn == false)
                        currentRound.IsDeath = true;
                }
                else if (eventType == "INSTANCE")
                {
                    // "INSTANCE" タイプの接続を受けたら、メッセージ内の "Value" フィールドを使ってインスタンス接続を開始する
                    string instanceValue = json.Value<string>("Value") ?? "";
                    if (!string.IsNullOrEmpty(instanceValue))
                    {
                        Task.Run(() => ConnectToInstance(instanceValue));
                        isNotifyActivated = false; ;
                    }
                }
                else if (eventType == "MASTER_CHANGE")
                {
                    isNextRoundSpecial = true;
                    roundCycle = 1; // 確定特殊カウントをリセット
                    this.Invoke(new Action(() =>
                    {
                        UpdateNextRoundPrediction();
                    }));

                }
                else if (eventType == "IS_SABOTEUR")
                {
                    bool isSaboteur = json.Value<bool?>("Value") ?? false;
                    //もしtesterNamesに含まれている場合、サボタージュの音を鳴らす
                    if (currentRound != null && isSaboteur)
                    {
                        if (testerNames.Contains(playerDisplayName) && !punishSoundPlayed)
                        {
                            tester_IDICIDEDKILLALLPlayer.controls.play();
                            punishSoundPlayed = true;
                        }
                    }
                }
                else if (eventType == "SAVED")
                {
                    string savecode = json.Value<string>("Value") ?? String.Empty;
                    if (savecode != String.Empty && AppSettings.apikey != String.Empty)
                    {
                        // https://toncloud.sprink.cloud/api/savecode/create/{apikey} にPOSTリクエストを送信(savecodeを送信)
                        using (var client = new HttpClient())
                        {
                            client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/savecode/create/" + AppSettings.apikey);
                            var content = new StringContent("{\"savecode\":\"" + savecode + "\"}", Encoding.UTF8, "application/json");
                            try
                            {
                                var response = await client.PostAsync("", content);
                                if (response.IsSuccessStatusCode)
                                {
                                    EventLogger.LogEvent("SaveCode", "Save code sent successfully.");
                                }
                                else
                                {
                                    EventLogger.LogEvent("SaveCodeError", $"Failed to send save code: {response.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                EventLogger.LogEvent("SaveCodeError", $"Exception occurred: {ex.Message}");
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
                            this.Invoke(new Action(() =>
                            {
                                if (currentRound != null)
                                {
                                    currentRound.InstancePlayersCount = playerCount;

                                    this.Invoke(new Action(async () =>
                                    {
                                        await SendPieSizeOscMessagesAsync(playerCount);
                                    }));
                                }

                            }));
                            break;
                        default:
                            EventLogger.LogEvent("CustomEvent", $"Unknown custom event: {customEvent}");
                            break;
                    }

                }
            }
            catch (Exception)
            {
                EventLogger.LogEvent(LanguageManager.Translate("ParseError"), message);
            }
        }

        /// <summary>
        /// 共通のラウンド終了処理
        /// </summary>
        /// <param name="status">"☠" または "✅"</param>
        private void FinalizeCurrentRound(string status)
        {
            if (currentRound != null)
            {
                string roundType = currentRound.RoundType;
                roundMapNames[roundType] = currentRound.MapName ?? "";
                if (!roundAggregates.ContainsKey(roundType))
                    roundAggregates[roundType] = new RoundAggregate();
                roundAggregates[roundType].Total++;
                if (currentRound.IsDeath)
                    roundAggregates[roundType].Death++;
                else
                    roundAggregates[roundType].Survival++;
                if (!string.IsNullOrEmpty(currentRound.TerrorKey))
                {
                    if (!terrorAggregates.ContainsKey(roundType))
                        terrorAggregates[roundType] = new Dictionary<string, TerrorAggregate>();
                    var terrorDict = terrorAggregates[roundType];
                    string terrorKey = currentRound.TerrorKey;
                    if (!terrorDict.ContainsKey(terrorKey))
                        terrorDict[terrorKey] = new TerrorAggregate();
                    if (lastOptedIn)
                    {
                        if (currentRound.IsDeath)
                            terrorDict[terrorKey].Death++;
                        else
                            terrorDict[terrorKey].Survival++;
                    }
                    else
                    {
                        terrorDict[terrorKey].Death++;
                    }
                    terrorDict[terrorKey].Total++;
                    if (!terrorMapNames.ContainsKey(roundType))
                        terrorMapNames[roundType] = new Dictionary<string, string>();
                    terrorMapNames[roundType][terrorKey] = currentRound.MapName ?? "";
                }
                if (!string.IsNullOrEmpty(currentRound.MapName))
                    roundMapNames[currentRound.RoundType] = currentRound.MapName;

                // 新規追加：特殊ラウンド出現方式のロジック
                var normalTypes = new List<string> { "クラシック", "Classic", "RUN", "走れ" };
                var overrideSpecialTypes = new List<string> { "アンバウンド", "8ページ", "ゴースト" };
                var confirmedSpecialTypes = new List<string> { "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "狂気", "ダブル・トラブル", "EX" };

                // 既存のロジックを以下のように変更
                if (normalTypes.Any(type => currentRound.RoundType.Contains(type)))
                {
                    // 通常ラウンドの場合
                    if (roundCycle == 1)
                        isNextRoundSpecial = false;
                    else if (roundCycle == 2)
                        isNextRoundSpecial = false; // 表示は「通常ラウンド or 特殊ラウンド」
                    else
                        isNextRoundSpecial = true;
                }
                else if (overrideSpecialTypes.Contains(currentRound.RoundType))
                {
                    // "アンバウンド", "8ページ", "ゴースト"の場合：通常を上書き（特殊として確定しない）
                    isNextRoundSpecial = false;
                    // roundCycle はそのまま維持（特殊カウントはリセットしない）
                }
                else if (confirmedSpecialTypes.Contains(currentRound.RoundType))
                {
                    // "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "狂気", "ダブル・トラブル", "EX"の場合：特殊抽選が確定
                    isNextRoundSpecial = true;
                    roundCycle = 1;  // 確定特殊カウントをリセット
                }
                else
                {
                    // その他の場合は特殊ラウンドとみなし、確定特殊カウントをリセット
                    isNextRoundSpecial = true;
                    roundCycle = 1;
                }

                this.Invoke(new Action(() =>
                {
                    UpdateNextRoundPrediction();
                    UpdateAggregateStatsDisplay();
                    AppendRoundLog(currentRound, status);
                    ClearEventDisplays();
                    ClearItemDisplay();
                    uoloadRoundLog(currentRound, status);
                    lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}";
                }));
                currentRound = null;
            }
        }

        private void uoloadRoundLog(RoundData round, string status)
        {
            // ラウンドログをアップロードする処理を実装
            /**
 * POST /api/roundlogs/create/apikey
 * body = {
 *   roundType, terror1, terror2, terror3,
 *   map, item, damage, isAlive, instanceSize, timestamp
 * }
 */
            if (string.IsNullOrEmpty(AppSettings.apikey))
            {
                EventLogger.LogEvent("RoundLogUpload", "APIキーが設定されていません。アップロードをスキップします。");
                return;
            }

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/roundlogs/create/" + AppSettings.apikey);
                var payload = new
                {
                    roundType = round.RoundType,
                    terror1 = round.TerrorKey.Split('&').ElementAtOrDefault(0)?.Trim(),
                    terror2 = round.TerrorKey.Split('&').ElementAtOrDefault(1)?.Trim(),
                    terror3 = round.TerrorKey.Split('&').ElementAtOrDefault(2)?.Trim(),
                    map = round.MapName,
                    item = round.ItemNames.Count > 0 ? string.Join("、", round.ItemNames) : LanguageManager.Translate("アイテム未使用"),
                    damage = round.Damage,
                    isAlive = !round.IsDeath,
                    instanceSize = round.InstancePlayersCount
                };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                try
                {
                    var response = client.PostAsync("", content).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        EventLogger.LogEvent("RoundLogUpload", "ラウンドログのアップロードに成功しました。");
                    }
                    else
                    {
                        EventLogger.LogEvent("RoundLogUploadError", $"ラウンドログのアップロードに失敗しました: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    EventLogger.LogEvent("RoundLogUploadError", $"ラウンドログのアップロード中にエラーが発生しました: {ex.Message}");
                }
            }


            EventLogger.LogEvent("RoundLogUpload", $"Round: {round.RoundType}, Status: {status}, Map: {round.MapName}");
        }

        private async Task StartOSCListenerAsync(CancellationToken token)
        {
            // OSC受信ポートを9001に設定し、Rug.OscのOscReceiverを利用する
            var receiver = new OscReceiver(AppSettings.OSCPort);
            try
            {
                receiver.Connect();
                this.Invoke(new Action(() =>
                {
                    lblOSCStatus.Text = "OSC: " + LanguageManager.Translate("Connected");
                    lblOSCStatus.ForeColor = Color.Green;
                }));
                while (!token.IsCancellationRequested)
                {

                    OscPacket packet = receiver.Receive();
                    if (packet != null)
                    {
                        if (packet is OscMessage message)
                        {
                            /*
                            if (message.Address == "/avatar/parameters/VelocityMagnitude")
                            {
                                try
                                {
                                    receivedVelocityMagnitude = float.Parse(message.ToString().Split(',')[1].Split('f')[0]);
                                }
                                catch { }
                            }
                            else if (message.Address == "/avatar/parameters/VelocityY")
                            {
                                try
                                {
                                    receivedVelocityY = float.Parse(message.ToString().Split(',')[1].Split('f')[0]);
                                }
                                catch { }
                            }
                            else if (message.Address == "/avatar/parameters/suside")
                            {
                                // 自殺用パラメーター: trueなら自殺処理を即実行
                                bool suicideFlag = false;
                                if (message.Arguments.Count > 0)
                                {
                                    try { suicideFlag = Convert.ToBoolean(message.Arguments[0]); } catch { }
                                }
                                if (suicideFlag)
                                {
                                    // ラウンド中でなくても、即自殺（自殺キー操作）
                                    Task.Run(() => PerformAutoSuicide());
                                }
                            }
                            else if (message.Address == "/avatar/parameters/autosuside")
                            {
                                // 自動自殺モード切替：値があればその真偽で上書き、なければ設定画面の値を使用
                                bool autoSuicideOSC = false;
                                if (message.Arguments.Count > 0)
                                {
                                    try { autoSuicideOSC = Convert.ToBoolean(message.Arguments[0]); } catch { }
                                    AppSettings.AutoSuicideEnabled = autoSuicideOSC;
                                }
                            }
                            currentVelocity = Math.Abs(receivedVelocityMagnitude);// - Math.Abs(receivedVelocityY);
                            EventLogger.LogEvent("Receive: ", $"{message.Address} => Computed Velocity: {currentVelocity:F2}");
                            this.Invoke(new Action(() =>
                            {
                                lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}";
                            }));*/
                            HandleOscMessage(message);
                        }
                    }
                    else
                    {
                        await Task.Delay(10, token);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    lblOSCStatus.Text = "OSC: " + LanguageManager.Translate("Disconnected");
                    lblOSCStatus.ForeColor = Color.Red;
                }));
                await Task.Delay(300, token);

                EventLogger.LogEvent("Error: ", ex.ToString());
            }
            finally
            {
                receiver.Close();
            }
        }

        private void VelocityTimer_Tick(object sender, EventArgs e)
        {
            // 無操作判定：VelocityMagnitudeの絶対値が1未満の場合、最低1秒連続してidleと判定する
            if (currentRound != null && currentVelocity < 1)
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
                        afkPlayer.controls.play();
                        afkSoundPlayed = true;
                        Task.Run(() => SendAlertOscMessagesAsync(0.1f));
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
            if (currentRound == null)
            {
                string itemText = InfoPanel.ItemValue.Text;
                bool hasCoil = itemText.IndexOf("Coil", StringComparison.OrdinalIgnoreCase) >= 0;
                // 既存条件：6～7の範囲
                bool condition1 = hasCoil && (currentVelocity >= 6 && currentVelocity < 7);
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
                            punishPlayer.controls.play();
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
            // roundCycle == 1: 次は通常ラウンド
            // roundCycle == 2: 「通常ラウンド or 特殊ラウンド」と表示（50/50の抽選結果によるため不明）
            // roundCycle >= 3: 次は特殊ラウンド
            if (roundCycle == 1)
            {
                InfoPanel.NextRoundType.Text = "通常ラウンド";
                InfoPanel.NextRoundType.ForeColor = Color.White;
            }
            else if (roundCycle == 2)
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
            int overallTotal = roundAggregates.Values.Sum(r => r.Total);
            foreach (var kvp in roundAggregates)
            {
                string roundType = kvp.Key;
                // ラウンドタイプごとのフィルターが有効なら対象のラウンドタイプのみ表示
                if (AppSettings.RoundTypeStats != null && AppSettings.RoundTypeStats.Count > 0 && !AppSettings.RoundTypeStats.Contains(roundType))
                    continue;

                RoundAggregate agg = kvp.Value;
                var parts = new List<string>();
                parts.Add(roundType);
                if (AppSettings.Filter_Appearance)
                    parts.Add(LanguageManager.Translate("出現回数") + "=" + agg.Total);
                if (AppSettings.Filter_Survival)
                    parts.Add(LanguageManager.Translate("生存回数") + "=" + agg.Survival);
                if (AppSettings.Filter_Death)
                    parts.Add(LanguageManager.Translate("死亡回数") + "=" + agg.Death);
                if (AppSettings.Filter_SurvivalRate)
                    parts.Add(string.Format(LanguageManager.Translate("生存率") + "={0:F1}%", agg.SurvivalRate));
                if (overallTotal > 0 && AppSettings.Filter_Appearance)
                {
                    double occurrenceRate = agg.Total * 100.0 / overallTotal;
                    parts.Add(string.Format(LanguageManager.Translate("出現率") + "={0:F1}%", occurrenceRate));
                }
                string roundLine = string.Join(" ", parts);
                AppendLine(rtbStatsDisplay, roundLine, Color.Black);

                // テラーのフィルター
                if (AppSettings.Filter_Terror && terrorAggregates.ContainsKey(roundType))
                {
                    var terrorDict = terrorAggregates[roundType];
                    foreach (var terrorKvp in terrorDict)
                    {
                        string terrorKey = terrorKvp.Key;
                        TerrorAggregate tAgg = terrorKvp.Value;
                        var terrorParts = new List<string>();
                        terrorParts.Add(terrorKey);
                        if (AppSettings.Filter_Appearance)
                            terrorParts.Add(LanguageManager.Translate("出現回数") + "=" + tAgg.Total);
                        if (AppSettings.Filter_Survival)
                            terrorParts.Add(LanguageManager.Translate("生存回数") + "=" + tAgg.Survival);
                        if (AppSettings.Filter_Death)
                            terrorParts.Add(LanguageManager.Translate("死亡回数") + "=" + tAgg.Death);
                        if (AppSettings.Filter_SurvivalRate)
                            terrorParts.Add(string.Format(LanguageManager.Translate("生存率") + "={0:F1}%", tAgg.SurvivalRate));
                        string terrorLine = string.Join(" ", terrorParts);
                        Color rawColor = terrorColors.ContainsKey(terrorKey) ? terrorColors[terrorKey] : Color.Black;
                        Color terrorColor = (AppSettings.FixedTerrorColor != Color.Empty && AppSettings.FixedTerrorColor != Color.White)
                            ? AppSettings.FixedTerrorColor
                            : AdjustColorForVisibility(rawColor);
                        AppendIndentedLine(rtbStatsDisplay, terrorLine, terrorColor);
                    }
                }
                AppendLine(rtbStatsDisplay, "", Color.Black);
            }
            if (AppSettings.ShowDebug)
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
            lblStatsTitle.Visible = AppSettings.ShowStats;
            rtbStatsDisplay.Visible = AppSettings.ShowStats;
            lblRoundLogTitle.Visible = AppSettings.ShowRoundLog;
            logPanel.RoundLogTextBox.Visible = AppSettings.ShowRoundLog;
        }

        private void AppendRoundLog(RoundData round, string status)
        {
            string items = round.ItemNames.Count > 0 ? string.Join("、", round.ItemNames) : LanguageManager.Translate("アイテム未使用");
            string logEntry = string.Format("ラウンドタイプ: {0}, テラー: {1}, MAP: {2}, アイテム: {3}, ダメージ: {4}, 生死: {5}",
                round.RoundType, round.TerrorKey, round.MapName, items, round.Damage, status);
            roundLogHistory.Add(new Tuple<RoundData, string>(round, logEntry));
            UpdateRoundLogDisplay();
        }

        private void UpdateRoundLogDisplay()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateRoundLogDisplay));
                return;
            }
            logPanel.RoundLogTextBox.Clear();
            foreach (var entry in roundLogHistory)
            {
                logPanel.RoundLogTextBox.AppendText(entry.Item2 + Environment.NewLine);
            }
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
            if (currentRound != null)
            {
                if (!currentRound.ItemNames.Contains(itemName))
                    currentRound.ItemNames.Add(itemName);
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

            terrorInfoPanel.UpdateInfo(names, terrorInfoData);
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
                if (currentRound == null)
                {
                    currentRound = new RoundData
                    {
                        RoundType = "Active Round",
                        IsDeath = false,
                        TerrorKey = "",
                        MapName = InfoPanel.MapValue.Text,
                        Damage = 0
                    };
                    if (!string.IsNullOrEmpty(InfoPanel.ItemValue.Text))
                        currentRound.ItemNames.Add(InfoPanel.ItemValue.Text);
                    this.Invoke(new Action(() =>
                    {
                        InfoPanel.RoundTypeValue.Text = currentRound.RoundType;
                        InfoPanel.RoundTypeValue.ForeColor = Color.White;
                        InfoPanel.DamageValue.Text = "0";
                    }));
                }
            }
            else
            {
                if (currentRound != null)
                {
                    FinalizeCurrentRound(currentRound.IsDeath ? "☠" : "✅");
                }
            }

            if (currentRound != null && AppSettings.AutoSuicideRoundTypes != null &&
                AppSettings.AutoSuicideRoundTypes.Contains(currentRound.RoundType))
            {
                // 自動自殺モードを起動（非同期で実行）
                Task.Run(() => PerformAutoSuicide());
            }
        }

        private void PerformAutoSuicide()
        {

            EventLogger.LogEvent("Suicide", "Performing Suside");
            LaunchSuicideInputIfExists();
            EventLogger.LogEvent("Suicide", "finish");
        }

        private Color ConvertColorFromInt(int colorInt)
        {
            int r = (colorInt >> 16) & 0xFF;
            int g = (colorInt >> 8) & 0xFF;
            int b = colorInt & 0xFF;
            return Color.FromArgb(r, g, b);
        }

        private void UpdateTerrorDisplay(JObject json)
        {
            string displayName = json.Value<string>("DisplayName") ?? "";
            int displayColorInt = json.Value<int>("DisplayColor");
            Color color = ConvertColorFromInt(displayColorInt);
            JArray namesArray = json.Value<JArray>("Names");
            if (namesArray != null)
            {
                var names = namesArray.Select(token => token.ToString()).ToList();
                string joinedNames = string.Join(" & ", names);
                if (joinedNames != displayName)
                    InfoPanel.TerrorValue.Text = displayName + Environment.NewLine + string.Join(Environment.NewLine, names);
                else
                    InfoPanel.TerrorValue.Text = displayName;
                // 固定色が設定されていればその色を使用、なければ従来の色を使用
                InfoPanel.TerrorValue.ForeColor = (AppSettings.FixedTerrorColor != Color.Empty)
                    ? AppSettings.FixedTerrorColor
                    : color;
                if (!string.IsNullOrEmpty(joinedNames))
                {
                    terrorColors[joinedNames] = color;
                }
                UpdateTerrorInfoPanel(names);
            }
            else
            {
                UpdateTerrorInfoPanel(null);
            }
        }


        private void HandleOscMessage(OscMessage message)
        {
            if (message.Address == "/avatar/parameters/VelocityMagnitude")
            {
                try
                {
                    receivedVelocityMagnitude = Convert.ToSingle(message.ToArray()[0]);
                }
                catch { }
            }
            else if (message.Address == "/avatar/parameters/VelocityY")
            {
                try
                {
                    receivedVelocityY = Convert.ToSingle(message.ToArray()[0]);
                }
                catch { }
            }
            else if (message.Address == "/avatar/parameters/suside")
            {
                bool suicideFlag = false;
                if (message.Count > 0)
                {
                    try { suicideFlag = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                if (suicideFlag)
                {
                    Task.Run(() => PerformAutoSuicide());
                }
            }
            else if (message.Address == "/avatar/parameters/autosuside")
            {
                bool autoSuicideOSC = false;
                if (message.Count > 0)
                {
                    try { autoSuicideOSC = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                    AppSettings.AutoSuicideEnabled = autoSuicideOSC;
                }
            }
            else if (message.Address == "/avatar/parameters/setalert")
            {
                float setAlertValue = 0;
                if (message.Count > 0)
                {

                    try
                    {
                        setAlertValue = Convert.ToSingle(message.ToArray()[0]);
                    }
                    catch { }
                }
                if (setAlertValue != 0)
                {
                    // OSCで受信した setalert の値が0でなければ、ton.sprink.cloudのwebsocketにAlertメッセージを送信する
                    Task.Run(() => SendAlertToTonSprinkAsync((float)setAlertValue));
                }
            }
            else if (message.Address == "/avatar/parameters/getlatestsavecode")
            {
                bool getLatestSaveCode = false;
                if (message.Count > 0)
                {
                    try { getLatestSaveCode = Convert.ToBoolean(message.ToArray()[0]); } catch { }
                }
                // getLatestSaveCodeがtrueなら、最新のセーブコードを取得してクリップボードにコピーする
                if (getLatestSaveCode)
                {
                    // https://toncloud.sprink.cloud/api/savecode/get/{apikey}/latest にGETリクエストを送信して最新のセーブコードを取得、それをクリップボードに
                    if (!string.IsNullOrEmpty(AppSettings.apikey))
                    {
                        using (var client = new HttpClient())
                        {
                            client.BaseAddress = new Uri("https://toncloud.sprink.cloud/api/savecode/get/" + AppSettings.apikey + "/latest");
                            try
                            {
                                var response = client.GetAsync("").Result;
                                if (response.IsSuccessStatusCode)
                                {
                                    //jsonを取得して、"savecode"フィールドの値をクリップボードにコピー
                                    var jsonResponse = response.Content.ReadAsStringAsync().Result;
                                    var json = JObject.Parse(jsonResponse);
                                    string saveCode = json.Value<string>("savecode") ?? "";
                                    Thread thread = new Thread(() => Clipboard.SetText(saveCode));
                                    thread.SetApartmentState(ApartmentState.STA);
                                    thread.Start();
                                    thread.Join();
                                    EventLogger.LogEvent("SaveCode", "Latest save code copied to clipboard: " + saveCode);
                                }
                                else
                                {
                                    EventLogger.LogEvent("SaveCodeError", $"Failed to get latest save code: {response.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                EventLogger.LogEvent("SaveCodeError", $"Exception occurred: {ex.Message}");
                            }
                        }
                    }
                }


            }

            currentVelocity = Math.Abs(receivedVelocityMagnitude);
            EventLogger.LogEvent("Receive: ", $"{message.Address} => Computed Velocity: {currentVelocity:F2}");
            this.Invoke(new Action(() =>
            {
                lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}  Members: {connected}";
            }));
        }

        private async Task ConnectToInstance(string instanceValue)
        {
            string url = $"ws://xy.f5.si:8880/ToNRoundCounter/{instanceValue}";
            instanceWsConnection = new ClientWebSocket();
            try
            {
                await instanceWsConnection.ConnectAsync(new Uri(url), CancellationToken.None);
                while (instanceWsConnection.State == WebSocketState.Open)
                {
                    var buffer = new byte[8192];
                    WebSocketReceiveResult result = await instanceWsConnection.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await instanceWsConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else
                    {
                        var messageBytes = new List<byte>();
                        messageBytes.AddRange(buffer.Take(result.Count));
                        while (!result.EndOfMessage)
                        {
                            result = await instanceWsConnection.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            messageBytes.AddRange(buffer.Take(result.Count));
                        }
                        string msg = Encoding.UTF8.GetString(messageBytes.ToArray());
                        ProcessInstanceMessage(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                EventLogger.LogEvent("InstanceError", ex.ToString());
            }
            finally
            {
                if (instanceWsConnection != null)
                {
                    try { await instanceWsConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); } catch { }
                    instanceWsConnection.Dispose();
                    instanceWsConnection = null;
                    //0.3秒おきに再試行
                    await Task.Delay(300);
                    _ = Task.Run(() => ConnectToInstance(instanceValue));
                }
            }
        }

        // 受信したインスタンス用 WebSocket メッセージを処理するメソッド
        private void ProcessInstanceMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                string type = json.Value<string>("type") ?? "";
                EventLogger.LogEvent("ReceivedWSType", type);
                if (type == "JoinedMember" || type == "LeavedMember")
                {
                    connected = json.Value<int>("connected");
                    // VelocityMagnitude の右隣に接続人数を表示する（既存の lblDebugInfo を更新）
                    this.Invoke(new Action(() =>
                    {
                        lblDebugInfo.Text = $"VelocityMagnitude: {currentVelocity:F2}  Members: {connected}";
                    }));
                }
                else if (type == "alertIncoming")
                    using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 9000))
                    {
                        EventLogger.LogEvent("alertIncoming", "start process");
                        float alertNum = json.Value<float>("alertNum");
                        bool isLocal = json.Value<bool>("isLocal");
                        // OSC で /avatar/parameters/alert に対して、3秒間0と alertNum を0.25秒間隔で交互に送信し、その後0を送信
                        Task.Run(() => SendAlertOscMessagesAsync(alertNum, isLocal));
                    }
            }
            catch (Exception ex)
            {
                EventLogger.LogEvent("InstanceProcessError", ex.ToString());
            }
        }

        // OSC 送信用メソッド（Rug.Osc の OscSender を使用）
        private async Task SendAlertOscMessagesAsync(float alertNum, bool isLocal = true)
        {
            await sendAlertSemaphore.WaitAsync();
            try
            {
                EventLogger.LogEvent("SendAlertOscMessagesAsync", "start process");
                using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 0, 9000))
                {
                    string switchString = isLocal ? "_Local" : "";
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "start connect");
                    sender.Connect();
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "connected");
                    DateTime startTime = DateTime.Now;
                    bool sendAlert = true;
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "start send");
                    notifyPlayer.controls.play();
                    while ((DateTime.Now - startTime).TotalSeconds < 2)
                    {
                        EventLogger.LogEvent("SendAlertOscMessagesAsync", "send " + sendAlert.ToString());
                        var msg = new OscMessage("/avatar/parameters/alert" + switchString, sendAlert ? alertNum : 0);
                        sender.Send(msg);
                        sendAlert = !sendAlert;
                        await Task.Delay(250);
                    }
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "send 0");
                    var msg0 = new OscMessage("/avatar/parameters/alert" + switchString, 0f);
                    sender.Send(msg0);
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "closing");
                    sender.Close();
                    EventLogger.LogEvent("SendAlertOscMessagesAsync", "closed");
                }
            }
            finally
            {
                sendAlertSemaphore.Release();
            }
        }

        private async Task SendPieSizeOscMessagesAsync(float piesizetNum, bool isLocal = true)
        {
            await sendAlertSemaphore.WaitAsync();
            try
            {
                EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "start process");
                using (var sender = new OscSender(IPAddress.Parse("127.0.0.1"), 0, 9000))
                {
                    string switchString = isLocal ? "_Local" : "";
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "start connect");
                    sender.Connect();
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "connected");
                    DateTime startTime = DateTime.Now;
                    bool sendAlert = true;
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "start send");
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "send " + piesizetNum * 1 / 20);
                    var msg = new OscMessage("/avatar/parameters/Breast_size", piesizetNum * 1 / 20);
                    sender.Send(msg);
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "closing");
                    sender.Close();
                    EventLogger.LogEvent("SendPieSizeOscMessagesAsync", "closed");
                }
            }
            finally
            {
                sendAlertSemaphore.Release();
            }
        }

        private async Task SendAlertToTonSprinkAsync(float alertNum)
        {
            if (instanceWsConnection != null && instanceWsConnection.State == WebSocketState.Open)
            {
                var jsonMessage = new JObject
                {
                    ["type"] = "Alert",
                    ["alertNum"] = alertNum
                };
                string message = jsonMessage.ToString();
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                await instanceWsConnection.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                // 必要に応じてエラーログを記録するか、再接続処理を実装してください
                EventLogger.LogEvent("AlertSendError", "Instance WebSocket connection is not available.");
            }
        }

        private void LaunchSuicideInputIfExists()
        {
            NativeMethods.press_keys();

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
    }
}
