using System;
using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Utils;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using ToNRoundCounter.Models;

namespace ToNRoundCounter.UI
{
    public class SettingsPanel : UserControl
    {


        public NumericUpDown oscPortNumericUpDown { get; private set; }
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
        public ComboBox autoSuicidePresetComboBox { get; private set; }
        public Button autoSuicidePresetSaveButton { get; private set; }
        public Button autoSuicidePresetLoadButton { get; private set; }
        public Button autoSuicidePresetExportButton { get; private set; }
        public Button autoSuicidePresetImportButton { get; private set; }
        public TextBox autoSuicideDetailTextBox { get; private set; }
        public CheckBox autoSuicideFuzzyCheckBox { get; private set; }
        private int autoSuicideAutoRuleCount = 0;

        public Label apiKeyLabel { get; private set; }
        public TextBox apiKeyTextBox { get; private set; }


        public SettingsPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(560, 1000);

            int margin = 10;
            int currentY = margin;

            GroupBox grpOsc = new GroupBox();
            grpOsc.Text = LanguageManager.Translate("OSC設定");
            grpOsc.Location = new Point(margin, currentY);  // currentY を適切に調整してください
            grpOsc.Size = new Size(540, 60);
            this.Controls.Add(grpOsc);

            Label oscPortLabel = new Label();
            oscPortLabel.Text = LanguageManager.Translate("OSC接続ポート:");
            oscPortLabel.AutoSize = true;
            oscPortLabel.Location = new Point(margin, 20);
            grpOsc.Controls.Add(oscPortLabel);

            oscPortNumericUpDown = new NumericUpDown();
            oscPortNumericUpDown.Minimum = 1024;
            oscPortNumericUpDown.Maximum = 65535;
            oscPortNumericUpDown.Value = AppSettings.OSCPort;  // AppSettings.OSCPort の初期値を使用
            oscPortNumericUpDown.Location = new Point(oscPortLabel.Right + 10, 20);
            grpOsc.Controls.Add(oscPortNumericUpDown);

            currentY += grpOsc.Height + margin;

            // 表示設定グループ
            GroupBox grpDisplay = new GroupBox();
            grpDisplay.Text = LanguageManager.Translate("表示設定");
            grpDisplay.Location = new Point(margin, currentY);
            grpDisplay.Size = new Size(540, 100);
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
            grpFilter.Size = new Size(540, 70);
            this.Controls.Add(grpFilter);

            int innerMargin = 10;
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
            grpAdditional.Size = new Size(540, 200);
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
            RoundTypeStatsListBox.Items.Add("コールドナイト");
            RoundTypeStatsListBox.Items.Add("ミスティックムーン");
            RoundTypeStatsListBox.Items.Add("ブラッドムーン");
            RoundTypeStatsListBox.Items.Add("トワイライト");
            RoundTypeStatsListBox.Items.Add("ソルスティス");
            RoundTypeStatsListBox.Items.Add("霧");
            RoundTypeStatsListBox.Items.Add("8ページ");
            RoundTypeStatsListBox.Items.Add("狂気");
            RoundTypeStatsListBox.Items.Add("ゴースト");
            RoundTypeStatsListBox.Items.Add("ダブル・トラブル");
            RoundTypeStatsListBox.Items.Add("EX");
            RoundTypeStatsListBox.Items.Add("アンバウンド");
            grpAdditional.Controls.Add(RoundTypeStatsListBox);

            currentY += grpAdditional.Height + margin;

            // 背景色設定グループ（個別設定）
            GroupBox grpBg = new GroupBox();
            grpBg.Text = LanguageManager.Translate("背景色設定");
            grpBg.Location = new Point(margin, currentY);
            grpBg.Size = new Size(540, 120);
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

            GroupBox grpAutoSuicide = new GroupBox();
            grpAutoSuicide.Text = LanguageManager.Translate("自動自殺モード");
            grpAutoSuicide.Location = new Point(margin, currentY);
            grpAutoSuicide.Size = new Size(540, 100);
            this.Controls.Add(grpAutoSuicide);

            int autoInnerY = 20;
            autoSuicideCheckBox = new CheckBox();
            autoSuicideCheckBox.Name = "AutoSuicideCheckBox";
            autoSuicideCheckBox.Text = LanguageManager.Translate("自動自殺を有効にする");
            autoSuicideCheckBox.AutoSize = true;
            autoSuicideCheckBox.Location = new Point(innerMargin, autoInnerY);
            autoSuicideCheckBox.Checked = AppSettings.AutoSuicideEnabled;
            //autoSuicideCheckBox.Enabled = false;
            grpAutoSuicide.Controls.Add(autoSuicideCheckBox);

            Label autoSuicideRoundLabel = new Label();
            autoSuicideRoundLabel.Text = LanguageManager.Translate("自動自殺対象ラウンド:");
            autoSuicideRoundLabel.AutoSize = true;
            autoSuicideRoundLabel.Location = new Point(innerMargin, autoInnerY + 30);
            grpAutoSuicide.Controls.Add(autoSuicideRoundLabel);

            autoSuicideRoundListBox = new CheckedListBox();
            autoSuicideRoundListBox.Name = "AutoSuicideRoundListBox";
            autoSuicideRoundListBox.Location = new Point(autoSuicideRoundLabel.Right + 10, autoInnerY + 25);
            autoSuicideRoundListBox.Size = new Size(400, 150);
            autoSuicideRoundListBox.Items.Add("クラシック");
            autoSuicideRoundListBox.Items.Add("オルタネイト");
            autoSuicideRoundListBox.Items.Add("パニッシュ");
            autoSuicideRoundListBox.Items.Add("サボタージュ");
            autoSuicideRoundListBox.Items.Add("ブラッドバス");
            autoSuicideRoundListBox.Items.Add("ミッドナイト");
            //autoSuicideRoundListBox.Items.Add("走れ！");
            autoSuicideRoundListBox.Items.Add("コールドナイト");
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
                autoSuicideRoundListBox.SetItemChecked(i, AppSettings.AutoSuicideRoundTypes.Contains(item));
            }
            //autoSuicideRoundListBox.Enabled = false;
            grpAutoSuicide.Controls.Add(autoSuicideRoundListBox);
            autoSuicideRoundListBox.ItemCheck += (s, e) =>
            {
                BeginInvoke(new Action(UpdateAutoSuicideDetailAutoLines));
            };

            Label autoSuicidePresetLabel = new Label();
            autoSuicidePresetLabel.Text = LanguageManager.Translate("プリセット:");
            autoSuicidePresetLabel.AutoSize = true;
            autoSuicidePresetLabel.Location = new Point(innerMargin, autoSuicideRoundListBox.Bottom + 10);
            grpAutoSuicide.Controls.Add(autoSuicidePresetLabel);

            autoSuicidePresetComboBox = new ComboBox();
            autoSuicidePresetComboBox.Name = "AutoSuicidePresetComboBox";
            autoSuicidePresetComboBox.Location = new Point(autoSuicidePresetLabel.Right + 10, autoSuicideRoundListBox.Bottom + 5);
            autoSuicidePresetComboBox.Width = 200;
            foreach (var key in AppSettings.AutoSuicidePresets.Keys)
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
                if (!string.IsNullOrEmpty(name))
                {
                    CleanAutoSuicideDetailRules();
                    var preset = new AutoSuicidePreset
                    {
                        RoundTypes = autoSuicideRoundListBox.CheckedItems.Cast<object>().Select(i => i.ToString()).ToList(),
                        DetailCustom = GetCustomAutoSuicideLines(),
                        Fuzzy = autoSuicideFuzzyCheckBox.Checked
                    };
                    AppSettings.AutoSuicidePresets[name] = preset;
                    if (!autoSuicidePresetComboBox.Items.Contains(name))
                        autoSuicidePresetComboBox.Items.Add(name);
                    AppSettings.Save();
                    MessageBox.Show(LanguageManager.Translate("プリセットを保存しました。"), LanguageManager.Translate("情報"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetSaveButton);

            autoSuicidePresetLoadButton = new Button();
            autoSuicidePresetLoadButton.Text = LanguageManager.Translate("読み込み");
            autoSuicidePresetLoadButton.AutoSize = true;
            autoSuicidePresetLoadButton.Location = new Point(autoSuicidePresetSaveButton.Right + 10, autoSuicideRoundListBox.Bottom + 5);
            autoSuicidePresetLoadButton.Click += (s, e) =>
            {
                string name = autoSuicidePresetComboBox.Text.Trim();
                if (!string.IsNullOrEmpty(name) && AppSettings.AutoSuicidePresets.ContainsKey(name))
                {
                    var preset = AppSettings.AutoSuicidePresets[name];
                    for (int i = 0; i < autoSuicideRoundListBox.Items.Count; i++)
                    {
                        string item = autoSuicideRoundListBox.Items[i].ToString();
                        autoSuicideRoundListBox.SetItemChecked(i, preset.RoundTypes.Contains(item));
                    }
                    autoSuicideFuzzyCheckBox.Checked = preset.Fuzzy;
                    var autoLinesLocal = GenerateAutoSuicideLines();
                    autoSuicideAutoRuleCount = autoLinesLocal.Length;
                    autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, autoLinesLocal.Concat(preset.DetailCustom));
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
                            AppSettings.AutoSuicidePresets[name] = preset;
                            if (!autoSuicidePresetComboBox.Items.Contains(name))
                                autoSuicidePresetComboBox.Items.Add(name);
                            AppSettings.Save();
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
                if (!string.IsNullOrEmpty(name) && AppSettings.AutoSuicidePresets.ContainsKey(name))
                {
                    SaveFileDialog dialog = new SaveFileDialog();
                    dialog.Filter = "JSON Files|*.json|All Files|*.*";
                    dialog.FileName = name + ".json";
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var preset = AppSettings.AutoSuicidePresets[name];
                        File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(preset, Formatting.Indented));
                        MessageBox.Show(LanguageManager.Translate("プリセットをエクスポートしました。"), LanguageManager.Translate("情報"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };
            grpAutoSuicide.Controls.Add(autoSuicidePresetExportButton);

            grpAutoSuicide.Height = autoSuicidePresetExportButton.Bottom + 10;

            currentY += grpAutoSuicide.Height + margin;

            GroupBox grpAutoSuicideDetail = new GroupBox();
            grpAutoSuicideDetail.Text = LanguageManager.Translate("自動自殺詳細設定");
            grpAutoSuicideDetail.Location = new Point(margin, currentY);
            grpAutoSuicideDetail.Size = new Size(540, 180);
            this.Controls.Add(grpAutoSuicideDetail);

            autoSuicideDetailTextBox = new TextBox();
            autoSuicideDetailTextBox.Multiline = true;
            autoSuicideDetailTextBox.ScrollBars = ScrollBars.Vertical;
            autoSuicideDetailTextBox.Size = new Size(500, 100);
            autoSuicideDetailTextBox.Location = new Point(innerMargin, 20);
            grpAutoSuicideDetail.Controls.Add(autoSuicideDetailTextBox);

            autoSuicideFuzzyCheckBox = new CheckBox();
            autoSuicideFuzzyCheckBox.Text = LanguageManager.Translate("曖昧マッチング");
            autoSuicideFuzzyCheckBox.AutoSize = true;
            autoSuicideFuzzyCheckBox.Location = new Point(innerMargin, autoSuicideDetailTextBox.Bottom + 10);
            grpAutoSuicideDetail.Controls.Add(autoSuicideFuzzyCheckBox);

            grpAutoSuicideDetail.Height = autoSuicideFuzzyCheckBox.Bottom + 10;

            autoSuicideAutoRuleCount = autoSuicideRoundListBox.Items.Count;
            var autoLines = GenerateAutoSuicideLines();
            var lines = new List<string>(autoLines);
            if (AppSettings.AutoSuicideDetailCustom != null)
                lines.AddRange(AppSettings.AutoSuicideDetailCustom);
            var cleaned = CleanRules(lines);
            var split = SplitAutoAndCustom(cleaned);
            autoSuicideAutoRuleCount = split.autoLines.Count;
            autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, split.autoLines.Concat(split.customLines));
            autoSuicideFuzzyCheckBox.Checked = AppSettings.AutoSuicideFuzzyMatch;

            currentY += grpAutoSuicideDetail.Height + margin;

            int innerMargin2 = 10; // ToNRoundCounter-Cloudの設定用の内側のマージン
            int apiInnerY = 20; // ToNRoundCounter-Cloudの設定用の初期Y座標
            //apiキー設定
            GroupBox grpApiKey = new GroupBox();
            grpApiKey.Text = LanguageManager.Translate("ToNRoundCounter-Cloudの設定");
            grpApiKey.Location = new Point(margin, currentY);
            grpApiKey.Size = new Size(540, 300);
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
            apiKeyTextBox.Text = AppSettings.apikey; // AppSettings.apikey の初期値を使用
            apiKeyTextBox.Location = new Point(apiKeyLabel.Right + 10, apiInnerY);
            apiKeyTextBox.Width = 400; // テキストボックスの幅を設定
            grpApiKey.Controls.Add(apiKeyTextBox);
            apiInnerY += apiKeyTextBox.Height + 10; // テキストボックスの下にスペースを確保
            // APIキーの保存ボタンはいらない
            grpApiKey.Height = apiInnerY + 20; // グループボックスの高さを調整
            // 最後に、パネルの高さを調整
            this.Height = currentY + grpApiKey.Height + margin;

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
            var custom = autoSuicideDetailTextBox.Lines.Skip(autoSuicideAutoRuleCount).ToList();
            var autoLines = GenerateAutoSuicideLines();
            autoSuicideAutoRuleCount = autoLines.Length;
            autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, autoLines.Concat(custom));
        }

        private List<Models.AutoSuicideRule> CleanRules(IEnumerable<string> lines)
        {
            var rules = new List<Models.AutoSuicideRule>();
            foreach (var line in lines)
            {
                if (Models.AutoSuicideRule.TryParse(line, out var r))
                    rules.Add(r);
            }
            var cleaned = new List<Models.AutoSuicideRule>();
            for (int i = rules.Count - 1; i >= 0; i--)
            {
                var r = rules[i];
                bool redundant = cleaned.Any(c => c.Covers(r));
                if (!redundant)
                    cleaned.Add(r);
            }
            cleaned.Reverse();
            return cleaned;
        }

        private (List<string> autoLines, List<string> customLines) SplitAutoAndCustom(List<Models.AutoSuicideRule> rules)
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
            autoSuicideAutoRuleCount = autoLines.Count;
            return (autoLines, customLines);
        }

        public List<string> GetCustomAutoSuicideLines()
        {
            return autoSuicideDetailTextBox.Lines.Skip(autoSuicideAutoRuleCount)
                .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
        }

        public void CleanAutoSuicideDetailRules()
        {
            var autoLines = GenerateAutoSuicideLines();
            var currentCustom = autoSuicideDetailTextBox.Lines.Skip(autoSuicideAutoRuleCount);
            var combined = autoLines.Concat(currentCustom);
            var cleaned = CleanRules(combined);
            var split = SplitAutoAndCustom(cleaned);
            autoSuicideAutoRuleCount = split.autoLines.Count;
            autoSuicideDetailTextBox.Text = string.Join(Environment.NewLine, split.autoLines.Concat(split.customLines));
        }
    }
}
