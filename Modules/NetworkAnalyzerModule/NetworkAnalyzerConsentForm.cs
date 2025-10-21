using System;
using System.Drawing;
using System.Windows.Forms;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    internal sealed class NetworkAnalyzerConsentForm : Form
    {
        private readonly Button _confirmButton;
        private readonly Button _acceptButton;

        public NetworkAnalyzerConsentForm()
        {
            Text = "NetworkAnalyzer 利用確認";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Padding = new Padding(10);
            MinimumSize = new Size(520, 420);

            var descriptionBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                BackColor = SystemColors.Window,
                Text = string.Join(Environment.NewLine, new[]
                {
                    "以下の内容を十分に理解したうえで同意してください:",
                    string.Empty,
                    "・このモジュールはローカルからのみアクセス可能なプロキシを構築する",
                    "・このモジュールは通信をキャプチャし、暗号化を解除して中身を確認する機能が入っている",
                    "・確認したデータはこのモジュールの動作のためだけに利用し、絶対にそれ以外の目的で利用しない。ログとしてあなたのPC内に保存される可能性はあるが、あなたの許可なしに外部に送信することは絶対にない。",
                    "・このモジュールをmodulesフォルダから削除することであなたはいつでもこれらの動作を停止できるし、それ以降はこれらの項目についての同意を撤回したものとみなす",
                    "・このモジュールの動作には構造上あなたのプライバシーに関する懸念を生みかねない機能がある。最大限プライバシーに配慮し、セキュリティリスクを最低限に抑えることを約束する",
                    "・このポップアップに同意した後、このモジュールはあなた専用の暗号鍵を作成し、システムへインストールを試みる。間違って同意ボタンを押したとしてもそこで拒否すればこのモジュールは動作しない。",
                    "・アンインストールにあたって疑問がある場合はdiscordId:yussyまで問い合わせてほしい",
                })
            };

            _confirmButton = new Button
            {
                Text = "内容を理解しました (1/2)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(3)
            };
            _confirmButton.Click += ConfirmButtonOnClick;

            _acceptButton = new Button
            {
                Text = "上記に同意する (2/2)",
                Enabled = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(3)
            };
            _acceptButton.Click += AcceptButtonOnClick;

            var declineButton = new Button
            {
                Text = "同意しない",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(3)
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            buttonPanel.Controls.Add(declineButton);
            buttonPanel.Controls.Add(_acceptButton);
            buttonPanel.Controls.Add(_confirmButton);

            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0)
            };
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            container.Controls.Add(descriptionBox, 0, 0);
            container.Controls.Add(buttonPanel, 0, 1);

            Controls.Add(container);

            CancelButton = declineButton;
        }

        public bool ConsentConfirmed { get; private set; }

        private void ConfirmButtonOnClick(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(this,
                "上記の内容を本当に理解し、自己責任でNetworkAnalyzerを利用しますか？",
                "理解の最終確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (result == DialogResult.Yes)
            {
                _confirmButton.Enabled = false;
                _acceptButton.Enabled = true;
                AcceptButton = _acceptButton;
            }
        }

        private void AcceptButtonOnClick(object? sender, EventArgs e)
        {
            var confirmation = MessageBox.Show(this,
                "暗号鍵の作成とインストール処理を行います。継続しますか？",
                "同意の最終確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirmation == DialogResult.Yes)
            {
                ConsentConfirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
