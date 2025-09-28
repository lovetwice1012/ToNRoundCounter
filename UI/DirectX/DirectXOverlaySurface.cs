using System;
using System.Drawing;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;
using DrawingColor = System.Drawing.Color;
namespace ToNRoundCounter.UI.DirectX
{
    internal interface IDirectXOverlaySurface
    {
        void SetBackgroundColor(DrawingColor color);

        bool HandlesChrome { get; }
    }

    internal abstract class DirectXOverlaySurface : Control, IDirectXOverlaySurface
    {
        private WindowRenderTarget? renderTarget;
        private SolidColorBrush? backgroundBrush;
        private SolidColorBrush? gripBrush;
        private RawColor4 backgroundColor = new RawColor4(0f, 0f, 0f, 0.6f);
        private Size preferredSize = new Size(220, 120);
        private bool deviceResourcesLost;

        protected DirectXOverlaySurface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            UpdateStyles();
            Margin = new Padding(0);
            DoubleBuffered = false;
        }

        public Padding ContentPadding { get; set; } = new Padding(12, 10, 12, 12);

        public bool HandlesChrome => true;

        protected Factory1 Direct2DFactory => DirectXDeviceManager.Instance.Direct2DFactory;

        protected SharpDX.DirectWrite.Factory DirectWriteFactory => DirectXDeviceManager.Instance.DirectWriteFactory;

        protected WindowRenderTarget? RenderTarget => renderTarget;

        protected RawRectangleF ContentRectangle
        {
            get
            {
                float left = ContentPadding.Left;
                float top = ContentPadding.Top;
                float right = Math.Max(left, Width - ContentPadding.Right);
                float bottom = Math.Max(top, Height - ContentPadding.Bottom);
                return new RawRectangleF(left, top, right, bottom);
            }
        }

        protected virtual float CornerRadius => 14f;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            deviceResourcesLost = true;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            ReleaseDeviceResources();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseDeviceResources();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress default background painting to avoid flicker.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Render();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (renderTarget != null && !renderTarget.IsDisposed)
            {
                try
                {
                    renderTarget.Resize(new Size2(Math.Max(Width, 1), Math.Max(Height, 1)));
                }
                catch (SharpDXException)
                {
                    deviceResourcesLost = true;
                }
            }

            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return preferredSize;
        }

        public void SetBackgroundColor(DrawingColor color)
        {
            backgroundColor = ToRawColor(color);
            Invalidate();
        }

        protected void UpdatePreferredSize(Size size)
        {
            preferredSize = size;
            if (AutoSize)
            {
                Size = preferredSize;
            }
        }

        protected abstract void RenderOverlay(WindowRenderTarget target);

        protected virtual void RenderAfterOverlay(WindowRenderTarget target)
        {
            // Derived classes can override if they need post overlay rendering.
        }

        protected SolidColorBrush GetBrush(ref SolidColorBrush? brush, RawColor4 color)
        {
            if (renderTarget == null)
            {
                throw new InvalidOperationException("Render target is not initialized.");
            }

            if (brush == null || brush.IsDisposed)
            {
                brush?.Dispose();
                brush = new SolidColorBrush(renderTarget, color);
            }
            else
            {
                brush.Color = color;
            }

            return brush;
        }

        private void Render()
        {
            if (!EnsureRenderTarget())
            {
                return;
            }

            if (renderTarget == null)
            {
                return;
            }

            try
            {
                renderTarget.BeginDraw();
                renderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
                renderTarget.Clear(new RawColor4(0f, 0f, 0f, 0f));

                var bounds = new RawRectangleF(0f, 0f, Math.Max(0, Width), Math.Max(0, Height));
                var rounded = new RoundedRectangle
                {
                    Rect = bounds,
                    RadiusX = CornerRadius,
                    RadiusY = CornerRadius,
                };

                SolidColorBrush background = GetBrush(ref backgroundBrush, backgroundColor);
                renderTarget.FillRoundedRectangle(rounded, background);

                RenderOverlay(renderTarget);

                DrawResizeGrip(renderTarget);

                RenderAfterOverlay(renderTarget);

                renderTarget.EndDraw();
                deviceResourcesLost = false;
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode == SharpDX.Direct2D1.ResultCode.RecreateTarget ||
                    ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceRemoved)
                {
                    deviceResourcesLost = true;
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                if (deviceResourcesLost)
                {
                    ReleaseDeviceResources();
                }
            }
        }

        private bool EnsureRenderTarget()
        {
            if (renderTarget != null && !renderTarget.IsDisposed && !deviceResourcesLost)
            {
                return true;
            }

            ReleaseDeviceResources();

            if (!IsHandleCreated)
            {
                return false;
            }

            var renderProps = new RenderTargetProperties(new PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied))
            {
                Usage = RenderTargetUsage.None,
            };

            var hwndProps = new HwndRenderTargetProperties
            {
                Hwnd = Handle,
                PixelSize = new Size2(Math.Max(Width, 1), Math.Max(Height, 1)),
                PresentOptions = PresentOptions.Immediately,
            };

            renderTarget = new WindowRenderTarget(Direct2DFactory, renderProps, hwndProps)
            {
                TextAntialiasMode = TextAntialiasMode.Grayscale,
            };

            deviceResourcesLost = false;
            return true;
        }

        private void DrawResizeGrip(WindowRenderTarget target)
        {
            if (Width < 4 || Height < 4)
            {
                return;
            }

            const float lineSpacing = 4f;
            int lines = Math.Min(3, (int)Math.Max(0, ResizeGripSize / lineSpacing) + 1);
            if (lines <= 0)
            {
                return;
            }

            SolidColorBrush brush = GetBrush(ref gripBrush, new RawColor4(1f, 1f, 1f, 0.6f));

            for (int i = 0; i < lines; i++)
            {
                float offset = 1f + (i * lineSpacing);
                float startX = Width - 2f - offset;
                float startY = Height - 2f;
                float endX = Width - 2f;
                float endY = Height - 2f - offset;

                if (startX < 0f || endY < 0f)
                {
                    continue;
                }

                var start = new RawVector2(startX, startY);
                var end = new RawVector2(endX, endY);
                target.DrawLine(start, end, brush, 1f);
            }
        }

        private void ReleaseDeviceResources()
        {
            gripBrush?.Dispose();
            gripBrush = null;

            backgroundBrush?.Dispose();
            backgroundBrush = null;

            renderTarget?.Dispose();
            renderTarget = null;
        }

        protected static RawColor4 ToRawColor(DrawingColor color)
        {
            const float inverse = 1f / 255f;
            return new RawColor4(color.R * inverse, color.G * inverse, color.B * inverse, color.A * inverse);
        }

        protected const int ResizeGripSize = 16;
    }
}
