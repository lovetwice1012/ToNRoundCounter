using System;
using System.Drawing;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Overlay dialog to confirm auto-suicide when desire players exist
    /// </summary>
    public class AutoSuicideConfirmationOverlay : Form
    {
        private readonly Label messageLabel;
        private readonly Button yesButton;
        private readonly Button noButton;
        private readonly Timer autoCloseTimer;
        private int remainingSeconds = 10;

        public bool UserConfirmed { get; private set; }
        public bool UserCancelled { get; private set; }

        public AutoSuicideConfirmationOverlay(int desirePlayerCount)
        {
            // Form configuration
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(220, 30, 30, 30);
            Size = new Size(500, 200);

            // Message label
            messageLabel = new Label
            {
                Text = $"生存希望者が {desirePlayerCount} 人います。\n自動自殺を実行しますか？\n\n({remainingSeconds}秒後に自動実行)",
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 120,
                Padding = new Padding(10),
            };
            Controls.Add(messageLabel);

            // Button panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(80, 10, 80, 10),
                WrapContents = false,
            };

            // Yes button
            yesButton = new Button
            {
                Text = "実行",
                Width = 120,
                Height = 40,
                BackColor = Color.FromArgb(200, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 12f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            yesButton.FlatAppearance.BorderSize = 0;
            yesButton.Click += (s, e) =>
            {
                UserConfirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttonPanel.Controls.Add(yesButton);

            // Spacer
            buttonPanel.Controls.Add(new Panel { Width = 20 });

            // No button
            noButton = new Button
            {
                Text = "キャンセル",
                Width = 120,
                Height = 40,
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 12f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            noButton.FlatAppearance.BorderSize = 0;
            noButton.Click += (s, e) =>
            {
                UserCancelled = true;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttonPanel.Controls.Add(noButton);

            Controls.Add(buttonPanel);

            // Auto-close timer
            autoCloseTimer = new Timer
            {
                Interval = 1000,
            };
            autoCloseTimer.Tick += (s, e) =>
            {
                remainingSeconds--;
                if (remainingSeconds <= 0)
                {
                    autoCloseTimer.Stop();
                    UserConfirmed = true;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    messageLabel.Text = $"生存希望者が {desirePlayerCount} 人います。\n自動自殺を実行しますか？\n\n({remainingSeconds}秒後に自動実行)";
                }
            };
            autoCloseTimer.Start();

            // Border paint
            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(200, 200, 60), 3))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                autoCloseTimer?.Stop();
                autoCloseTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            autoCloseTimer?.Stop();
            base.OnFormClosing(e);
        }
    }
}
