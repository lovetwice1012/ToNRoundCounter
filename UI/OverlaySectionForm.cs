using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class OverlaySectionForm : Form
    {
        private readonly Label valueLabel;

        public OverlaySectionForm(string title)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(180, 30, 30, 30);
            ForeColor = Color.White;
            Opacity = 0.95;
            Padding = new Padding(12);
            MinimumSize = new Size(240, 100);
            ClientSize = new Size(260, 120);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(layout);

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = Color.White,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            RegisterDragEvents(titleLabel);
            layout.Controls.Add(titleLabel, 0, 0);

            valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 16f, FontStyle.Regular),
                AutoSize = false,
                TextAlign = ContentAlignment.TopLeft,
                BackColor = Color.Transparent,
            };
            RegisterDragEvents(valueLabel);
            layout.Controls.Add(valueLabel, 0, 1);

            RegisterDragEvents(this);
            RegisterDragEvents(layout);
        }

        public void SetValue(string value)
        {
            valueLabel.Text = value ?? string.Empty;
        }

        public void EnsureTopMost()
        {
            if (!TopMost)
            {
                TopMost = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureTopMost();
        }

        private void RegisterDragEvents(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };
        }

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    }
}
