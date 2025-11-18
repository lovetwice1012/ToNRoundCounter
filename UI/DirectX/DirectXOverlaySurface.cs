using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
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
        private ID2D1HwndRenderTarget? renderTarget;
        private ID2D1SolidColorBrush? backgroundBrush;
        private ID2D1SolidColorBrush? gripBrush;
        private Color4 backgroundColor = new Color4(0f, 0f, 0f, 0.6f);
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

        protected ID2D1Factory1 Direct2DFactory => DirectXDeviceManager.Instance.Direct2DFactory;

        protected Vortice.DirectWrite.IDWriteFactory DirectWriteFactory => DirectXDeviceManager.Instance.DirectWriteFactory;

        protected ID2D1HwndRenderTarget? RenderTarget => renderTarget;

        protected RawRectF ContentRectangle
        {
            get
            {
                float left = ContentPadding.Left;
                float top = ContentPadding.Top;
                float right = Math.Max(left, Width - ContentPadding.Right);
                float bottom = Math.Max(top, Height - ContentPadding.Bottom);
                return new RawRectF(left, top, right, bottom);
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
            if (renderTarget != null)
            {
                try
                {
                    renderTarget.Resize(new SizeI(Math.Max(Width, 1), Math.Max(Height, 1)));
                }
                catch
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
            backgroundColor = ToColor4(color);
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

        protected abstract void RenderOverlay(ID2D1HwndRenderTarget target);

        protected virtual void RenderAfterOverlay(ID2D1HwndRenderTarget target)
        {
            // Derived classes can override if they need post overlay rendering.
        }

        protected ID2D1SolidColorBrush GetBrush(ref ID2D1SolidColorBrush? brush, Color4 color)
        {
            if (renderTarget == null)
            {
                throw new InvalidOperationException("Render target is not initialized.");
            }

            if (brush == null)
            {
                brush = renderTarget.CreateSolidColorBrush(color);
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
                renderTarget.Clear(new Color4(0f, 0f, 0f, 0f));

                var bounds = new RawRectF(0f, 0f, Math.Max(0, Width), Math.Max(0, Height));
                var rounded = new RoundedRectangle
                {
                    Rect = bounds,
                    RadiusX = CornerRadius,
                    RadiusY = CornerRadius,
                };

                ID2D1SolidColorBrush background = GetBrush(ref backgroundBrush, backgroundColor);
                renderTarget.FillRoundedRectangle(rounded, background);

                RenderOverlay(renderTarget);

                DrawResizeGrip(renderTarget);

                RenderAfterOverlay(renderTarget);

                renderTarget.EndDraw();
                deviceResourcesLost = false;
            }
            catch (Vortice.SharpGenException ex)
            {
                if (ex.HResult == Vortice.Direct2D1.ResultCode.RecreateTarget.Code ||
                    ex.HResult == Vortice.DXGI.ResultCode.DeviceRemoved.Code)
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
            if (renderTarget != null && !deviceResourcesLost)
            {
                return true;
            }

            ReleaseDeviceResources();

            if (!IsHandleCreated)
            {
                return false;
            }

            var renderProps = new RenderTargetProperties
            {
                PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.Direct2D1.AlphaMode.Premultiplied),
                Usage = RenderTargetUsage.None,
            };

            var hwndProps = new HwndRenderTargetProperties
            {
                Hwnd = Handle,
                PixelSize = new SizeI(Math.Max(Width, 1), Math.Max(Height, 1)),
                PresentOptions = PresentOptions.Immediately,
            };

            renderTarget = Direct2DFactory.CreateHwndRenderTarget(renderProps, hwndProps);
            renderTarget.TextAntialiasMode = TextAntialiasMode.Grayscale;

            deviceResourcesLost = false;
            return true;
        }

        private void DrawResizeGrip(ID2D1HwndRenderTarget target)
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

            ID2D1SolidColorBrush brush = GetBrush(ref gripBrush, new Color4(1f, 1f, 1f, 0.6f));

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

                var start = new Vector2(startX, startY);
                var end = new Vector2(endX, endY);
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

        protected static Color4 ToColor4(DrawingColor color)
        {
            const float inverse = 1f / 255f;
            return new Color4(color.R * inverse, color.G * inverse, color.B * inverse, color.A * inverse);
        }

        protected const int ResizeGripSize = 16;
    }
}
