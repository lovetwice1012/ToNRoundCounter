using System;
using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Domain;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using Serilog;

namespace ToNRoundCounter.UI
{
    public class SettingsPanel : UserControl
    {
        private readonly IAppSettings _settings;


        public NumericUpDown oscPortNumericUpDown { get; private set; }
        public TextBox webSocketIpTextBox { get; private set; }
        // 統計情報表示・デバッグ情報チェック
        public CheckBox ShowStatsCheckBox { get; private set; }
        public CheckBox DebugInfoCheckBox { get; private set; }

        // フィルター項目（MAPは削除）
        public CheckBox RoundTypeCheckBox { get; private set; }
        public CheckBox TerrorCheckBox { get; private set; }
        public CheckBox AppearanceCountCheckBox { get; private set; }
        public CheckBox SurvivalCountCheckBox { get; private set; }
        public CheckBox DeathCountCheckBox { get; private set; }
        public CheckBox SurvivalRateCheckBox { get; private set; }

        // ラウンドログ表示切替チェックボックス
        public CheckBox ToggleRoundLogCheckBox { get; private set; }

        // 追加設定項目
        public Label FixedTerrorColorLabel { get; private set; }
        public Button FixedTerrorColorButton { get; private set; }
        public Label BackgroundColorLabel { get; private set; }
        public Button BackgroundColorButton { get; private set; }
        public Label RoundTypeStatsLabel { get; private set; }
        public CheckedListBox RoundTypeStatsListBox { get; private set; }

        // 個別背景色設定
        public Label InfoPanelBgLabel { get; private set; }
        public Button InfoPanelBgButton { get; private set; }
        public Label StatsBgLabel { get; private set; }
        public Button StatsBgButton { get; private set; }
        public Label LogBgLabel { get; private set; }
        public Button LogBgButton { get; private set; }
        public CheckedListBox autoSuicideRoundListBox { get; internal set; }
        public CheckBox autoSuicideCheckBox { get; internal set; }
        public Label autoSuicideRoundLabel { get; private set; }
        public CheckBox autoSuicideUseDetailCheckBox { get; private set; }
        public Label autoSuicidePresetLabel { get; private set; }
        public ComboBox autoSuicidePresetComboBox { get; private set; }
        public Button autoSuicidePresetSaveButton { get; private set; }
        public Button autoSuicidePresetLoadButton { get; private set; }
        public Button autoSuicidePresetExportButton { get; private set; }
        public Button autoSuicidePresetImportButton { get; private set; }
        public TextBox autoSuicideDetailTextBox { get; private set; }
        public CheckBox autoSuicideFuzzyCheckBox { get; private set; }
        public Button autoSuicideSettingsConfirmButton { get; private set; }
        public LinkLabel autoSuicideDetailDocLink { get; private set; }
        private int autoSuicideAutoRuleCount = 0;

        public Label apiKeyLabel { get; private set; }
        public TextBox apiKeyTextBox { get; private set; }

        public CheckBox DarkThemeCheckBox { get; private set; }


        public SettingsPanel(IAppSettings settings)
        {
            _settings = settings;
            this.BorderStyle = BorderStyle.FixedSingle;

            int margin = 10;
            int columnWidth = 540;
            int totalWidth = columnWidth * 2 + margin * 3;
            this.Size = new Size(totalWidth, 1000);

            int currentY = margin;
            int innerMargin = 10;

            DarkThemeCheckBox = new CheckBox();
            DarkThemeCheckBox.Text = LanguageManager.Translate("ダークテーマを使用する");
            DarkThemeCheckBox.AutoSize = true;
            DarkThemeCheckBox.Location = new Point(margin, currentY);
            DarkThemeCheckBox.Checked = _settings.Theme == ThemeType.Dark;
            this.Controls.Add(DarkThemeCheckBox);
            currentY += DarkThemeCheckBox.Height + margin;

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
            grpAutoSuicide.Location = new Point(margin * 2 + columnWidth, currentY);
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
            autoSuicidePresetSaveButton.Click += async (s, e) =>
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
                await _settings.SaveAsync();
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
            autoSuicidePresetImportButton.Click += async (s, e) =>
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
                            await _settings.SaveAsync();
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
                        errors.Add($"{i + 1}行目: {LanguageManager.Translate(err)}");
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

                var roundWildcards = rulesCheck.Where(r => r.Round != null && r.Terror == null && !r.RoundNegate).ToList();
                var detailRules = rulesCheck.Where(r => r.Round != null && r.Terror != null && !r.RoundNegate).ToList();
                var processedDetail = new HashSet<AutoSuicideRule>();
                var simpleRounds = new List<Tuple<string, int>>();

                foreach (var rw in roundWildcards)
                {
                    var roundName = rw.Round;
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
                            var terrors = eg.Select(rule => rule.Terror).ToList();
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
                    var rounds = g.SelectMany(x => x.Rounds).Distinct().ToList();
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
                        var rounds = rule.GetRoundTerms() ?? new List<string> { rule.RoundExpression };
                        var terrors = rule.GetTerrorTerms() ?? new List<string> { rule.TerrorExpression };
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
                    sb.AppendLine($"{rg.Key}では以下の設定が適用されています");
                    foreach (var ag in rg.GroupBy(r => r.Value))
                    {
                        var terrors = ag.Select(a => a.Terror).ToList();
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

                var terrorGroups = rulesCheck.Where(r => r.RoundExpression == null && r.TerrorExpression != null)
                                             .Select(r => new { r.TerrorNegate, r.Value, Terrors = r.GetTerrorTerms() })
                                             .Where(x => x.Terrors != null)
                                             .GroupBy(x => new { x.TerrorNegate, x.Value });
                foreach (var g in terrorGroups)
                {
                    var terrors = g.SelectMany(x => x.Terrors).Distinct().ToList();
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

            // 最後に、パネルの高さを調整
            this.Width = totalWidth;
            this.Height = currentY + margin;

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
                if (AutoSuicideRule.TryParse(line, out var r))
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

        private void AutoSuicideUseDetailCheckBox_CheckedChanged(object sender, EventArgs e)
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

        private void AutoSuicideCheckBox_CheckedChanged(object sender, EventArgs e)
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
