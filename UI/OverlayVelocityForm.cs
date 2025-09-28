using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Mathematics.Interop;
using ToNRoundCounter.UI.DirectX;
using DWFontStyle = SharpDX.DirectWrite.FontStyle;

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
            string afkText = $"AFK: {clampedIdle:F1}秒";
            velocitySurface.SetVelocityText(speedText, afkText);
        }

        private sealed class VelocityOverlaySurface : DirectXOverlaySurface
        {
            private readonly Color speedOnColor = Color.FromArgb(255, 255, 180, 70);
            private readonly Color speedOffColor = Color.FromArgb(70, 120, 70, 20);
            private readonly Color afkColor = Color.White;
            private readonly float digitWidth = 34f;
            private readonly float digitHeight = 58f;
            private readonly float digitSpacing = 8f;
            private readonly float rowSpacing = 6f;
            private readonly float afkFontSize = 10.5f;

            private string speedText = "00.00";
            private string afkText = string.Empty;
            private float afkTextWidth;
            private float afkTextHeight;

            private TextFormat? afkFormat;
            private SolidColorBrush? speedOnBrush;
            private SolidColorBrush? speedOffBrush;
            private SolidColorBrush? afkBrush;

            public VelocityOverlaySurface()
            {
                AutoSize = true;
                ContentPadding = new Padding(12, 10, 12, 12);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            public void SetVelocityText(string speed, string afk)
            {
                speedText = speed ?? string.Empty;
                afkText = afk ?? string.Empty;
                RecalculatePreferredSize();
                Invalidate();
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            protected override void RenderOverlay(WindowRenderTarget target)
            {
                if (afkFormat == null)
                {
                    EnsureTextFormat();
                }

                var onBrush = GetBrush(ref speedOnBrush, ToRawColor(speedOnColor));
                var offBrush = GetBrush(ref speedOffBrush, ToRawColor(speedOffColor));
                var afkTextBrush = GetBrush(ref afkBrush, ToRawColor(afkColor));

                float x = ContentPadding.Left;
                float y = ContentPadding.Top;
                float availableWidth = Math.Max(0f, Width - ContentPadding.Horizontal);

                DirectXSegmentRenderer.Draw(target, speedText, new RawVector2(x, y), digitWidth, digitHeight, digitSpacing, onBrush, offBrush);

                if (!string.IsNullOrEmpty(afkText) && afkFormat != null && afkTextHeight > 0f)
                {
                    y += digitHeight + rowSpacing;
                    var textRect = new RawRectangleF(x, y, x + availableWidth, y + afkTextHeight);
                    target.DrawText(afkText, afkFormat, textRect, afkTextBrush, DrawTextOptions.None, MeasuringMode.Natural);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    speedOnBrush?.Dispose();
                    speedOnBrush = null;

                    speedOffBrush?.Dispose();
                    speedOffBrush = null;

                    afkBrush?.Dispose();
                    afkBrush = null;

                    afkFormat?.Dispose();
                    afkFormat = null;
                }

                base.Dispose(disposing);
            }

            private void EnsureTextFormat()
            {
                afkFormat?.Dispose();
                string fontFamily = Font?.FontFamily?.Name ?? "Segoe UI";
                afkFormat = new TextFormat(DirectWriteFactory, fontFamily, FontWeight.Regular, DWFontStyle.Normal, FontStretch.Normal, afkFontSize)
                {
                    TextAlignment = TextAlignment.Leading,
                    ParagraphAlignment = ParagraphAlignment.Near,
                };
            }

            private void RecalculatePreferredSize()
            {
                afkTextWidth = 0f;
                afkTextHeight = 0f;

                if (afkFormat != null && !string.IsNullOrEmpty(afkText))
                {
                    using var layout = new TextLayout(DirectWriteFactory, afkText, afkFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    afkTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    afkTextHeight = metrics.Height;
                }

                float speedWidth = DirectXSegmentRenderer.Measure(speedText, digitWidth, digitSpacing);
                float contentWidth = Math.Max(speedWidth, afkTextWidth);
                float contentHeight = digitHeight;
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
