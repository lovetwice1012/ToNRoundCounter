using System;
using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.UI;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter.UI
{
    public class SettingsForm : Form
    {
        private SettingsPanel settingsPanel;
        private Button btnOK;
        private Button btnCancel;

        public SettingsPanel SettingsPanel { get { return settingsPanel; } }

        public SettingsForm()
        {
            this.Text = LanguageManager.Translate("設定");
            this.Size = new Size(600, 950);
            this.StartPosition = FormStartPosition.CenterParent;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 設定パネルをフォーム全体に配置（下部にボタン用のスペースを確保するため Fill を使用）
            settingsPanel = new SettingsPanel();
            settingsPanel.Dock = DockStyle.Fill;

            // 下部にボタン専用のパネルを追加
            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Bottom;
            buttonPanel.Height = 50; // ボタンパネルの高さ

            // FlowLayoutPanel を使用して OK と キャンセル ボタンを右寄せに配置
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

            // フォームに設定パネルとボタンパネルを追加
            this.Controls.Add(settingsPanel);
            this.Controls.Add(buttonPanel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }
    }
}
