using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Mathematics.Interop;
using ToNRoundCounter.UI.DirectX;

namespace ToNRoundCounter.UI
{
    public sealed class OverlayClockForm : OverlaySectionForm
    {
        private readonly ClockOverlaySurface clockSurface;

        public OverlayClockForm(string title)
            : base(title, new ClockOverlaySurface())
        {
            clockSurface = (ClockOverlaySurface)ContentControl;
            clockSurface.Margin = new Padding(0);
        }

        public void UpdateTime(DateTimeOffset timestamp, CultureInfo culture)
        {
            string dayName = GetDayName(timestamp, culture);
            string dateText = $"{timestamp:yyyy:MM:dd} ({dayName})";
            string timeText = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            clockSurface.SetClockText(dateText, timeText);
        }

        private static string GetDayName(DateTimeOffset timestamp, CultureInfo culture)
        {
            string dayName = culture.DateTimeFormat.GetDayName(timestamp.DayOfWeek);
            if (dayName.EndsWith("曜日", StringComparison.Ordinal))
            {
                int trimmedLength = Math.Max(0, dayName.Length - 2);
                dayName = trimmedLength > 0 ? dayName.Substring(0, trimmedLength) : dayName;
            }

            if (string.IsNullOrEmpty(dayName))
            {
                dayName = culture.DateTimeFormat.GetAbbreviatedDayName(timestamp.DayOfWeek);
            }

            return dayName;
        }

        private sealed class ClockOverlaySurface : DirectXOverlaySurface
        {
            private readonly Color dateColor = Color.White;
            private readonly Color timeOnColor = Color.FromArgb(255, 120, 200, 255);
            private readonly Color timeOffColor = Color.FromArgb(60, 40, 70, 90);
            private readonly float digitWidth = 34f;
            private readonly float digitHeight = 58f;
            private readonly float digitSpacing = 8f;
            private readonly float rowSpacing = 6f;

            private string dateText = string.Empty;
            private string timeText = "00:00:00";
            private float dateTextWidth;
            private float dateTextHeight;

            private TextFormat? dateFormat;
            private SolidColorBrush? dateBrush;
            private SolidColorBrush? segmentOnBrush;
            private SolidColorBrush? segmentOffBrush;

            public ClockOverlaySurface()
            {
                AutoSize = true;
                ContentPadding = new Padding(12, 10, 12, 12);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            public void SetClockText(string date, string time)
            {
                dateText = date ?? string.Empty;
                timeText = time ?? string.Empty;
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
                if (dateFormat == null)
                {
                    EnsureTextFormat();
                }

                var textBrush = GetBrush(ref dateBrush, ToRawColor(dateColor));
                var onBrush = GetBrush(ref segmentOnBrush, ToRawColor(timeOnColor));
                var offBrush = GetBrush(ref segmentOffBrush, ToRawColor(timeOffColor));

                float x = ContentPadding.Left;
                float y = ContentPadding.Top;
                float availableWidth = Math.Max(0f, Width - ContentPadding.Horizontal);

                if (!string.IsNullOrEmpty(dateText) && dateFormat != null && dateTextHeight > 0f)
                {
                    var textRect = new RawRectangleF(x, y, x + availableWidth, y + dateTextHeight);
                    target.DrawText(dateText, dateFormat, textRect, textBrush, DrawTextOptions.None, MeasuringMode.Natural);
                    y += dateTextHeight + rowSpacing;
                }

                DirectXSegmentRenderer.Draw(target, timeText, new RawVector2(x, y), digitWidth, digitHeight, digitSpacing, onBrush, offBrush);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    dateBrush?.Dispose();
                    dateBrush = null;

                    segmentOnBrush?.Dispose();
                    segmentOnBrush = null;

                    segmentOffBrush?.Dispose();
                    segmentOffBrush = null;

                    dateFormat?.Dispose();
                    dateFormat = null;
                }

                base.Dispose(disposing);
            }

            private void EnsureTextFormat()
            {
                dateFormat?.Dispose();
                string fontFamily = Font?.FontFamily?.Name ?? "Segoe UI";
                dateFormat = new TextFormat(DirectWriteFactory, fontFamily, FontWeight.Regular, FontStyle.Normal, FontStretch.Normal, 12f)
                {
                    TextAlignment = TextAlignment.Leading,
                    ParagraphAlignment = ParagraphAlignment.Near,
                };
            }

            private void RecalculatePreferredSize()
            {
                dateTextWidth = 0f;
                dateTextHeight = 0f;

                if (dateFormat != null && !string.IsNullOrEmpty(dateText))
                {
                    using var layout = new TextLayout(DirectWriteFactory, dateText, dateFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    dateTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    dateTextHeight = metrics.Height;
                }

                float timeWidth = DirectXSegmentRenderer.Measure(timeText, digitWidth, digitSpacing);
                float contentWidth = Math.Max(dateTextWidth, timeWidth);
                float contentHeight = digitHeight;
                if (dateTextHeight > 0f)
                {
                    contentHeight = dateTextHeight + rowSpacing + digitHeight;
                }

                int width = (int)Math.Ceiling(contentWidth + ContentPadding.Horizontal);
                int height = (int)Math.Ceiling(contentHeight + ContentPadding.Vertical);
                UpdatePreferredSize(new Size(width, height));
            }
        }
    }
}
