using System;
using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.UI;
using ToNRoundCounter.Application;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    public class SettingsForm : Form
    {
        private readonly IAppSettings _settings;
        private readonly CloudWebSocketClient? _cloudClient;
        private SettingsPanel settingsPanel = null!;
        private Button btnOK = null!;
        private Button btnCancel = null!;
        private SplitContainer splitContainer = null!;
        private Panel categoryPanel = null!;
        private Panel contentPanel = null!;
        private Button[] categoryButtons = null!;
        private static readonly SettingsCategory?[] CategoryValues = new SettingsCategory?[]
        {
            SettingsCategory.General,
            SettingsCategory.Overlay,
            SettingsCategory.AutoSuicide,
            SettingsCategory.Recording,
            SettingsCategory.Other,
        };

        public SettingsPanel SettingsPanel { get { return settingsPanel; } }

        public SettingsForm(IAppSettings settings, CloudWebSocketClient? cloudClient = null)
        {
            _settings = settings;
            _cloudClient = cloudClient;

            this.Text = LanguageManager.Translate("設定");
            var workingArea = Screen.FromControl(Form.ActiveForm ?? this).WorkingArea;
            int desiredWidth = Math.Min(1850, Math.Max(640, workingArea.Width - 40));
            int desiredHeight = Math.Min(1000, Math.Max(480, workingArea.Height - 40));
            this.MinimumSize = new Size(640, 480);
            this.Size = new Size(desiredWidth, desiredHeight);
            this.StartPosition = FormStartPosition.CenterParent;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // SplitContainer: 左側カテゴリ + 右側コンテンツ
            splitContainer = new SplitContainer();
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            // Panel1MinSize/Panel2MinSize はコントロールをフォームに追加して実サイズが確定した後に Load イベントで設定する
            // (既定サイズ 150x100 の状態で Panel2MinSize=200 を代入すると SplitterDistance 例外が発生するため)

            // 左側: カテゴリナビゲーション
            categoryPanel = new Panel();
            categoryPanel.Dock = DockStyle.Fill;
            categoryPanel.BorderStyle = BorderStyle.FixedSingle;
            categoryPanel.AutoScroll = true;
            categoryPanel.BackColor = SystemColors.ControlDark;

            // カテゴリボタン
            string[] categories = new[]
            {
                LanguageManager.Translate("一般"),
                LanguageManager.Translate("オーバーレイ"),
                LanguageManager.Translate("自動自殺"),
                LanguageManager.Translate("録画"),
                LanguageManager.Translate("その他")
            };

            categoryButtons = new Button[categories.Length];
            int categoryY = 5;
            for (int i = 0; i < categories.Length; i++)
            {
                Button btn = new Button();
                btn.Text = categories[i];
                btn.Dock = DockStyle.Top;
                btn.Height = 40;
                btn.TextAlign = ContentAlignment.MiddleLeft;
                btn.FlatStyle = FlatStyle.Flat;
                btn.Tag = i;
                btn.BackColor = i == 0 ? SystemColors.Highlight : SystemColors.Control;
                btn.ForeColor = i == 0 ? SystemColors.HighlightText : SystemColors.ControlText;
                btn.Click += (s, e) => SelectCategory((int)btn.Tag);
                categoryPanel.Controls.Add(btn);
                categoryButtons[i] = btn;
                categoryY += btn.Height + 2;
            }

            splitContainer.Panel1.Controls.Add(categoryPanel);

            // 右側: コンテンツパネル
            contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.BorderStyle = BorderStyle.FixedSingle;

            settingsPanel = new SettingsPanel(_settings, _cloudClient);
            settingsPanel.Dock = DockStyle.Fill;
            contentPanel.Controls.Add(settingsPanel);

            splitContainer.Panel2.Controls.Add(contentPanel);

            // ボタンパネル
            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Bottom;
            buttonPanel.Height = 50;

            FlowLayoutPanel flowPanel = new FlowLayoutPanel();
            flowPanel.Dock = DockStyle.Fill;
            flowPanel.FlowDirection = FlowDirection.RightToLeft;
            flowPanel.Padding = new Padding(10);
            flowPanel.AutoSize = true;

            btnCancel = new Button();
            btnCancel.Text = LanguageManager.Translate("キャンセル");
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Size = new Size(100, 30);

            btnOK = new Button();
            btnOK.Text = LanguageManager.Translate("OK");
            btnOK.DialogResult = DialogResult.OK;
            btnOK.Size = new Size(100, 30);

            flowPanel.Controls.Add(btnCancel);
            flowPanel.Controls.Add(btnOK);
            buttonPanel.Controls.Add(flowPanel);

            this.Controls.Add(splitContainer);
            this.Controls.Add(buttonPanel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            // SplitterDistance must be set after the SplitContainer is added to a parent
            // and after the form has been laid out so that the value is honored.
            this.Load += (s, e) =>
            {
                ConfigureSplitter();
                SelectCategory(0);
            };
        }

        private void ConfigureSplitter()
        {
            try
            {
                int width = splitContainer.Width;
                if (width <= 0)
                {
                    return;
                }
                int desiredPanel1Min = 120;
                int desiredPanel2Min = 200;
                int desiredSplitter = 180;
                // 現在の幅に合わせて安全に調整する
                int available = width - splitContainer.SplitterWidth;
                if (available < desiredPanel1Min + 10)
                {
                    desiredPanel1Min = Math.Max(40, available / 3);
                    desiredPanel2Min = Math.Max(40, available - desiredPanel1Min - splitContainer.SplitterWidth);
                }
                else if (desiredPanel1Min + desiredPanel2Min > available)
                {
                    desiredPanel2Min = Math.Max(40, available - desiredPanel1Min);
                }
                int maxDistance = width - desiredPanel2Min - splitContainer.SplitterWidth;
                int clampedDistance = Math.Max(desiredPanel1Min, Math.Min(desiredSplitter, Math.Max(desiredPanel1Min, maxDistance)));
                // 順番が重要: まず SplitterDistance を安全な値にしてから MinSize を適用
                splitContainer.SplitterDistance = clampedDistance;
                splitContainer.Panel1MinSize = desiredPanel1Min;
                splitContainer.Panel2MinSize = desiredPanel2Min;
                splitContainer.SplitterDistance = clampedDistance;
            }
            catch
            {
                // レイアウト未確定時の例外は無視
            }
        }

        private void SelectCategory(int categoryIndex)
        {
            // カテゴリボタンの選択状態を更新
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                categoryButtons[i].BackColor = i == categoryIndex 
                    ? SystemColors.Highlight 
                    : SystemColors.Control;
                categoryButtons[i].ForeColor = i == categoryIndex 
                    ? SystemColors.HighlightText 
                    : SystemColors.ControlText;
            }

            // 選択されたカテゴリに合わせて設定項目を絞り込み表示
            if (settingsPanel != null && categoryIndex >= 0 && categoryIndex < CategoryValues.Length)
            {
                settingsPanel.SetCategoryFilter(CategoryValues[categoryIndex]);
            }
        }
    }
}
