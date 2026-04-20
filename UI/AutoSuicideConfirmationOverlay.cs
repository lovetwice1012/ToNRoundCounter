using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Modal overlay used to confirm auto-suicide when other players have
    /// requested to stay alive. Redesigned to use the unified overlay theme,
    /// a progress bar for the countdown, and clearer button hierarchy.
    /// </summary>
    public class AutoSuicideConfirmationOverlay : Form
    {
        private readonly Label headerLabel;
        private readonly Label messageLabel;
        private readonly Label countdownLabel;
        private readonly Panel progressTrack;
        private readonly Panel progressFill;
        private readonly Button yesButton;
        private readonly Button noButton;
        private readonly Timer autoCloseTimer;
        private readonly int desirePlayerCount;
        private readonly int totalSeconds = 10;
        private int remainingSeconds;

        public bool UserConfirmed { get; private set; }
        public bool UserCancelled { get; private set; }

        public AutoSuicideConfirmationOverlay(int desirePlayerCount)
        {
            this.desirePlayerCount = desirePlayerCount;
            remainingSeconds = totalSeconds;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = OverlayTheme.SurfaceElevated;
            ForeColor = OverlayTheme.TextPrimary;
            Size = new Size(540, 260);
            Padding = new Padding(24);
            DoubleBuffered = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            headerLabel = new Label
            {
                Text = "\u26A0  自動自殺の確認",
                ForeColor = OverlayTheme.StatePending,
                Font = new Font("Yu Gothic UI", 14f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            root.Controls.Add(headerLabel, 0, 0);

            messageLabel = new Label
            {
                Text = $"生存希望者が {desirePlayerCount} 人います。\n自動自殺を実行しますか？",
                ForeColor = OverlayTheme.TextPrimary,
                Font = new Font("Yu Gothic UI", 12f, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14),
            };
            root.Controls.Add(messageLabel, 0, 1);

            countdownLabel = new Label
            {
                Text = $"自動実行まで {remainingSeconds} 秒",
                ForeColor = OverlayTheme.TextSecondary,
                Font = new Font("Yu Gothic UI", 10f, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            root.Controls.Add(countdownLabel, 0, 2);

            progressTrack = new Panel
            {
                Height = 6,
                Dock = DockStyle.Top,
                BackColor = OverlayTheme.Surface,
                Margin = new Padding(0, 0, 0, 18),
            };
            progressFill = new Panel
            {
                Dock = DockStyle.Left,
                BackColor = OverlayTheme.StatePending,
                Width = progressTrack.Width,
            };
            progressTrack.Controls.Add(progressFill);
            root.Controls.Add(progressTrack, 0, 3);

            var buttonPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent,
                Height = 48,
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            noButton = CreateButton("キャンセル", OverlayTheme.SurfaceHeader, OverlayTheme.TextPrimary);
            noButton.Click += (s, e) =>
            {
                UserCancelled = true;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttonPanel.Controls.Add(noButton, 0, 0);

            yesButton = CreateButton("実行する", OverlayTheme.StateDanger, Color.White);
            yesButton.Click += (s, e) =>
            {
                UserConfirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttonPanel.Controls.Add(yesButton, 1, 0);

            root.Controls.Add(buttonPanel, 0, 4);
            Controls.Add(root);

            autoCloseTimer = new Timer { Interval = 1000 };
            autoCloseTimer.Tick += AutoCloseTimer_Tick;
            autoCloseTimer.Start();

            Paint += OnFramePaint;
            Load += (_, _) => UpdateProgress();
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(8),
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Yu Gothic UI", 11.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Height = 44,
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = OverlayTheme.Blend(backColor, Color.White, 0.12f);
            btn.FlatAppearance.MouseDownBackColor = OverlayTheme.Blend(backColor, Color.Black, 0.15f);
            return btn;
        }

        private void AutoCloseTimer_Tick(object? sender, EventArgs e)
        {
            remainingSeconds--;
            if (remainingSeconds <= 0)
            {
                autoCloseTimer.Stop();
                UserConfirmed = true;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            countdownLabel.Text = $"自動実行まで {remainingSeconds} 秒";
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (progressTrack.Width <= 0)
            {
                return;
            }
            float ratio = Math.Max(0f, Math.Min(1f, remainingSeconds / (float)totalSeconds));
            progressFill.Width = (int)Math.Round(progressTrack.Width * ratio);
            progressFill.BackColor = ratio > 0.5f
                ? OverlayTheme.StatePending
                : OverlayTheme.Blend(OverlayTheme.StatePending, OverlayTheme.StateDanger, 1f - ratio * 2f);
        }

        private void OnFramePaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Accent strip at top
            using (var accentBrush = new SolidBrush(OverlayTheme.StatePending))
            {
                g.FillRectangle(accentBrush, 0, 0, Width, 4);
            }

            // Outer border
            using (var pen = new Pen(OverlayTheme.WithAlpha(OverlayTheme.StatePending, 200), 1.5f))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
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

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }
    }
}
