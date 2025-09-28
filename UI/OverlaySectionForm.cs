using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure.Interop;
using ToNRoundCounter.UI.DirectX;

namespace ToNRoundCounter.UI
{
    public class OverlaySectionForm : Form
    {
        protected Control ContentControl { get; }

        private readonly Label? valueLabel;
        private readonly TableLayoutPanel layout;
        private readonly Color baseBackgroundColor;
        private Size baseContentSize;
        private Size pendingBaseContentSize;
        private float currentScaleFactor = 1f;
        private float pendingScaleFactor = 1f;
        private Size defaultBaseContentSize;
        private float defaultScaleFactor = 1f;
        private bool defaultSizeCaptured;
        private bool isUserResizing;
        private bool isInSystemDragOperation;
        private bool interactionActive;
        private double backgroundOpacity = DefaultBackgroundOpacity;

        private const double DefaultBackgroundOpacity = 0.7d;

        public OverlaySectionForm(string title)
            : this(title, null)
        {
        }

        protected OverlaySectionForm(string title, Control? content)
        {
            SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor,
                    true);
            UpdateStyles();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = false;
            DoubleBuffered = true;
            baseBackgroundColor = Color.Black;
            BackColor = baseBackgroundColor;
            ForeColor = Color.White;
            Padding = new Padding(12);
            MinimumSize = new Size(150, 60);
            ClientSize = new Size(220, 110);
            ResizeRedraw = true;
            SetBackgroundOpacity(DefaultBackgroundOpacity);

            layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

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
            ConfigureContentControl();
            layout.Controls.Add(ContentControl, 0, 0);

            RegisterDragEvents(this);
            RegisterDragEvents(layout);
            AdjustSizeToContent();
            baseContentSize = Size;
            pendingBaseContentSize = baseContentSize;
            CaptureDefaultSize();
        }

        public virtual void SetValue(string value)
        {
            if (valueLabel != null)
            {
                valueLabel.Text = value ?? string.Empty;
            }

            AdjustSizeToContent();
        }

        protected void CaptureDefaultSize()
        {
            EnsureBaseContentSize();

            defaultBaseContentSize = baseContentSize;
            defaultScaleFactor = currentScaleFactor <= 0f ? 1f : currentScaleFactor;
            defaultSizeCaptured = true;
        }

        public float ScaleFactor
        {
            get => currentScaleFactor;
            set => SetScaleFactor(value);
        }

        public void EnsureTopMost() => SetTopMostState(true);

        public void SetBackgroundOpacity(double opacity)
        {
            double clamped = opacity;
            if (clamped < 0.2d)
            {
                clamped = 0.2d;
            }
            else if (clamped > 1d)
            {
                clamped = 1d;
            }

            backgroundOpacity = clamped;
            Opacity = clamped;
            Color overlayColor = baseBackgroundColor;
            BackColor = overlayColor;

            if (ContentControl is IDirectXOverlaySurface directXSurface)
            {
                directXSurface.SetBackgroundColor(overlayColor);
            }

            Invalidate();
        }

        public void SetTopMostState(bool shouldBeTopMost)
        {
            bool stateChanged = TopMost != shouldBeTopMost;

            if (stateChanged)
            {
                TopMost = shouldBeTopMost;
            }

            if (IsHandleCreated)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    shouldBeTopMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }

            if (shouldBeTopMost && Visible)
            {
                BringToFront();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRoundedCorners();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedCorners();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ContentControl is IDirectXOverlaySurface directXSurface && directXSurface.HandlesChrome)
            {
                return;
            }

            DrawResizeGrip(e.Graphics);
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

        private void UpdateRoundedCorners()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using GraphicsPath regionPath = CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
            Region?.Dispose();
            Region = new Region(regionPath);
        }

        protected void RegisterDragEvents(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (e.Clicks >= 3)
                {
                    ResetToDefaultSize();
                    return;
                }

                BeginUserInteraction();

                if (!IsInResizeGrip(control, e.Location))
                {
                    ReleaseCapture();
                    _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };

            control.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (!isInSystemDragOperation)
                {
                    EndUserInteraction();
                }
            };
        }

        private void ConfigureContentControl()
        {
            if (ContentControl == null)
            {
                return;
            }

            ContentControl.Dock = DockStyle.Top;
            ContentControl.Margin = new Padding(0);

            if (ContentControl is IDirectXOverlaySurface directXSurface)
            {
                directXSurface.SetBackgroundColor(BackColor);
            }
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
                    isInSystemDragOperation = true;
                    BeginUserInteraction();
                    isUserResizing = true;
                    pendingScaleFactor = currentScaleFactor;
                    pendingBaseContentSize = baseContentSize;
                    break;
                case WM_EXITSIZEMOVE:
                    if (isUserResizing)
                    {
                        isUserResizing = false;
                        baseContentSize = pendingBaseContentSize;
                        SetScaleFactor(pendingScaleFactor);
                        WindowUtilities.TryFocusProcessWindowIfAltNotPressed("VRChat");
                    }
                    if (isInSystemDragOperation)
                    {
                        isInSystemDragOperation = false;
                        EndUserInteraction();
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
            EnsureBaseContentSize();

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            int clampedWidth = ClampDimensionToScale(width, baseContentSize.Width, MinimumSize.Width);
            int clampedHeight = ClampDimensionToScale(height, baseContentSize.Height, MinimumSize.Height);

            rect.Right = rect.Left + clampedWidth;
            rect.Bottom = rect.Top + clampedHeight;

            float scaleFromWidth = baseContentSize.Width <= 0 ? currentScaleFactor : clampedWidth / (float)baseContentSize.Width;
            float scaleFromHeight = baseContentSize.Height <= 0 ? currentScaleFactor : clampedHeight / (float)baseContentSize.Height;

            float scale = currentScaleFactor;
            if (baseContentSize.Width > 0 && baseContentSize.Height > 0)
            {
                scale = Math.Min(scaleFromWidth, scaleFromHeight);
                scale = Math.Max(MinScaleFactor, Math.Min(MaxScaleFactor, scale));
            }

            pendingScaleFactor = scale;

            if (scale <= 0f)
            {
                pendingBaseContentSize = new Size(Math.Max(clampedWidth, MinimumSize.Width), Math.Max(clampedHeight, MinimumSize.Height));
                return;
            }

            int pendingBaseWidth = (int)Math.Round(clampedWidth / scale);
            int pendingBaseHeight = (int)Math.Round(clampedHeight / scale);

            pendingBaseWidth = Math.Max(pendingBaseWidth, MinimumSize.Width);
            pendingBaseHeight = Math.Max(pendingBaseHeight, MinimumSize.Height);

            pendingBaseContentSize = new Size(pendingBaseWidth, pendingBaseHeight);
        }

        private int ClampDimensionToScale(int value, int baseDimension, int minimumDimension)
        {
            if (baseDimension <= 0)
            {
                baseDimension = minimumDimension > 0 ? minimumDimension : value;
            }

            int min = (int)Math.Round(baseDimension * MinScaleFactor);
            int max = (int)Math.Round(baseDimension * MaxScaleFactor);

            if (min <= 0)
            {
                min = baseDimension;
            }

            if (max <= 0)
            {
                max = baseDimension;
            }

            min = Math.Max(min, minimumDimension);
            max = Math.Max(max, min);

            return Math.Max(min, Math.Min(value, max));
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
            pendingBaseContentSize = baseContentSize;
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

        public void ApplySavedSize(Size savedSize)
        {
            savedSize = EnsureMinimumSize(savedSize);
            if (savedSize == Size)
            {
                UpdateBaseContentSizeFromCurrentSize();
                return;
            }

            Size = savedSize;
            UpdateBaseContentSizeFromCurrentSize();
        }

        private Size EnsureMinimumSize(Size size)
        {
            int width = Math.Max(size.Width, MinimumSize.Width);
            int height = Math.Max(size.Height, MinimumSize.Height);
            return new Size(width, height);
        }

        private void UpdateBaseContentSizeFromCurrentSize()
        {
            EnsureBaseContentSize();

            if (currentScaleFactor <= 0f)
            {
                currentScaleFactor = 1f;
            }

            int baseWidth = (int)Math.Round(Size.Width / currentScaleFactor);
            int baseHeight = (int)Math.Round(Size.Height / currentScaleFactor);

            baseWidth = Math.Max(baseWidth, MinimumSize.Width);
            baseHeight = Math.Max(baseHeight, MinimumSize.Height);

            baseContentSize = new Size(baseWidth, baseHeight);
            pendingBaseContentSize = baseContentSize;
        }

        private void EnsureBaseContentSize()
        {
            if (baseContentSize.Width > 0 && baseContentSize.Height > 0)
            {
                return;
            }

            int width = Math.Max(Width, MinimumSize.Width);
            int height = Math.Max(Height, MinimumSize.Height);
            baseContentSize = new Size(width, height);
            pendingBaseContentSize = baseContentSize;
        }

        private void ResetToDefaultSize()
        {
            if (!defaultSizeCaptured)
            {
                CaptureDefaultSize();
            }

            float targetScale = Math.Max(MinScaleFactor, Math.Min(MaxScaleFactor, defaultScaleFactor <= 0f ? 1f : defaultScaleFactor));
            float current = currentScaleFactor <= 0f ? 1f : currentScaleFactor;
            float relativeScale = targetScale / current;

            if (Math.Abs(relativeScale - 1f) > 0.01f)
            {
                Scale(new SizeF(relativeScale, relativeScale));
            }

            currentScaleFactor = targetScale;
            pendingScaleFactor = targetScale;

            Size baseSize = EnsureMinimumSize(defaultBaseContentSize);
            baseContentSize = baseSize;
            pendingBaseContentSize = baseContentSize;

            layout.PerformLayout();
            ApplyScaledSize();
        }

        private void BeginUserInteraction()
        {
            if (interactionActive)
            {
                return;
            }

            interactionActive = true;
            DragInteractionStarted?.Invoke(this, EventArgs.Empty);
        }

        private void EndUserInteraction()
        {
            if (!interactionActive)
            {
                return;
            }

            interactionActive = false;
            DragInteractionEnded?.Invoke(this, EventArgs.Empty);
        }

        private void DrawResizeGrip(Graphics graphics)
        {
            if (graphics == null || Width < 4 || Height < 4)
            {
                return;
            }

            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.None;
            var gripColor = Color.FromArgb(150, 255, 255, 255);

            using var gripPen = new Pen(gripColor, 1f);
            const int lineSpacing = 4;
            int lines = Math.Min(3, (ResizeGripSize / lineSpacing) + 1);

            for (int i = 0; i < lines; i++)
            {
                int offset = 1 + (i * lineSpacing);
                int startX = Width - 2 - offset;
                int startY = Height - 2;
                int endX = Width - 2;
                int endY = Height - 2 - offset;

                if (startX < 0 || endY < 0)
                {
                    continue;
                }

                graphics.DrawLine(gripPen, startX, startY, endX, endY);
            }

            graphics.SmoothingMode = previousMode;
        }

        public event EventHandler? DragInteractionStarted;

        public event EventHandler? DragInteractionEnded;

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
            }
            else
            {
                var arcRect = new Rectangle(bounds.Location, new Size(diameter, diameter));

                path.AddArc(arcRect, 180, 90);

                arcRect.X = bounds.Right - diameter;
                path.AddArc(arcRect, 270, 90);

                arcRect.Y = bounds.Bottom - diameter;
                path.AddArc(arcRect, 0, 90);

                arcRect.X = bounds.Left;
                path.AddArc(arcRect, 90, 90);

                path.CloseFigure();
            }

            return path;
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
        private const int CornerRadius = 14;
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

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new(-1);
            public static readonly IntPtr HWND_NOTOPMOST = new(-2);

            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOACTIVATE = 0x0010;
            public const uint SWP_NOOWNERZORDER = 0x0200;

            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(
                IntPtr hWnd,
                IntPtr hWndInsertAfter,
                int X,
                int Y,
                int cx,
                int cy,
                uint uFlags);
        }
    }
}
