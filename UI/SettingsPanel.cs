using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Domain;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using System.Net;
using Serilog;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Identifies which fixed sound the user requested to test from the settings panel.
    /// </summary>
    public enum SoundTestKind
    {
        Notification,
        Afk,
        Punish,
    }

    public class SettingsPanel : UserControl
    {
        private readonly IAppSettings _settings;
        private readonly CloudWebSocketClient? _cloudClient;
        private int _cloudClientStartRequested;

        private Label languageLabel = null!;
        public ComboBox LanguageComboBox { get; private set; } = null!;
        public NumericUpDown oscPortNumericUpDown { get; private set; } = null!;
        public TextBox webSocketIpTextBox { get; private set; } = null!;
        public CheckBox ShowStatsCheckBox { get; private set; } = null!;
        public CheckBox DebugInfoCheckBox { get; private set; } = null!;

        public CheckBox RoundTypeCheckBox { get; private set; } = null!;
        public CheckBox TerrorCheckBox { get; private set; } = null!;
        public CheckBox AppearanceCountCheckBox { get; private set; } = null!;
        public CheckBox SurvivalCountCheckBox { get; private set; } = null!;
        public CheckBox DeathCountCheckBox { get; private set; } = null!;
        public CheckBox SurvivalRateCheckBox { get; private set; } = null!;

        public CheckBox OverlayVelocityCheckBox { get; private set; } = null!;
        public CheckBox OverlayTerrorCheckBox { get; private set; } = null!;
        public CheckBox OverlayDamageCheckBox { get; private set; } = null!;
        public CheckBox OverlayAngleCheckBox { get; private set; } = null!;
        public CheckBox OverlayNextRoundCheckBox { get; private set; } = null!;
        public CheckBox OverlayRoundStatusCheckBox { get; private set; } = null!;
        public CheckBox OverlayRoundHistoryCheckBox { get; private set; } = null!;
        public CheckBox OverlayRoundStatsCheckBox { get; private set; } = null!;
        public NumericUpDown OverlayRoundHistoryCountNumeric { get; private set; } = null!;
        public CheckBox OverlayTerrorInfoCheckBox { get; private set; } = null!;
        public CheckBox OverlayShortcutsCheckBox { get; private set; } = null!;
        public CheckBox OverlayClockCheckBox { get; private set; } = null!;
        public CheckBox OverlayInstanceTimerCheckBox { get; private set; } = null!;
        public CheckBox OverlayInstanceMembersCheckBox { get; private set; } = null!;
        public CheckBox OverlayVotingCheckBox { get; private set; } = null!;
        public CheckBox OverlayUnboundTerrorDetailsCheckBox { get; private set; } = null!;
        public TrackBar OverlayOpacityTrackBar { get; private set; } = null!;
        public Label OverlayOpacityValueLabel { get; private set; } = null!;

        public CheckBox ToggleRoundLogCheckBox { get; private set; } = null!;

        public Label FixedTerrorColorLabel { get; private set; } = null!;
        public Button FixedTerrorColorButton { get; private set; } = null!;
        public Label BackgroundColorLabel { get; private set; } = null!;
        public Button BackgroundColorButton { get; private set; } = null!;
        public Label RoundTypeStatsLabel { get; private set; } = null!;
        public CheckedListBox RoundTypeStatsListBox { get; private set; } = null!;

        public Label InfoPanelBgLabel { get; private set; } = null!;
        public Button InfoPanelBgButton { get; private set; } = null!;
        public Label StatsBgLabel { get; private set; } = null!;
        public Button StatsBgButton { get; private set; } = null!;
        public Label LogBgLabel { get; private set; } = null!;
        public Button LogBgButton { get; private set; } = null!;
        public CheckedListBox autoSuicideRoundListBox { get; internal set; } = null!;
        public CheckBox autoSuicideCheckBox { get; internal set; } = null!;
        public Label autoSuicideRoundLabel { get; private set; } = null!;
        public CheckBox autoSuicideUseDetailCheckBox { get; private set; } = null!;
        public Label autoSuicidePresetLabel { get; private set; } = null!;
        public ComboBox autoSuicidePresetComboBox { get; private set; } = null!;
        public Button autoSuicidePresetSaveButton { get; private set; } = null!;
        public Button autoSuicidePresetLoadButton { get; private set; } = null!;
        public Button autoSuicidePresetExportButton { get; private set; } = null!;
        public Button autoSuicidePresetImportButton { get; private set; } = null!;
        public TextBox autoSuicideDetailTextBox { get; private set; } = null!;
        public CheckBox autoSuicideFuzzyCheckBox { get; private set; } = null!;
        public Button autoSuicideSettingsConfirmButton { get; private set; } = null!;
        public LinkLabel autoSuicideDetailDocLink { get; private set; } = null!;
        private int autoSuicideAutoRuleCount = 0;

        public Label apiKeyLabel { get; private set; } = null!;
        public TextBox apiKeyTextBox { get; private set; } = null!;
        public CheckBox CloudSyncEnabledCheckBox { get; private set; } = null!;
        public TextBox CloudPlayerNameTextBox { get; private set; } = null!;
        public TextBox CloudWebSocketUrlTextBox { get; private set; } = null!;

        public ComboBox ThemeComboBox { get; private set; } = null!;
        public FlowLayoutPanel ModuleExtensionsPanel { get; private set; } = null!;
        public CheckBox AutoLaunchEnabledCheckBox { get; private set; } = null!;
        private DataGridView autoLaunchEntriesGrid = null!;
        private Button autoLaunchAddButton = null!;
        private Button autoLaunchRemoveButton = null!;
        private Button autoLaunchBrowseButton = null!;
        public CheckBox ItemMusicEnabledCheckBox { get; private set; } = null!;
        private DataGridView itemMusicEntriesGrid = null!;
        private Button itemMusicAddButton = null!;
        private Button itemMusicRemoveButton = null!;
        private Button itemMusicBrowseButton = null!;
        public CheckBox RoundBgmEnabledCheckBox { get; private set; } = null!;
        private DataGridView roundBgmEntriesGrid = null!;
        private Button roundBgmAddButton = null!;
        private Button roundBgmRemoveButton = null!;
        private Button roundBgmBrowseButton = null!;
        private ComboBox roundBgmConflictBehaviorComboBox = null!;
        private Label roundBgmConflictBehaviorLabel = null!;
        private List<RoundBgmConflictOption> roundBgmConflictOptions = new List<RoundBgmConflictOption>();
        public TrackBar NotificationVolumeTrackBar { get; private set; } = null!;
        public TrackBar AfkVolumeTrackBar { get; private set; } = null!;
        public TrackBar PunishVolumeTrackBar { get; private set; } = null!;
        public TrackBar MasterVolumeTrackBar { get; private set; } = null!;
        private Label notificationVolumeValueLabel = null!;
        private Label afkVolumeValueLabel = null!;
        private Label punishVolumeValueLabel = null!;
        private Label masterVolumeValueLabel = null!;
        public CheckBox MasterMutedCheckBox { get; private set; } = null!;
        public CheckBox NotificationMutedCheckBox { get; private set; } = null!;
        public CheckBox AfkMutedCheckBox { get; private set; } = null!;
        public CheckBox PunishMutedCheckBox { get; private set; } = null!;
        public CheckBox ItemMusicMutedCheckBox { get; private set; } = null!;
        public CheckBox RoundBgmMutedCheckBox { get; private set; } = null!;
        public CheckBox DbDisplayCheckBox { get; private set; } = null!;
        public ComboBox AudioOutputDeviceComboBox { get; private set; } = null!;
        public TextBox MasterMuteHotkeyTextBox { get; private set; } = null!;
        public CheckBox EqualizerEnabledCheckBox { get; private set; } = null!;
        public ComboBox EqualizerPresetComboBox { get; private set; } = null!;
        private TrackBar[] _equalizerBandTrackBars = Array.Empty<TrackBar>();
        private Label[] _equalizerBandValueLabels = Array.Empty<Label>();
        private readonly List<TrackBar> _volumeTrackBars = new List<TrackBar>();
        private readonly Dictionary<TrackBar, Label> _volumeValueLabels = new Dictionary<TrackBar, Label>();

        /// <summary>
        /// Raised when the user clicks a "test play" button next to a fixed-sound volume slider.
        /// </summary>
        public event EventHandler<SoundTestKind>? TestSoundRequested;
        public TextBox DiscordWebhookUrlTextBox { get; private set; } = null!;
        public CheckBox AutoRecordingEnabledCheckBox { get; private set; } = null!;
        public TextBox AutoRecordingWindowTitleTextBox { get; private set; } = null!;
        public NumericUpDown AutoRecordingFrameRateNumeric { get; private set; } = null!;
        public ComboBox AutoRecordingResolutionComboBox { get; private set; } = null!;
        public CheckBox AutoRecordingIncludeOverlayCheckBox { get; private set; } = null!;
        public TextBox AutoRecordingOutputDirectoryTextBox { get; private set; } = null!;
        public ComboBox AutoRecordingFormatComboBox { get; private set; } = null!;
        public ComboBox AutoRecordingCodecComboBox { get; private set; } = null!;
        public NumericUpDown AutoRecordingVideoBitrateNumeric { get; private set; } = null!;
        public NumericUpDown AutoRecordingAudioBitrateNumeric { get; private set; } = null!;
        public ComboBox AutoRecordingHardwareEncoderComboBox { get; private set; } = null!;
        public CheckedListBox AutoRecordingRoundTypesListBox { get; private set; } = null!;
        public TextBox AutoRecordingTerrorNamesTextBox { get; private set; } = null!;
        private Button autoRecordingBrowseOutputButton = null!;
        private Button roundLogExportButton = null!;
        private Label? autoRecordingCodecHelpLabel;
        private Label? autoRecordingFrameRateLimitLabel;
        private Label? autoRecordingResolutionLabel;
        private Label? autoRecordingVideoBitrateHelpLabel;
        private Label? autoRecordingVideoBitrateUnitLabel;
        private Label? autoRecordingAudioBitrateLabel;
        private Label? autoRecordingAudioBitrateHelpLabel;
        private Label? autoRecordingAudioBitrateUnitLabel;
        private Label? autoRecordingHardwareEncoderHelpLabel;
        private bool autoRecordingCodecSupportsAudio = true;

        private const string AutoLaunchEnabledColumnName = "AutoLaunchEnabled";
        private const string AutoLaunchPathColumnName = "AutoLaunchPath";
        private const string AutoLaunchArgumentsColumnName = "AutoLaunchArguments";
        private const string ItemMusicEnabledColumnName = "ItemMusicEnabled";
        private const string ItemMusicItemColumnName = "ItemMusicItem";
        private const string ItemMusicPathColumnName = "ItemMusicPath";
        private const string ItemMusicMinSpeedColumnName = "ItemMusicMinSpeed";
        private const string ItemMusicMaxSpeedColumnName = "ItemMusicMaxSpeed";
        private const string ItemMusicVolumeColumnName = "ItemMusicVolume";
        private const string RoundBgmEnabledColumnName = "RoundBgmEnabled";
        private const string RoundBgmRoundColumnName = "RoundBgmRound";
        private const string RoundBgmTerrorColumnName = "RoundBgmTerror";
        private const string RoundBgmPathColumnName = "RoundBgmPath";
        private const string RoundBgmVolumeColumnName = "RoundBgmVolume";

        private static readonly (string Code, string ResourceKey)[] LanguageDisplayKeys = new[]
        {
            ("ja", "Language_Japanese"),
            ("en", "Language_English"),
            ("en-US", "Language_EnglishUnitedStates"),
            ("en-GB", "Language_EnglishUnitedKingdom"),
            ("ko", "Language_Korean"),
            ("zh-Hans", "Language_ChineseSimplified"),
            ("da", "Language_Danish"),
            ("de", "Language_German"),
            ("es", "Language_Spanish"),
            ("es-419", "Language_SpanishLatinAmerica"),
            ("fr", "Language_French"),
            ("hr", "Language_Croatian"),
            ("it", "Language_Italian"),
            ("lt", "Language_Lithuanian"),
            ("hu", "Language_Hungarian"),
            ("nl", "Language_Dutch"),
            ("nb", "Language_Norwegian"),
            ("pl", "Language_Polish"),
            ("pt-BR", "Language_PortugueseBrazil"),
            ("ro", "Language_Romanian"),
            ("fi", "Language_Finnish"),
            ("sv", "Language_Swedish"),
            ("vi", "Language_Vietnamese"),
            ("tr", "Language_Turkish"),
            ("th", "Language_Thai"),
            ("el", "Language_Greek"),
            ("bg", "Language_Bulgarian"),
            ("ru", "Language_Russian"),
            ("uk", "Language_Ukrainian"),
        };

        private static readonly string[] KnownRoundTypes = new[]
        {
            "クラシック", "走れ！", "オルタネイト", "パニッシュ", "狂気", "サボタージュ", "霧", "ブラッドバス", "ダブルトラブル",
            "EX", "ミッドナイト", "ゴースト", "8ページ", "アンバウンド", "寒い夜", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス"
        };


        private readonly Dictionary<Control, Point> _originalLocations = new Dictionary<Control, Point>();
        private Size _originalAutoScrollMinSize;

        public void SetCategoryFilter(SettingsCategory? category)
        {
            this.SuspendLayout();
            try
            {
                CacheOriginalLayoutIfNeeded();
                foreach (Control c in this.Controls)
                {
                    if (c.Tag is SettingsCategory cat)
                    {
                        c.Visible = !category.HasValue || cat == category.Value;
                    }
                }
                if (category.HasValue)
                {
                    ReflowVisibleControls();
                }
                else
                {
                    RestoreOriginalLayout();
                }
            }
            finally
            {
                this.ResumeLayout(true);
            }
        }

        private void CacheOriginalLayoutIfNeeded()
        {
            if (_originalLocations.Count > 0)
            {
                return;
            }
            foreach (Control c in this.Controls)
            {
                if (c.Tag is SettingsCategory)
                {
                    _originalLocations[c] = c.Location;
                }
            }
            _originalAutoScrollMinSize = this.AutoScrollMinSize;
        }

        private void RestoreOriginalLayout()
        {
            foreach (var kv in _originalLocations)
            {
                kv.Key.Location = kv.Value;
            }
            if (_originalAutoScrollMinSize != Size.Empty)
            {
                this.AutoScrollMinSize = _originalAutoScrollMinSize;
            }
        }

        private void ReflowVisibleControls()
        {
            const int margin = 10;
            const int columnWidth = 540;
            int leftX = margin;
            int y = margin;
            int maxRight = leftX + columnWidth;

            // 元の配置順 (上→下、同一行は左→右) を維持しつつ、すべて左列に寄せる
            var visible = new System.Collections.Generic.List<Control>();
            foreach (Control c in this.Controls)
            {
                if (!(c.Tag is SettingsCategory) || !c.Visible)
                {
                    continue;
                }
                visible.Add(c);
            }
            visible.Sort((a, b) =>
            {
                Point pa = _originalLocations.TryGetValue(a, out var la) ? la : a.Location;
                Point pb = _originalLocations.TryGetValue(b, out var lb) ? lb : b.Location;
                int cmp = pa.Y.CompareTo(pb.Y);
                if (cmp != 0) return cmp;
                return pa.X.CompareTo(pb.X);
            });

            foreach (var c in visible)
            {
                c.Location = new Point(leftX, y);
                y = c.Bottom + margin;
                if (c.Right > maxRight)
                {
                    maxRight = c.Right;
                }
            }

            int totalHeight = y + margin;
            int totalWidth = Math.Max(maxRight + margin, columnWidth + margin * 2);
            this.AutoScrollMinSize = new Size(totalWidth, totalHeight);
        }

        public SettingsPanel(IAppSettings settings, CloudWebSocketClient? cloudClient = null)
        {
            _settings = settings;
            _cloudClient = cloudClient;
            this.BorderStyle = BorderStyle.FixedSingle;

            int margin = 10;
            int columnWidth = 540;
            int totalWidth = columnWidth * 3 + margin * 4;
            this.Size = new Size(totalWidth, 1100);
            this.AutoScroll = true;
            this.AutoScrollMinSize = new Size(totalWidth, 1100);

            int currentY = margin;
            int rightColumnY = margin;
            int thirdColumnY = margin;
            int thirdColumnX = margin * 3 + columnWidth * 2;
            int innerMargin = 10;

            languageLabel = new Label();
            languageLabel.Text = LanguageManager.Translate("言語");
            languageLabel.AutoSize = true;
            languageLabel.Location = new Point(margin, currentY + 4);
            languageLabel.Tag = SettingsCategory.General;
            this.Controls.Add(languageLabel);

            LanguageComboBox = new ComboBox();
            LanguageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            LanguageComboBox.Location = new Point(languageLabel.Right + 10, currentY);
            LanguageComboBox.Width = columnWidth - (LanguageComboBox.Left - margin);
            LanguageComboBox.Tag = SettingsCategory.General;
            this.Controls.Add(LanguageComboBox);
            LoadLanguageOptions(_settings.Language);
            currentY += LanguageComboBox.Height + margin;

            Label themeLabel = new Label();
            themeLabel.Text = LanguageManager.Translate("テーマ");
            themeLabel.AutoSize = true;
            themeLabel.Location = new Point(margin, currentY + 4);
            themeLabel.Tag = SettingsCategory.General;
            this.Controls.Add(themeLabel);

            ThemeComboBox = new ComboBox();
            ThemeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ThemeComboBox.Location = new Point(themeLabel.Right + 10, currentY);
            ThemeComboBox.Width = columnWidth - (ThemeComboBox.Left - margin);
            ThemeComboBox.Tag = SettingsCategory.General;
            this.Controls.Add(ThemeComboBox);
            LoadThemeOptions(Theme.RegisteredThemes, _settings.ThemeKey);
            currentY += ThemeComboBox.Height + margin;

            GroupBox grpOsc = new GroupBox();
            grpOsc.Text = LanguageManager.Translate("OSC設定");
            grpOsc.Location = new Point(margin, currentY);
            grpOsc.Size = new Size(columnWidth, 60);
            grpOsc.Tag = SettingsCategory.General;
            this.Controls.Add(grpOsc);

            Label wsIpLabel = new Label();
            wsIpLabel.Text = LanguageManager.Translate("WebSocket IP:");
            wsIpLabel.AutoSize = true;
            wsIpLabel.Location = new Point(margin, 20);
            grpOsc.Controls.Add(wsIpLabel);

            webSocketIpTextBox = new TextBox();
            webSocketIpTextBox.Text = _settings.WebSocketIp;
            webSocketIpTextBox.Location = new Point(wsIpLabel.Right + 10, 18);
            webSocketIpTextBox.Width = 120;
            grpOsc.Controls.Add(webSocketIpTextBox);

            Label oscPortLabel = new Label();
            oscPortLabel.Text = LanguageManager.Translate("OSC接続ポート:");
            oscPortLabel.AutoSize = true;
            oscPortLabel.Location = new Point(webSocketIpTextBox.Right + 20, 20);
            grpOsc.Controls.Add(oscPortLabel);

            oscPortNumericUpDown = new NumericUpDown();
            oscPortNumericUpDown.Minimum = 1024;
            oscPortNumericUpDown.Maximum = 65535;
            oscPortNumericUpDown.Value = _settings.OSCPort;
            oscPortNumericUpDown.Location = new Point(oscPortLabel.Right + 10, 20);
            grpOsc.Controls.Add(oscPortNumericUpDown);

            GroupBox grpAutoSuicide = new GroupBox();
            grpAutoSuicide.Text = LanguageManager.Translate("自動自殺モード");
            grpAutoSuicide.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpAutoSuicide.Size = new Size(columnWidth, 100);
            grpAutoSuicide.Tag = SettingsCategory.AutoSuicide;
            this.Controls.Add(grpAutoSuicide);

            int autoInnerY = 20;
            autoSuicideCheckBox = new CheckBox();
            autoSuicideCheckBox.Name = "AutoSuicideCheckBox";
            autoSuicideCheckBox.Text = LanguageManager.Translate("自動自殺を有効にする");
            autoSuicideCheckBox.AutoSize = true;
            autoSuicideCheckBox.Location = new Point(innerMargin, autoInnerY);
            autoSuicideCheckBox.Checked = _settings.AutoSuicideEnabled;
            grpAutoSuicide.Controls.Add(autoSuicideCheckBox);

            autoSuicideUseDetailCheckBox = new CheckBox();
            autoSuicideUseDetailCheckBox.Text = LanguageManager.Translate("詳細設定を利用する");
            autoSuicideUseDetailCheckBox.AutoSize = true;
            autoSuicideUseDetailCheckBox.Location = new Point(innerMargin, autoSuicideCheckBox.Bottom + 5);
            grpAutoSuicide.Controls.Add(autoSuicideUseDetailCheckBox);

            autoSuicideFuzzyCheckBox = new CheckBox();
            autoSuicideFuzzyCheckBox.Text = LanguageManager.Translate("曖昧マッチング");
            autoSuicideFuzzyCheckBox.AutoSize = true;
            autoSuicideFuzzyCheckBox.Location = new Point(autoSuicideUseDetailCheckBox.Right + 10, autoSuicideUseDetailCheckBox.Top);
            grpAutoSuicide.Controls.Add(autoSuicideFuzzyCheckBox);
            autoSuicideDetailDocLink = new LinkLabel();
            autoSuicideDetailDocLink.Text = LanguageManager.Translate("詳細設定ドキュメント");
            autoSuicideDetailDocLink.AutoSize = true;
            autoSuicideDetailDocLink.Location = new Point(innerMargin, autoSuicideUseDetailCheckBox.Bottom + 5);
            autoSuicideDetailDocLink.Links.Add(0, autoSuicideDetailDocLink.Text.Length,
                "https://github.com/lovetwice1012/ToNRoundCounter/blob/master/docs/auto-suicide-detail-settings.md");
            autoSuicideDetailDocLink.LinkClicked += (s, e) =>
            {
                string? url = e.Link?.LinkData?.ToString();
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            };
            grpAutoSuicide.Controls.Add(autoSuicideDetailDocLink);

            autoInnerY = autoSuicideDetailDocLink.Bottom + 10;
            autoSuicideRoundLabel = new Label();
            autoSuicideRoundLabel.Text = LanguageManager.Translate("自動自殺対象ラウンド:");
            autoSuicideRoundLabel.AutoSize = true;
            autoSuicideRoundLabel.Location = new Point(innerMargin, autoInnerY);
            grpAutoSuicide.Controls.Add(autoSuicideRoundLabel);

            autoSuicideRoundListBox = new CheckedListBox();
            autoSuicideRoundListBox.Name = "AutoSuicideRoundListBox";
            autoSuicideRoundListBox.Location = new Point(autoSuicideRoundLabel.Right + 10, autoInnerY);
            autoSuicideRoundListBox.Size = new Size(400, 150);
            autoSuicideRoundListBox.Items.Add("クラシック");
            autoSuicideRoundListBox.Items.Add("オルタネイト");
            autoSuicideRoundListBox.Items.Add("パニッシュ");
            autoSuicideRoundListBox.Items.Add("サボタージュ");
            autoSuicideRoundListBox.Items.Add("ブラッドバス");
            autoSuicideRoundListBox.Items.Add("ミッドナイト");
            autoSuicideRoundListBox.Items.Add("寒い夜");
            autoSuicideRoundListBox.Items.Add("ミスティックムーン");
            autoSuicideRoundListBox.Items.Add("ブラッドムーン");
            autoSuicideRoundListBox.Items.Add("トワイライト");
            autoSuicideRoundListBox.Items.Add("ソルスティス");
            autoSuicideRoundListBox.Items.Add("霧");
            autoSuicideRoundListBox.Items.Add("8ページ");
            autoSuicideRoundListBox.Items.Add("狂気");
            autoSuicideRoundListBox.Items.Add("ゴースト");
            autoSuicideRoundListBox.Items.Add("ダブルトラブル");
            autoSuicideRoundListBox.Items.Add("EX");
            autoSuicideRoundListBox.Items.Add("アンバウンド");
            for (int i = 0; i < autoSuicideRoundListBox.Items.Count; i++)
            {
                string item = autoSuicideRoundListBox.Items[i]?.ToString() ?? string.Empty;
                autoSuicideRoundListBox.SetItemChecked(i, !string.IsNullOrEmpty(item) && _settings.AutoSuicideRoundTypes.Contains(item));
            }
            grpAutoSuicide.Controls.Add(autoSuicideRoundListBox);
            autoSuicideRoundListBox.ItemCheck += (s, e) =>
            {
                BeginInvoke(new Action(UpdateAutoSuicideDetailAutoLines));
            };
            autoSuicideDetailTextBox = new TextBox();
            autoSuicideDetailTextBox.Multiline = true;
            autoSuicideDetailTextBox.AcceptsReturn = true;
            autoSuicideDetailTextBox.ScrollBars = ScrollBars.Vertical;
            autoSuicideDetailTextBox.Size = autoSuicideRoundListBox.Size;
            autoSuicideDetailTextBox.Location = autoSuicideRoundListBox.Location;
            grpAutoSuicide.Controls.Add(autoSuicideDetailTextBox);

            autoSuicidePresetLabel = new Label();
            autoSuicidePresetLabel.Text = LanguageManager.Translate("プリセット:");
            autoSuicidePresetLabel.AutoSize = true;
            autoSuicidePresetLabel.Location = new Point(innerMargin, autoSuicideRoundListBox.Bottom + 10);
            grpAutoSuicide.Controls.Add(autoSuicidePresetLabel);

            autoSuicidePresetComboBox = new ComboBox();
            autoSuicidePresetComboBox.Name = "AutoSuicidePresetComboBox";
            autoSuicidePresetComboBox.Location = new Point(autoSuicidePresetLabel.Right + 10, autoSuicideRoundListBox.Bottom + 5);
            autoSuicidePresetComboBox.Width = 200;
            foreach (var key in _settings.AutoSuicidePresets.Keys)
            {
                autoSuicidePresetComboBox.Items.Add(key);
            }
            grpAutoSuicide.Controls.Add(autoSuicidePresetComboBox);

            autoSuicidePresetSaveButton = new Button();
            autoSuicidePresetSaveButton.Text = LanguageManager.Translate("保存");
            autoSuicidePresetSaveButton.AutoSize = true;
            autoSuicidePresetSaveButton.Location = new Point(autoSuicidePresetComboBox.Right + 10, autoSuicideRoundListBox.Bottom + 5);
            autoSuicidePresetSaveButton.Click += (s, e) =>
            {
                string name = autoSuicidePresetComboBox.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff");
                    autoSuicidePresetComboBox.Text = name;
                }
                CleanAutoSuicideDetailRules();
                var preset = new AutoSuicidePreset
                {
                    RoundTypes = autoSuicideRoundListBox.CheckedItems.Cast<object>()
                        .Select(i => i?.ToString())
                        .Where(i => !string.IsNullOrEmpty(i))
                        .Cast<string>()
                        .ToList(),
                    DetailCustom = GetCustomAutoSuicideLines(),
                    Fuzzy = autoSuicideFuzzyCheckBox.Checked
                };
                _settings.AutoSuicidePresets[name] = preset;
                if (!autoSuicidePresetComboBox.Items.Contains(name))
                    autoSuicidePresetComboBox.Items.Add(name);
                MessageBox.Show(LanguageManager.Translate("プリセットを保存しました。"), LanguageManager.Translate("情報"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetSaveButton);

            autoSuicidePresetLoadButton = new Button();
            autoSuicidePresetLoadButton.Text = LanguageManager.Translate("読み込み");
            autoSuicidePresetLoadButton.AutoSize = true;
            autoSuicidePresetLoadButton.Location = new Point(autoSuicidePresetSaveButton.Right + 10, autoSuicideRoundListBox.Bottom + 5);
            autoSuicidePresetLoadButton.Click += (s, e) =>
            {
                string name = autoSuicidePresetComboBox.Text.Trim();
                if (!string.IsNullOrEmpty(name) && _settings.AutoSuicidePresets.ContainsKey(name))
                {
                    var preset = _settings.AutoSuicidePresets[name];
                    for (int i = 0; i < autoSuicideRoundListBox.Items.Count; i++)
                    {
                        string item = autoSuicideRoundListBox.Items[i]?.ToString() ?? string.Empty;
                        autoSuicideRoundListBox.SetItemChecked(i, !string.IsNullOrEmpty(item) && preset.RoundTypes.Contains(item));
                    }
                    autoSuicideFuzzyCheckBox.Checked = preset.Fuzzy;
                    autoSuicideAutoRuleCount = 0;
                    autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, preset.DetailCustom);
                    if (!autoSuicideUseDetailCheckBox.Checked)
                    {
                        UpdateAutoSuicideDetailAutoLines();
                    }
                }
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetLoadButton);

            autoSuicidePresetImportButton = new Button();
            autoSuicidePresetImportButton.Text = LanguageManager.Translate("インポート");
            autoSuicidePresetImportButton.AutoSize = true;
            autoSuicidePresetImportButton.Location = new Point(autoSuicidePresetSaveButton.Left, autoSuicidePresetSaveButton.Bottom + 5);
            autoSuicidePresetImportButton.Click += (s, e) =>
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Filter = "JSON Files|*.json|All Files|*.*";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(dialog.FileName);
                        var preset = JsonConvert.DeserializeObject<AutoSuicidePreset>(json);
                        if (preset != null)
                        {
                            string name = Path.GetFileNameWithoutExtension(dialog.FileName);
                            _settings.AutoSuicidePresets[name] = preset;
                            if (!autoSuicidePresetComboBox.Items.Contains(name))
                                autoSuicidePresetComboBox.Items.Add(name);
                            MessageBox.Show(LanguageManager.Translate("プリセットをインポートしました。"), LanguageManager.Translate("情報"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, LanguageManager.Translate("エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetImportButton);

            autoSuicidePresetExportButton = new Button();
            autoSuicidePresetExportButton.Text = LanguageManager.Translate("エクスポート");
            autoSuicidePresetExportButton.AutoSize = true;
            autoSuicidePresetExportButton.Location = new Point(autoSuicidePresetLoadButton.Left, autoSuicidePresetLoadButton.Bottom + 5);
            autoSuicidePresetExportButton.Click += (s, e) =>
            {
                string name = autoSuicidePresetComboBox.Text.Trim();
                if (!string.IsNullOrEmpty(name) && _settings.AutoSuicidePresets.ContainsKey(name))
                {
                    SaveFileDialog dialog = new SaveFileDialog();
                    dialog.Filter = "JSON Files|*.json|All Files|*.*";
                    dialog.FileName = name + ".json";
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var preset = _settings.AutoSuicidePresets[name];
                        File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(preset, Formatting.Indented));
                        MessageBox.Show(LanguageManager.Translate("プリセットをエクスポートしました。"), LanguageManager.Translate("情報"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetExportButton);

            autoSuicideAutoRuleCount = autoSuicideRoundListBox.Items.Count;
            var lines = new List<string>();
            if (!_settings.AutoSuicideUseDetail)
            {
                var autoLines = GenerateAutoSuicideLines();
                lines.AddRange(autoLines);
            }
            if (_settings.AutoSuicideDetailCustom != null)
                lines.AddRange(_settings.AutoSuicideDetailCustom);
            var (cleaned, unparsedLines) = CleanRules(lines);
            if (unparsedLines.Any())
            {
                Log.Warning("Failed to parse auto-suicide rule lines: {Lines}", string.Join(", ", unparsedLines));
            }
            if (!_settings.AutoSuicideUseDetail)
            {
                var split = SplitAutoAndCustom(cleaned);
                autoSuicideAutoRuleCount = split.autoLines.Count;
                autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, split.autoLines.Concat(split.customLines));
            }
            else
            {
                autoSuicideAutoRuleCount = 0;
                autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, cleaned.Select(r => r.ToString()));
            }
            autoSuicideFuzzyCheckBox.Checked = _settings.AutoSuicideFuzzyMatch;
            autoSuicideUseDetailCheckBox.CheckedChanged += AutoSuicideUseDetailCheckBox_CheckedChanged;
            autoSuicideUseDetailCheckBox.Checked = _settings.AutoSuicideUseDetail;

            autoSuicideSettingsConfirmButton = new Button();
            autoSuicideSettingsConfirmButton.Text = LanguageManager.Translate("設定内容確認");
            autoSuicideSettingsConfirmButton.AutoSize = true;
            autoSuicideSettingsConfirmButton.Location = new Point(autoSuicidePresetExportButton.Right + 10, autoSuicidePresetExportButton.Top);
            autoSuicideSettingsConfirmButton.Click += (s, e) =>
            {
                var rawLines = autoSuicideDetailTextBox.Lines;
                var trimmed = new List<string>();
                var errors = new List<string>();
                for (int i = 0; i < rawLines.Length; i++)
                {
                    string line = rawLines[i].Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (AutoSuicideRule.TryParseDetailed(line, out var _, out var err))
                    {
                        trimmed.Add(line);
                    }
                    else
                    {
                        var errorKey = err ?? string.Empty;
                        errors.Add($"{i + 1}行目: {LanguageManager.Translate(errorKey)}");
                    }
                }
                if (errors.Any())
                {
                    MessageBox.Show(string.Join(Environment.NewLine, errors), LanguageManager.Translate("構文エラー"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var (rulesCheck, unparsedLines) = CleanRules(trimmed);
                if (unparsedLines.Any())
                {
                    var msg = LanguageManager.Translate("解析できなかった行があります:") + Environment.NewLine + string.Join(Environment.NewLine, unparsedLines);
                    MessageBox.Show(msg, LanguageManager.Translate("警告"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log.Warning("Failed to parse auto-suicide rule lines: {Lines}", string.Join(", ", unparsedLines));
                }
                if (rulesCheck.All(r => r.Value == 0))
                {
                    MessageBox.Show(LanguageManager.Translate("現在の設定では自動自殺を行いません"), LanguageManager.Translate("設定内容"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var sb = new StringBuilder();
                if (!autoSuicideCheckBox.Checked)
                {
                    sb.AppendLine(LanguageManager.Translate("自動自殺は無効になっています"));
                    sb.AppendLine();
                }

                Func<int, string> GetActionText = value =>
                {
                    switch (value)
                    {
                        case 1:
                            return "自動自爆を行います";
                        case 2:
                            return "遅延自爆を行います";
                        default:
                            return "何もしません";
                    }
                };

                Func<List<string>, bool> ShouldBullet = items => items.Count > 3 || string.Join(",", items).Length > 20;

                var globalGroups = rulesCheck.Where(r => r.Round == null && r.Terror == null)
                                             .GroupBy(r => r.Value);
                foreach (var g in globalGroups)
                {
                    sb.AppendLine($"全てのラウンドの全てのテラーで{GetActionText(g.Key)}");
                }

                var roundWildcards = rulesCheck.Where(r => r.Round != null && r.TerrorExpression == null && !r.RoundNegate).ToList();
                var detailRules = rulesCheck.Where(r => r.Round != null && r.Terror != null && !r.RoundNegate).ToList();
                var processedDetail = new HashSet<AutoSuicideRule>();
                var simpleRounds = new List<Tuple<string, int>>();
                var roundsWithHeader = new HashSet<string>();

                foreach (var rw in roundWildcards)
                {
                    if (string.IsNullOrEmpty(rw.Round))
                        continue;

                    var roundName = rw.Round!;
                    var baseValue = rw.Value;
                    var relatedDetails = detailRules.Where(d => d.Round == roundName).ToList();
                    var exceptions = relatedDetails.Where(d => d.Value != baseValue || d.TerrorNegate).ToList();
                    var sameActionDetails = relatedDetails.Except(exceptions).ToList();

                    if (!exceptions.Any())
                    {
                        simpleRounds.Add(Tuple.Create(roundName, baseValue));
                    }
                    else
                    {
                        sb.AppendLine($"{roundName}では全てのテラーで{GetActionText(baseValue)}。");
                        foreach (var eg in exceptions.GroupBy(ex => ex.Value))
                        {
                            var terrors = eg.Select(rule => rule.Terror ?? string.Empty).Where(t => !string.IsNullOrEmpty(t)).ToList();
                            bool useBullet = ShouldBullet(terrors);
                            sb.AppendLine($"ただし、以下に記載する条件では{GetActionText(eg.Key)}");
                            if (useBullet)
                            {
                                foreach (var t in terrors)
                                    sb.AppendLine($"・{t}");
                            }
                            else
                            {
                                sb.AppendLine($"・{string.Join(",", terrors)}");
                            }
                            foreach (var t in eg)
                                processedDetail.Add(t);
                        }
                    }

                    foreach (var d in sameActionDetails)
                        processedDetail.Add(d);
                }

                foreach (var g in simpleRounds.GroupBy(r => r.Item2))
                {
                    var rounds = g.Select(r => r.Item1).ToList();
                    bool useBullet = ShouldBullet(rounds);
                    if (useBullet)
                    {
                        sb.AppendLine($"以下のラウンドでは全てのテラーで{GetActionText(g.Key)}");
                        foreach (var r in rounds)
                            sb.AppendLine($"・{r}");
                    }
                    else
                    {
                        sb.AppendLine($"{string.Join(",", rounds)}では全てのテラーで{GetActionText(g.Key)}");
                    }
                }

                var negRoundGroups = rulesCheck.Where(r => r.TerrorExpression == null && r.RoundNegate)
                                               .Select(r => new { r.Value, Rounds = r.GetRoundTerms() })
                                               .Where(x => x.Rounds != null)
                                               .GroupBy(x => x.Value);
                foreach (var g in negRoundGroups)
                {
                    var rounds = g.SelectMany(x => x.Rounds ?? Enumerable.Empty<string>()).Distinct().ToList();
                    bool useBullet = ShouldBullet(rounds);
                    if (useBullet)
                    {
                        sb.AppendLine($"以下のラウンド以外の全てのラウンドで、{GetActionText(g.Key)}");
                        foreach (var r in rounds)
                            sb.AppendLine($"・{r}");
                    }
                    else
                    {
                        sb.AppendLine($"{string.Join(",", rounds)}以外の全てのラウンドで、{GetActionText(g.Key)}");
                    }
                }

                var negRoundTerrorRules = rulesCheck.Where(r => r.RoundNegate && r.TerrorExpression != null)
                                                     .GroupBy(r => r.Value);
                foreach (var g in negRoundTerrorRules)
                {
                    foreach (var rule in g)
                    {
                        var rounds = rule.GetRoundTerms();
                        if (rounds == null || rounds.Count == 0)
                        {
                            rounds = string.IsNullOrEmpty(rule.RoundExpression)
                                ? new List<string>()
                                : new List<string> { rule.RoundExpression! };
                        }

                        var terrors = rule.GetTerrorTerms();
                        if (terrors == null || terrors.Count == 0)
                        {
                            terrors = string.IsNullOrEmpty(rule.TerrorExpression)
                                ? new List<string>()
                                : new List<string> { rule.TerrorExpression! };
                        }
                        bool roundBullet = ShouldBullet(rounds);
                        bool terrorBullet = ShouldBullet(terrors);
                        string roundPart = roundBullet
                            ? "以下のラウンド以外のラウンドで"
                            : $"{string.Join(",", rounds)}以外のラウンドで";
                        string terrorPart;
                        if (rule.TerrorNegate)
                            terrorPart = terrorBullet ? "以下のテラー以外が出現した時" : $"{string.Join(",", terrors)}以外が出現した時";
                        else
                            terrorPart = terrorBullet ? "以下のテラーが出現した時" : $"{string.Join(",", terrors)}が出現した時";
                        sb.AppendLine($"{roundPart}{terrorPart}、{GetActionText(rule.Value)}");
                        if (roundBullet)
                        {
                            foreach (var r in rounds)
                                sb.AppendLine($"・{r}");
                        }
                        if (terrorBullet)
                        {
                            foreach (var t in terrors)
                                sb.AppendLine($"・{t}");
                        }
                    }
                }

                var remainingDetail = detailRules.Where(d => !processedDetail.Contains(d))
                                                .GroupBy(d => d.Round);
                foreach (var rg in remainingDetail)
                {
                    if (string.IsNullOrEmpty(rg.Key))
                        continue;

                    var roundKey = rg.Key!;
                    if (roundsWithHeader.Add(roundKey))
                        sb.AppendLine($"{roundKey}では以下の設定が適用されています");
                    foreach (var ag in rg.GroupBy(r => r.Value))
                    {
                        var terrors = ag.Select(a => a.Terror ?? string.Empty).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        bool useBullet = ShouldBullet(terrors);
                        if (useBullet)
                        {
                            sb.AppendLine($"・以下のテラーが出現した時、{GetActionText(ag.Key)}");
                            foreach (var t in terrors)
                                sb.AppendLine($"　・{t}");
                        }
                        else
                        {
                            sb.AppendLine($"・{string.Join(",", terrors)}が出現した時、{GetActionText(ag.Key)}");
                        }
                    }
                }

                var complexRoundRules = rulesCheck.Where(IsComplexRoundRule)
                                                   .GroupBy(r => r.Round);
                foreach (var cg in complexRoundRules)
                {
                    if (string.IsNullOrEmpty(cg.Key))
                        continue;

                    var roundKey = cg.Key!;
                    if (roundsWithHeader.Add(roundKey))
                        sb.AppendLine($"{roundKey}では以下の設定が適用されています");
                    foreach (var rule in cg)
                    {
                        var terrors = rule.GetTerrorTerms();
                        bool useBullet = terrors != null && ShouldBullet(terrors);
                        string condition;
                        if (terrors != null)
                        {
                            var terrorList = terrors;
                            condition = rule.TerrorNegate
                                ? (useBullet ? "以下のテラー以外が出現した時" : $"{string.Join(",", terrorList)}以外が出現した時")
                                : (useBullet ? "以下のテラーが出現した時" : $"{string.Join(",", terrorList)}が出現した時");
                        }
                        else
                        {
                            condition = rule.TerrorNegate
                                ? $"{rule.TerrorExpression}以外が出現した時"
                                : $"{rule.TerrorExpression}が出現した時";
                        }

                        sb.AppendLine($"・{condition}、{GetActionText(rule.Value)}");
                        if (useBullet)
                        {
                            foreach (var t in terrors!)
                                sb.AppendLine($"　・{t}");
                        }
                    }
                }

                var terrorGroups = rulesCheck.Where(r => r.RoundExpression == null && r.TerrorExpression != null)
                                             .Select(r => new { r.TerrorNegate, r.Value, Terrors = r.GetTerrorTerms() })
                                             .Where(x => x.Terrors != null)
                                             .GroupBy(x => new { x.TerrorNegate, x.Value });
                foreach (var g in terrorGroups)
                {
                    var terrors = g.SelectMany(x => x.Terrors ?? Enumerable.Empty<string>()).Distinct().ToList();
                    bool useBullet = ShouldBullet(terrors);
                    if (g.Key.TerrorNegate)
                    {
                        if (useBullet)
                        {
                            sb.AppendLine($"全てのラウンドで以下のテラー以外が出現した時、{GetActionText(g.Key.Value)}");
                            foreach (var t in terrors)
                                sb.AppendLine($"・{t}");
                        }
                        else
                        {
                            sb.AppendLine($"全てのラウンドで{string.Join(",", terrors)}以外が出現した時、{GetActionText(g.Key.Value)}");
                        }
                    }
                    else
                    {
                        if (useBullet)
                        {
                            sb.AppendLine($"全てのラウンドで以下のテラーが出現した時、{GetActionText(g.Key.Value)}");
                            foreach (var t in terrors)
                                sb.AppendLine($"・{t}");
                        }
                        else
                        {
                            sb.AppendLine($"全てのラウンドで{string.Join(",", terrors)}が出現した時、{GetActionText(g.Key.Value)}");
                        }
                    }
                }

                MessageBox.Show(sb.ToString().Trim(), LanguageManager.Translate("設定内容"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            grpAutoSuicide.Controls.Add(autoSuicideSettingsConfirmButton);

            grpAutoSuicide.Height = autoSuicideSettingsConfirmButton.Bottom + 10;
            autoSuicideCheckBox.CheckedChanged += AutoSuicideCheckBox_CheckedChanged;
            AutoSuicideUseDetailCheckBox_CheckedChanged(null, EventArgs.Empty);
            AutoSuicideCheckBox_CheckedChanged(null, EventArgs.Empty);

            rightColumnY = grpAutoSuicide.Bottom + margin;

            GroupBox grpOverlay = new GroupBox();
            grpOverlay.Text = LanguageManager.Translate("オーバーレイ設定");
            grpOverlay.Location = new Point(thirdColumnX, thirdColumnY);
            grpOverlay.Size = new Size(columnWidth, 230);
            grpOverlay.Tag = SettingsCategory.Overlay;
            this.Controls.Add(grpOverlay);

            int overlayInnerMargin = 10;
            int overlayInnerY = 25;

            OverlayVelocityCheckBox = new CheckBox();
            OverlayVelocityCheckBox.Text = LanguageManager.Translate("速度を表示");
            OverlayVelocityCheckBox.AutoSize = true;
            OverlayVelocityCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayVelocityCheckBox);

            overlayInnerY = OverlayVelocityCheckBox.Bottom + 8;

            OverlayAngleCheckBox = new CheckBox();
            OverlayAngleCheckBox.Text = LanguageManager.Translate("角度を表示");
            OverlayAngleCheckBox.AutoSize = true;
            OverlayAngleCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayAngleCheckBox);

            overlayInnerY = OverlayAngleCheckBox.Bottom + 8;

            OverlayTerrorCheckBox = new CheckBox();
            OverlayTerrorCheckBox.Text = LanguageManager.Translate("テラーを表示");
            OverlayTerrorCheckBox.AutoSize = true;
            OverlayTerrorCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayTerrorCheckBox);

            overlayInnerY = OverlayTerrorCheckBox.Bottom + 4;

            OverlayUnboundTerrorDetailsCheckBox = new CheckBox();
            OverlayUnboundTerrorDetailsCheckBox.Text = LanguageManager.Translate("アンバウンドのテラー内容を表示");
            OverlayUnboundTerrorDetailsCheckBox.AutoSize = true;
            OverlayUnboundTerrorDetailsCheckBox.Location = new Point(overlayInnerMargin + 20, overlayInnerY);
            grpOverlay.Controls.Add(OverlayUnboundTerrorDetailsCheckBox);

            overlayInnerY = OverlayUnboundTerrorDetailsCheckBox.Bottom + 8;

            OverlayDamageCheckBox = new CheckBox();
            OverlayDamageCheckBox.Text = LanguageManager.Translate("ダメージを表示");
            OverlayDamageCheckBox.AutoSize = true;
            OverlayDamageCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayDamageCheckBox);

            overlayInnerY = OverlayDamageCheckBox.Bottom + 8;

            OverlayNextRoundCheckBox = new CheckBox();
            OverlayNextRoundCheckBox.Text = LanguageManager.Translate("次ラウンド予測を表示");
            OverlayNextRoundCheckBox.AutoSize = true;
            OverlayNextRoundCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayNextRoundCheckBox);

            overlayInnerY = OverlayNextRoundCheckBox.Bottom + 8;

            OverlayRoundStatusCheckBox = new CheckBox();
            OverlayRoundStatusCheckBox.Text = LanguageManager.Translate("ラウンド状況を表示");
            OverlayRoundStatusCheckBox.AutoSize = true;
            OverlayRoundStatusCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayRoundStatusCheckBox);

            overlayInnerY = OverlayRoundStatusCheckBox.Bottom + 8;

            OverlayRoundHistoryCheckBox = new CheckBox();
            OverlayRoundHistoryCheckBox.Text = LanguageManager.Translate("ラウンドタイプ推移を表示");
            OverlayRoundHistoryCheckBox.AutoSize = true;
            OverlayRoundHistoryCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayRoundHistoryCheckBox);

            overlayInnerY = OverlayRoundHistoryCheckBox.Bottom + 8;

            OverlayRoundStatsCheckBox = new CheckBox();
            OverlayRoundStatsCheckBox.Text = LanguageManager.Translate("ラウンド統計を表示");
            OverlayRoundStatsCheckBox.AutoSize = true;
            OverlayRoundStatsCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayRoundStatsCheckBox);

            overlayInnerY = OverlayRoundStatsCheckBox.Bottom + 8;

            var overlayHistoryCountLabel = new Label();
            overlayHistoryCountLabel.Text = LanguageManager.Translate("履歴表示数:");
            overlayHistoryCountLabel.AutoSize = true;
            overlayHistoryCountLabel.Location = new Point(overlayInnerMargin + 4, overlayInnerY + 4);
            grpOverlay.Controls.Add(overlayHistoryCountLabel);

            OverlayRoundHistoryCountNumeric = new NumericUpDown();
            OverlayRoundHistoryCountNumeric.Minimum = 1;
            OverlayRoundHistoryCountNumeric.Maximum = 10;
            OverlayRoundHistoryCountNumeric.Value = 3;
            OverlayRoundHistoryCountNumeric.Location = new Point(overlayHistoryCountLabel.Right + 10, overlayInnerY);
            OverlayRoundHistoryCountNumeric.Width = 60;
            grpOverlay.Controls.Add(OverlayRoundHistoryCountNumeric);

            overlayInnerY = OverlayRoundHistoryCountNumeric.Bottom + 8;

            OverlayTerrorInfoCheckBox = new CheckBox();
            OverlayTerrorInfoCheckBox.Text = LanguageManager.Translate("テラー詳細情報を表示");
            OverlayTerrorInfoCheckBox.AutoSize = true;
            OverlayTerrorInfoCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayTerrorInfoCheckBox);

            overlayInnerY = OverlayTerrorInfoCheckBox.Bottom + 8;

            OverlayShortcutsCheckBox = new CheckBox();
            OverlayShortcutsCheckBox.Text = LanguageManager.Translate("ショートカットを表示");
            OverlayShortcutsCheckBox.AutoSize = true;
            OverlayShortcutsCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayShortcutsCheckBox);

            overlayInnerY = OverlayShortcutsCheckBox.Bottom + 8;

            OverlayClockCheckBox = new CheckBox();
            OverlayClockCheckBox.Text = LanguageManager.Translate("時計を表示");
            OverlayClockCheckBox.AutoSize = true;
            OverlayClockCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayClockCheckBox);

            overlayInnerY = OverlayClockCheckBox.Bottom + 8;

            OverlayInstanceTimerCheckBox = new CheckBox();
            OverlayInstanceTimerCheckBox.Text = LanguageManager.Translate("滞在タイマーを表示");
            OverlayInstanceTimerCheckBox.AutoSize = true;
            OverlayInstanceTimerCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            grpOverlay.Controls.Add(OverlayInstanceTimerCheckBox);

            overlayInnerY = OverlayInstanceTimerCheckBox.Bottom + 8;

            OverlayInstanceMembersCheckBox = new CheckBox();
            OverlayInstanceMembersCheckBox.Text = LanguageManager.Translate("インスタンスメンバーを表示");
            OverlayInstanceMembersCheckBox.AutoSize = true;
            OverlayInstanceMembersCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            OverlayInstanceMembersCheckBox.Checked = _settings.OverlayShowInstanceMembers;
            OverlayInstanceMembersCheckBox.Enabled = _settings.CloudSyncEnabled;
            grpOverlay.Controls.Add(OverlayInstanceMembersCheckBox);

            overlayInnerY = OverlayInstanceMembersCheckBox.Bottom + 8;

            OverlayVotingCheckBox = new CheckBox();
            OverlayVotingCheckBox.Text = LanguageManager.Translate("投票状況を表示");
            OverlayVotingCheckBox.AutoSize = true;
            OverlayVotingCheckBox.Location = new Point(overlayInnerMargin, overlayInnerY);
            OverlayVotingCheckBox.Checked = _settings.OverlayShowVoting;
            OverlayVotingCheckBox.Enabled = _settings.CloudSyncEnabled;
            grpOverlay.Controls.Add(OverlayVotingCheckBox);

            overlayInnerY = OverlayVotingCheckBox.Bottom + 12;

            var overlayOpacityLabel = new Label();
            overlayOpacityLabel.Text = LanguageManager.Translate("透明度");
            overlayOpacityLabel.AutoSize = true;
            overlayOpacityLabel.Location = new Point(overlayInnerMargin + 4, overlayInnerY + 4);
            grpOverlay.Controls.Add(overlayOpacityLabel);

            OverlayOpacityValueLabel = new Label();
            OverlayOpacityValueLabel.AutoSize = false;
            OverlayOpacityValueLabel.Width = 60;
            OverlayOpacityValueLabel.TextAlign = ContentAlignment.MiddleRight;
            OverlayOpacityValueLabel.Location = new Point(columnWidth - overlayInnerMargin - OverlayOpacityValueLabel.Width, overlayInnerY + 4);
            grpOverlay.Controls.Add(OverlayOpacityValueLabel);

            OverlayOpacityTrackBar = new TrackBar();
            OverlayOpacityTrackBar.Minimum = 20;
            OverlayOpacityTrackBar.Maximum = 100;
            OverlayOpacityTrackBar.TickFrequency = 5;
            OverlayOpacityTrackBar.TickStyle = TickStyle.None;
            OverlayOpacityTrackBar.LargeChange = 5;
            OverlayOpacityTrackBar.SmallChange = 1;
            OverlayOpacityTrackBar.Width = columnWidth - overlayInnerMargin * 2;
            OverlayOpacityTrackBar.Location = new Point(overlayInnerMargin + 4, overlayOpacityLabel.Bottom + 6);
            grpOverlay.Controls.Add(OverlayOpacityTrackBar);
            OverlayOpacityTrackBar.ValueChanged += (s, e) => UpdateOverlayOpacityLabel();

            overlayInnerY = OverlayOpacityTrackBar.Bottom + 8;
            UpdateOverlayOpacityLabel();

            grpOverlay.Height = overlayInnerY + 7;
            thirdColumnY = grpOverlay.Bottom + margin;

            GroupBox roundLogExportGroup = new GroupBox();
            roundLogExportGroup.Text = LanguageManager.Translate("ラウンドログエクスポート");
            roundLogExportGroup.Location = new Point(thirdColumnX, thirdColumnY);
            roundLogExportGroup.Size = new Size(columnWidth, 180);
            roundLogExportGroup.Tag = SettingsCategory.Recording;
            this.Controls.Add(roundLogExportGroup);

            int exportInnerMargin = 10;
            Label roundLogExportDescriptionLabel = new Label();
            roundLogExportDescriptionLabel.AutoSize = true;
            roundLogExportDescriptionLabel.MaximumSize = new Size(columnWidth - exportInnerMargin * 2, 0);
            roundLogExportDescriptionLabel.Location = new Point(exportInnerMargin, 25);
            roundLogExportDescriptionLabel.Text = LanguageManager.Translate("tontrack.meにインポート可能な形式でラウンドログをエクスポートします。\nエクスポート後、インポート画面に出力されたjsonファイルをドラッグアンドドロップしてください。\n利用にはToNTracker+拡張機能のインストールが必要です。");
            roundLogExportGroup.Controls.Add(roundLogExportDescriptionLabel);

            roundLogExportButton = new Button();
            roundLogExportButton.Text = LanguageManager.Translate("ラウンドログをエクスポート");
            //roundLogExportButton.Enabled = false;
            roundLogExportButton.AutoSize = true;
            roundLogExportButton.Location = new Point(exportInnerMargin, roundLogExportDescriptionLabel.Bottom + 12);
            roundLogExportButton.Click += RoundLogExportButton_Click;
            roundLogExportGroup.Controls.Add(roundLogExportButton);

            roundLogExportGroup.Height = roundLogExportButton.Bottom + 20;
            thirdColumnY = roundLogExportGroup.Bottom + margin;

            GroupBox grpAutoRecording = new GroupBox();
            grpAutoRecording.Text = LanguageManager.Translate("自動録画設定");
            grpAutoRecording.Location = new Point(thirdColumnX, thirdColumnY);
            grpAutoRecording.Size = new Size(columnWidth, 360);
            grpAutoRecording.Tag = SettingsCategory.Recording;
            this.Controls.Add(grpAutoRecording);

            int autoRecordingInnerY = 25;

            AutoRecordingEnabledCheckBox = new CheckBox();
            AutoRecordingEnabledCheckBox.Text = LanguageManager.Translate("指定条件でVRChatを自動録画する");
            AutoRecordingEnabledCheckBox.Checked = false;
            AutoRecordingEnabledCheckBox.AutoSize = true;
            AutoRecordingEnabledCheckBox.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(AutoRecordingEnabledCheckBox);

            autoRecordingInnerY = AutoRecordingEnabledCheckBox.Bottom + 8;

            Label autoRecordingWindowTitleLabel = new Label();
            autoRecordingWindowTitleLabel.Text = LanguageManager.Translate("録画対象ウィンドウ");
            autoRecordingWindowTitleLabel.AutoSize = true;
            autoRecordingWindowTitleLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingWindowTitleLabel);

            AutoRecordingWindowTitleTextBox = new TextBox();
            AutoRecordingWindowTitleTextBox.Location = new Point(innerMargin, autoRecordingWindowTitleLabel.Bottom + 4);
            AutoRecordingWindowTitleTextBox.Width = columnWidth - innerMargin * 2;
            grpAutoRecording.Controls.Add(AutoRecordingWindowTitleTextBox);

            autoRecordingInnerY = AutoRecordingWindowTitleTextBox.Bottom + 4;

            Label autoRecordingWindowHelpLabel = new Label();
            autoRecordingWindowHelpLabel.Text = LanguageManager.Translate("ウィンドウ名またはプロセス名の一部を指定できます");
            autoRecordingWindowHelpLabel.AutoSize = true;
            autoRecordingWindowHelpLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingWindowHelpLabel);

            autoRecordingInnerY = autoRecordingWindowHelpLabel.Bottom + 8;

            Label autoRecordingFrameRateLabel = new Label();
            autoRecordingFrameRateLabel.Text = LanguageManager.Translate("録画フレームレート");
            autoRecordingFrameRateLabel.AutoSize = true;
            autoRecordingFrameRateLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingFrameRateLabel);

            AutoRecordingFrameRateNumeric = new NumericUpDown();
            AutoRecordingFrameRateNumeric.Location = new Point(innerMargin, autoRecordingFrameRateLabel.Bottom + 4);
            AutoRecordingFrameRateNumeric.Width = 80;
            AutoRecordingFrameRateNumeric.Minimum = 5;
            AutoRecordingFrameRateNumeric.Maximum = 240;
            AutoRecordingFrameRateNumeric.Value = Math.Min(Math.Max(_settings.AutoRecordingFrameRate, 5), 240);
            AutoRecordingFrameRateNumeric.ValueChanged += AutoRecordingFrameRateNumeric_ValueChanged;
            grpAutoRecording.Controls.Add(AutoRecordingFrameRateNumeric);

            Label autoRecordingFpsLabel = new Label();
            autoRecordingFpsLabel.Text = LanguageManager.Translate("fps");
            autoRecordingFpsLabel.AutoSize = true;
            autoRecordingFpsLabel.Location = new Point(AutoRecordingFrameRateNumeric.Right + 8, AutoRecordingFrameRateNumeric.Top + 4);
            grpAutoRecording.Controls.Add(autoRecordingFpsLabel);

            autoRecordingFrameRateLimitLabel = new Label();
            autoRecordingFrameRateLimitLabel.AutoSize = true;
            autoRecordingFrameRateLimitLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingFrameRateLimitLabel.Location = new Point(innerMargin, AutoRecordingFrameRateNumeric.Bottom + 4);
            autoRecordingFrameRateLimitLabel.Visible = false;
            grpAutoRecording.Controls.Add(autoRecordingFrameRateLimitLabel);

            autoRecordingInnerY = autoRecordingFrameRateLimitLabel.Bottom + 8;

            autoRecordingResolutionLabel = new Label();
            autoRecordingResolutionLabel.Text = LanguageManager.Translate("AutoRecording_ResolutionLabel");
            autoRecordingResolutionLabel.AutoSize = true;
            autoRecordingResolutionLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingResolutionLabel);

            AutoRecordingResolutionComboBox = new ComboBox();
            AutoRecordingResolutionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            AutoRecordingResolutionComboBox.Location = new Point(innerMargin, autoRecordingResolutionLabel.Bottom + 4);
            AutoRecordingResolutionComboBox.Width = columnWidth - innerMargin * 2;
            AutoRecordingResolutionComboBox.DisplayMember = nameof(RecordingResolutionOptionItem.Display);
            AutoRecordingResolutionComboBox.ValueMember = nameof(RecordingResolutionOptionItem.Id);
            AutoRecordingResolutionComboBox.SelectedIndexChanged += AutoRecordingResolutionComboBox_SelectedIndexChanged;
            grpAutoRecording.Controls.Add(AutoRecordingResolutionComboBox);

            autoRecordingInnerY = AutoRecordingResolutionComboBox.Bottom + 10;

            AutoRecordingIncludeOverlayCheckBox = new CheckBox();
            AutoRecordingIncludeOverlayCheckBox.Text = LanguageManager.Translate("AutoRecording_IncludeOverlay");
            AutoRecordingIncludeOverlayCheckBox.AutoSize = true;
            AutoRecordingIncludeOverlayCheckBox.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(AutoRecordingIncludeOverlayCheckBox);

            autoRecordingInnerY = AutoRecordingIncludeOverlayCheckBox.Bottom + 10;

            Label autoRecordingOutputLabel = new Label();
            autoRecordingOutputLabel.Text = LanguageManager.Translate("出力フォルダー");
            autoRecordingOutputLabel.AutoSize = true;
            autoRecordingOutputLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingOutputLabel);

            AutoRecordingOutputDirectoryTextBox = new TextBox();
            AutoRecordingOutputDirectoryTextBox.Location = new Point(innerMargin, autoRecordingOutputLabel.Bottom + 4);
            AutoRecordingOutputDirectoryTextBox.Width = columnWidth - innerMargin * 3 - 90;
            grpAutoRecording.Controls.Add(AutoRecordingOutputDirectoryTextBox);

            autoRecordingBrowseOutputButton = new Button();
            autoRecordingBrowseOutputButton.Text = LanguageManager.Translate("参照...");
            autoRecordingBrowseOutputButton.AutoSize = true;
            autoRecordingBrowseOutputButton.Location = new Point(AutoRecordingOutputDirectoryTextBox.Right + 10, AutoRecordingOutputDirectoryTextBox.Top - 2);
            autoRecordingBrowseOutputButton.Click += (s, e) =>
            {
                BrowseForAutoRecordingOutputDirectory();
                RefreshAutoRecordingControlsState();
            };
            grpAutoRecording.Controls.Add(autoRecordingBrowseOutputButton);

            autoRecordingInnerY = AutoRecordingOutputDirectoryTextBox.Bottom + 8;

            Label autoRecordingFormatLabel = new Label();
            autoRecordingFormatLabel.Text = LanguageManager.Translate("AutoRecording_FormatLabel");
            autoRecordingFormatLabel.AutoSize = true;
            autoRecordingFormatLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingFormatLabel);

            AutoRecordingFormatComboBox = new ComboBox();
            AutoRecordingFormatComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            AutoRecordingFormatComboBox.Location = new Point(innerMargin, autoRecordingFormatLabel.Bottom + 4);
            AutoRecordingFormatComboBox.Width = columnWidth - innerMargin * 2;
            AutoRecordingFormatComboBox.DisplayMember = nameof(RecordingFormatOption.Display);
            AutoRecordingFormatComboBox.ValueMember = nameof(RecordingFormatOption.Extension);
            var autoRecordingFormatOptions = CreateAutoRecordingFormatOptions();
            AutoRecordingFormatComboBox.Items.AddRange(autoRecordingFormatOptions.Cast<object>().ToArray());
            AutoRecordingFormatComboBox.SelectedIndexChanged += AutoRecordingFormatComboBox_SelectedIndexChanged;
            grpAutoRecording.Controls.Add(AutoRecordingFormatComboBox);

            Label autoRecordingFormatHelpLabel = new Label();
            autoRecordingFormatHelpLabel.Text = LanguageManager.Translate("AutoRecording_FormatHelp");
            autoRecordingFormatHelpLabel.AutoSize = true;
            autoRecordingFormatHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingFormatHelpLabel.Location = new Point(innerMargin, AutoRecordingFormatComboBox.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingFormatHelpLabel);

            autoRecordingInnerY = autoRecordingFormatHelpLabel.Bottom + 10;

            Label autoRecordingCodecLabel = new Label();
            autoRecordingCodecLabel.Text = LanguageManager.Translate("AutoRecording_VideoCodecLabel");
            autoRecordingCodecLabel.AutoSize = true;
            autoRecordingCodecLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingCodecLabel);

            AutoRecordingCodecComboBox = new ComboBox();
            AutoRecordingCodecComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            AutoRecordingCodecComboBox.Location = new Point(innerMargin, autoRecordingCodecLabel.Bottom + 4);
            AutoRecordingCodecComboBox.Width = columnWidth - innerMargin * 2;
            AutoRecordingCodecComboBox.DisplayMember = nameof(RecordingCodecOption.Display);
            AutoRecordingCodecComboBox.ValueMember = nameof(RecordingCodecOption.CodecId);
            AutoRecordingCodecComboBox.SelectedIndexChanged += AutoRecordingCodecComboBox_SelectedIndexChanged;
            grpAutoRecording.Controls.Add(AutoRecordingCodecComboBox);

            autoRecordingCodecHelpLabel = new Label();
            autoRecordingCodecHelpLabel.Text = LanguageManager.Translate("AutoRecording_VideoCodecHelp");
            autoRecordingCodecHelpLabel.AutoSize = true;
            autoRecordingCodecHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingCodecHelpLabel.Location = new Point(innerMargin, AutoRecordingCodecComboBox.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingCodecHelpLabel);

            autoRecordingInnerY = autoRecordingCodecHelpLabel.Bottom + 10;

            Label autoRecordingVideoBitrateLabel = new Label();
            autoRecordingVideoBitrateLabel.Text = LanguageManager.Translate("AutoRecording_VideoBitrateLabel");
            autoRecordingVideoBitrateLabel.AutoSize = true;
            autoRecordingVideoBitrateLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingVideoBitrateLabel);

            AutoRecordingVideoBitrateNumeric = new NumericUpDown();
            AutoRecordingVideoBitrateNumeric.Location = new Point(innerMargin, autoRecordingVideoBitrateLabel.Bottom + 4);
            AutoRecordingVideoBitrateNumeric.Width = 140;
            AutoRecordingVideoBitrateNumeric.Minimum = 0;
            AutoRecordingVideoBitrateNumeric.Maximum = 500_000_000;
            AutoRecordingVideoBitrateNumeric.Increment = 500_000;
            AutoRecordingVideoBitrateNumeric.ThousandsSeparator = true;
            grpAutoRecording.Controls.Add(AutoRecordingVideoBitrateNumeric);

            autoRecordingVideoBitrateUnitLabel = new Label();
            autoRecordingVideoBitrateUnitLabel.Text = LanguageManager.Translate("AutoRecording_BitrateUnit");
            autoRecordingVideoBitrateUnitLabel.AutoSize = true;
            autoRecordingVideoBitrateUnitLabel.Location = new Point(AutoRecordingVideoBitrateNumeric.Right + 8, AutoRecordingVideoBitrateNumeric.Top + 4);
            grpAutoRecording.Controls.Add(autoRecordingVideoBitrateUnitLabel);

            autoRecordingVideoBitrateHelpLabel = new Label();
            autoRecordingVideoBitrateHelpLabel.Text = LanguageManager.Translate("AutoRecording_VideoBitrateHelp");
            autoRecordingVideoBitrateHelpLabel.AutoSize = true;
            autoRecordingVideoBitrateHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingVideoBitrateHelpLabel.Location = new Point(innerMargin, AutoRecordingVideoBitrateNumeric.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingVideoBitrateHelpLabel);

            autoRecordingInnerY = autoRecordingVideoBitrateHelpLabel.Bottom + 10;

            autoRecordingAudioBitrateLabel = new Label();
            autoRecordingAudioBitrateLabel.Text = LanguageManager.Translate("AutoRecording_AudioBitrateLabel");
            autoRecordingAudioBitrateLabel.AutoSize = true;
            autoRecordingAudioBitrateLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingAudioBitrateLabel);

            AutoRecordingAudioBitrateNumeric = new NumericUpDown();
            AutoRecordingAudioBitrateNumeric.Location = new Point(innerMargin, autoRecordingAudioBitrateLabel.Bottom + 4);
            AutoRecordingAudioBitrateNumeric.Width = 140;
            AutoRecordingAudioBitrateNumeric.Minimum = 0;
            AutoRecordingAudioBitrateNumeric.Maximum = 1_000_000;
            AutoRecordingAudioBitrateNumeric.Increment = 16_000;
            AutoRecordingAudioBitrateNumeric.ThousandsSeparator = true;
            grpAutoRecording.Controls.Add(AutoRecordingAudioBitrateNumeric);

            autoRecordingAudioBitrateUnitLabel = new Label();
            autoRecordingAudioBitrateUnitLabel.Text = LanguageManager.Translate("AutoRecording_BitrateUnit");
            autoRecordingAudioBitrateUnitLabel.AutoSize = true;
            autoRecordingAudioBitrateUnitLabel.Location = new Point(AutoRecordingAudioBitrateNumeric.Right + 8, AutoRecordingAudioBitrateNumeric.Top + 4);
            grpAutoRecording.Controls.Add(autoRecordingAudioBitrateUnitLabel);

            autoRecordingAudioBitrateHelpLabel = new Label();
            autoRecordingAudioBitrateHelpLabel.Text = LanguageManager.Translate("AutoRecording_AudioBitrateHelp");
            autoRecordingAudioBitrateHelpLabel.AutoSize = true;
            autoRecordingAudioBitrateHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingAudioBitrateHelpLabel.Location = new Point(innerMargin, AutoRecordingAudioBitrateNumeric.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingAudioBitrateHelpLabel);

            autoRecordingInnerY = autoRecordingAudioBitrateHelpLabel.Bottom + 10;

            Label autoRecordingHardwareEncoderLabel = new Label();
            autoRecordingHardwareEncoderLabel.Text = LanguageManager.Translate("AutoRecording_HardwareEncoderLabel");
            autoRecordingHardwareEncoderLabel.AutoSize = true;
            autoRecordingHardwareEncoderLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingHardwareEncoderLabel);

            AutoRecordingHardwareEncoderComboBox = new ComboBox();
            AutoRecordingHardwareEncoderComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            AutoRecordingHardwareEncoderComboBox.Location = new Point(innerMargin, autoRecordingHardwareEncoderLabel.Bottom + 4);
            AutoRecordingHardwareEncoderComboBox.Width = columnWidth - innerMargin * 2;
            AutoRecordingHardwareEncoderComboBox.DisplayMember = nameof(HardwareEncoderOption.Display);
            AutoRecordingHardwareEncoderComboBox.ValueMember = nameof(HardwareEncoderOption.Id);
            AutoRecordingHardwareEncoderComboBox.SelectedIndexChanged += AutoRecordingHardwareEncoderComboBox_SelectedIndexChanged;
            grpAutoRecording.Controls.Add(AutoRecordingHardwareEncoderComboBox);

            autoRecordingHardwareEncoderHelpLabel = new Label();
            autoRecordingHardwareEncoderHelpLabel.Text = LanguageManager.Translate("AutoRecording_HardwareEncoderHelp");
            autoRecordingHardwareEncoderHelpLabel.AutoSize = true;
            autoRecordingHardwareEncoderHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingHardwareEncoderHelpLabel.Location = new Point(innerMargin, AutoRecordingHardwareEncoderComboBox.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingHardwareEncoderHelpLabel);

            autoRecordingInnerY = autoRecordingHardwareEncoderHelpLabel.Bottom + 12;

            Label autoRecordingRoundsLabel = new Label();
            autoRecordingRoundsLabel.Text = LanguageManager.Translate("録画開始ラウンド");
            autoRecordingRoundsLabel.AutoSize = true;
            autoRecordingRoundsLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingRoundsLabel);

            AutoRecordingRoundTypesListBox = new CheckedListBox();
            AutoRecordingRoundTypesListBox.Location = new Point(innerMargin, autoRecordingRoundsLabel.Bottom + 4);
            AutoRecordingRoundTypesListBox.Size = new Size(columnWidth - innerMargin * 2, 90);
            AutoRecordingRoundTypesListBox.Items.AddRange(KnownRoundTypes);
            grpAutoRecording.Controls.Add(AutoRecordingRoundTypesListBox);

            autoRecordingInnerY = AutoRecordingRoundTypesListBox.Bottom + 8;

            Label autoRecordingTerrorsLabel = new Label();
            autoRecordingTerrorsLabel.Text = LanguageManager.Translate("録画開始テラー");
            autoRecordingTerrorsLabel.AutoSize = true;
            autoRecordingTerrorsLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingTerrorsLabel);

            AutoRecordingTerrorNamesTextBox = new TextBox();
            AutoRecordingTerrorNamesTextBox.Location = new Point(innerMargin, autoRecordingTerrorsLabel.Bottom + 4);
            AutoRecordingTerrorNamesTextBox.Width = columnWidth - innerMargin * 2;
            AutoRecordingTerrorNamesTextBox.Height = 70;
            AutoRecordingTerrorNamesTextBox.Multiline = true;
            grpAutoRecording.Controls.Add(AutoRecordingTerrorNamesTextBox);

            Label autoRecordingTerrorHelpLabel = new Label();
            autoRecordingTerrorHelpLabel.Text = LanguageManager.Translate("複数テラーの入力説明");
            autoRecordingTerrorHelpLabel.AutoSize = true;
            autoRecordingTerrorHelpLabel.Location = new Point(innerMargin, AutoRecordingTerrorNamesTextBox.Bottom + 2);
            grpAutoRecording.Controls.Add(autoRecordingTerrorHelpLabel);

            grpAutoRecording.Height = autoRecordingTerrorHelpLabel.Bottom + 15;

            AutoRecordingEnabledCheckBox.CheckedChanged += (s, e) => RefreshAutoRecordingControlsState();
            AutoRecordingWindowTitleTextBox.Text = _settings.AutoRecordingWindowTitle;
            AutoRecordingFrameRateNumeric.Value = Math.Min(Math.Max(_settings.AutoRecordingFrameRate, 5), 240);
            AutoRecordingOutputDirectoryTextBox.Text = _settings.AutoRecordingOutputDirectory;
            AutoRecordingEnabledCheckBox.Checked = _settings.AutoRecordingEnabled;
            AutoRecordingIncludeOverlayCheckBox.Checked = _settings.AutoRecordingIncludeOverlay;
            SetAutoRecordingFormat(_settings.AutoRecordingOutputExtension);
            RefreshAutoRecordingCodecOptions(_settings.AutoRecordingVideoCodec);
            RefreshAutoRecordingResolutionOptions(_settings.AutoRecordingResolution);
            if (AutoRecordingVideoBitrateNumeric != null)
            {
                AutoRecordingVideoBitrateNumeric.Value = ClampToNumericRange(AutoRecordingVideoBitrateNumeric, _settings.AutoRecordingVideoBitrate);
            }
            if (AutoRecordingAudioBitrateNumeric != null)
            {
                AutoRecordingAudioBitrateNumeric.Value = ClampToNumericRange(AutoRecordingAudioBitrateNumeric, _settings.AutoRecordingAudioBitrate);
            }
            RefreshAutoRecordingHardwareOptions(_settings.AutoRecordingHardwareEncoder);
            SetAutoRecordingRoundTypes(_settings.AutoRecordingRoundTypes);
            SetAutoRecordingTerrors(_settings.AutoRecordingTerrors);
            RefreshAutoRecordingControlsState();

            thirdColumnY += grpAutoRecording.Height + margin;

            currentY = currentY + grpOsc.Bottom + margin;

            GroupBox grpDisplay = new GroupBox();
            grpDisplay.Text = LanguageManager.Translate("表示設定");
            grpDisplay.Location = new Point(margin, currentY);
            grpDisplay.Size = new Size(columnWidth, 100);
            grpDisplay.Tag = SettingsCategory.General;
            this.Controls.Add(grpDisplay);

            ShowStatsCheckBox = new CheckBox();
            ShowStatsCheckBox.Text = LanguageManager.Translate("統計情報を表示する");
            ShowStatsCheckBox.AutoSize = true;
            ShowStatsCheckBox.Location = new Point(10, 20);
            grpDisplay.Controls.Add(ShowStatsCheckBox);

            DebugInfoCheckBox = new CheckBox();
            DebugInfoCheckBox.Text = LanguageManager.Translate("デバッグ情報表示");
            DebugInfoCheckBox.AutoSize = true;
            DebugInfoCheckBox.Location = new Point(ShowStatsCheckBox.Right + 20, 20);
            grpDisplay.Controls.Add(DebugInfoCheckBox);

            ToggleRoundLogCheckBox = new CheckBox();
            ToggleRoundLogCheckBox.Text = LanguageManager.Translate("ラウンドログを表示する");
            ToggleRoundLogCheckBox.AutoSize = true;
            ToggleRoundLogCheckBox.Location = new Point(10, 50);
            grpDisplay.Controls.Add(ToggleRoundLogCheckBox);

            currentY += grpDisplay.Height + margin;

            GroupBox grpFilter = new GroupBox();
            grpFilter.Text = LanguageManager.Translate("フィルター設定");
            grpFilter.Location = new Point(margin, currentY);
            grpFilter.Size = new Size(columnWidth, 70);
            grpFilter.Tag = SettingsCategory.General;
            this.Controls.Add(grpFilter);

            int innerY = 20;
            RoundTypeCheckBox = new CheckBox();
            RoundTypeCheckBox.Text = LanguageManager.Translate("ラウンドタイプ");
            RoundTypeCheckBox.AutoSize = true;
            RoundTypeCheckBox.Location = new Point(innerMargin, innerY);
            grpFilter.Controls.Add(RoundTypeCheckBox);

            TerrorCheckBox = new CheckBox();
            TerrorCheckBox.Text = LanguageManager.Translate("テラー");
            TerrorCheckBox.AutoSize = true;
            TerrorCheckBox.Location = new Point(RoundTypeCheckBox.Right + innerMargin, innerY);
            grpFilter.Controls.Add(TerrorCheckBox);

            AppearanceCountCheckBox = new CheckBox();
            AppearanceCountCheckBox.Text = LanguageManager.Translate("出現回数");
            AppearanceCountCheckBox.AutoSize = true;
            AppearanceCountCheckBox.Location = new Point(TerrorCheckBox.Right + innerMargin, innerY);
            grpFilter.Controls.Add(AppearanceCountCheckBox);

            SurvivalCountCheckBox = new CheckBox();
            SurvivalCountCheckBox.Text = LanguageManager.Translate("生存回数");
            SurvivalCountCheckBox.AutoSize = true;
            SurvivalCountCheckBox.Location = new Point(AppearanceCountCheckBox.Right + innerMargin, innerY);
            grpFilter.Controls.Add(SurvivalCountCheckBox);

            DeathCountCheckBox = new CheckBox();
            DeathCountCheckBox.Text = LanguageManager.Translate("死亡回数");
            DeathCountCheckBox.AutoSize = true;
            DeathCountCheckBox.Location = new Point(SurvivalCountCheckBox.Right + innerMargin, innerY);
            grpFilter.Controls.Add(DeathCountCheckBox);

            SurvivalRateCheckBox = new CheckBox();
            SurvivalRateCheckBox.Text = LanguageManager.Translate("生存率");
            SurvivalRateCheckBox.AutoSize = true;
            SurvivalRateCheckBox.Location = new Point(DeathCountCheckBox.Right + innerMargin, innerY);
            grpFilter.Controls.Add(SurvivalRateCheckBox);

            currentY += grpFilter.Height + margin;

            GroupBox grpAutoLaunch = new GroupBox();
            grpAutoLaunch.Text = LanguageManager.Translate("自動起動設定");
            grpAutoLaunch.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpAutoLaunch.Size = new Size(columnWidth, 240);
            grpAutoLaunch.Tag = SettingsCategory.Other;
            this.Controls.Add(grpAutoLaunch);

            int autoLaunchInnerY = 25;

            AutoLaunchEnabledCheckBox = new CheckBox();
            AutoLaunchEnabledCheckBox.Text = LanguageManager.Translate("外部アプリを自動起動する");
            AutoLaunchEnabledCheckBox.AutoSize = true;
            AutoLaunchEnabledCheckBox.Location = new Point(innerMargin, autoLaunchInnerY);
            grpAutoLaunch.Controls.Add(AutoLaunchEnabledCheckBox);

            autoLaunchInnerY = AutoLaunchEnabledCheckBox.Bottom + 10;

            autoLaunchEntriesGrid = new DataGridView();
            autoLaunchEntriesGrid.Name = "AutoLaunchEntriesGrid";
            autoLaunchEntriesGrid.Location = new Point(innerMargin, autoLaunchInnerY);
            autoLaunchEntriesGrid.Size = new Size(columnWidth - innerMargin * 2, 120);
            autoLaunchEntriesGrid.AllowUserToAddRows = false;
            autoLaunchEntriesGrid.AllowUserToResizeRows = false;
            autoLaunchEntriesGrid.RowHeadersVisible = false;
            autoLaunchEntriesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            autoLaunchEntriesGrid.MultiSelect = false;
            autoLaunchEntriesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            var autoLaunchEnabledColumn = new DataGridViewCheckBoxColumn();
            autoLaunchEnabledColumn.Name = AutoLaunchEnabledColumnName;
            autoLaunchEnabledColumn.HeaderText = LanguageManager.Translate("有効");
            autoLaunchEnabledColumn.Width = 70;
            autoLaunchEnabledColumn.FillWeight = 20;
            autoLaunchEntriesGrid.Columns.Add(autoLaunchEnabledColumn);

            var autoLaunchPathColumn = new DataGridViewTextBoxColumn();
            autoLaunchPathColumn.Name = AutoLaunchPathColumnName;
            autoLaunchPathColumn.HeaderText = LanguageManager.Translate("実行ファイル");
            autoLaunchPathColumn.FillWeight = 55;
            autoLaunchEntriesGrid.Columns.Add(autoLaunchPathColumn);

            var autoLaunchArgumentsColumn = new DataGridViewTextBoxColumn();
            autoLaunchArgumentsColumn.Name = AutoLaunchArgumentsColumnName;
            autoLaunchArgumentsColumn.HeaderText = LanguageManager.Translate("引数");
            autoLaunchArgumentsColumn.FillWeight = 25;
            autoLaunchEntriesGrid.Columns.Add(autoLaunchArgumentsColumn);

            grpAutoLaunch.Controls.Add(autoLaunchEntriesGrid);

            autoLaunchAddButton = new Button();
            autoLaunchAddButton.Text = LanguageManager.Translate("追加");
            autoLaunchAddButton.AutoSize = true;
            autoLaunchAddButton.Location = new Point(innerMargin, autoLaunchEntriesGrid.Bottom + 10);
            autoLaunchAddButton.Click += (s, e) =>
            {
                autoLaunchEntriesGrid.Rows.Add(true, string.Empty, string.Empty);
                if (autoLaunchEntriesGrid.Rows.Count > 0)
                {
                    autoLaunchEntriesGrid.ClearSelection();
                    autoLaunchEntriesGrid.Rows[autoLaunchEntriesGrid.Rows.Count - 1].Selected = true;
                }
                RefreshAutoLaunchControlsState();
            };
            grpAutoLaunch.Controls.Add(autoLaunchAddButton);

            autoLaunchRemoveButton = new Button();
            autoLaunchRemoveButton.Text = LanguageManager.Translate("削除");
            autoLaunchRemoveButton.AutoSize = true;
            autoLaunchRemoveButton.Location = new Point(autoLaunchAddButton.Right + 10, autoLaunchEntriesGrid.Bottom + 10);
            autoLaunchRemoveButton.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in autoLaunchEntriesGrid.SelectedRows)
                {
                    if (!row.IsNewRow)
                    {
                        autoLaunchEntriesGrid.Rows.Remove(row);
                    }
                }
                RefreshAutoLaunchControlsState();
            };
            grpAutoLaunch.Controls.Add(autoLaunchRemoveButton);

            autoLaunchBrowseButton = new Button();
            autoLaunchBrowseButton.Text = LanguageManager.Translate("参照...");
            autoLaunchBrowseButton.AutoSize = true;
            autoLaunchBrowseButton.Location = new Point(autoLaunchRemoveButton.Right + 10, autoLaunchEntriesGrid.Bottom + 10);
            autoLaunchBrowseButton.Click += (s, e) =>
            {
                BrowseForAutoLaunchExecutable();
                RefreshAutoLaunchControlsState();
            };
            grpAutoLaunch.Controls.Add(autoLaunchBrowseButton);

            grpAutoLaunch.Height = autoLaunchBrowseButton.Bottom + 15;

            AutoLaunchEnabledCheckBox.CheckedChanged += (s, e) => RefreshAutoLaunchControlsState();
            autoLaunchEntriesGrid.SelectionChanged += (s, e) => RefreshAutoLaunchControlsState();
            AutoLaunchEnabledCheckBox.Checked = _settings.AutoLaunchEnabled;
            LoadAutoLaunchEntries(_settings.AutoLaunchEntries);

            rightColumnY += grpAutoLaunch.Height + margin;

            GroupBox grpItemMusic = new GroupBox();
            grpItemMusic.Text = LanguageManager.Translate("アイテム音楽ギミック");
            grpItemMusic.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpItemMusic.Size = new Size(columnWidth, 270);
            grpItemMusic.Tag = SettingsCategory.Overlay;
            this.Controls.Add(grpItemMusic);

            int itemMusicInnerY = 25;

            ItemMusicEnabledCheckBox = new CheckBox();
            ItemMusicEnabledCheckBox.Text = LanguageManager.Translate("特定アイテムで音楽を再生する");
            ItemMusicEnabledCheckBox.AutoSize = true;
            ItemMusicEnabledCheckBox.Location = new Point(innerMargin, itemMusicInnerY);
            grpItemMusic.Controls.Add(ItemMusicEnabledCheckBox);

            itemMusicInnerY = ItemMusicEnabledCheckBox.Bottom + 10;

            itemMusicEntriesGrid = new DataGridView();
            itemMusicEntriesGrid.Name = "ItemMusicEntriesGrid";
            itemMusicEntriesGrid.Location = new Point(innerMargin, itemMusicInnerY);
            itemMusicEntriesGrid.Size = new Size(columnWidth - innerMargin * 2, 150);
            itemMusicEntriesGrid.AllowUserToAddRows = false;
            itemMusicEntriesGrid.AllowUserToResizeRows = false;
            itemMusicEntriesGrid.RowHeadersVisible = false;
            itemMusicEntriesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            itemMusicEntriesGrid.MultiSelect = false;
            itemMusicEntriesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            var itemMusicEnabledColumn = new DataGridViewCheckBoxColumn();
            itemMusicEnabledColumn.Name = ItemMusicEnabledColumnName;
            itemMusicEnabledColumn.HeaderText = LanguageManager.Translate("有効");
            itemMusicEnabledColumn.Width = 45;
            itemMusicEnabledColumn.FillWeight = 15;
            itemMusicEntriesGrid.Columns.Add(itemMusicEnabledColumn);

            var itemMusicItemColumn = new DataGridViewTextBoxColumn();
            itemMusicItemColumn.Name = ItemMusicItemColumnName;
            itemMusicItemColumn.HeaderText = LanguageManager.Translate("対象アイテム名");
            itemMusicItemColumn.FillWeight = 35;
            itemMusicEntriesGrid.Columns.Add(itemMusicItemColumn);

            var itemMusicPathColumn = new DataGridViewTextBoxColumn();
            itemMusicPathColumn.Name = ItemMusicPathColumnName;
            itemMusicPathColumn.HeaderText = LanguageManager.Translate("再生する音声");
            itemMusicPathColumn.ToolTipText = LanguageManager.Translate("複数のファイルを '|' で区切って指定するとプレイリストとして順番に再生されます。 YouTube URL (youtube.com / youtu.be) も指定可能で、初回ダウンロード後に再生されます。");
            itemMusicPathColumn.FillWeight = 35;
            itemMusicEntriesGrid.Columns.Add(itemMusicPathColumn);

            var itemMusicMinSpeedColumn = new DataGridViewTextBoxColumn();
            itemMusicMinSpeedColumn.Name = ItemMusicMinSpeedColumnName;
            itemMusicMinSpeedColumn.HeaderText = LanguageManager.Translate("最小速度");
            itemMusicMinSpeedColumn.ValueType = typeof(double);
            itemMusicMinSpeedColumn.DefaultCellStyle.Format = "0.##";
            itemMusicMinSpeedColumn.FillWeight = 15;
            itemMusicEntriesGrid.Columns.Add(itemMusicMinSpeedColumn);

            var itemMusicMaxSpeedColumn = new DataGridViewTextBoxColumn();
            itemMusicMaxSpeedColumn.Name = ItemMusicMaxSpeedColumnName;
            itemMusicMaxSpeedColumn.HeaderText = LanguageManager.Translate("最大速度");
            itemMusicMaxSpeedColumn.ValueType = typeof(double);
            itemMusicMaxSpeedColumn.DefaultCellStyle.Format = "0.##";
            itemMusicMaxSpeedColumn.FillWeight = 15;
            itemMusicEntriesGrid.Columns.Add(itemMusicMaxSpeedColumn);

            var itemMusicVolumeColumn = new DataGridViewTextBoxColumn();
            itemMusicVolumeColumn.Name = ItemMusicVolumeColumnName;
            itemMusicVolumeColumn.HeaderText = LanguageManager.Translate("音量(%)");
            itemMusicVolumeColumn.ValueType = typeof(int);
            itemMusicVolumeColumn.DefaultCellStyle.Format = "0";
            itemMusicVolumeColumn.ToolTipText = LanguageManager.Translate("0〜100の範囲で再生音量を指定します。");
            itemMusicVolumeColumn.FillWeight = 15;
            itemMusicEntriesGrid.Columns.Add(itemMusicVolumeColumn);

            grpItemMusic.Controls.Add(itemMusicEntriesGrid);

            itemMusicAddButton = new Button();
            itemMusicAddButton.Text = LanguageManager.Translate("追加");
            itemMusicAddButton.AutoSize = true;
            itemMusicAddButton.Location = new Point(innerMargin, itemMusicEntriesGrid.Bottom + 10);
            itemMusicAddButton.Click += (s, e) =>
            {
                itemMusicEntriesGrid.Rows.Add(true, string.Empty, string.Empty, 0d, 0d, 100);
                if (itemMusicEntriesGrid.Rows.Count > 0)
                {
                    itemMusicEntriesGrid.ClearSelection();
                    itemMusicEntriesGrid.Rows[itemMusicEntriesGrid.Rows.Count - 1].Selected = true;
                }
                RefreshItemMusicControlsState();
            };
            grpItemMusic.Controls.Add(itemMusicAddButton);

            itemMusicRemoveButton = new Button();
            itemMusicRemoveButton.Text = LanguageManager.Translate("削除");
            itemMusicRemoveButton.AutoSize = true;
            itemMusicRemoveButton.Location = new Point(itemMusicAddButton.Right + 10, itemMusicEntriesGrid.Bottom + 10);
            itemMusicRemoveButton.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in itemMusicEntriesGrid.SelectedRows)
                {
                    if (!row.IsNewRow)
                    {
                        itemMusicEntriesGrid.Rows.Remove(row);
                    }
                }
                RefreshItemMusicControlsState();
            };
            grpItemMusic.Controls.Add(itemMusicRemoveButton);

            itemMusicBrowseButton = new Button();
            itemMusicBrowseButton.Text = LanguageManager.Translate("参照...");
            itemMusicBrowseButton.AutoSize = true;
            itemMusicBrowseButton.Location = new Point(itemMusicRemoveButton.Right + 10, itemMusicEntriesGrid.Bottom + 10);
            itemMusicBrowseButton.Click += (s, e) =>
            {
                BrowseForItemMusicSound();
                RefreshItemMusicControlsState();
            };
            grpItemMusic.Controls.Add(itemMusicBrowseButton);

            grpItemMusic.Height = itemMusicBrowseButton.Bottom + 15;

            ItemMusicEnabledCheckBox.CheckedChanged += (s, e) => RefreshItemMusicControlsState();
            itemMusicEntriesGrid.SelectionChanged += (s, e) => RefreshItemMusicControlsState();
            ItemMusicEnabledCheckBox.Checked = _settings.ItemMusicEnabled;
            LoadItemMusicEntries(_settings.ItemMusicEntries);

            rightColumnY += grpItemMusic.Height + margin;

            GroupBox grpRoundBgm = new GroupBox();
            grpRoundBgm.Text = LanguageManager.Translate("ラウンド/テラーBGM設定");
            grpRoundBgm.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpRoundBgm.Size = new Size(columnWidth, 260);
            grpRoundBgm.Tag = SettingsCategory.Overlay;
            this.Controls.Add(grpRoundBgm);

            int roundBgmInnerY = 25;

            RoundBgmEnabledCheckBox = new CheckBox();
            RoundBgmEnabledCheckBox.Text = LanguageManager.Translate("ラウンド/テラーごとのBGMを再生する");
            RoundBgmEnabledCheckBox.AutoSize = true;
            RoundBgmEnabledCheckBox.Location = new Point(innerMargin, roundBgmInnerY);
            grpRoundBgm.Controls.Add(RoundBgmEnabledCheckBox);

            roundBgmInnerY = RoundBgmEnabledCheckBox.Bottom + 10;

            roundBgmEntriesGrid = new DataGridView();
            roundBgmEntriesGrid.Name = "RoundBgmEntriesGrid";
            roundBgmEntriesGrid.Location = new Point(innerMargin, roundBgmInnerY);
            roundBgmEntriesGrid.Size = new Size(columnWidth - innerMargin * 2, 150);
            roundBgmEntriesGrid.AllowUserToAddRows = false;
            roundBgmEntriesGrid.AllowUserToResizeRows = false;
            roundBgmEntriesGrid.RowHeadersVisible = false;
            roundBgmEntriesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            roundBgmEntriesGrid.MultiSelect = false;
            roundBgmEntriesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            var roundBgmEnabledColumn = new DataGridViewCheckBoxColumn();
            roundBgmEnabledColumn.Name = RoundBgmEnabledColumnName;
            roundBgmEnabledColumn.HeaderText = LanguageManager.Translate("有効");
            roundBgmEnabledColumn.Width = 45;
            roundBgmEnabledColumn.FillWeight = 15;
            roundBgmEntriesGrid.Columns.Add(roundBgmEnabledColumn);

            var roundBgmRoundColumn = new DataGridViewTextBoxColumn();
            roundBgmRoundColumn.Name = RoundBgmRoundColumnName;
            roundBgmRoundColumn.HeaderText = LanguageManager.Translate("ラウンド名");
            roundBgmRoundColumn.FillWeight = 30;
            roundBgmEntriesGrid.Columns.Add(roundBgmRoundColumn);

            var roundBgmTerrorColumn = new DataGridViewTextBoxColumn();
            roundBgmTerrorColumn.Name = RoundBgmTerrorColumnName;
            roundBgmTerrorColumn.HeaderText = LanguageManager.Translate("テラー名");
            roundBgmTerrorColumn.FillWeight = 30;
            roundBgmEntriesGrid.Columns.Add(roundBgmTerrorColumn);

            var roundBgmPathColumn = new DataGridViewTextBoxColumn();
            roundBgmPathColumn.Name = RoundBgmPathColumnName;
            roundBgmPathColumn.HeaderText = LanguageManager.Translate("再生する音声");
            roundBgmPathColumn.ToolTipText = LanguageManager.Translate("複数のファイルを '|' で区切って指定するとプレイリストとして順番に再生されます。 YouTube URL (youtube.com / youtu.be) も指定可能で、初回ダウンロード後に再生されます。");
            roundBgmPathColumn.FillWeight = 40;
            roundBgmEntriesGrid.Columns.Add(roundBgmPathColumn);

            var roundBgmVolumeColumn = new DataGridViewTextBoxColumn();
            roundBgmVolumeColumn.Name = RoundBgmVolumeColumnName;
            roundBgmVolumeColumn.HeaderText = LanguageManager.Translate("音量(%)");
            roundBgmVolumeColumn.ValueType = typeof(int);
            roundBgmVolumeColumn.DefaultCellStyle.Format = "0";
            roundBgmVolumeColumn.ToolTipText = LanguageManager.Translate("0〜100の範囲で再生音量を指定します。");
            roundBgmVolumeColumn.FillWeight = 15;
            roundBgmEntriesGrid.Columns.Add(roundBgmVolumeColumn);

            grpRoundBgm.Controls.Add(roundBgmEntriesGrid);

            roundBgmAddButton = new Button();
            roundBgmAddButton.Text = LanguageManager.Translate("追加");
            roundBgmAddButton.AutoSize = true;
            roundBgmAddButton.Location = new Point(innerMargin, roundBgmEntriesGrid.Bottom + 10);
            roundBgmAddButton.Click += (s, e) =>
            {
                roundBgmEntriesGrid.Rows.Add(true, string.Empty, string.Empty, string.Empty, 100);
                if (roundBgmEntriesGrid.Rows.Count > 0)
                {
                    roundBgmEntriesGrid.ClearSelection();
                    roundBgmEntriesGrid.Rows[roundBgmEntriesGrid.Rows.Count - 1].Selected = true;
                }
                RefreshRoundBgmControlsState();
            };
            grpRoundBgm.Controls.Add(roundBgmAddButton);

            roundBgmRemoveButton = new Button();
            roundBgmRemoveButton.Text = LanguageManager.Translate("削除");
            roundBgmRemoveButton.AutoSize = true;
            roundBgmRemoveButton.Location = new Point(roundBgmAddButton.Right + 10, roundBgmEntriesGrid.Bottom + 10);
            roundBgmRemoveButton.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in roundBgmEntriesGrid.SelectedRows)
                {
                    if (!row.IsNewRow)
                    {
                        roundBgmEntriesGrid.Rows.Remove(row);
                    }
                }
                RefreshRoundBgmControlsState();
            };
            grpRoundBgm.Controls.Add(roundBgmRemoveButton);

            roundBgmBrowseButton = new Button();
            roundBgmBrowseButton.Text = LanguageManager.Translate("参照...");
            roundBgmBrowseButton.AutoSize = true;
            roundBgmBrowseButton.Location = new Point(roundBgmRemoveButton.Right + 10, roundBgmEntriesGrid.Bottom + 10);
            roundBgmBrowseButton.Click += (s, e) =>
            {
                BrowseForRoundBgmSound();
                RefreshRoundBgmControlsState();
            };
            grpRoundBgm.Controls.Add(roundBgmBrowseButton);

            int conflictLabelY = roundBgmBrowseButton.Bottom + 15;

            roundBgmConflictBehaviorLabel = new Label();
            roundBgmConflictBehaviorLabel.AutoSize = true;
            roundBgmConflictBehaviorLabel.Text = LanguageManager.Translate("競合時の優先設定:");
            roundBgmConflictBehaviorLabel.Location = new Point(innerMargin, conflictLabelY);
            grpRoundBgm.Controls.Add(roundBgmConflictBehaviorLabel);

            roundBgmConflictOptions = new List<RoundBgmConflictOption>
            {
                new RoundBgmConflictOption(RoundBgmItemConflictBehavior.ItemMusicPriority, LanguageManager.Translate("アイテムサウンドを優先する")),
                new RoundBgmConflictOption(RoundBgmItemConflictBehavior.RoundBgmPriority, LanguageManager.Translate("ラウンドBGMを優先する")),
                new RoundBgmConflictOption(RoundBgmItemConflictBehavior.PlayBoth, LanguageManager.Translate("両方とも再生する"))
            };

            roundBgmConflictBehaviorComboBox = new ComboBox();
            roundBgmConflictBehaviorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            roundBgmConflictBehaviorComboBox.DisplayMember = nameof(RoundBgmConflictOption.DisplayName);
            roundBgmConflictBehaviorComboBox.ValueMember = nameof(RoundBgmConflictOption.Behavior);
            roundBgmConflictBehaviorComboBox.DataSource = roundBgmConflictOptions;
            roundBgmConflictBehaviorComboBox.Location = new Point(innerMargin, roundBgmConflictBehaviorLabel.Bottom + 5);
            roundBgmConflictBehaviorComboBox.Width = columnWidth - innerMargin * 2;
            grpRoundBgm.Controls.Add(roundBgmConflictBehaviorComboBox);

            grpRoundBgm.Height = roundBgmConflictBehaviorComboBox.Bottom + 15;

            RoundBgmEnabledCheckBox.CheckedChanged += (s, e) => RefreshRoundBgmControlsState();
            roundBgmEntriesGrid.SelectionChanged += (s, e) => RefreshRoundBgmControlsState();
            RoundBgmEnabledCheckBox.Checked = _settings.RoundBgmEnabled;
            LoadRoundBgmEntries(_settings.RoundBgmEntries);
            SetRoundBgmItemConflictBehavior(_settings.RoundBgmItemConflictBehavior);

            rightColumnY += grpRoundBgm.Height + margin;

            BuildNotificationVolumesGroup(margin * 2 + columnWidth, rightColumnY, columnWidth, innerMargin, out int notificationVolumesHeight);
            rightColumnY += notificationVolumesHeight + margin;

            BuildEqualizerGroup(margin * 2 + columnWidth, rightColumnY, columnWidth, innerMargin, out int equalizerHeight);
            rightColumnY += equalizerHeight + margin;

            GroupBox grpAdditional = new GroupBox();
            grpAdditional.Text = LanguageManager.Translate("追加設定");
            grpAdditional.Location = new Point(margin, currentY);
            grpAdditional.Size = new Size(columnWidth, 200);
            grpAdditional.Tag = SettingsCategory.General;
            this.Controls.Add(grpAdditional);

            innerY = 20;
            FixedTerrorColorLabel = new Label();
            FixedTerrorColorLabel.Text = LanguageManager.Translate("テラーの名前の色固定:");
            FixedTerrorColorLabel.AutoSize = true;
            FixedTerrorColorLabel.Location = new Point(innerMargin, innerY);
            grpAdditional.Controls.Add(FixedTerrorColorLabel);

            FixedTerrorColorButton = new Button();
            FixedTerrorColorButton.Text = LanguageManager.Translate("色選択");
            FixedTerrorColorButton.AutoSize = true;
            FixedTerrorColorButton.Location = new Point(FixedTerrorColorLabel.Right + 10, innerY);
            FixedTerrorColorButton.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        FixedTerrorColorLabel.BackColor = cd.Color;
                    }
                }
            };
            grpAdditional.Controls.Add(FixedTerrorColorButton);

            innerY += FixedTerrorColorButton.Height + 10;
            RoundTypeStatsLabel = new Label();
            RoundTypeStatsLabel.Text = LanguageManager.Translate("ラウンドタイプごとの統計表示設定:");
            RoundTypeStatsLabel.AutoSize = true;
            RoundTypeStatsLabel.Location = new Point(innerMargin, innerY);
            grpAdditional.Controls.Add(RoundTypeStatsLabel);

            innerY += RoundTypeStatsLabel.Height + 5;
            RoundTypeStatsListBox = new CheckedListBox();
            RoundTypeStatsListBox.Location = new Point(innerMargin, innerY);
            RoundTypeStatsListBox.Size = new Size(500, 100);
            RoundTypeStatsListBox.Items.Add("クラシック");
            RoundTypeStatsListBox.Items.Add("オルタネイト");
            RoundTypeStatsListBox.Items.Add("パニッシュ");
            RoundTypeStatsListBox.Items.Add("サボタージュ");
            RoundTypeStatsListBox.Items.Add("ブラッドバス");
            RoundTypeStatsListBox.Items.Add("ミッドナイト");
            RoundTypeStatsListBox.Items.Add("走れ！");
            RoundTypeStatsListBox.Items.Add("寒い夜");
            RoundTypeStatsListBox.Items.Add("ミスティックムーン");
            RoundTypeStatsListBox.Items.Add("ブラッドムーン");
            RoundTypeStatsListBox.Items.Add("トワイライト");
            RoundTypeStatsListBox.Items.Add("ソルスティス");
            RoundTypeStatsListBox.Items.Add("霧");
            RoundTypeStatsListBox.Items.Add("8ページ");
            RoundTypeStatsListBox.Items.Add("狂気");
            RoundTypeStatsListBox.Items.Add("ゴースト");
            RoundTypeStatsListBox.Items.Add("ダブルトラブル");
            RoundTypeStatsListBox.Items.Add("EX");
            RoundTypeStatsListBox.Items.Add("アンバウンド");
            grpAdditional.Controls.Add(RoundTypeStatsListBox);

            currentY += grpAdditional.Height + margin;

            GroupBox grpBg = new GroupBox();
            grpBg.Text = LanguageManager.Translate("背景色設定");
            grpBg.Location = new Point(margin, currentY);
            grpBg.Size = new Size(columnWidth, 120);
            grpBg.Tag = SettingsCategory.General;
            this.Controls.Add(grpBg);

            innerY = 20;
            InfoPanelBgLabel = new Label();
            InfoPanelBgLabel.Text = LanguageManager.Translate("情報表示欄背景色:");
            InfoPanelBgLabel.AutoSize = true;
            InfoPanelBgLabel.Location = new Point(innerMargin, innerY);
            InfoPanelBgLabel.BackColor = Color.DarkGray;
            grpBg.Controls.Add(InfoPanelBgLabel);

            InfoPanelBgButton = new Button();
            InfoPanelBgButton.Text = LanguageManager.Translate("色選択");
            InfoPanelBgButton.AutoSize = true;
            InfoPanelBgButton.Location = new Point(InfoPanelBgLabel.Right + 10, innerY);
            InfoPanelBgButton.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        InfoPanelBgLabel.BackColor = cd.Color;
                    }
                }
            };
            grpBg.Controls.Add(InfoPanelBgButton);

            innerY += InfoPanelBgButton.Height + 10;
            StatsBgLabel = new Label();
            StatsBgLabel.Text = LanguageManager.Translate("統計表示欄背景色:");
            StatsBgLabel.AutoSize = true;
            StatsBgLabel.Location = new Point(innerMargin, innerY);
            StatsBgLabel.BackColor = Color.DarkGray;
            grpBg.Controls.Add(StatsBgLabel);

            StatsBgButton = new Button();
            StatsBgButton.Text = LanguageManager.Translate("色選択");
            StatsBgButton.AutoSize = true;
            StatsBgButton.Location = new Point(StatsBgLabel.Right + 10, innerY);
            StatsBgButton.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        StatsBgLabel.BackColor = cd.Color;
                    }
                }
            };
            grpBg.Controls.Add(StatsBgButton);

            innerY += StatsBgButton.Height + 10;
            LogBgLabel = new Label();
            LogBgLabel.Text = LanguageManager.Translate("ラウンドログ背景色:");
            LogBgLabel.AutoSize = true;
            LogBgLabel.Location = new Point(innerMargin, innerY);
            LogBgLabel.BackColor = Color.DarkGray;
            grpBg.Controls.Add(LogBgLabel);

            LogBgButton = new Button();
            LogBgButton.Text = LanguageManager.Translate("色選択");
            LogBgButton.AutoSize = true;
            LogBgButton.Location = new Point(LogBgLabel.Right + 10, innerY);
            LogBgButton.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        LogBgLabel.BackColor = cd.Color;
                    }
                }
            };
            grpBg.Controls.Add(LogBgButton);

            currentY += grpBg.Height + margin;

            int innerMargin2 = 10;
            int apiInnerY = 20;
            GroupBox grpApiKey = new GroupBox();
            grpApiKey.Text = LanguageManager.Translate("ToNRoundCounter-Cloudの設定");
            grpApiKey.Location = new Point(margin, currentY);
            grpApiKey.Size = new Size(columnWidth, 300);
            grpApiKey.Tag = SettingsCategory.Other;
            this.Controls.Add(grpApiKey);

            Label apiKeyDescription = new Label();
            apiKeyDescription.Text = LanguageManager.Translate("ToNRoundCounter-Cloudはセーブコードの複数端末間での全自動同期などの機能を持つクラウドサービスです。\n利用にはAPIキーが必要です。\nAPIキーはwebサイトから取得してください。");
            apiKeyDescription.Size = new Size(grpApiKey.Width - innerMargin2 * 2, 60);
            apiKeyDescription.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(apiKeyDescription);
            apiInnerY += apiKeyDescription.Height + 10;
            grpApiKey.Height = apiInnerY + 50;

            Button openCloudButton = new Button();
            openCloudButton.Text = LanguageManager.Translate("ToNRoundCounter-Cloudを開く");
            openCloudButton.AutoSize = true;
            openCloudButton.Location = new Point(innerMargin2, apiInnerY);
            openCloudButton.Click += async (s, e) =>
            {
                try
                {
                    var cloudPlayerName = _settings.CloudPlayerName?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(cloudPlayerName))
                    {
                        MessageBox.Show(
                            LanguageManager.Translate("クラウドプレイヤー名を設定してください。"),
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    if (string.IsNullOrEmpty(_settings.ApiKey))
                    {
                        MessageBox.Show(
                            LanguageManager.Translate("APIキーを設定してください。"),
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    // CloudWebSocketClientが利用可能か確認
                    if (_cloudClient == null)
                    {
                        MessageBox.Show(
                            LanguageManager.Translate("CloudWebSocketクライアントが利用できません。"),
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        return;
                    }

                    var endpoint = CloudWebSocketUrlTextBox.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        try
                        {
                            _cloudClient.UpdateEndpoint(endpoint);
                        }
                        catch (UriFormatException)
                        {
                            MessageBox.Show(
                                LanguageManager.Translate("Cloud WebSocket URLの形式が正しくありません。"),
                                LanguageManager.Translate("エラー"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }

                    using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var connected = await EnsureCloudClientConnectedAsync(TimeSpan.FromSeconds(8), connectTimeoutCts.Token).ConfigureAwait(true);
                        if (!connected)
                        {
                            MessageBox.Show(
                                LanguageManager.Translate("クラウドサーバーに接続できませんでした。Cloud WebSocket URL とサーバー起動状態を確認してください。"),
                                LanguageManager.Translate("エラー"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }

                    // ワンタイムトークンを生成
                    using var tokenTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var (token, loginUrl) = await _cloudClient.GenerateOneTimeTokenAsync(
                        cloudPlayerName,
                        _settings.ApiKey,
                        tokenTimeoutCts.Token
                    );

                    string dashboardUrl = !string.IsNullOrWhiteSpace(loginUrl)
                        ? loginUrl
                        : "http://localhost:8080/api/auth/one-time-token";

                    _settings.CloudPlayerName = cloudPlayerName;
                    await _settings.SaveAsync();

                    OpenOneTimeTokenPostLogin(dashboardUrl, token);
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show(
                        LanguageManager.Translate("ダッシュボード接続トークンの生成がタイムアウトしました。時間をおいて再試行してください。"),
                        LanguageManager.Translate("エラー"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        LanguageManager.Translate("ダッシュボードを開く際にエラーが発生しました: ") + ex.Message,
                        LanguageManager.Translate("エラー"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            };
            grpApiKey.Controls.Add(openCloudButton);
            apiInnerY += openCloudButton.Height + 10;

            CloudSyncEnabledCheckBox = new CheckBox();
            CloudSyncEnabledCheckBox.Text = LanguageManager.Translate("クラウド同期を有効化");
            CloudSyncEnabledCheckBox.AutoSize = true;
            CloudSyncEnabledCheckBox.Location = new Point(innerMargin2, apiInnerY);
            CloudSyncEnabledCheckBox.Checked = _settings.CloudSyncEnabled;
            CloudSyncEnabledCheckBox.CheckedChanged += (s, e) =>
            {
                OverlayInstanceMembersCheckBox.Enabled = CloudSyncEnabledCheckBox.Checked;
                OverlayVotingCheckBox.Enabled = CloudSyncEnabledCheckBox.Checked;
            };
            grpApiKey.Controls.Add(CloudSyncEnabledCheckBox);
            apiInnerY += CloudSyncEnabledCheckBox.Height + 10;

            Label cloudPlayerNameLabel = new Label();
            cloudPlayerNameLabel.Text = LanguageManager.Translate("クラウドプレイヤー名:");
            cloudPlayerNameLabel.AutoSize = true;
            cloudPlayerNameLabel.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(cloudPlayerNameLabel);

            CloudPlayerNameTextBox = new TextBox();
            CloudPlayerNameTextBox.Width = 250;
            CloudPlayerNameTextBox.Location = new Point(cloudPlayerNameLabel.Right + 10, apiInnerY - 3);
            CloudPlayerNameTextBox.Text = _settings.CloudPlayerName ?? string.Empty;
            CloudPlayerNameTextBox.ReadOnly = true;
            CloudPlayerNameTextBox.TabStop = false;
            CloudPlayerNameTextBox.BackColor = SystemColors.Control;
            grpApiKey.Controls.Add(CloudPlayerNameTextBox);
            apiInnerY += CloudPlayerNameTextBox.Height + 10;

            Label cloudUrlLabel = new Label();
            cloudUrlLabel.Text = LanguageManager.Translate("クラウド WebSocket URL:");
            cloudUrlLabel.AutoSize = true;
            cloudUrlLabel.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(cloudUrlLabel);

            CloudWebSocketUrlTextBox = new TextBox();
            CloudWebSocketUrlTextBox.Width = 400;
            CloudWebSocketUrlTextBox.Location = new Point(cloudUrlLabel.Right + 10, apiInnerY - 3);
            CloudWebSocketUrlTextBox.Text = string.IsNullOrWhiteSpace(_settings.CloudWebSocketUrl)
                ? "ws://localhost:3000/ws"
                : _settings.CloudWebSocketUrl;
            grpApiKey.Controls.Add(CloudWebSocketUrlTextBox);
            apiInnerY += CloudWebSocketUrlTextBox.Height + 10;

            apiKeyLabel = new Label();
            apiKeyLabel.Text = LanguageManager.Translate("APIキー:");
            apiKeyLabel.AutoSize = true;
            apiKeyLabel.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(apiKeyLabel);

            apiKeyTextBox = new TextBox();
            apiKeyTextBox.Name = "ApiKeyTextBox";
            apiKeyTextBox.Text = _settings.ApiKey;
            apiKeyTextBox.Location = new Point(apiKeyLabel.Right + 10, apiInnerY);
            apiKeyTextBox.Width = 300; // テキストボックスの幅を調整
            apiKeyTextBox.ReadOnly = true; // 読み取り専用に設定
            apiKeyTextBox.BackColor = SystemColors.Control;
            grpApiKey.Controls.Add(apiKeyTextBox);

            // APIキー生成/再生成ボタン
            Button generateApiKeyButton = new Button();
            generateApiKeyButton.Text = string.IsNullOrEmpty(_settings.ApiKey)
                ? LanguageManager.Translate("APIキーを生成")
                : LanguageManager.Translate("APIキーを再生成");
            generateApiKeyButton.AutoSize = true;
            generateApiKeyButton.Location = new Point(apiKeyTextBox.Right + 10, apiInnerY - 2);
            generateApiKeyButton.Click += async (s, e) =>
            {
                try
                {
                    var cloudPlayerName = _settings.CloudPlayerName?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(cloudPlayerName))
                    {
                        MessageBox.Show(
                            LanguageManager.Translate("クラウドプレイヤー名を先に設定してください。"),
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    if (_cloudClient == null)
                    {
                        MessageBox.Show(
                            LanguageManager.Translate("クラウド機能が利用できません。\n\nアプリを再起動してクラウド同期を有効にしてください。"),
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    var endpoint = CloudWebSocketUrlTextBox.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        try
                        {
                            _cloudClient.UpdateEndpoint(endpoint);
                        }
                        catch (UriFormatException)
                        {
                            MessageBox.Show(
                                LanguageManager.Translate("Cloud WebSocket URLの形式が正しくありません。"),
                                LanguageManager.Translate("エラー"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }

                    var confirmResult = MessageBox.Show(
                        string.IsNullOrEmpty(_settings.ApiKey)
                            ? LanguageManager.Translate("新しいAPIキーを生成しますか?")
                            : LanguageManager.Translate("APIキーを再生成すると、古いAPIキーは無効になります。続行しますか?"),
                        LanguageManager.Translate("確認"),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (confirmResult != DialogResult.Yes)
                        return;

                    try
                    {
                        using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                        {
                            var connected = await EnsureCloudClientConnectedAsync(TimeSpan.FromSeconds(8), connectTimeoutCts.Token).ConfigureAwait(true);
                            if (!connected)
                            {
                                MessageBox.Show(
                                    LanguageManager.Translate("クラウドサーバーに接続できませんでした。Cloud WebSocket URL とサーバー起動状態を確認してください。"),
                                    LanguageManager.Translate("エラー"),
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning
                                );
                                return;
                            }
                        }

                        // 既存のCloudWebSocketClientを使用してAPIキー生成
                        using var registerTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var (userId, apiKey) = await _cloudClient.RegisterUserAsync(
                            cloudPlayerName,
                            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                            registerTimeoutCts.Token
                        );

                        // UIスレッドで更新（読み取り専用でも表示を確実に更新）
                        if (apiKeyTextBox.InvokeRequired)
                        {
                            apiKeyTextBox.Invoke(new Action(() =>
                            {
                                apiKeyTextBox.ReadOnly = false;
                                apiKeyTextBox.Text = apiKey;
                                apiKeyTextBox.ReadOnly = true;
                                apiKeyTextBox.Refresh();
                            }));
                        }
                        else
                        {
                            apiKeyTextBox.ReadOnly = false;
                            apiKeyTextBox.Text = apiKey;
                            apiKeyTextBox.ReadOnly = true;
                            apiKeyTextBox.Refresh();
                        }

                        _settings.ApiKey = apiKey;
                        _settings.CloudPlayerName = cloudPlayerName;

                        // 設定を永続化
                        await _settings.SaveAsync();

                        MessageBox.Show(
                            LanguageManager.Translate("APIキーが生成されました。このキーは安全に保管してください。"),
                            LanguageManager.Translate("成功"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );

                        if (generateApiKeyButton.InvokeRequired)
                        {
                            generateApiKeyButton.Invoke(new Action(() =>
                            {
                                generateApiKeyButton.Text = LanguageManager.Translate("APIキーを再生成");
                            }));
                        }
                        else
                        {
                            generateApiKeyButton.Text = LanguageManager.Translate("APIキーを再生成");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        MessageBox.Show(
                            "APIキー生成リクエストがタイムアウトしました。\n\nレスポンスが返ってきません。\nアプリを再起動してから、もう一度お試しください。",
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                    catch (Exception ex)
                    {
                        var errorMessage = $"APIキーの生成に失敗しました:\n\n" +
                                         $"エラー: {ex.Message}\n\n" +
                                         $"詳細:\n{ex.ToString()}";

                        MessageBox.Show(
                            errorMessage,
                            LanguageManager.Translate("エラー"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        LanguageManager.Translate("予期しないエラーが発生しました: ") + ex.Message,
                        LanguageManager.Translate("エラー"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            };
            grpApiKey.Controls.Add(generateApiKeyButton);

            apiInnerY += apiKeyTextBox.Height + 10; // テキストボックスの下にスペースを確保
            grpApiKey.Height = apiInnerY + 20; // グループボックスの高さを調整

            currentY += grpApiKey.Height + margin;

            GroupBox grpDiscord = new GroupBox();
            grpDiscord.Text = LanguageManager.Translate("Discord通知設定");
            grpDiscord.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpDiscord.Size = new Size(columnWidth, 130);
            grpDiscord.Tag = SettingsCategory.Other;
            this.Controls.Add(grpDiscord);

            int discordInnerMargin = 10;
            int discordInnerY = 25;

            Label discordDescriptionLabel = new Label();
            discordDescriptionLabel.Text = LanguageManager.Translate("ラウンド結果をDiscordに送信するWebhook URLを設定します。空欄で無効化されます。");
            discordDescriptionLabel.AutoSize = false;
            discordDescriptionLabel.Size = new Size(grpDiscord.Width - discordInnerMargin * 2, 30);
            discordDescriptionLabel.Location = new Point(discordInnerMargin, discordInnerY - 5);
            grpDiscord.Controls.Add(discordDescriptionLabel);

            discordInnerY = discordDescriptionLabel.Bottom + 10;

            Label discordUrlLabel = new Label();
            discordUrlLabel.Text = LanguageManager.Translate("Webhook URL:");
            discordUrlLabel.AutoSize = true;
            discordUrlLabel.Location = new Point(discordInnerMargin, discordInnerY);
            grpDiscord.Controls.Add(discordUrlLabel);

            DiscordWebhookUrlTextBox = new TextBox();
            DiscordWebhookUrlTextBox.Width = grpDiscord.Width - discordUrlLabel.Right - discordInnerMargin * 3;
            DiscordWebhookUrlTextBox.Location = new Point(discordUrlLabel.Right + 10, discordInnerY - 3);
            DiscordWebhookUrlTextBox.Text = _settings.DiscordWebhookUrl;
            grpDiscord.Controls.Add(DiscordWebhookUrlTextBox);

            grpDiscord.Height = DiscordWebhookUrlTextBox.Bottom + 20;

            rightColumnY += grpDiscord.Height + margin;

            ModuleExtensionsPanel = new FlowLayoutPanel();
            ModuleExtensionsPanel.Name = "ModuleExtensionsPanel";
            ModuleExtensionsPanel.AutoSize = true;
            ModuleExtensionsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ModuleExtensionsPanel.FlowDirection = FlowDirection.TopDown;
            ModuleExtensionsPanel.WrapContents = false;
            ModuleExtensionsPanel.Location = new Point(margin, currentY);
            ModuleExtensionsPanel.Width = columnWidth;
            ModuleExtensionsPanel.Tag = SettingsCategory.Other;
            this.Controls.Add(ModuleExtensionsPanel);

            int moduleMargin = margin;
            ModuleExtensionsPanel.ControlAdded += (s, e) =>
            {
                ModuleExtensionsPanel.Width = columnWidth;
                currentY = Math.Max(currentY, ModuleExtensionsPanel.Bottom);
                this.Height = Math.Max(Math.Max(currentY, rightColumnY), Math.Max(thirdColumnY, ModuleExtensionsPanel.Bottom)) + moduleMargin;
            };

            this.Width = totalWidth;
            this.Height = Math.Max(Math.Max(currentY, rightColumnY), thirdColumnY) + margin;

        }

        private static void OpenOneTimeTokenPostLogin(string loginUrl, string token)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"tonround-cloud-login-{Guid.NewGuid():N}.html");
            string html = $@"<!doctype html>
<html lang=""ja"">
<head>
  <meta charset=""utf-8"">
  <meta name=""referrer"" content=""no-referrer"">
  <title>ToNRoundCounter Cloud</title>
</head>
<body>
  <form id=""login"" method=""post"" action=""{WebUtility.HtmlEncode(loginUrl)}"">
    <input type=""hidden"" name=""token"" value=""{WebUtility.HtmlEncode(token)}"">
    <input type=""hidden"" name=""client_version"" value=""1.0.0"">
    <input type=""hidden"" name=""redirect"" value=""1"">
  </form>
  <script>document.getElementById('login').submit();</script>
</body>
</html>";

            File.WriteAllText(tempPath, html, Encoding.UTF8);
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                    File.Delete(tempPath);
                }
                catch
                {
                }
            });
        }

        private async Task<bool> EnsureCloudClientConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (_cloudClient == null)
            {
                return false;
            }

            if (_cloudClient.IsConnected)
            {
                return true;
            }

            // Cloud sync disabled means MainForm did not start the cloud client.
            // Start it on demand so API key/token operations from settings can still work.
            if (!_settings.CloudSyncEnabled && Interlocked.Exchange(ref _cloudClientStartRequested, 1) == 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cloudClient.StartAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger?.Warning(ex, "Cloud client start from settings panel failed.");
                        Interlocked.Exchange(ref _cloudClientStartRequested, 0);
                    }
                });
            }

            var deadline = DateTime.UtcNow + timeout;
            while (!_cloudClient.IsConnected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        private async void RoundLogExportButton_Click(object? sender, EventArgs e)
        {
            try
            {
                await RoundLogExportButton_ClickAsync();
            }
            catch (Exception ex)
            {
                Log.Logger?.Error(ex, "Unhandled error in round log export.");
                MessageBox.Show(
                    $"エクスポート中に予期しないエラーが発生しました: {ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async Task RoundLogExportButton_ClickAsync()
        {
            using SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "JSON Files|*.json|All Files|*.*";
            saveDialog.DefaultExt = "json";
            saveDialog.FileName = $"tontrack_round_logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documentsPath) && Directory.Exists(documentsPath))
            {
                saveDialog.InitialDirectory = documentsPath;
            }

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            string dataDirectory = ResolveRoundLogDataDirectory();

            RoundLogExportOptions options;
            try
            {
                options = RoundLogExportOptions.FromPaths(dataDirectory, saveDialog.FileName);
            }
            catch (Exception ex)
            {
                Log.Logger?.Error(ex, "Failed to prepare round log export options.");
                MessageBox.Show(
                    string.Format(LanguageManager.Translate("ラウンドログのエクスポート準備に失敗しました: {0}"), ex.Message),
                    LanguageManager.Translate("エラー"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var exporter = new RoundLogExporter(Log.Logger);

            try
            {
                roundLogExportButton.Enabled = false;
                UseWaitCursor = true;

                int exportedCount = await exporter.ExportAsync(options);

                MessageBox.Show(
                    string.Format(LanguageManager.Translate("ラウンドログを{0}件エクスポートしました。"), exportedCount),
                    LanguageManager.Translate("完了"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Logger?.Error(ex, "Failed to export round logs.");
                MessageBox.Show(
                    string.Format(LanguageManager.Translate("ラウンドログのエクスポートに失敗しました: {0}"), ex.Message),
                    LanguageManager.Translate("エラー"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                roundLogExportButton.Enabled = true;
            }
        }

        private static string ResolveRoundLogDataDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            return Path.Combine(baseDirectory, "data");
        }

        public void LoadLanguageOptions(string? selectedLanguage)
        {
            if (LanguageComboBox == null)
            {
                return;
            }

            var items = new List<LanguageOption>();
            foreach (var (code, resourceKey) in LanguageDisplayKeys)
            {
                var displayName = LanguageManager.Translate(resourceKey);
                items.Add(new LanguageOption(code, displayName));
            }

            LanguageComboBox.DisplayMember = nameof(LanguageOption.DisplayName);
            LanguageComboBox.ValueMember = nameof(LanguageOption.Code);
            LanguageComboBox.DataSource = items;

            var normalized = LanguageManager.NormalizeCulture(selectedLanguage);
            var selected = items.FirstOrDefault(i => string.Equals(i.Code, normalized, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                LanguageComboBox.SelectedValue = selected.Code;
            }
            else if (items.Count > 0)
            {
                LanguageComboBox.SelectedIndex = 0;
            }
        }

        public string SelectedLanguage => LanguageComboBox?.SelectedValue as string ?? LanguageManager.DefaultCulture;

        public void LoadThemeOptions(IEnumerable<ThemeDescriptor> themes, string selectedThemeKey)
        {
            if (ThemeComboBox == null)
            {
                return;
            }

            var sourceThemes = themes ?? Theme.RegisteredThemes;
            var items = new List<ThemeListItem>();
            if (sourceThemes != null)
            {
                foreach (var descriptor in sourceThemes)
                {
                    if (descriptor == null)
                    {
                        continue;
                    }

                    items.Add(new ThemeListItem(descriptor));
                }
            }

            items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));

            ThemeComboBox.DisplayMember = nameof(ThemeListItem.DisplayName);
            ThemeComboBox.ValueMember = nameof(ThemeListItem.Key);
            ThemeComboBox.DataSource = items;

            var selected = items.FirstOrDefault(i => string.Equals(i.Key, selectedThemeKey, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                ThemeComboBox.SelectedValue = selected.Key;
            }
            else if (items.Count > 0)
            {
                ThemeComboBox.SelectedIndex = 0;
            }
        }

        public string SelectedThemeKey => ThemeComboBox?.SelectedValue as string ?? Theme.DefaultThemeKey;

        public ThemeDescriptor? GetSelectedThemeDescriptor()
        {
            return ThemeComboBox?.SelectedItem is ThemeListItem item ? item.Descriptor : null;
        }

        public T AddModuleExtensionControl<T>(T control) where T : Control
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            if (ModuleExtensionsPanel == null)
            {
                throw new InvalidOperationException("Module extensions panel is not available yet.");
            }

            if (control.Margin == Padding.Empty)
            {
                control.Margin = new Padding(0, 0, 0, 10);
            }
            else if (control.Margin.Bottom == 0)
            {
                control.Margin = new Padding(control.Margin.Left, control.Margin.Top, control.Margin.Right, 10);
            }

            if (control.Dock == DockStyle.None && control.Width <= 0)
            {
                int targetWidth = ModuleExtensionsPanel.ClientSize.Width;
                if (targetWidth <= 0)
                {
                    targetWidth = ModuleExtensionsPanel.Width;
                }

                if (targetWidth > 0)
                {
                    control.Width = Math.Max(control.Width, targetWidth - control.Margin.Horizontal);
                }
            }

            ModuleExtensionsPanel.Controls.Add(control);
            return control;
        }

        public GroupBox AddModuleSettingsGroup(string title)
        {
            if (ModuleExtensionsPanel == null)
            {
                throw new InvalidOperationException("Module extensions panel is not available yet.");
            }

            var group = new GroupBox
            {
                Text = title ?? string.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10)
            };

            AddModuleExtensionControl(group);
            return group;
        }

        public void SetOverlayOpacity(double opacity)
        {
            if (OverlayOpacityTrackBar == null)
            {
                return;
            }

            if (opacity <= 0d)
            {
                opacity = 0.95d;
            }

            int value = (int)Math.Round(opacity * 100d);
            value = Math.Max(OverlayOpacityTrackBar.Minimum, Math.Min(OverlayOpacityTrackBar.Maximum, value));
            OverlayOpacityTrackBar.Value = value;
            UpdateOverlayOpacityLabel();
        }

        public double GetOverlayOpacity()
        {
            if (OverlayOpacityTrackBar == null)
            {
                return 1d;
            }

            return OverlayOpacityTrackBar.Value / 100d;
        }

        private void UpdateOverlayOpacityLabel()
        {
            if (OverlayOpacityTrackBar == null || OverlayOpacityValueLabel == null)
            {
                return;
            }

            OverlayOpacityValueLabel.Text = $"{OverlayOpacityTrackBar.Value}%";
        }

        private sealed class LanguageOption
        {
            public LanguageOption(string code, string displayName)
            {
                Code = code;
                DisplayName = displayName;
            }

            public string Code { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private sealed class ThemeListItem
        {
            public ThemeListItem(ThemeDescriptor descriptor)
            {
                Descriptor = descriptor;
                Key = descriptor.Key;
                DisplayName = descriptor.DisplayName;
            }

            public string Key { get; }

            public string DisplayName { get; }

            public ThemeDescriptor Descriptor { get; }

            public override string ToString() => DisplayName;
        }

        public void LoadAutoLaunchEntries(IEnumerable<AutoLaunchEntry> entries)
        {
            if (autoLaunchEntriesGrid == null)
            {
                return;
            }

            autoLaunchEntriesGrid.Rows.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    autoLaunchEntriesGrid.Rows.Add(entry.Enabled, entry.ExecutablePath ?? string.Empty, entry.Arguments ?? string.Empty);
                }
            }

            RefreshAutoLaunchControlsState();
        }

        public List<AutoLaunchEntry> GetAutoLaunchEntries()
        {
            var result = new List<AutoLaunchEntry>();

            if (autoLaunchEntriesGrid == null)
            {
                return result;
            }

            foreach (DataGridViewRow row in autoLaunchEntriesGrid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                string path = (Convert.ToString(row.Cells[AutoLaunchPathColumnName].Value) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string arguments = (Convert.ToString(row.Cells[AutoLaunchArgumentsColumnName].Value) ?? string.Empty).Trim();
                bool enabled = row.Cells[AutoLaunchEnabledColumnName].Value is bool b && b;

                result.Add(new AutoLaunchEntry
                {
                    Enabled = enabled,
                    ExecutablePath = path,
                    Arguments = arguments
                });
            }

            return result;
        }

        private RecordingFormatOption[] CreateAutoRecordingFormatOptions()
        {
            return new[]
            {
                new RecordingFormatOption("avi", LanguageManager.Translate("AutoRecording_FormatOption_AVI")),
                new RecordingFormatOption("mp4", LanguageManager.Translate("AutoRecording_FormatOption_MP4")),
                new RecordingFormatOption("mov", LanguageManager.Translate("AutoRecording_FormatOption_MOV")),
                new RecordingFormatOption("wmv", LanguageManager.Translate("AutoRecording_FormatOption_WMV")),
                new RecordingFormatOption("mpg", LanguageManager.Translate("AutoRecording_FormatOption_MPG")),
                new RecordingFormatOption("asf", LanguageManager.Translate("AutoRecording_FormatOption_ASF")),
                new RecordingFormatOption("vob", LanguageManager.Translate("AutoRecording_FormatOption_VOB")),
                new RecordingFormatOption("gif", LanguageManager.Translate("AutoRecording_FormatOption_GIF")),
            };
        }

        private void RefreshAutoRecordingCodecOptions(string? preferredCodec = null)
        {
            if (AutoRecordingCodecComboBox == null)
            {
                return;
            }

            if (AutoRecordingFormatComboBox?.SelectedItem is not RecordingFormatOption formatOption)
            {
                AutoRecordingCodecComboBox.Items.Clear();
                autoRecordingCodecSupportsAudio = true;
                UpdateAudioBitrateEnabledState();
                UpdateAutoRecordingFrameRateLimitLabel();
                return;
            }

            string? selection = preferredCodec ?? (AutoRecordingCodecComboBox.SelectedItem as RecordingCodecOption)?.CodecId;
            var items = new List<RecordingCodecOption>();

            try
            {
                foreach (var codec in AutoRecordingService.GetCodecOptions(formatOption.Extension))
                {
                    string display = LanguageManager.Translate(codec.LocalizationKey);
                    items.Add(new RecordingCodecOption(codec.CodecId, display, codec.SupportsAudio));
                }
            }
            catch
            {
                items.Add(new RecordingCodecOption(AutoRecordingService.DefaultCodec, LanguageManager.Translate("AutoRecording_CodecOption_Fallback"), true));
            }

            RecordingCodecOption? selected = null;
            AutoRecordingCodecComboBox.BeginUpdate();
            AutoRecordingCodecComboBox.Items.Clear();
            foreach (var option in items)
            {
                AutoRecordingCodecComboBox.Items.Add(option);
                if (selected == null && selection != null && string.Equals(option.CodecId, selection, StringComparison.OrdinalIgnoreCase))
                {
                    selected = option;
                }
            }
            AutoRecordingCodecComboBox.EndUpdate();

            if (AutoRecordingCodecComboBox.Items.Count > 0)
            {
                AutoRecordingCodecComboBox.SelectedItem = selected ?? AutoRecordingCodecComboBox.Items[0];
            }

            if (AutoRecordingCodecComboBox.SelectedItem is RecordingCodecOption activeOption)
            {
                autoRecordingCodecSupportsAudio = activeOption.SupportsAudio;
            }
            else
            {
                autoRecordingCodecSupportsAudio = true;
            }

            UpdateAudioBitrateEnabledState();
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void RefreshAutoRecordingHardwareOptions(string? preferredId = null)
        {
            if (AutoRecordingHardwareEncoderComboBox == null)
            {
                return;
            }

            string? selection = preferredId ?? (AutoRecordingHardwareEncoderComboBox.SelectedItem as HardwareEncoderOption)?.Id;
            var uiOptions = new List<HardwareEncoderOption>();

            try
            {
                foreach (var option in AutoRecordingService.GetHardwareEncoderOptions())
                {
                    string display = LanguageManager.Translate(option.LocalizationKey);
                    if (!string.IsNullOrWhiteSpace(option.AdapterName))
                    {
                        display = string.Format(CultureInfo.CurrentCulture, display, option.AdapterName);
                    }

                    uiOptions.Add(new HardwareEncoderOption(option.Id, display));
                }
            }
            catch
            {
                uiOptions.Add(new HardwareEncoderOption(AutoRecordingService.DefaultHardwareEncoderOptionId, LanguageManager.Translate("AutoRecording_HardwareOption_Auto")));
                uiOptions.Add(new HardwareEncoderOption(AutoRecordingService.SoftwareHardwareEncoderOptionId, LanguageManager.Translate("AutoRecording_HardwareOption_Software")));
            }

            AutoRecordingHardwareEncoderComboBox.BeginUpdate();
            AutoRecordingHardwareEncoderComboBox.Items.Clear();
            HardwareEncoderOption? selected = null;
            foreach (var option in uiOptions)
            {
                AutoRecordingHardwareEncoderComboBox.Items.Add(option);
                if (selected == null && selection != null && string.Equals(option.Id, selection, StringComparison.OrdinalIgnoreCase))
                {
                    selected = option;
                }
            }
            AutoRecordingHardwareEncoderComboBox.EndUpdate();

            if (AutoRecordingHardwareEncoderComboBox.Items.Count > 0)
            {
                AutoRecordingHardwareEncoderComboBox.SelectedItem = selected ?? AutoRecordingHardwareEncoderComboBox.Items[0];
            }

            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void RefreshAutoRecordingResolutionOptions(string? preferredId = null)
        {
            if (AutoRecordingResolutionComboBox == null)
            {
                return;
            }

            string? selection = preferredId ?? (AutoRecordingResolutionComboBox.SelectedItem as RecordingResolutionOptionItem)?.Id;
            var uiOptions = new List<RecordingResolutionOptionItem>();

            foreach (var option in AutoRecordingService.GetRecordingResolutionOptions())
            {
                string display = LanguageManager.Translate(option.LocalizationKey);
                uiOptions.Add(new RecordingResolutionOptionItem(option.Id, display));
            }

            AutoRecordingResolutionComboBox.BeginUpdate();
            AutoRecordingResolutionComboBox.Items.Clear();
            RecordingResolutionOptionItem? selected = null;
            foreach (var option in uiOptions)
            {
                AutoRecordingResolutionComboBox.Items.Add(option);
                if (selected == null && selection != null && string.Equals(option.Id, selection, StringComparison.OrdinalIgnoreCase))
                {
                    selected = option;
                }
            }
            AutoRecordingResolutionComboBox.EndUpdate();

            if (AutoRecordingResolutionComboBox.Items.Count > 0)
            {
                AutoRecordingResolutionComboBox.SelectedItem = selected ?? AutoRecordingResolutionComboBox.Items[0];
            }

            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void AutoRecordingFormatComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            RefreshAutoRecordingCodecOptions();
        }

        private void AutoRecordingCodecComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (AutoRecordingCodecComboBox?.SelectedItem is RecordingCodecOption option)
            {
                autoRecordingCodecSupportsAudio = option.SupportsAudio;
            }
            else
            {
                autoRecordingCodecSupportsAudio = true;
            }

            UpdateAudioBitrateEnabledState();
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void AutoRecordingFrameRateNumeric_ValueChanged(object? sender, EventArgs e)
        {
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void AutoRecordingHardwareEncoderComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void AutoRecordingResolutionComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void UpdateAudioBitrateEnabledState()
        {
            bool enabled = AutoRecordingEnabledCheckBox?.Checked ?? false;
            bool allowAudio = enabled && autoRecordingCodecSupportsAudio;

            if (AutoRecordingAudioBitrateNumeric != null)
            {
                AutoRecordingAudioBitrateNumeric.Enabled = allowAudio;
            }

            if (autoRecordingAudioBitrateLabel != null)
            {
                autoRecordingAudioBitrateLabel.Enabled = enabled;
            }

            if (autoRecordingAudioBitrateUnitLabel != null)
            {
                autoRecordingAudioBitrateUnitLabel.Enabled = allowAudio;
            }

            if (autoRecordingAudioBitrateHelpLabel != null)
            {
                string key = autoRecordingCodecSupportsAudio
                    ? "AutoRecording_AudioBitrateHelp"
                    : "AutoRecording_AudioBitrateNotAvailable";
                autoRecordingAudioBitrateHelpLabel.Text = LanguageManager.Translate(key);
                autoRecordingAudioBitrateHelpLabel.Enabled = enabled;
            }
        }

        private void UpdateAutoRecordingFrameRateLimitLabel()
        {
            if (autoRecordingFrameRateLimitLabel == null || AutoRecordingFrameRateNumeric == null)
            {
                return;
            }

            bool recordingEnabled = AutoRecordingEnabledCheckBox?.Checked ?? false;
            if (!recordingEnabled)
            {
                autoRecordingFrameRateLimitLabel.Visible = false;
                autoRecordingFrameRateLimitLabel.Text = string.Empty;
                return;
            }

            string codecId = AutoRecordingService.DefaultCodec;
            if (AutoRecordingCodecComboBox?.SelectedItem is RecordingCodecOption codecOption)
            {
                codecId = codecOption.CodecId;
            }

            string hardwareId = AutoRecordingService.DefaultHardwareEncoderOptionId;
            if (AutoRecordingHardwareEncoderComboBox?.SelectedItem is HardwareEncoderOption hardwareOption)
            {
                hardwareId = hardwareOption.Id;
            }

            string resolutionId = AutoRecordingService.DefaultResolutionOptionId;
            if (AutoRecordingResolutionComboBox?.SelectedItem is RecordingResolutionOptionItem resolutionOption)
            {
                resolutionId = resolutionOption.Id;
            }

            int requested = Convert.ToInt32(AutoRecordingFrameRateNumeric.Value);
            int limit = AutoRecordingService.ResolveConfiguredFrameRateLimit(codecId, hardwareId, resolutionId);

            if (requested > limit)
            {
                autoRecordingFrameRateLimitLabel.Text = string.Format(
                    LanguageManager.Translate("AutoRecording_FrameRateLimited"),
                    limit,
                    requested);
                autoRecordingFrameRateLimitLabel.Visible = true;
            }
            else
            {
                autoRecordingFrameRateLimitLabel.Visible = false;
                autoRecordingFrameRateLimitLabel.Text = string.Empty;
            }
        }

        private static decimal ClampToNumericRange(NumericUpDown control, int value)
        {
            decimal decimalValue = value;
            if (decimalValue < control.Minimum)
            {
                return control.Minimum;
            }

            if (decimalValue > control.Maximum)
            {
                return control.Maximum;
            }

            return decimalValue;
        }

        private void SetAutoRecordingFormat(string? extension)
        {
            if (AutoRecordingFormatComboBox == null)
            {
                return;
            }

            string normalized = (extension ?? string.Empty).Trim().TrimStart('.');
            foreach (var option in AutoRecordingFormatComboBox.Items.OfType<RecordingFormatOption>())
            {
                if (string.Equals(option.Extension, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    AutoRecordingFormatComboBox.SelectedItem = option;
                    return;
                }
            }

            if (AutoRecordingFormatComboBox.Items.Count > 0)
            {
                AutoRecordingFormatComboBox.SelectedIndex = 0;
            }
        }

        public string GetAutoRecordingOutputExtension()
        {
            if (AutoRecordingFormatComboBox?.SelectedItem is RecordingFormatOption option)
            {
                return option.Extension;
            }

            return AutoRecordingService.SupportedExtensions[0];
        }

        public string GetAutoRecordingResolution()
        {
            if (AutoRecordingResolutionComboBox?.SelectedItem is RecordingResolutionOptionItem option)
            {
                return option.Id;
            }

            return AutoRecordingService.DefaultResolutionOptionId;
        }

        public string GetAutoRecordingVideoCodec()
        {
            if (AutoRecordingCodecComboBox?.SelectedItem is RecordingCodecOption option)
            {
                return option.CodecId;
            }

            return AutoRecordingService.DefaultCodec;
        }

        public int GetAutoRecordingVideoBitrate()
        {
            if (AutoRecordingVideoBitrateNumeric == null)
            {
                return 0;
            }

            return Convert.ToInt32(AutoRecordingVideoBitrateNumeric.Value);
        }

        public int GetAutoRecordingAudioBitrate()
        {
            if (AutoRecordingAudioBitrateNumeric == null)
            {
                return 0;
            }

            return Convert.ToInt32(AutoRecordingAudioBitrateNumeric.Value);
        }

        public string GetAutoRecordingHardwareEncoder()
        {
            if (AutoRecordingHardwareEncoderComboBox?.SelectedItem is HardwareEncoderOption option)
            {
                return option.Id;
            }

            return AutoRecordingService.DefaultHardwareEncoderOptionId;
        }

        public void SetAutoRecordingRoundTypes(IEnumerable<string>? roundTypes)
        {
            if (AutoRecordingRoundTypesListBox == null)
            {
                return;
            }

            var selections = new HashSet<string>(roundTypes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < AutoRecordingRoundTypesListBox.Items.Count; i++)
            {
                string item = Convert.ToString(AutoRecordingRoundTypesListBox.Items[i]) ?? string.Empty;
                AutoRecordingRoundTypesListBox.SetItemChecked(i, selections.Contains(item));
            }
        }

        public List<string> GetAutoRecordingRoundTypes()
        {
            if (AutoRecordingRoundTypesListBox == null)
            {
                return new List<string>();
            }

            return AutoRecordingRoundTypesListBox.CheckedItems.Cast<object>()
                .Select(item => Convert.ToString(item) ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        public void SetAutoRecordingTerrors(IEnumerable<string>? terrors)
        {
            if (AutoRecordingTerrorNamesTextBox == null)
            {
                return;
            }

            if (terrors == null)
            {
                AutoRecordingTerrorNamesTextBox.Text = string.Empty;
                return;
            }

            AutoRecordingTerrorNamesTextBox.Text = string.Join(Environment.NewLine,
                terrors.Select(t => t ?? string.Empty).Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        public List<string> GetAutoRecordingTerrors()
        {
            if (AutoRecordingTerrorNamesTextBox == null)
            {
                return new List<string>();
            }

            return (AutoRecordingTerrorNamesTextBox.Lines ?? Array.Empty<string>())
                .Select(line => (line ?? string.Empty).Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        public void LoadItemMusicEntries(IEnumerable<ItemMusicEntry> entries)
        {
            if (itemMusicEntriesGrid == null)
            {
                return;
            }

            itemMusicEntriesGrid.Rows.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    itemMusicEntriesGrid.Rows.Add(entry.Enabled,
                        entry.ItemName ?? string.Empty,
                        entry.SoundPath ?? string.Empty,
                        entry.MinSpeed,
                        entry.MaxSpeed,
                        ClampVolumePercent((int)Math.Round(entry.Volume * 100)));
                }
            }

            RefreshItemMusicControlsState();
        }

        public void LoadRoundBgmEntries(IEnumerable<RoundBgmEntry> entries)
        {
            if (roundBgmEntriesGrid == null)
            {
                return;
            }

            roundBgmEntriesGrid.Rows.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    roundBgmEntriesGrid.Rows.Add(entry.Enabled,
                        entry.RoundType ?? string.Empty,
                        entry.TerrorType ?? string.Empty,
                        entry.SoundPath ?? string.Empty,
                        ClampVolumePercent((int)Math.Round(entry.Volume * 100)));
                }
            }

            RefreshRoundBgmControlsState();
        }

        public void SetRoundBgmItemConflictBehavior(RoundBgmItemConflictBehavior behavior)
        {
            if (roundBgmConflictBehaviorComboBox == null || roundBgmConflictOptions == null || roundBgmConflictOptions.Count == 0)
            {
                return;
            }

            var option = roundBgmConflictOptions.FirstOrDefault(o => o.Behavior == behavior)
                         ?? roundBgmConflictOptions.FirstOrDefault(o => o.Behavior == RoundBgmItemConflictBehavior.PlayBoth);

            if (option != null)
            {
                roundBgmConflictBehaviorComboBox.SelectedItem = option;
            }
        }

        public RoundBgmItemConflictBehavior GetRoundBgmItemConflictBehavior()
        {
            if (roundBgmConflictBehaviorComboBox?.SelectedItem is RoundBgmConflictOption option)
            {
                return option.Behavior;
            }

            return RoundBgmItemConflictBehavior.PlayBoth;
        }

        public List<ItemMusicEntry> GetItemMusicEntries()
        {
            var result = new List<ItemMusicEntry>();

            if (itemMusicEntriesGrid == null)
            {
                return result;
            }

            foreach (DataGridViewRow row in itemMusicEntriesGrid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                string itemName = Convert.ToString(row.Cells[ItemMusicItemColumnName].Value) ?? string.Empty;
                string soundPath = Convert.ToString(row.Cells[ItemMusicPathColumnName].Value) ?? string.Empty;
                bool enabled = row.Cells[ItemMusicEnabledColumnName].Value is bool b && b;

                if (string.IsNullOrWhiteSpace(itemName) && string.IsNullOrWhiteSpace(soundPath))
                {
                    continue;
                }

                double minSpeed = Math.Max(0, GetDoubleFromCell(row.Cells[ItemMusicMinSpeedColumnName].Value, 0));
                double maxSpeed = GetDoubleFromCell(row.Cells[ItemMusicMaxSpeedColumnName].Value, minSpeed);
                if (maxSpeed < minSpeed)
                {
                    maxSpeed = minSpeed;
                }

                double volume = GetVolumeFromCell(row.Cells[ItemMusicVolumeColumnName].Value);

                result.Add(new ItemMusicEntry
                {
                    Enabled = enabled,
                    ItemName = itemName.Trim(),
                    SoundPath = soundPath?.Trim() ?? string.Empty,
                    MinSpeed = minSpeed,
                    MaxSpeed = maxSpeed,
                    Volume = volume
                });
            }

            return result;
        }

        public List<RoundBgmEntry> GetRoundBgmEntries()
        {
            var result = new List<RoundBgmEntry>();

            if (roundBgmEntriesGrid == null)
            {
                return result;
            }

            foreach (DataGridViewRow row in roundBgmEntriesGrid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                bool enabled = row.Cells[RoundBgmEnabledColumnName].Value is bool b && b;
                string roundName = Convert.ToString(row.Cells[RoundBgmRoundColumnName].Value) ?? string.Empty;
                string terrorName = Convert.ToString(row.Cells[RoundBgmTerrorColumnName].Value) ?? string.Empty;
                string soundPath = Convert.ToString(row.Cells[RoundBgmPathColumnName].Value) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(roundName) && string.IsNullOrWhiteSpace(terrorName) && string.IsNullOrWhiteSpace(soundPath))
                {
                    continue;
                }

                result.Add(new RoundBgmEntry
                {
                    Enabled = enabled,
                    RoundType = roundName?.Trim() ?? string.Empty,
                    TerrorType = terrorName?.Trim() ?? string.Empty,
                    SoundPath = soundPath?.Trim() ?? string.Empty,
                    Volume = GetVolumeFromCell(row.Cells[RoundBgmVolumeColumnName].Value)
                });
            }

            return result;
        }

        private void BrowseForAutoLaunchExecutable()
        {
            if (autoLaunchEntriesGrid == null || autoLaunchEntriesGrid.SelectedRows.Count == 0)
            {
                return;
            }

            var row = autoLaunchEntriesGrid.SelectedRows[0];

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "実行ファイル|*.exe;*.bat;*.cmd;*.com|すべてのファイル|*.*";
                string current = Convert.ToString(row.Cells[AutoLaunchPathColumnName].Value) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    dialog.FileName = current;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    row.Cells[AutoLaunchPathColumnName].Value = dialog.FileName;
                }
            }
        }

        private void BrowseForItemMusicSound()
        {
            if (itemMusicEntriesGrid == null || itemMusicEntriesGrid.SelectedRows.Count == 0)
            {
                return;
            }

            var row = itemMusicEntriesGrid.SelectedRows[0];

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "音声ファイル|*.mp3;*.wav;*.ogg;*.flac;*.wma;*.aac;*.m4a|すべてのファイル|*.*";
                dialog.Multiselect = true;
                dialog.Title = LanguageManager.Translate("音声ファイル選択 (複数選択でプレイリスト化)");
                string current = Convert.ToString(row.Cells[ItemMusicPathColumnName].Value) ?? string.Empty;
                string firstExisting = current.Split('|')[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstExisting))
                {
                    dialog.FileName = firstExisting;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    row.Cells[ItemMusicPathColumnName].Value = string.Join("|", dialog.FileNames);
                }
            }
        }

        private void BrowseForRoundBgmSound()
        {
            if (roundBgmEntriesGrid == null || roundBgmEntriesGrid.SelectedRows.Count == 0)
            {
                return;
            }

            var row = roundBgmEntriesGrid.SelectedRows[0];

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "音声ファイル|*.mp3;*.wav;*.ogg;*.flac;*.wma;*.aac;*.m4a|すべてのファイル|*.*";
                dialog.Multiselect = true;
                dialog.Title = LanguageManager.Translate("音声ファイル選択 (複数選択でプレイリスト化)");
                string current = Convert.ToString(row.Cells[RoundBgmPathColumnName].Value) ?? string.Empty;
                string firstExisting = current.Split('|')[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstExisting))
                {
                    dialog.FileName = firstExisting;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    row.Cells[RoundBgmPathColumnName].Value = string.Join("|", dialog.FileNames);
                }
            }
        }

        private void BrowseForAutoRecordingOutputDirectory()
        {
            if (AutoRecordingOutputDirectoryTextBox == null)
            {
                return;
            }

            using (var dialog = new FolderBrowserDialog())
            {
                string current = AutoRecordingOutputDirectoryTextBox.Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    AutoRecordingOutputDirectoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void RefreshAutoLaunchControlsState()
        {
            bool enabled = AutoLaunchEnabledCheckBox?.Checked ?? false;
            if (autoLaunchEntriesGrid != null)
            {
                autoLaunchEntriesGrid.Enabled = enabled;
            }
            if (autoLaunchAddButton != null)
            {
                autoLaunchAddButton.Enabled = enabled;
            }

            bool hasSelection = autoLaunchEntriesGrid != null && autoLaunchEntriesGrid.SelectedRows.Count > 0;

            if (autoLaunchRemoveButton != null)
            {
                autoLaunchRemoveButton.Enabled = enabled && hasSelection;
            }

            if (autoLaunchBrowseButton != null)
            {
                autoLaunchBrowseButton.Enabled = enabled && hasSelection;
            }
        }

        private void RefreshAutoRecordingControlsState()
        {
            bool enabled = AutoRecordingEnabledCheckBox?.Checked ?? false;
            if (AutoRecordingWindowTitleTextBox != null)
            {
                AutoRecordingWindowTitleTextBox.Enabled = enabled;
            }
            if (AutoRecordingFrameRateNumeric != null)
            {
                AutoRecordingFrameRateNumeric.Enabled = enabled;
            }
            if (AutoRecordingResolutionComboBox != null)
            {
                AutoRecordingResolutionComboBox.Enabled = enabled;
            }
            if (AutoRecordingIncludeOverlayCheckBox != null)
            {
                AutoRecordingIncludeOverlayCheckBox.Enabled = enabled;
            }
            if (AutoRecordingOutputDirectoryTextBox != null)
            {
                AutoRecordingOutputDirectoryTextBox.Enabled = enabled;
            }
            if (AutoRecordingFormatComboBox != null)
            {
                AutoRecordingFormatComboBox.Enabled = enabled;
            }
            if (autoRecordingBrowseOutputButton != null)
            {
                autoRecordingBrowseOutputButton.Enabled = enabled;
            }
            if (AutoRecordingCodecComboBox != null)
            {
                AutoRecordingCodecComboBox.Enabled = enabled;
            }
            if (autoRecordingCodecHelpLabel != null)
            {
                autoRecordingCodecHelpLabel.Enabled = enabled;
            }
            if (AutoRecordingVideoBitrateNumeric != null)
            {
                AutoRecordingVideoBitrateNumeric.Enabled = enabled;
            }
            if (autoRecordingVideoBitrateHelpLabel != null)
            {
                autoRecordingVideoBitrateHelpLabel.Enabled = enabled;
            }
            if (autoRecordingVideoBitrateUnitLabel != null)
            {
                autoRecordingVideoBitrateUnitLabel.Enabled = enabled;
            }
            if (AutoRecordingRoundTypesListBox != null)
            {
                AutoRecordingRoundTypesListBox.Enabled = enabled;
            }
            if (AutoRecordingTerrorNamesTextBox != null)
            {
                AutoRecordingTerrorNamesTextBox.Enabled = enabled;
            }
            if (AutoRecordingHardwareEncoderComboBox != null)
            {
                AutoRecordingHardwareEncoderComboBox.Enabled = enabled;
            }
            if (autoRecordingHardwareEncoderHelpLabel != null)
            {
                autoRecordingHardwareEncoderHelpLabel.Enabled = enabled;
            }

            if (autoRecordingFrameRateLimitLabel != null)
            {
                autoRecordingFrameRateLimitLabel.Enabled = enabled;
            }

            if (autoRecordingResolutionLabel != null)
            {
                autoRecordingResolutionLabel.Enabled = enabled;
            }

            UpdateAudioBitrateEnabledState();
            UpdateAutoRecordingFrameRateLimitLabel();
        }

        private void BuildNotificationVolumesGroup(int x, int y, int columnWidth, int innerMargin, out int totalHeight)
        {
            GroupBox grpVolumes = new GroupBox();
            grpVolumes.Text = LanguageManager.Translate("サウンド音量設定");
            grpVolumes.Location = new Point(x, y);
            grpVolumes.Size = new Size(columnWidth, 200);
            grpVolumes.Tag = SettingsCategory.General;
            this.Controls.Add(grpVolumes);

            int innerY = 25;

            DbDisplayCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = LanguageManager.Translate("音量をdB表示"),
                Location = new Point(innerMargin, innerY)
            };
            DbDisplayCheckBox.CheckedChanged += (_, _) => RefreshAllVolumeLabels();
            grpVolumes.Controls.Add(DbDisplayCheckBox);
            innerY = DbDisplayCheckBox.Bottom + 6;

            // Output device selector
            Label deviceLabel = new Label
            {
                AutoSize = true,
                Text = LanguageManager.Translate("出力デバイス"),
                Location = new Point(innerMargin, innerY)
            };
            grpVolumes.Controls.Add(deviceLabel);
            innerY = deviceLabel.Bottom + 2;

            AudioOutputDeviceComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(innerMargin, innerY),
                Width = columnWidth - innerMargin * 2,
            };
            PopulateAudioOutputDevices(_settings.AudioOutputDeviceNumber);
            grpVolumes.Controls.Add(AudioOutputDeviceComboBox);
            innerY = AudioOutputDeviceComboBox.Bottom + 8;

            MasterVolumeTrackBar = CreateVolumeTrackBar();
            masterVolumeValueLabel = new Label();
            innerY = AddVolumeRow(grpVolumes, LanguageManager.Translate("マスター音量"), MasterVolumeTrackBar, masterVolumeValueLabel, columnWidth, innerMargin, innerY, _settings.MasterVolume, null);

            MasterMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("全体ミュート"), columnWidth, innerMargin, ref innerY, _settings.MasterMuted);

            innerY += 4;

            NotificationVolumeTrackBar = CreateVolumeTrackBar();
            notificationVolumeValueLabel = new Label();
            innerY = AddVolumeRow(grpVolumes, LanguageManager.Translate("通知音"), NotificationVolumeTrackBar, notificationVolumeValueLabel, columnWidth, innerMargin, innerY, _settings.NotificationSoundVolume, SoundTestKind.Notification);
            NotificationMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("通知音をミュート"), columnWidth, innerMargin, ref innerY, _settings.NotificationSoundMuted);

            AfkVolumeTrackBar = CreateVolumeTrackBar();
            afkVolumeValueLabel = new Label();
            innerY = AddVolumeRow(grpVolumes, LanguageManager.Translate("AFK警告音"), AfkVolumeTrackBar, afkVolumeValueLabel, columnWidth, innerMargin, innerY, _settings.AfkSoundVolume, SoundTestKind.Afk);
            AfkMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("AFK警告音をミュート"), columnWidth, innerMargin, ref innerY, _settings.AfkSoundMuted);

            PunishVolumeTrackBar = CreateVolumeTrackBar();
            punishVolumeValueLabel = new Label();
            innerY = AddVolumeRow(grpVolumes, LanguageManager.Translate("ペナルティ検知音"), PunishVolumeTrackBar, punishVolumeValueLabel, columnWidth, innerMargin, innerY, _settings.PunishSoundVolume, SoundTestKind.Punish);
            PunishMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("ペナルティ検知音をミュート"), columnWidth, innerMargin, ref innerY, _settings.PunishSoundMuted);

            ItemMusicMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("アイテム音楽をミュート"), columnWidth, innerMargin, ref innerY, _settings.ItemMusicMuted);
            RoundBgmMutedCheckBox = AddMuteCheckBox(grpVolumes, LanguageManager.Translate("ラウンドBGMをミュート"), columnWidth, innerMargin, ref innerY, _settings.RoundBgmMuted);

            // Master mute global hotkey
            innerY += 4;
            Label hotkeyLabel = new Label
            {
                AutoSize = true,
                Text = LanguageManager.Translate("マスターミュート切替ホットキー"),
                Location = new Point(innerMargin, innerY)
            };
            grpVolumes.Controls.Add(hotkeyLabel);
            innerY = hotkeyLabel.Bottom + 2;

            MasterMuteHotkeyTextBox = new TextBox
            {
                ReadOnly = true,
                Location = new Point(innerMargin, innerY),
                Width = columnWidth - innerMargin * 2 - 64,
                Text = _settings.MasterMuteHotkey ?? string.Empty,
                Cursor = Cursors.Hand,
            };
            MasterMuteHotkeyTextBox.GotFocus += (_, _) => MasterMuteHotkeyTextBox.BackColor = SystemColors.Highlight;
            MasterMuteHotkeyTextBox.LostFocus += (_, _) => MasterMuteHotkeyTextBox.BackColor = SystemColors.Window;
            MasterMuteHotkeyTextBox.KeyDown += (s, e) =>
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                string formatted = ToNRoundCounter.Infrastructure.Interop.GlobalHotkey.Format(e.KeyData);
                if (!string.IsNullOrEmpty(formatted))
                {
                    MasterMuteHotkeyTextBox.Text = formatted;
                }
            };
            grpVolumes.Controls.Add(MasterMuteHotkeyTextBox);

            Button hotkeyClearButton = new Button
            {
                Text = LanguageManager.Translate("クリア"),
                Location = new Point(MasterMuteHotkeyTextBox.Right + 4, innerY - 1),
                Width = 56,
                Height = MasterMuteHotkeyTextBox.Height + 2,
            };
            hotkeyClearButton.Click += (_, _) => MasterMuteHotkeyTextBox.Text = string.Empty;
            grpVolumes.Controls.Add(hotkeyClearButton);
            innerY = MasterMuteHotkeyTextBox.Bottom + 4;

            grpVolumes.Height = innerY + 10;
            totalHeight = grpVolumes.Height;
        }

        private static CheckBox AddMuteCheckBox(GroupBox group, string labelText, int columnWidth, int innerMargin, ref int y, bool initialValue)
        {
            CheckBox cb = new CheckBox
            {
                AutoSize = true,
                Text = labelText,
                Checked = initialValue,
                Location = new Point(innerMargin, y)
            };
            group.Controls.Add(cb);
            y = cb.Bottom + 4;
            return cb;
        }

        private static readonly string[] EqualizerBandLabels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

        private static readonly (string Name, double[] Gains)[] EqualizerPresets = new[]
        {
            ("フラット",       new double[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 }),
            ("ボーカル強調",   new double[] { -3, -2, -1,  1,  3,  4,  3,  1, -1, -2 }),
            ("バス強調",       new double[] {  6,  5,  4,  2,  0,  0,  0,  0,  0,  0 }),
            ("トレブル強調",   new double[] {  0,  0,  0,  0,  0,  1,  3,  5,  6,  6 }),
            ("ロック",         new double[] {  4,  3,  2,  0, -1,  0,  2,  4,  5,  5 }),
            ("ポップ",         new double[] { -1,  0,  2,  4,  5,  4,  2,  0, -1, -2 }),
            ("ジャズ",         new double[] {  3,  2,  1,  2,  0,  1,  0,  2,  3,  3 }),
            ("クラシック",     new double[] {  4,  3,  2,  2,  0,  0,  0,  2,  3,  4 }),
        };

        private void BuildEqualizerGroup(int x, int y, int columnWidth, int innerMargin, out int totalHeight)
        {
            int bandCount = 10;
            GroupBox grpEq = new GroupBox
            {
                Text = LanguageManager.Translate("イコライザー (10 バンド)"),
                Location = new Point(x, y),
                Size = new Size(columnWidth, 240),
                Tag = SettingsCategory.General,
            };
            this.Controls.Add(grpEq);

            int innerY = 25;

            EqualizerEnabledCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = LanguageManager.Translate("イコライザーを有効化"),
                Checked = _settings.EqualizerEnabled,
                Location = new Point(innerMargin, innerY),
            };
            grpEq.Controls.Add(EqualizerEnabledCheckBox);
            innerY = EqualizerEnabledCheckBox.Bottom + 4;

            Label presetLabel = new Label
            {
                AutoSize = true,
                Text = LanguageManager.Translate("プリセット"),
                Location = new Point(innerMargin, innerY + 4),
            };
            grpEq.Controls.Add(presetLabel);

            EqualizerPresetComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(presetLabel.Right + 6, innerY),
                Width = columnWidth - presetLabel.Right - 6 - innerMargin - 64,
            };
            foreach (var p in EqualizerPresets)
            {
                EqualizerPresetComboBox.Items.Add(p.Name);
            }
            EqualizerPresetComboBox.Items.Add(LanguageManager.Translate("カスタム"));
            EqualizerPresetComboBox.SelectedIndex = EqualizerPresetComboBox.Items.Count - 1;
            grpEq.Controls.Add(EqualizerPresetComboBox);

            Button applyPresetButton = new Button
            {
                Text = LanguageManager.Translate("適用"),
                Location = new Point(EqualizerPresetComboBox.Right + 4, innerY - 1),
                Width = 56,
                Height = EqualizerPresetComboBox.Height + 2,
            };
            applyPresetButton.Click += (_, _) => ApplySelectedEqualizerPreset();
            grpEq.Controls.Add(applyPresetButton);
            innerY = EqualizerPresetComboBox.Bottom + 8;

            // 10 vertical sliders.
            int availableWidth = columnWidth - innerMargin * 2;
            int bandWidth = availableWidth / bandCount;
            int sliderHeight = 110;
            int labelHeight = 14;

            _equalizerBandTrackBars = new TrackBar[bandCount];
            _equalizerBandValueLabels = new Label[bandCount];

            double[] initialGains = _settings.EqualizerBandGains ?? new double[bandCount];

            for (int i = 0; i < bandCount; i++)
            {
                int bx = innerMargin + i * bandWidth;
                int initialValue = (int)Math.Round(i < initialGains.Length ? initialGains[i] : 0);
                if (initialValue < -12) initialValue = -12;
                if (initialValue > 12) initialValue = 12;

                Label valueLabel = new Label
                {
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = $"{initialValue:+0;-0;0}dB",
                    Location = new Point(bx, innerY),
                    Size = new Size(bandWidth, labelHeight),
                    Font = new Font(this.Font.FontFamily, 7f),
                };
                grpEq.Controls.Add(valueLabel);

                TrackBar bar = new TrackBar
                {
                    Orientation = Orientation.Vertical,
                    Minimum = -12,
                    Maximum = 12,
                    TickFrequency = 3,
                    Value = initialValue,
                    Size = new Size(bandWidth, sliderHeight),
                    Location = new Point(bx, valueLabel.Bottom),
                    AutoSize = false,
                };
                int captured = i;
                bar.ValueChanged += (_, _) =>
                {
                    int v = bar.Value;
                    valueLabel.Text = $"{v:+0;-0;0}dB";
                    // mark as custom on user edit (not when applying preset).
                    if (!_suppressEqPresetReset && EqualizerPresetComboBox != null)
                    {
                        EqualizerPresetComboBox.SelectedIndex = EqualizerPresetComboBox.Items.Count - 1;
                    }
                };

                Label freqLabel = new Label
                {
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = EqualizerBandLabels[i],
                    Location = new Point(bx, bar.Bottom),
                    Size = new Size(bandWidth, labelHeight),
                    Font = new Font(this.Font.FontFamily, 7f),
                };
                grpEq.Controls.Add(bar);
                grpEq.Controls.Add(freqLabel);

                _equalizerBandTrackBars[i] = bar;
                _equalizerBandValueLabels[i] = valueLabel;
            }

            int bottom = (_equalizerBandTrackBars.Length > 0 ? _equalizerBandTrackBars[0].Bottom : innerY) + labelHeight + 10;
            grpEq.Height = bottom + 6;
            totalHeight = grpEq.Height;
        }

        private bool _suppressEqPresetReset;

        private void ApplySelectedEqualizerPreset()
        {
            if (EqualizerPresetComboBox == null || _equalizerBandTrackBars.Length == 0) return;
            int idx = EqualizerPresetComboBox.SelectedIndex;
            if (idx < 0 || idx >= EqualizerPresets.Length) return; // "カスタム" is past presets
            double[] gains = EqualizerPresets[idx].Gains;
            _suppressEqPresetReset = true;
            try
            {
                for (int i = 0; i < _equalizerBandTrackBars.Length; i++)
                {
                    int v = (int)Math.Round(i < gains.Length ? gains[i] : 0);
                    if (v < -12) v = -12;
                    if (v > 12) v = 12;
                    _equalizerBandTrackBars[i].Value = v;
                }
            }
            finally
            {
                _suppressEqPresetReset = false;
            }
        }

        public bool GetEqualizerEnabled() => EqualizerEnabledCheckBox?.Checked ?? false;
        public void SetEqualizerEnabled(bool value) { if (EqualizerEnabledCheckBox != null) EqualizerEnabledCheckBox.Checked = value; }

        public double[] GetEqualizerBandGains()
        {
            double[] result = new double[10];
            for (int i = 0; i < result.Length && i < _equalizerBandTrackBars.Length; i++)
            {
                result[i] = _equalizerBandTrackBars[i].Value;
            }
            return result;
        }

        public void SetEqualizerBandGains(double[]? gains)
        {
            if (_equalizerBandTrackBars.Length == 0) return;
            _suppressEqPresetReset = true;
            try
            {
                for (int i = 0; i < _equalizerBandTrackBars.Length; i++)
                {
                    int v = 0;
                    if (gains != null && i < gains.Length)
                    {
                        v = (int)Math.Round(gains[i]);
                        if (v < -12) v = -12;
                        if (v > 12) v = 12;
                    }
                    _equalizerBandTrackBars[i].Value = v;
                }
            }
            finally
            {
                _suppressEqPresetReset = false;
            }
        }

        private static TrackBar CreateVolumeTrackBar()
        {
            return new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                AutoSize = false,
                Height = 32,
            };
        }

        private int AddVolumeRow(GroupBox group, string labelText, TrackBar trackBar, Label valueLabel, int columnWidth, int innerMargin, int y, double initialVolume, SoundTestKind? testKind)
        {
            Label nameLabel = new Label();
            nameLabel.AutoSize = true;
            nameLabel.Text = labelText;
            nameLabel.Location = new Point(innerMargin, y);
            group.Controls.Add(nameLabel);

            int valueLabelWidth = 70;
            valueLabel.AutoSize = false;
            valueLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            valueLabel.Width = valueLabelWidth;
            valueLabel.Height = 20;
            valueLabel.Location = new Point(columnWidth - innerMargin - valueLabelWidth, y);
            group.Controls.Add(valueLabel);

            int trackY = nameLabel.Bottom + 2;
            int trackWidth = columnWidth - innerMargin * 2;
            int testButtonWidth = 0;
            Button? testButton = null;
            if (testKind.HasValue)
            {
                testButtonWidth = 56;
                testButton = new Button
                {
                    Text = LanguageManager.Translate("試聴"),
                    Width = testButtonWidth,
                    Height = 28,
                    Location = new Point(innerMargin + trackWidth - testButtonWidth, trackY + 2)
                };
                var kind = testKind.Value;
                testButton.Click += (_, _) => TestSoundRequested?.Invoke(this, kind);
                group.Controls.Add(testButton);
                trackWidth -= testButtonWidth + 6;
            }

            trackBar.Location = new Point(innerMargin, trackY);
            trackBar.Width = trackWidth;
            int initialPercent = ClampVolumePercent((int)Math.Round(initialVolume * 100));
            trackBar.Value = initialPercent;
            valueLabel.Text = FormatVolumeLabel(initialPercent);
            trackBar.ValueChanged += (s, e) => valueLabel.Text = FormatVolumeLabel(trackBar.Value);
            group.Controls.Add(trackBar);

            _volumeTrackBars.Add(trackBar);
            _volumeValueLabels[trackBar] = valueLabel;

            return trackBar.Bottom + 8;
        }

        private string FormatVolumeLabel(int percent)
        {
            if (DbDisplayCheckBox != null && DbDisplayCheckBox.Checked)
            {
                if (percent <= 0) return "-∞ dB";
                double db = 20.0 * Math.Log10(percent / 100.0);
                return db.ToString("0.0") + " dB";
            }
            return percent + "%";
        }

        private void RefreshAllVolumeLabels()
        {
            foreach (var tb in _volumeTrackBars)
            {
                if (_volumeValueLabels.TryGetValue(tb, out var lbl))
                {
                    lbl.Text = FormatVolumeLabel(tb.Value);
                }
            }
        }

        public double GetNotificationSoundVolume() => (NotificationVolumeTrackBar?.Value ?? 100) / 100.0;
        public double GetAfkSoundVolume() => (AfkVolumeTrackBar?.Value ?? 100) / 100.0;
        public double GetPunishSoundVolume() => (PunishVolumeTrackBar?.Value ?? 100) / 100.0;

        public void SetNotificationSoundVolume(double volume)
        {
            if (NotificationVolumeTrackBar == null) return;
            int percent = ClampVolumePercent((int)Math.Round(volume * 100));
            NotificationVolumeTrackBar.Value = percent;
            if (notificationVolumeValueLabel != null) notificationVolumeValueLabel.Text = FormatVolumeLabel(percent);
        }

        public void SetAfkSoundVolume(double volume)
        {
            if (AfkVolumeTrackBar == null) return;
            int percent = ClampVolumePercent((int)Math.Round(volume * 100));
            AfkVolumeTrackBar.Value = percent;
            if (afkVolumeValueLabel != null) afkVolumeValueLabel.Text = FormatVolumeLabel(percent);
        }

        public void SetPunishSoundVolume(double volume)
        {
            if (PunishVolumeTrackBar == null) return;
            int percent = ClampVolumePercent((int)Math.Round(volume * 100));
            PunishVolumeTrackBar.Value = percent;
            if (punishVolumeValueLabel != null) punishVolumeValueLabel.Text = FormatVolumeLabel(percent);
        }

        public double GetMasterVolume() => (MasterVolumeTrackBar?.Value ?? 100) / 100.0;
        public void SetMasterVolume(double volume)
        {
            if (MasterVolumeTrackBar == null) return;
            int percent = ClampVolumePercent((int)Math.Round(volume * 100));
            MasterVolumeTrackBar.Value = percent;
            if (masterVolumeValueLabel != null) masterVolumeValueLabel.Text = FormatVolumeLabel(percent);
        }

        public bool GetMasterMuted() => MasterMutedCheckBox?.Checked ?? false;
        public void SetMasterMuted(bool value) { if (MasterMutedCheckBox != null) MasterMutedCheckBox.Checked = value; }
        public bool GetNotificationSoundMuted() => NotificationMutedCheckBox?.Checked ?? false;
        public void SetNotificationSoundMuted(bool value) { if (NotificationMutedCheckBox != null) NotificationMutedCheckBox.Checked = value; }
        public bool GetAfkSoundMuted() => AfkMutedCheckBox?.Checked ?? false;
        public void SetAfkSoundMuted(bool value) { if (AfkMutedCheckBox != null) AfkMutedCheckBox.Checked = value; }
        public bool GetPunishSoundMuted() => PunishMutedCheckBox?.Checked ?? false;
        public void SetPunishSoundMuted(bool value) { if (PunishMutedCheckBox != null) PunishMutedCheckBox.Checked = value; }
        public bool GetItemMusicMuted() => ItemMusicMutedCheckBox?.Checked ?? false;
        public void SetItemMusicMuted(bool value) { if (ItemMusicMutedCheckBox != null) ItemMusicMutedCheckBox.Checked = value; }
        public bool GetRoundBgmMuted() => RoundBgmMutedCheckBox?.Checked ?? false;
        public void SetRoundBgmMuted(bool value) { if (RoundBgmMutedCheckBox != null) RoundBgmMutedCheckBox.Checked = value; }

        private sealed class AudioDeviceItem
        {
            public int DeviceNumber { get; }
            public string DisplayName { get; }
            public AudioDeviceItem(int deviceNumber, string displayName)
            {
                DeviceNumber = deviceNumber;
                DisplayName = displayName;
            }
            public override string ToString() => DisplayName;
        }

        private void PopulateAudioOutputDevices(int currentDeviceNumber)
        {
            if (AudioOutputDeviceComboBox == null) return;
            AudioOutputDeviceComboBox.Items.Clear();
            AudioOutputDeviceComboBox.Items.Add(new AudioDeviceItem(-1, LanguageManager.Translate("システム既定 (WAVE_MAPPER)")));
            try
            {
                int count = NAudio.Wave.WaveOut.DeviceCount;
                for (int i = 0; i < count; i++)
                {
                    string name;
                    try
                    {
                        var caps = NAudio.Wave.WaveOut.GetCapabilities(i);
                        name = string.IsNullOrWhiteSpace(caps.ProductName) ? $"Device {i}" : caps.ProductName;
                    }
                    catch
                    {
                        name = $"Device {i}";
                    }
                    AudioOutputDeviceComboBox.Items.Add(new AudioDeviceItem(i, $"{i}: {name}"));
                }
            }
            catch
            {
                // ignore enumeration failures; default item remains.
            }
            SelectAudioOutputDevice(currentDeviceNumber);
        }

        private void SelectAudioOutputDevice(int deviceNumber)
        {
            if (AudioOutputDeviceComboBox == null) return;
            for (int i = 0; i < AudioOutputDeviceComboBox.Items.Count; i++)
            {
                if (AudioOutputDeviceComboBox.Items[i] is AudioDeviceItem item && item.DeviceNumber == deviceNumber)
                {
                    AudioOutputDeviceComboBox.SelectedIndex = i;
                    return;
                }
            }
            if (AudioOutputDeviceComboBox.Items.Count > 0)
            {
                AudioOutputDeviceComboBox.SelectedIndex = 0;
            }
        }

        public int GetAudioOutputDeviceNumber()
        {
            if (AudioOutputDeviceComboBox?.SelectedItem is AudioDeviceItem item) return item.DeviceNumber;
            return -1;
        }

        public void SetAudioOutputDeviceNumber(int deviceNumber) => SelectAudioOutputDevice(deviceNumber);

        public string GetMasterMuteHotkey() => MasterMuteHotkeyTextBox?.Text?.Trim() ?? string.Empty;
        public void SetMasterMuteHotkey(string value) { if (MasterMuteHotkeyTextBox != null) MasterMuteHotkeyTextBox.Text = value ?? string.Empty; }

        private void RefreshItemMusicControlsState()
        {
            bool enabled = ItemMusicEnabledCheckBox?.Checked ?? false;
            if (itemMusicEntriesGrid != null)
            {
                itemMusicEntriesGrid.Enabled = enabled;
            }
            if (itemMusicAddButton != null)
            {
                itemMusicAddButton.Enabled = enabled;
            }

            bool hasSelection = itemMusicEntriesGrid != null && itemMusicEntriesGrid.SelectedRows.Count > 0;

            if (itemMusicRemoveButton != null)
            {
                itemMusicRemoveButton.Enabled = enabled && hasSelection;
            }

            if (itemMusicBrowseButton != null)
            {
                itemMusicBrowseButton.Enabled = enabled && hasSelection;
            }
        }

        private void RefreshRoundBgmControlsState()
        {
            bool enabled = RoundBgmEnabledCheckBox?.Checked ?? false;
            if (roundBgmEntriesGrid != null)
            {
                roundBgmEntriesGrid.Enabled = enabled;
            }

            if (roundBgmAddButton != null)
            {
                roundBgmAddButton.Enabled = enabled;
            }

            bool hasSelection = roundBgmEntriesGrid != null && roundBgmEntriesGrid.SelectedRows.Count > 0;

            if (roundBgmRemoveButton != null)
            {
                roundBgmRemoveButton.Enabled = enabled && hasSelection;
            }

            if (roundBgmBrowseButton != null)
            {
                roundBgmBrowseButton.Enabled = enabled && hasSelection;
            }

            if (roundBgmConflictBehaviorComboBox != null)
            {
                roundBgmConflictBehaviorComboBox.Enabled = enabled;
            }

            if (roundBgmConflictBehaviorLabel != null)
            {
                roundBgmConflictBehaviorLabel.Enabled = enabled;
            }
        }

        private static double GetDoubleFromCell(object? value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    return fallback;
                }
                return d;
            }

            string text = Convert.ToString(value) ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                if (double.IsNaN(parsed) || double.IsInfinity(parsed))
                {
                    return fallback;
                }
                return parsed;
            }

            return fallback;
        }

        private static double GetVolumeFromCell(object? value)
        {
            int percent = 100;
            if (value is int i)
            {
                percent = i;
            }
            else if (value != null)
            {
                string text = Convert.ToString(value) ?? string.Empty;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsedInt) ||
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    percent = parsedInt;
                }
                else if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsedDouble) ||
                         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedDouble))
                {
                    percent = (int)Math.Round(parsedDouble);
                }
            }

            percent = ClampVolumePercent(percent);
            return percent / 100.0;
        }

        private static int ClampVolumePercent(int percent)
        {
            if (percent < 0) return 0;
            if (percent > 100) return 100;
            return percent;
        }

        private sealed class RecordingFormatOption
        {
            public RecordingFormatOption(string extension, string display)
            {
                Extension = extension;
                Display = display;
            }

            public string Extension { get; }

            public string Display { get; }
        }

        private sealed class RecordingResolutionOptionItem
        {
            public RecordingResolutionOptionItem(string id, string display)
            {
                Id = id;
                Display = display;
            }

            public string Id { get; }

            public string Display { get; }

            public override string ToString()
            {
                return Display;
            }
        }

        private sealed class RecordingCodecOption
        {
            public RecordingCodecOption(string codecId, string display, bool supportsAudio)
            {
                CodecId = codecId;
                Display = display;
                SupportsAudio = supportsAudio;
            }

            public string CodecId { get; }

            public string Display { get; }

            public bool SupportsAudio { get; }

            public override string ToString()
            {
                return Display;
            }
        }

        private sealed class HardwareEncoderOption
        {
            public HardwareEncoderOption(string id, string display)
            {
                Id = id;
                Display = display;
            }

            public string Id { get; }

            public string Display { get; }

            public override string ToString()
            {
                return Display;
            }
        }

        private sealed class RoundBgmConflictOption
        {
            public RoundBgmConflictOption(RoundBgmItemConflictBehavior behavior, string displayName)
            {
                Behavior = behavior;
                DisplayName = displayName;
            }

            public RoundBgmItemConflictBehavior Behavior { get; }

            public string DisplayName { get; }
        }

        private string[] GenerateAutoSuicideLines()
        {
            var lines = new List<string>();
            for (int i = 0; i < autoSuicideRoundListBox.Items.Count; i++)
            {
                string round = autoSuicideRoundListBox.Items[i]?.ToString() ?? string.Empty;
                bool isChecked = autoSuicideRoundListBox.GetItemChecked(i);
                lines.Add($"{round}::{(isChecked ? 1 : 0)}");
            }
            return lines.ToArray();
        }

        private void UpdateAutoSuicideDetailAutoLines()
        {
            if (autoSuicideUseDetailCheckBox != null && autoSuicideUseDetailCheckBox.Checked)
                return;
            var custom = autoSuicideDetailTextBox.Lines.Skip(autoSuicideAutoRuleCount).ToList();
            var autoLines = GenerateAutoSuicideLines();
            autoSuicideAutoRuleCount = autoLines.Length;
            autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, autoLines.Concat(custom));
        }

        private (List<AutoSuicideRule> cleaned, List<string> unparsedLines) CleanRules(IEnumerable<string> lines)
        {
            var rules = new List<AutoSuicideRule>();
            var unparsedLines = new List<string>();
            foreach (var line in lines)
            {
                if (AutoSuicideRule.TryParse(line, out var r) && r != null)
                {
                    rules.Add(r);
                }
                else
                {
                    unparsedLines.Add(line);
                }
            }
            var cleaned = new List<AutoSuicideRule>();
            for (int i = rules.Count - 1; i >= 0; i--)
            {
                var r = rules[i];
                bool redundant = cleaned.Any(c => c.Covers(r));
                if (!redundant)
                    cleaned.Add(r);
            }
            cleaned.Reverse();
            return (cleaned, unparsedLines);
        }

        private (List<string> autoLines, List<string> customLines) SplitAutoAndCustom(List<AutoSuicideRule> rules)
        {
            var autoLines = new List<string>();
            var customLines = new List<string>();
            foreach (var r in rules)
            {
                string line = r.ToString();
                if (IsSimpleAutoSuicideRule(r))
                {
                    autoLines.Add(line);
                }
                else
                {
                    customLines.Add(line);
                }
            }
            return (autoLines, customLines);
        }

        private bool IsSimpleAutoSuicideRule(AutoSuicideRule rule)
        {
            return !rule.RoundNegate &&
                   !rule.TerrorNegate &&
                   rule.Terror == null &&
                   rule.Round != null &&
                   autoSuicideRoundListBox.Items.Contains(rule.Round);
        }

        private static bool IsComplexRoundRule(AutoSuicideRule rule)
        {
            return rule.Round != null &&
                   rule.TerrorExpression != null &&
                   rule.Terror == null &&
                   !rule.RoundNegate;
        }

        public List<string> GetCustomAutoSuicideLines()
        {
            if (autoSuicideUseDetailCheckBox != null && autoSuicideUseDetailCheckBox.Checked)
            {
                return autoSuicideDetailTextBox.Lines
                    .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            }
            var (cleaned, unparsedLines) = CleanRules(autoSuicideDetailTextBox.Lines);
            if (unparsedLines.Any())
            {
                Log.Warning("Failed to parse auto-suicide rule lines: {Lines}", string.Join(", ", unparsedLines));
                MessageBox.Show(LanguageManager.Translate("解析できなかった行があります:") + Environment.NewLine + string.Join(Environment.NewLine, unparsedLines),
                    LanguageManager.Translate("警告"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            var split = SplitAutoAndCustom(cleaned);
            return split.customLines;
        }

        public void CleanAutoSuicideDetailRules()
        {
            if (autoSuicideUseDetailCheckBox != null && autoSuicideUseDetailCheckBox.Checked)
            {
                var (cleaned, unparsedLines) = CleanRules(autoSuicideDetailTextBox.Lines);
                if (unparsedLines.Any())
                {
                    Log.Warning("Failed to parse auto-suicide rule lines: {Lines}", string.Join(", ", unparsedLines));
                    MessageBox.Show(LanguageManager.Translate("解析できなかった行があります:") + Environment.NewLine + string.Join(Environment.NewLine, unparsedLines),
                        LanguageManager.Translate("警告"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, cleaned.Select(r => r.ToString()));
            }
            else
            {
                var (cleanedCurrent, unparsedLines) = CleanRules(autoSuicideDetailTextBox.Lines);
                if (unparsedLines.Any())
                {
                    Log.Warning("Failed to parse auto-suicide rule lines: {Lines}", string.Join(", ", unparsedLines));
                    MessageBox.Show(LanguageManager.Translate("解析できなかった行があります:") + Environment.NewLine + string.Join(Environment.NewLine, unparsedLines),
                        LanguageManager.Translate("警告"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                var split = SplitAutoAndCustom(cleanedCurrent);
                var autoLines = GenerateAutoSuicideLines();
                autoSuicideAutoRuleCount = autoLines.Length;
                autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, autoLines.Concat(split.customLines));
            }
        }

        private void AutoSuicideUseDetailCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            var checkBox = sender as CheckBox ?? autoSuicideUseDetailCheckBox;

            if (checkBox == null ||
                autoSuicideRoundListBox == null ||
                autoSuicideRoundLabel == null ||
                autoSuicideDetailTextBox == null ||
                autoSuicideFuzzyCheckBox == null ||
                autoSuicideSettingsConfirmButton == null ||
                autoSuicideDetailDocLink == null)
            {
                return;
            }

            bool useDetail = checkBox.Checked;
            autoSuicideRoundListBox.Enabled = !useDetail;
            autoSuicideRoundListBox.Visible = autoSuicideCheckBox.Checked && !useDetail;
            autoSuicideRoundLabel.Visible = autoSuicideCheckBox.Checked && !useDetail;
            autoSuicideDetailTextBox.Visible = autoSuicideCheckBox.Checked && useDetail;
            autoSuicideFuzzyCheckBox.Visible = autoSuicideSettingsConfirmButton.Visible = autoSuicideCheckBox.Checked && useDetail;
            autoSuicideDetailDocLink.Visible = autoSuicideCheckBox.Checked && useDetail;

            if (useDetail)
            {
                autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, autoSuicideDetailTextBox.Lines.Skip(autoSuicideAutoRuleCount));
                autoSuicideAutoRuleCount = 0;
            }
            else
            {
                autoSuicideAutoRuleCount = autoSuicideRoundListBox.Items.Count;
                UpdateAutoSuicideDetailAutoLines();
            }
        }

        private void AutoSuicideCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            bool enabled = autoSuicideCheckBox.Checked;
            autoSuicideUseDetailCheckBox.Visible = enabled;
            autoSuicidePresetLabel.Visible = enabled;
            autoSuicidePresetComboBox.Visible = enabled;
            autoSuicidePresetSaveButton.Visible = enabled;
            autoSuicidePresetLoadButton.Visible = enabled;
            autoSuicidePresetImportButton.Visible = enabled;
            autoSuicidePresetExportButton.Visible = enabled;
            AutoSuicideUseDetailCheckBox_CheckedChanged(null, EventArgs.Empty);
            if (!enabled)
            {
                autoSuicideRoundLabel.Visible = false;
                autoSuicideRoundListBox.Visible = false;
                autoSuicideDetailTextBox.Visible = false;
                autoSuicideDetailDocLink.Visible = false;
            }
        }
    }
}
