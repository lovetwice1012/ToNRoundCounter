using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure.Interop;

namespace ToNRoundCounter.UI
{
    public class OverlaySectionForm : Form
    {
        protected Control ContentControl { get; }

        private readonly Label? valueLabel;
        private readonly TableLayoutPanel layout;
        private Size baseContentSize;
        private double aspectRatio;
        private float currentScaleFactor = 1f;
        private float pendingScaleFactor = 1f;
        private bool isUserResizing;

        public OverlaySectionForm(string title)
            : this(title, null)
        {
        }

        protected OverlaySectionForm(string title, Control? content)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = false;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(180, 30, 30, 30);
            ForeColor = Color.White;
            Opacity = 0.95;
            Padding = new Padding(12);
            MinimumSize = new Size(180, 100);
            ClientSize = new Size(220, 120);
            ResizeRedraw = true;

            layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 6),
            };
            RegisterDragEvents(titleLabel);
            layout.Controls.Add(titleLabel, 0, 0);

            if (content == null)
            {
                valueLabel = new Label
                {
                    Dock = DockStyle.Top,
                    ForeColor = Color.White,
                    Font = new Font(Font.FontFamily, 16f, FontStyle.Regular),
                    AutoSize = true,
                    MaximumSize = new Size(600, 0),
                    TextAlign = ContentAlignment.TopLeft,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0),
                };
                content = valueLabel;
            }

            ContentControl = content;
            RegisterDragEvents(ContentControl);
            ContentControl.Dock = DockStyle.Top;
            layout.Controls.Add(ContentControl, 0, 1);

            RegisterDragEvents(this);
            RegisterDragEvents(layout);
            AdjustSizeToContent();
            baseContentSize = Size;
            aspectRatio = Size.Height == 0 ? 1d : (double)Size.Width / Size.Height;
        }

        public virtual void SetValue(string value)
        {
            if (valueLabel != null)
            {
                valueLabel.Text = value ?? string.Empty;
            }

            AdjustSizeToContent();
        }

        public float ScaleFactor
        {
            get => currentScaleFactor;
            set => SetScaleFactor(value);
        }

        public void EnsureTopMost() => SetTopMostState(true);

        public void SetTopMostState(bool shouldBeTopMost)
        {
            if (TopMost == shouldBeTopMost)
            {
                return;
            }

            TopMost = shouldBeTopMost;

            if (shouldBeTopMost && Visible)
            {
                BringToFront();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
            var gripRect = new Rectangle(Width - ResizeGripSize - 2, Height - ResizeGripSize - 2, ResizeGripSize, ResizeGripSize);
            ControlPaint.DrawSizeGrip(e.Graphics, Color.Transparent, gripRect);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureTopMost();
        }

        protected void AdjustSizeToContent()
        {
            if (layout == null)
            {
                return;
            }

            layout.PerformLayout();
            Size preferred = layout.GetPreferredSize(Size.Empty);
            int contentWidth = Math.Max(preferred.Width, MinimumSize.Width - Padding.Horizontal);
            int contentHeight = Math.Max(preferred.Height, MinimumSize.Height - Padding.Vertical);

            Size scaledSize = new Size(contentWidth + Padding.Horizontal, contentHeight + Padding.Vertical);
            UpdateBaseContentSize(scaledSize);
            ApplyScaledSize();
        }

        protected void RegisterDragEvents(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && !IsInResizeGrip(control, e.Location))
                {
                    ReleaseCapture();
                    _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    if ((int)m.Result == HTCLIENT)
                    {
                        var cursorPos = PointToClient(Cursor.Position);
                        if (cursorPos.X >= Width - ResizeGripSize && cursorPos.Y >= Height - ResizeGripSize)
                        {
                            m.Result = (IntPtr)HTBOTTOMRIGHT;
                            return;
                        }
                    }
                    return;
                case WM_ENTERSIZEMOVE:
                    isUserResizing = true;
                    pendingScaleFactor = currentScaleFactor;
                    break;
                case WM_EXITSIZEMOVE:
                    if (isUserResizing)
                    {
                        isUserResizing = false;
                        SetScaleFactor(pendingScaleFactor);
                        WindowUtilities.TryFocusProcessWindowIfAltNotPressed("VRChat");
                    }
                    break;
                case WM_SIZING:
                    if (isUserResizing)
                    {
                        var rect = Marshal.PtrToStructure<RECT>(m.LParam);
                        AdjustSizingRectangle(ref rect);
                        Marshal.StructureToPtr(rect, m.LParam, true);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        private void AdjustSizingRectangle(ref RECT rect)
        {
            if (baseContentSize.Width <= 0 || baseContentSize.Height <= 0)
            {
                baseContentSize = new Size(Math.Max(Width, MinimumSize.Width), Math.Max(Height, MinimumSize.Height));
                aspectRatio = baseContentSize.Height == 0 ? 1d : (double)baseContentSize.Width / baseContentSize.Height;
            }

            double ratio = aspectRatio <= 0 ? 1d : aspectRatio;
            int width = rect.Right - rect.Left;
            int clampedWidth = ClampWidthToScale(width);
            int height = (int)Math.Round(clampedWidth / ratio);
            rect.Right = rect.Left + clampedWidth;
            rect.Bottom = rect.Top + height;

            if (baseContentSize.Width > 0)
            {
                pendingScaleFactor = Math.Max(MinScaleFactor, Math.Min(MaxScaleFactor, clampedWidth / (float)baseContentSize.Width));
            }
        }

        private int ClampWidthToScale(int width)
        {
            int minWidth = (int)Math.Round(baseContentSize.Width * MinScaleFactor);
            int maxWidth = (int)Math.Round(baseContentSize.Width * MaxScaleFactor);
            if (minWidth <= 0)
            {
                minWidth = baseContentSize.Width;
            }
            if (maxWidth <= 0)
            {
                maxWidth = baseContentSize.Width;
            }
            return Math.Max(minWidth, Math.Min(width, maxWidth));
        }

        private void UpdateBaseContentSize(Size scaledSize)
        {
            if (scaledSize.Width <= 0 || scaledSize.Height <= 0)
            {
                return;
            }

            int baseWidth = (int)Math.Round(scaledSize.Width / currentScaleFactor);
            int baseHeight = (int)Math.Round(scaledSize.Height / currentScaleFactor);
            baseWidth = Math.Max(baseWidth, MinimumSize.Width);
            baseHeight = Math.Max(baseHeight, MinimumSize.Height);
            baseContentSize = new Size(baseWidth, baseHeight);
            aspectRatio = baseHeight == 0 ? 1d : (double)baseWidth / baseHeight;
        }

        private void ApplyScaledSize()
        {
            if (baseContentSize.Width <= 0 || baseContentSize.Height <= 0)
            {
                return;
            }

            int targetWidth = (int)Math.Round(baseContentSize.Width * currentScaleFactor);
            int targetHeight = (int)Math.Round(baseContentSize.Height * currentScaleFactor);
            targetWidth = Math.Max(targetWidth, MinimumSize.Width);
            targetHeight = Math.Max(targetHeight, MinimumSize.Height);

            if (Size.Width == targetWidth && Size.Height == targetHeight)
            {
                return;
            }

            Size = new Size(targetWidth, targetHeight);
        }

        private void SetScaleFactor(float scale)
        {
            scale = Math.Max(MinScaleFactor, Math.Min(MaxScaleFactor, scale));
            if (Math.Abs(scale - currentScaleFactor) < 0.01f)
            {
                ApplyScaledSize();
                return;
            }

            float relativeScale = currentScaleFactor == 0f ? scale : scale / currentScaleFactor;
            currentScaleFactor = scale;

            if (Math.Abs(relativeScale - 1f) > 0.01f)
            {
                Scale(new SizeF(relativeScale, relativeScale));
                layout.PerformLayout();
                Size preferred = layout.GetPreferredSize(Size.Empty);
                int contentWidth = Math.Max(preferred.Width, MinimumSize.Width - Padding.Horizontal);
                int contentHeight = Math.Max(preferred.Height, MinimumSize.Height - Padding.Vertical);
                Size scaledSize = new Size(contentWidth + Padding.Horizontal, contentHeight + Padding.Vertical);
                UpdateBaseContentSize(scaledSize);
            }

            ApplyScaledSize();
        }

        private bool IsInResizeGrip(Control control, Point location)
        {
            if (control == null)
            {
                return false;
            }

            Point screenPoint = control.PointToScreen(location);
            Point formPoint = PointToClient(screenPoint);
            return formPoint.X >= Width - ResizeGripSize && formPoint.Y >= Height - ResizeGripSize;
        }

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_SIZING = 0x0214;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCLIENT = 1;
        private const int HTBOTTOMRIGHT = 17;
        private const int ResizeGripSize = 16;
        private const float MinScaleFactor = 0.6f;
        private const float MaxScaleFactor = 2.5f;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
