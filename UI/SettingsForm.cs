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

        public SettingsPanel SettingsPanel { get { return settingsPanel; } }

        public SettingsForm(IAppSettings settings, CloudWebSocketClient? cloudClient = null)
        {
            _settings = settings;
            _cloudClient = cloudClient;

            this.Text = LanguageManager.Translate("設定");
            this.Size = new Size(1850, 1000);
            this.StartPosition = FormStartPosition.CenterParent;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 設定パネルをフォーム全体に配置（下部にボタン用のスペースを確保するため Fill を使用）
            settingsPanel = new SettingsPanel(_settings, _cloudClient);
            settingsPanel.Dock = DockStyle.Fill;

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

            this.Controls.Add(settingsPanel);
            this.Controls.Add(buttonPanel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }
    }
}
