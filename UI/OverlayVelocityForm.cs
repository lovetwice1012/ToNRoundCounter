using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using ToNRoundCounter.UI.DirectX;
using DWFontStyle = Vortice.DirectWrite.FontStyle;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace ToNRoundCounter.UI
{
    public sealed class OverlayVelocityForm : OverlaySectionForm
    {
        private readonly VelocityOverlaySurface velocitySurface;

        public OverlayVelocityForm(string title)
            : base(title, new VelocityOverlaySurface())
        {
            velocitySurface = (VelocityOverlaySurface)ContentControl;
            velocitySurface.Margin = new Padding(0);
        }

        public void UpdateReadings(double velocity, double idleSeconds)
        {
            double clampedIdle = idleSeconds < 0 ? 0 : idleSeconds;
            string speedText = velocity.ToString("00.00", CultureInfo.InvariantCulture);
            string afkText = $"AFK: {clampedIdle:F1}\u79d2";
            if (velocitySurface.SetVelocityText(speedText, afkText))
            {
                // Layout only needs to be recomputed when the rendered text actually changed.
                // UpdateReadings fires at 20Hz and speed often stays constant across ticks.
                AdjustSizeToContent();
            }
        }

        private sealed class VelocityOverlaySurface : DirectXOverlaySurface
        {
            private readonly Color speedColor = Color.FromArgb(255, 255, 180, 70);
            private readonly Color afkColor = Color.White;
            private readonly float speedFontSize = 18f;
            private readonly float afkFontSize = 10.5f;
            private readonly float rowSpacing = 6f;

            private string speedText = "00.00";
            private string afkText = string.Empty;
            private float speedTextWidth;
            private float speedTextHeight;
            private float afkTextWidth;
            private float afkTextHeight;

            private IDWriteTextFormat? speedFormat;
            private IDWriteTextFormat? afkFormat;
            private ID2D1SolidColorBrush? speedBrush;
            private ID2D1SolidColorBrush? afkBrush;

            public VelocityOverlaySurface()
            {
                AutoSize = true;
                ContentPadding = new Padding(12, 10, 12, 12);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            public bool SetVelocityText(string speed, string afk)
            {
                string nextSpeed = speed ?? string.Empty;
                string nextAfk = afk ?? string.Empty;
                if (string.Equals(speedText, nextSpeed, StringComparison.Ordinal) &&
                    string.Equals(afkText, nextAfk, StringComparison.Ordinal))
                {
                    return false;
                }

                speedText = nextSpeed;
                afkText = nextAfk;
                RecalculatePreferredSize();
                Invalidate();
                return true;
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);

                if (Visible)
                {
                    Invalidate();
                }
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            protected override void RenderOverlay(ID2D1HwndRenderTarget target)
            {
                if (speedFormat == null || afkFormat == null)
                {
                    EnsureTextFormat();
                }

                var speedBrushLocal = GetBrush(ref speedBrush, ToColor4(speedColor));
                var afkBrushLocal = GetBrush(ref afkBrush, ToColor4(afkColor));

                float x = ContentPadding.Left;
                float y = ContentPadding.Top;
                float availableWidth = Math.Max(0f, Width - ContentPadding.Horizontal);

                if (!string.IsNullOrEmpty(speedText) && speedFormat != null && speedTextHeight > 0f)
                {
                    var speedRect = new RawRectF(x, y, x + availableWidth, y + speedTextHeight);
                    target.DrawText(speedText, speedFormat, speedRect, speedBrushLocal, DrawTextOptions.None, MeasuringMode.Natural);
                    y += speedTextHeight + rowSpacing;
                }

                if (!string.IsNullOrEmpty(afkText) && afkFormat != null && afkTextHeight > 0f)
                {
                    var textRect = new RawRectF(x, y, x + availableWidth, y + afkTextHeight);
                    target.DrawText(afkText, afkFormat, textRect, afkBrushLocal, DrawTextOptions.None, MeasuringMode.Natural);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    speedBrush?.Dispose();
                    speedBrush = null;

                    afkBrush?.Dispose();
                    afkBrush = null;

                    speedFormat?.Dispose();
                    speedFormat = null;

                    afkFormat?.Dispose();
                    afkFormat = null;
                }

                base.Dispose(disposing);
            }

            private void EnsureTextFormat()
            {
                speedFormat?.Dispose();
                afkFormat?.Dispose();
                string fontFamily = Font?.FontFamily?.Name ?? "Segoe UI";
                speedFormat = DirectWriteFactory.CreateTextFormat(fontFamily, FontWeight.Regular, DWFontStyle.Normal, FontStretch.Normal, speedFontSize);
                speedFormat.TextAlignment = TextAlignment.Leading;
                speedFormat.ParagraphAlignment = ParagraphAlignment.Near;
                afkFormat = DirectWriteFactory.CreateTextFormat(fontFamily, FontWeight.Regular, DWFontStyle.Normal, FontStretch.Normal, afkFontSize);
                afkFormat.TextAlignment = TextAlignment.Leading;
                afkFormat.ParagraphAlignment = ParagraphAlignment.Near;
            }

            private void RecalculatePreferredSize()
            {
                speedTextWidth = 0f;
                speedTextHeight = 0f;
                afkTextWidth = 0f;
                afkTextHeight = 0f;

                if (speedFormat != null && !string.IsNullOrEmpty(speedText))
                {
                    using var layout = DirectWriteFactory.CreateTextLayout(speedText, speedFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    speedTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    speedTextHeight = metrics.Height;
                }

                if (afkFormat != null && !string.IsNullOrEmpty(afkText))
                {
                    using var layout = DirectWriteFactory.CreateTextLayout(afkText, afkFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    afkTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    afkTextHeight = metrics.Height;
                }

                float contentWidth = Math.Max(speedTextWidth, afkTextWidth);
                float contentHeight = speedTextHeight;
                if (afkTextHeight > 0f)
                {
                    contentHeight += rowSpacing + afkTextHeight;
                }

                int width = (int)Math.Ceiling(contentWidth + ContentPadding.Horizontal);
                int height = (int)Math.Ceiling(contentHeight + ContentPadding.Vertical);
                UpdatePreferredSize(new Size(width, height));
            }
        }
    }
}
