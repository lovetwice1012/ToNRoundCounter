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
        private bool isApplyingLiveScale;
        private bool interactionActive;
        private double backgroundOpacity = DefaultBackgroundOpacity;

        private const double DefaultBackgroundOpacity = 0.7d;

        // Cached paint resources (colors are static readonly so these are safe to cache statically)
        private static readonly Font s_headerFont = new Font(SystemFonts.DefaultFont.FontFamily, 8.25f, FontStyle.Bold);
        private static readonly SolidBrush s_headerBrush = new SolidBrush(OverlayTheme.SurfaceHeader);
        private static readonly Pen s_dividerPen = new Pen(OverlayTheme.BorderSubtle, 1f);
        private static readonly SolidBrush s_titleBrush = new SolidBrush(OverlayTheme.TextSecondary);
        private static readonly SolidBrush s_mutedBrush = new SolidBrush(OverlayTheme.TextMuted);
        private static readonly SolidBrush s_editingBrush = new SolidBrush(OverlayTheme.BorderEditing);

        // --- Redesign state --------------------------------------------------
        private readonly string sectionTitle;
        private bool isEditMode;
        private bool isHovered;
        private Color accentColor = OverlayTheme.StateInfo;
        private string sectionKey = string.Empty;

        /// <summary>
        /// When true, the overlay is interactive (drag/resize/snap enabled,
        /// header + handles visible). When false the overlay is locked so users
        /// can't accidentally move or resize it while playing.
        /// </summary>
        public bool IsEditMode
        {
            get => isEditMode;
            set => SetEditMode(value);
        }

        /// <summary>Identifier used to look up section accent / glyph.</summary>
        public string SectionKey
        {
            get => sectionKey;
            set
            {
                sectionKey = value ?? string.Empty;
                accentColor = OverlayTheme.GetSectionAccent(sectionKey);
                Invalidate();
            }
        }

        public OverlaySectionForm(string title)
            : this(title, null)
        {
        }

        protected OverlaySectionForm(string title, Control? content)
        {
            sectionTitle = title ?? string.Empty;
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
            baseBackgroundColor = OverlayTheme.Surface;
            BackColor = baseBackgroundColor;
            ForeColor = OverlayTheme.TextPrimary;
            Padding = new Padding(
                OverlayTheme.OuterPadding,
                OverlayTheme.OuterPadding + OverlayTheme.HeaderHeight,
                OverlayTheme.OuterPadding,
                OverlayTheme.OuterPadding);
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
                string nextValue = value ?? string.Empty;
                if (string.Equals(valueLabel.Text, nextValue, StringComparison.Ordinal))
                {
                    return;
                }

                valueLabel.Text = nextValue;
                valueLabel.Invalidate();
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
            // Apply scaling to the inner content live while the user is
            // dragging the resize grip. Without this the form border keeps up
            // with the cursor but the children only re-scale once the mouse is
            // released, which feels like the size is "stuck" mid-drag.
            if (isInSystemDragOperation && isUserResizing && !isApplyingLiveScale)
            {
                ApplyLiveResizeScaling();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawChrome(e.Graphics);
            if (ContentControl is IDirectXOverlaySurface directXSurface && directXSurface.HandlesChrome)
            {
                return;
            }

            if (isEditMode)
            {
                DrawResizeGrip(e.Graphics);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!isHovered)
            {
                isHovered = true;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (isHovered)
            {
                isHovered = false;
                Invalidate();
            }
        }

        private void DrawChrome(Graphics graphics)
        {
            if (graphics == null || Width <= 0 || Height <= 0)
            {
                return;
            }

            SmoothingMode prevSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var headerRect = new Rectangle(0, 0, Width, OverlayTheme.HeaderHeight);
            using (var headerPath = OverlayTheme.CreateRoundedPath(
                new Rectangle(0, 0, Width, Math.Min(Height, OverlayTheme.HeaderHeight + OverlayTheme.CornerRadius)),
                OverlayTheme.CornerRadius))
            {
                graphics.FillPath(s_headerBrush, headerPath);
            }
            graphics.DrawLine(s_dividerPen, 0, OverlayTheme.HeaderHeight, Width, OverlayTheme.HeaderHeight);

            // Accent strip
            var accentRect = new Rectangle(0, 0, Width, OverlayTheme.AccentStripHeight);
            using (var accentBrush = new SolidBrush(OverlayTheme.WithAlpha(accentColor, 220)))
            {
                graphics.FillRectangle(accentBrush, accentRect);
            }

            // Header text: section glyph + title
            string glyph = OverlayTheme.GetSectionGlyph(sectionKey);
            string headerText = string.IsNullOrEmpty(glyph) ? sectionTitle : $"{glyph}  {sectionTitle}";
            using (var glyphBrush = new SolidBrush(OverlayTheme.WithAlpha(accentColor, 230)))
            {
                if (!string.IsNullOrEmpty(glyph))
                {
                    var glyphSize = graphics.MeasureString(glyph, s_headerFont);
                    graphics.DrawString(glyph, s_headerFont, glyphBrush, 10f, (OverlayTheme.HeaderHeight - glyphSize.Height) / 2f);
                    float titleX = 10f + glyphSize.Width + 8f;
                    var titleSize = graphics.MeasureString(sectionTitle, s_headerFont);
                    graphics.DrawString(sectionTitle, s_headerFont, s_titleBrush, titleX, (OverlayTheme.HeaderHeight - titleSize.Height) / 2f);
                }
                else
                {
                    var size = graphics.MeasureString(headerText, s_headerFont);
                    graphics.DrawString(headerText, s_headerFont, s_titleBrush, 10f, (OverlayTheme.HeaderHeight - size.Height) / 2f);
                }

                // Right-side status: lock or edit indicator
                string indicator = isEditMode ? "✎ EDIT" : "● LOCKED";
                var indBrush = isEditMode ? s_editingBrush : s_mutedBrush;
                var indSize = graphics.MeasureString(indicator, s_headerFont);
                graphics.DrawString(indicator, s_headerFont, indBrush, Width - indSize.Width - 10f, (OverlayTheme.HeaderHeight - indSize.Height) / 2f);
            }

            // Outer border
            Color borderColor = isEditMode
                ? OverlayTheme.BorderEditing
                : isHovered ? OverlayTheme.BorderSubtle : OverlayTheme.BorderLocked;
            float borderWidth = isEditMode ? 1.6f : 1f;
            using (var borderPen = new Pen(OverlayTheme.WithAlpha(borderColor, 220), borderWidth))
            using (var borderPath = OverlayTheme.CreateRoundedPath(
                new Rectangle(0, 0, Width - 1, Height - 1), OverlayTheme.CornerRadius))
            {
                graphics.DrawPath(borderPen, borderPath);
            }

            graphics.SmoothingMode = prevSmoothing;
        }

        public void SetEditMode(bool enabled)
        {
            if (isEditMode == enabled)
            {
                return;
            }

            isEditMode = enabled;
            // Make sure we drop any pending interaction state when locking
            if (!enabled)
            {
                isUserResizing = false;
                isInSystemDragOperation = false;
                EndUserInteraction();
            }
            Invalidate();
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

            // While the user is interactively editing (edit mode toggled, an
            // active drag/resize, or a system-driven move/size loop), do not
            // auto-fit the form to its content. Doing so would snap the form
            // back to the preferred content size on every periodic data
            // update, undoing the user's manual resize.
            if (isEditMode || isUserResizing || isInSystemDragOperation || interactionActive)
            {
                UpdateBaseContentSizeFromCurrentSize();
                return;
            }

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
                    if (isEditMode)
                    {
                        ResetToDefaultSize();
                    }
                    return;
                }

                // Check if clicking on the header area (always draggable)
                Point clickPoint = (control is Form || control is TableLayoutPanel) 
                    ? e.Location 
                    : control.PointToScreen(e.Location);
                if (control is not Form)
                {
                    clickPoint = PointToClient(clickPoint);
                }

                bool isInHeader = clickPoint.Y < OverlayTheme.HeaderHeight;

                if (isInHeader)
                {
                    // Header is always draggable, regardless of edit mode
                    BeginUserInteraction();
                    ReleaseCapture();
                    _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
                else if (isEditMode)
                {
                    // Non-header areas are only draggable in edit mode
                    BeginUserInteraction();

                    if (!IsInResizeGrip(control, e.Location))
                    {
                        ReleaseCapture();
                        _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    }
                }
                // else: locked mode, non-header area - ignore drag attempts
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

        protected static void SuspendDrawing(Control control)
        {
            if (control == null || !control.IsHandleCreated)
            {
                return;
            }

            _ = SendMessage(control.Handle, WM_SETREDRAW, 0, 0);
        }

        protected static void ResumeDrawing(Control control)
        {
            if (control == null || !control.IsHandleCreated)
            {
                return;
            }

            _ = SendMessage(control.Handle, WM_SETREDRAW, 1, 0);
            control.Invalidate(true);
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
                    if ((int)m.Result == HTCLIENT && isEditMode)
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
                        // Children were already scaled live during the drag
                        // via ApplyLiveResizeScaling; just finalise the form
                        // size so currentScaleFactor and baseContentSize stay
                        // in sync with the final outer size.
                        ApplyLiveResizeScaling(force: true);
                        pendingScaleFactor = currentScaleFactor;
                        pendingBaseContentSize = baseContentSize;
                        WindowUtilities.TryFocusProcessWindowIfAltNotPressed("VRChat");
                    }
                    if (isInSystemDragOperation)
                    {
                        isInSystemDragOperation = false;
                        EndUserInteraction();
                    }
                    break;
                case WM_SIZING:
                    if (!isEditMode)
                    {
                        // Locked: cancel any resize attempt.
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    if (isUserResizing)
                    {
                        var rect = Marshal.PtrToStructure<RECT>(m.LParam);
                        AdjustSizingRectangle(ref rect);
                        Marshal.StructureToPtr(rect, m.LParam, true);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case WM_MOVING:
                    if (isEditMode)
                    {
                        var rect = Marshal.PtrToStructure<RECT>(m.LParam);
                        if (TryApplySnapping(ref rect))
                        {
                            Marshal.StructureToPtr(rect, m.LParam, true);
                        }
                        m.Result = (IntPtr)1;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        private bool TryApplySnapping(ref RECT rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            var screen = Screen.FromPoint(new Point(rect.Left, rect.Top));
            var area = screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            int snap = OverlayTheme.SnapDistance;
            bool snapped = false;

            // Edge snaps
            if (Math.Abs(rect.Left - area.Left) <= snap)
            {
                rect.Left = area.Left;
                rect.Right = rect.Left + width;
                snapped = true;
            }
            else if (Math.Abs(rect.Right - area.Right) <= snap)
            {
                rect.Right = area.Right;
                rect.Left = rect.Right - width;
                snapped = true;
            }

            if (Math.Abs(rect.Top - area.Top) <= snap)
            {
                rect.Top = area.Top;
                rect.Bottom = rect.Top + height;
                snapped = true;
            }
            else if (Math.Abs(rect.Bottom - area.Bottom) <= snap)
            {
                rect.Bottom = area.Bottom;
                rect.Top = rect.Bottom - height;
                snapped = true;
            }

            // Center horizontal/vertical snap
            int centerX = area.Left + area.Width / 2;
            int rectCenterX = rect.Left + width / 2;
            if (Math.Abs(rectCenterX - centerX) <= snap)
            {
                rect.Left = centerX - width / 2;
                rect.Right = rect.Left + width;
                snapped = true;
            }

            return snapped;
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

        private void ApplyLiveResizeScaling(bool force = false)
        {
            EnsureBaseContentSize();
            if (baseContentSize.Width <= 0 || baseContentSize.Height <= 0)
            {
                return;
            }

            float scaleW = Width / (float)baseContentSize.Width;
            float scaleH = Height / (float)baseContentSize.Height;
            float scale = Math.Min(scaleW, scaleH);
            scale = Math.Max(MinScaleFactor, Math.Min(MaxScaleFactor, scale));

            if (!force && Math.Abs(scale - currentScaleFactor) < 0.005f)
            {
                return;
            }

            float previousScale = currentScaleFactor <= 0f ? 1f : currentScaleFactor;
            float relative = scale / previousScale;
            currentScaleFactor = scale;
            pendingScaleFactor = scale;

            if (Math.Abs(relative - 1f) <= 0.005f)
            {
                return;
            }

            // Scale only the inner layout (and its children) so we don't
            // recursively change the form's outer Size while Windows is
            // already driving the resize loop.
            isApplyingLiveScale = true;
            try
            {
                layout.SuspendLayout();
                layout.Scale(new SizeF(relative, relative));
                layout.ResumeLayout(true);
                layout.PerformLayout();
            }
            finally
            {
                isApplyingLiveScale = false;
            }
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

            isUserResizing = true;
            try
            {
                Size = new Size(targetWidth, targetHeight);
            }
            finally
            {
                isUserResizing = false;
            }
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
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw three small dots in a triangular arrangement at the bottom-right.
            using var dotBrush = new SolidBrush(OverlayTheme.GripColor);
            const int dotSize = 3;
            const int spacing = 4;
            int baseX = Width - 6;
            int baseY = Height - 6;
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    int x = baseX - col * spacing;
                    int y = baseY - (row - col) * spacing;
                    graphics.FillEllipse(dotBrush, x - dotSize, y - dotSize, dotSize, dotSize);
                }
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
        private const int WM_MOVING = 0x0216;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_SETREDRAW = 0x000B;
        private const int HTCLIENT = 1;
        private const int HTBOTTOMRIGHT = 17;
        private const int ResizeGripSize = OverlayTheme.ResizeGripSize;
        private const int CornerRadius = OverlayTheme.CornerRadius;
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
