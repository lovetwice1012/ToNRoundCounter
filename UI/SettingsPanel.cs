using System;
using System.Drawing;
using System.Windows.Forms;
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
using Serilog;

namespace ToNRoundCounter.UI
{
    public class SettingsPanel : UserControl
    {
        private readonly IAppSettings _settings;


        private Label languageLabel = null!;
        public ComboBox LanguageComboBox { get; private set; } = null!;
        public NumericUpDown oscPortNumericUpDown { get; private set; } = null!;
        public TextBox webSocketIpTextBox { get; private set; } = null!;
        // 統計情報表示・デバッグ情報チェック
        public CheckBox ShowStatsCheckBox { get; private set; } = null!;
        public CheckBox DebugInfoCheckBox { get; private set; } = null!;

        // フィルター項目（MAPは削除）
        public CheckBox RoundTypeCheckBox { get; private set; } = null!;
        public CheckBox TerrorCheckBox { get; private set; } = null!;
        public CheckBox AppearanceCountCheckBox { get; private set; } = null!;
        public CheckBox SurvivalCountCheckBox { get; private set; } = null!;
        public CheckBox DeathCountCheckBox { get; private set; } = null!;
        public CheckBox SurvivalRateCheckBox { get; private set; } = null!;

        // オーバーレイ表示切替
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
        public CheckBox OverlayUnboundTerrorDetailsCheckBox { get; private set; } = null!;
        public TrackBar OverlayOpacityTrackBar { get; private set; } = null!;
        public Label OverlayOpacityValueLabel { get; private set; } = null!;

        // ラウンドログ表示切替チェックボックス
        public CheckBox ToggleRoundLogCheckBox { get; private set; } = null!;

        // 追加設定項目
        public Label FixedTerrorColorLabel { get; private set; } = null!;
        public Button FixedTerrorColorButton { get; private set; } = null!;
        public Label BackgroundColorLabel { get; private set; } = null!;
        public Button BackgroundColorButton { get; private set; } = null!;
        public Label RoundTypeStatsLabel { get; private set; } = null!;
        public CheckedListBox RoundTypeStatsListBox { get; private set; } = null!;

        // 個別背景色設定
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
        public TextBox DiscordWebhookUrlTextBox { get; private set; } = null!;
        public CheckBox AutoRecordingEnabledCheckBox { get; private set; } = null!;
        public TextBox AutoRecordingWindowTitleTextBox { get; private set; } = null!;
        public NumericUpDown AutoRecordingFrameRateNumeric { get; private set; } = null!;
        public TextBox AutoRecordingOutputDirectoryTextBox { get; private set; } = null!;
        public ComboBox AutoRecordingFormatComboBox { get; private set; } = null!;
        public CheckedListBox AutoRecordingRoundTypesListBox { get; private set; } = null!;
        public TextBox AutoRecordingTerrorNamesTextBox { get; private set; } = null!;
        private Button autoRecordingBrowseOutputButton = null!;
        private Button roundLogExportButton = null!;

        private const string AutoLaunchEnabledColumnName = "AutoLaunchEnabled";
        private const string AutoLaunchPathColumnName = "AutoLaunchPath";
        private const string AutoLaunchArgumentsColumnName = "AutoLaunchArguments";
        private const string ItemMusicEnabledColumnName = "ItemMusicEnabled";
        private const string ItemMusicItemColumnName = "ItemMusicItem";
        private const string ItemMusicPathColumnName = "ItemMusicPath";
        private const string ItemMusicMinSpeedColumnName = "ItemMusicMinSpeed";
        private const string ItemMusicMaxSpeedColumnName = "ItemMusicMaxSpeed";
        private const string RoundBgmEnabledColumnName = "RoundBgmEnabled";
        private const string RoundBgmRoundColumnName = "RoundBgmRound";
        private const string RoundBgmTerrorColumnName = "RoundBgmTerror";
        private const string RoundBgmPathColumnName = "RoundBgmPath";

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


        public SettingsPanel(IAppSettings settings)
        {
            _settings = settings;
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
            this.Controls.Add(languageLabel);

            LanguageComboBox = new ComboBox();
            LanguageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            LanguageComboBox.Location = new Point(languageLabel.Right + 10, currentY);
            LanguageComboBox.Width = columnWidth - (LanguageComboBox.Left - margin);
            this.Controls.Add(LanguageComboBox);
            LoadLanguageOptions(_settings.Language);
            currentY += LanguageComboBox.Height + margin;

            Label themeLabel = new Label();
            themeLabel.Text = LanguageManager.Translate("テーマ");
            themeLabel.AutoSize = true;
            themeLabel.Location = new Point(margin, currentY + 4);
            this.Controls.Add(themeLabel);

            ThemeComboBox = new ComboBox();
            ThemeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ThemeComboBox.Location = new Point(themeLabel.Right + 10, currentY);
            ThemeComboBox.Width = columnWidth - (ThemeComboBox.Left - margin);
            this.Controls.Add(ThemeComboBox);
            LoadThemeOptions(Theme.RegisteredThemes, _settings.ThemeKey);
            currentY += ThemeComboBox.Height + margin;

            GroupBox grpOsc = new GroupBox();
            grpOsc.Text = LanguageManager.Translate("OSC設定");
            grpOsc.Location = new Point(margin, currentY);  // currentY を適切に調整してください
            grpOsc.Size = new Size(columnWidth, 60);
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
            oscPortNumericUpDown.Value = _settings.OSCPort;  // _settings.OSCPort の初期値を使用
            oscPortNumericUpDown.Location = new Point(oscPortLabel.Right + 10, 20);
            grpOsc.Controls.Add(oscPortNumericUpDown);

            // 自動自殺モードグループ（右列）
            GroupBox grpAutoSuicide = new GroupBox();
            grpAutoSuicide.Text = LanguageManager.Translate("自動自殺モード");
            grpAutoSuicide.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpAutoSuicide.Size = new Size(columnWidth, 100);
            this.Controls.Add(grpAutoSuicide);

            int autoInnerY = 20;
            autoSuicideCheckBox = new CheckBox();
            autoSuicideCheckBox.Name = "AutoSuicideCheckBox";
            autoSuicideCheckBox.Text = LanguageManager.Translate("自動自殺を有効にする");
            autoSuicideCheckBox.AutoSize = true;
            autoSuicideCheckBox.Location = new Point(innerMargin, autoInnerY);
            autoSuicideCheckBox.Checked = _settings.AutoSuicideEnabled;
            //autoSuicideCheckBox.Enabled = false;
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
                var psi = new ProcessStartInfo(e.Link.LinkData.ToString()) { UseShellExecute = true };
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
                string item = autoSuicideRoundListBox.Items[i].ToString();
                autoSuicideRoundListBox.SetItemChecked(i, _settings.AutoSuicideRoundTypes.Contains(item));
            }
            //autoSuicideRoundListBox.Enabled = false;
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
                    RoundTypes = autoSuicideRoundListBox.CheckedItems.Cast<object>().Select(i => i.ToString()).ToList(),
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
                        string item = autoSuicideRoundListBox.Items[i].ToString();
                        autoSuicideRoundListBox.SetItemChecked(i, preset.RoundTypes.Contains(item));
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

                var complexRoundRules = rulesCheck.Where(r => r.Round != null && r.TerrorExpression != null && r.Terror == null && !r.RoundNegate)
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

            overlayInnerY = OverlayInstanceTimerCheckBox.Bottom + 12;

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
            this.Controls.Add(grpAutoRecording);

            int autoRecordingInnerY = 25;

            AutoRecordingEnabledCheckBox = new CheckBox();
            AutoRecordingEnabledCheckBox.Text = LanguageManager.Translate("指定条件でVRChatを自動録画する");
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
            AutoRecordingFrameRateNumeric.Maximum = 60;
            AutoRecordingFrameRateNumeric.Value = Math.Min(Math.Max(_settings.AutoRecordingFrameRate, 5), 60);
            grpAutoRecording.Controls.Add(AutoRecordingFrameRateNumeric);

            Label autoRecordingFpsLabel = new Label();
            autoRecordingFpsLabel.Text = LanguageManager.Translate("fps");
            autoRecordingFpsLabel.AutoSize = true;
            autoRecordingFpsLabel.Location = new Point(AutoRecordingFrameRateNumeric.Right + 8, AutoRecordingFrameRateNumeric.Top + 4);
            grpAutoRecording.Controls.Add(autoRecordingFpsLabel);

            autoRecordingInnerY = AutoRecordingFrameRateNumeric.Bottom + 10;

            Label autoRecordingCommandLabel = new Label();
            autoRecordingCommandLabel.Text = LanguageManager.Translate("録画コマンド");
            autoRecordingCommandLabel.AutoSize = true;
            autoRecordingCommandLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingCommandLabel);

            AutoRecordingCommandTextBox = new TextBox();
            AutoRecordingCommandTextBox.Location = new Point(innerMargin, autoRecordingCommandLabel.Bottom + 4);
            AutoRecordingCommandTextBox.Width = columnWidth - innerMargin * 3 - 90;
            grpAutoRecording.Controls.Add(AutoRecordingCommandTextBox);

            Button autoRecordingCommandBrowseButton = new Button();
            autoRecordingCommandBrowseButton.Text = LanguageManager.Translate("参照...");
            autoRecordingCommandBrowseButton.AutoSize = true;
            autoRecordingCommandBrowseButton.Location = new Point(AutoRecordingCommandTextBox.Right + 10, AutoRecordingCommandTextBox.Top - 2);
            autoRecordingCommandBrowseButton.Click += (s, e) =>
            {
                BrowseForAutoRecordingExecutable();
                RefreshAutoRecordingControlsState();
            };
            grpAutoRecording.Controls.Add(autoRecordingCommandBrowseButton);

            autoRecordingInnerY = AutoRecordingCommandTextBox.Bottom + 8;

            Label autoRecordingArgumentsLabel = new Label();
            autoRecordingArgumentsLabel.Text = LanguageManager.Translate("録画引数");
            autoRecordingArgumentsLabel.AutoSize = true;
            autoRecordingArgumentsLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingArgumentsLabel);

            AutoRecordingArgumentsTextBox = new TextBox();
            AutoRecordingArgumentsTextBox.Location = new Point(innerMargin, autoRecordingArgumentsLabel.Bottom + 4);
            AutoRecordingArgumentsTextBox.Width = columnWidth - innerMargin * 2;
            grpAutoRecording.Controls.Add(AutoRecordingArgumentsTextBox);

            autoRecordingInnerY = AutoRecordingArgumentsTextBox.Bottom + 4;

            Label autoRecordingArgumentsHelpLabel = new Label();
            autoRecordingArgumentsHelpLabel.Text = LanguageManager.Translate("録画引数の説明");
            autoRecordingArgumentsHelpLabel.AutoSize = true;
            autoRecordingArgumentsHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingArgumentsHelpLabel.Location = new Point(innerMargin, autoRecordingInnerY);
            grpAutoRecording.Controls.Add(autoRecordingArgumentsHelpLabel);

            autoRecordingInnerY = autoRecordingArgumentsHelpLabel.Bottom + 8;

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
            grpAutoRecording.Controls.Add(AutoRecordingFormatComboBox);

            Label autoRecordingFormatHelpLabel = new Label();
            autoRecordingFormatHelpLabel.Text = LanguageManager.Translate("AutoRecording_FormatHelp");
            autoRecordingFormatHelpLabel.AutoSize = true;
            autoRecordingFormatHelpLabel.MaximumSize = new Size(columnWidth - innerMargin * 2, 0);
            autoRecordingFormatHelpLabel.Location = new Point(innerMargin, AutoRecordingFormatComboBox.Bottom + 4);
            grpAutoRecording.Controls.Add(autoRecordingFormatHelpLabel);

            autoRecordingInnerY = autoRecordingFormatHelpLabel.Bottom + 10;

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
            AutoRecordingFrameRateNumeric.Value = Math.Min(Math.Max(_settings.AutoRecordingFrameRate, 5), 60);
            AutoRecordingOutputDirectoryTextBox.Text = _settings.AutoRecordingOutputDirectory;
            AutoRecordingEnabledCheckBox.Checked = _settings.AutoRecordingEnabled;
            SetAutoRecordingFormat(_settings.AutoRecordingOutputExtension);
            SetAutoRecordingRoundTypes(_settings.AutoRecordingRoundTypes);
            SetAutoRecordingTerrors(_settings.AutoRecordingTerrors);
            RefreshAutoRecordingControlsState();

            thirdColumnY += grpAutoRecording.Height + margin;

            currentY = currentY + grpOsc.Bottom + margin;

            // 表示設定グループ
            GroupBox grpDisplay = new GroupBox();
            grpDisplay.Text = LanguageManager.Translate("表示設定");
            grpDisplay.Location = new Point(margin, currentY);
            grpDisplay.Size = new Size(columnWidth, 100);
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

            // フィルター設定グループ
            GroupBox grpFilter = new GroupBox();
            grpFilter.Text = LanguageManager.Translate("フィルター設定");
            grpFilter.Location = new Point(margin, currentY);
            grpFilter.Size = new Size(columnWidth, 70);
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

            grpItemMusic.Controls.Add(itemMusicEntriesGrid);

            itemMusicAddButton = new Button();
            itemMusicAddButton.Text = LanguageManager.Translate("追加");
            itemMusicAddButton.AutoSize = true;
            itemMusicAddButton.Location = new Point(innerMargin, itemMusicEntriesGrid.Bottom + 10);
            itemMusicAddButton.Click += (s, e) =>
            {
                itemMusicEntriesGrid.Rows.Add(true, string.Empty, string.Empty, 0d, 0d);
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
            roundBgmPathColumn.FillWeight = 40;
            roundBgmEntriesGrid.Columns.Add(roundBgmPathColumn);

            grpRoundBgm.Controls.Add(roundBgmEntriesGrid);

            roundBgmAddButton = new Button();
            roundBgmAddButton.Text = LanguageManager.Translate("追加");
            roundBgmAddButton.AutoSize = true;
            roundBgmAddButton.Location = new Point(innerMargin, roundBgmEntriesGrid.Bottom + 10);
            roundBgmAddButton.Click += (s, e) =>
            {
                roundBgmEntriesGrid.Rows.Add(true, string.Empty, string.Empty, string.Empty);
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



            // 追加設定グループ
            GroupBox grpAdditional = new GroupBox();
            grpAdditional.Text = LanguageManager.Translate("追加設定");
            grpAdditional.Location = new Point(margin, currentY);
            grpAdditional.Size = new Size(columnWidth, 200);
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

            // 背景色設定グループ（個別設定）
            GroupBox grpBg = new GroupBox();
            grpBg.Text = LanguageManager.Translate("背景色設定");
            grpBg.Location = new Point(margin, currentY);
            grpBg.Size = new Size(columnWidth, 120);
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

            int innerMargin2 = 10; // ToNRoundCounter-Cloudの設定用の内側のマージン
            int apiInnerY = 20; // ToNRoundCounter-Cloudの設定用の初期Y座標
            //apiキー設定
            GroupBox grpApiKey = new GroupBox();
            grpApiKey.Text = LanguageManager.Translate("ToNRoundCounter-Cloudの設定");
            grpApiKey.Location = new Point(margin, currentY);
            grpApiKey.Size = new Size(columnWidth, 300);
            this.Controls.Add(grpApiKey);
            //説明
            Label apiKeyDescription = new Label();
            apiKeyDescription.Text = LanguageManager.Translate("ToNRoundCounter-Cloudはセーブコードの複数端末間での全自動同期などの機能を持つクラウドサービスです。\n利用にはAPIキーが必要です。\nAPIキーはwebサイトから取得してください。");
            apiKeyDescription.Size = new Size(grpApiKey.Width - innerMargin2 * 2, 60); // 説明文の幅をグループボックスの幅に合わせる
            apiKeyDescription.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(apiKeyDescription);
            apiInnerY += apiKeyDescription.Height + 10; // 説明文の下にスペースを確保
            grpApiKey.Height = apiInnerY + 50; // グループボックスの高さを調整
            //ToNRoundCounter-Cloudを開くを追加(https://toncloud.sprink.cloud)
            Button openCloudButton = new Button();
            openCloudButton.Text = LanguageManager.Translate("ToNRoundCounter-Cloudを開く");
            openCloudButton.AutoSize = true;
            openCloudButton.Location = new Point(innerMargin2, apiInnerY);
            openCloudButton.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start("https://toncloud.sprink.cloud");
            };
            grpApiKey.Controls.Add(openCloudButton);
            apiInnerY += openCloudButton.Height + 10; // ボタンの下にスペースを確保

            // APIキー入力欄
            apiKeyLabel = new Label();
            apiKeyLabel.Text = LanguageManager.Translate("APIキー:");
            apiKeyLabel.AutoSize = true;
            apiKeyLabel.Location = new Point(innerMargin2, apiInnerY);
            grpApiKey.Controls.Add(apiKeyLabel);
            apiKeyTextBox = new TextBox();
            apiKeyTextBox.Name = "ApiKeyTextBox";
            apiKeyTextBox.Text = _settings.apikey; // _settings.apikey の初期値を使用
            apiKeyTextBox.Location = new Point(apiKeyLabel.Right + 10, apiInnerY);
            apiKeyTextBox.Width = 400; // テキストボックスの幅を設定
            grpApiKey.Controls.Add(apiKeyTextBox);
            apiInnerY += apiKeyTextBox.Height + 10; // テキストボックスの下にスペースを確保
            // APIキーの保存ボタンはいらない
            grpApiKey.Height = apiInnerY + 20; // グループボックスの高さを調整

            currentY += grpApiKey.Height + margin;

            GroupBox grpDiscord = new GroupBox();
            grpDiscord.Text = LanguageManager.Translate("Discord通知設定");
            grpDiscord.Location = new Point(margin * 2 + columnWidth, rightColumnY);
            grpDiscord.Size = new Size(columnWidth, 130);
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
            this.Controls.Add(ModuleExtensionsPanel);

            int moduleMargin = margin;
            ModuleExtensionsPanel.ControlAdded += (s, e) =>
            {
                ModuleExtensionsPanel.Width = columnWidth;
                currentY = Math.Max(currentY, ModuleExtensionsPanel.Bottom);
                this.Height = Math.Max(Math.Max(currentY, rightColumnY), Math.Max(thirdColumnY, ModuleExtensionsPanel.Bottom)) + moduleMargin;
            };

            // 最後に、パネルの高さを調整
            this.Width = totalWidth;
            this.Height = Math.Max(Math.Max(currentY, rightColumnY), thirdColumnY) + margin;

        }

        private async void RoundLogExportButton_Click(object? sender, EventArgs e)
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
                new RecordingFormatOption("mkv", LanguageManager.Translate("AutoRecording_FormatOption_MKV")),
                new RecordingFormatOption("flv", LanguageManager.Translate("AutoRecording_FormatOption_FLV")),
                new RecordingFormatOption("asf", LanguageManager.Translate("AutoRecording_FormatOption_ASF")),
                new RecordingFormatOption("vob", LanguageManager.Translate("AutoRecording_FormatOption_VOB")),
                new RecordingFormatOption("gif", LanguageManager.Translate("AutoRecording_FormatOption_GIF")),
            };
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
                        entry.MaxSpeed);
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
                        entry.SoundPath ?? string.Empty);
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

                result.Add(new ItemMusicEntry
                {
                    Enabled = enabled,
                    ItemName = itemName.Trim(),
                    SoundPath = soundPath?.Trim() ?? string.Empty,
                    MinSpeed = minSpeed,
                    MaxSpeed = maxSpeed
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
                    SoundPath = soundPath?.Trim() ?? string.Empty
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
                string current = Convert.ToString(row.Cells[ItemMusicPathColumnName].Value) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    dialog.FileName = current;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    row.Cells[ItemMusicPathColumnName].Value = dialog.FileName;
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
                string current = Convert.ToString(row.Cells[RoundBgmPathColumnName].Value) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    dialog.FileName = current;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    row.Cells[RoundBgmPathColumnName].Value = dialog.FileName;
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
            if (AutoRecordingRoundTypesListBox != null)
            {
                AutoRecordingRoundTypesListBox.Enabled = enabled;
            }
            if (AutoRecordingTerrorNamesTextBox != null)
            {
                AutoRecordingTerrorNamesTextBox.Enabled = enabled;
            }
        }

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

        private static double GetDoubleFromCell(object value, double fallback)
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
                string round = autoSuicideRoundListBox.Items[i].ToString();
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
                if (!r.RoundNegate && !r.TerrorNegate && r.Terror == null && r.Round != null && autoSuicideRoundListBox.Items.Contains(r.Round))
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
            // Use the sender if available, otherwise fall back to the field. This
            // avoids a potential NullReferenceException when the handler is
            // invoked before the checkbox is initialised (e.g. during form
            // construction) or externally with a null sender.
            var checkBox = sender as CheckBox ?? autoSuicideUseDetailCheckBox;

            // If the checkbox or related controls haven't been created yet, just
            // exit early instead of throwing.
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
