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
            
            // Adjust parent form size to content after updating time
            AdjustSizeToContent();
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
            private readonly Color timeColor = Color.FromArgb(255, 120, 200, 255);
            private readonly float dateTimeFontSize = 14f;
            private readonly float rowSpacing = 6f;

            private string dateText = string.Empty;
            private string timeText = "00:00:00";
            private float dateTextWidth;
            private float dateTextHeight;
            private float timeTextWidth;
            private float timeTextHeight;

            private IDWriteTextFormat? dateFormat;
            private IDWriteTextFormat? timeFormat;
            private ID2D1SolidColorBrush? dateBrush;
            private ID2D1SolidColorBrush? timeBrush;

            public ClockOverlaySurface()
            {
                AutoSize = true;
                ContentPadding = new Padding(12, 10, 12, 12);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            public void SetClockText(string date, string time)
            {
                string nextDate = date ?? string.Empty;
                string nextTime = time ?? string.Empty;
                if (string.Equals(dateText, nextDate, StringComparison.Ordinal) &&
                    string.Equals(timeText, nextTime, StringComparison.Ordinal))
                {
                    return;
                }

                dateText = nextDate;
                timeText = nextTime;
                RecalculatePreferredSize();
                Invalidate();
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                EnsureTextFormat();
                RecalculatePreferredSize();
            }

            protected override void RenderOverlay(ID2D1HwndRenderTarget target)
            {
                if (dateFormat == null || timeFormat == null)
                {
                    EnsureTextFormat();
                }

                var textBrush = GetBrush(ref dateBrush, ToColor4(dateColor));
                var timeBrushLocal = GetBrush(ref timeBrush, ToColor4(timeColor));

                float x = ContentPadding.Left;
                float y = ContentPadding.Top;
                float availableWidth = Math.Max(0f, Width - ContentPadding.Horizontal);

                if (!string.IsNullOrEmpty(dateText) && dateFormat != null && dateTextHeight > 0f)
                {
                    var textRect = new RawRectF(x, y, x + availableWidth, y + dateTextHeight);
                    target.DrawText(dateText, dateFormat, textRect, textBrush, DrawTextOptions.None, MeasuringMode.Natural);
                    y += dateTextHeight + rowSpacing;
                }

                if (!string.IsNullOrEmpty(timeText) && timeFormat != null && timeTextHeight > 0f)
                {
                    var timeRect = new RawRectF(x, y, x + availableWidth, y + timeTextHeight);
                    target.DrawText(timeText, timeFormat, timeRect, timeBrushLocal, DrawTextOptions.None, MeasuringMode.Natural);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    dateBrush?.Dispose();
                    dateBrush = null;

                    timeBrush?.Dispose();
                    timeBrush = null;

                    dateFormat?.Dispose();
                    dateFormat = null;

                    timeFormat?.Dispose();
                    timeFormat = null;
                }

                base.Dispose(disposing);
            }

            private void EnsureTextFormat()
            {
                dateFormat?.Dispose();
                timeFormat?.Dispose();
                string fontFamily = Font?.FontFamily?.Name ?? "Segoe UI";
                dateFormat = DirectWriteFactory.CreateTextFormat(fontFamily, FontWeight.Regular, DWFontStyle.Normal, FontStretch.Normal, dateTimeFontSize);
                dateFormat.TextAlignment = TextAlignment.Leading;
                dateFormat.ParagraphAlignment = ParagraphAlignment.Near;
                timeFormat = DirectWriteFactory.CreateTextFormat(fontFamily, FontWeight.Regular, DWFontStyle.Normal, FontStretch.Normal, dateTimeFontSize);
                timeFormat.TextAlignment = TextAlignment.Leading;
                timeFormat.ParagraphAlignment = ParagraphAlignment.Near;
            }

            private void RecalculatePreferredSize()
            {
                dateTextWidth = 0f;
                dateTextHeight = 0f;
                timeTextWidth = 0f;
                timeTextHeight = 0f;

                if (dateFormat != null && !string.IsNullOrEmpty(dateText))
                {
                    using var layout = DirectWriteFactory.CreateTextLayout(dateText, dateFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    dateTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    dateTextHeight = metrics.Height;
                }

                if (timeFormat != null && !string.IsNullOrEmpty(timeText))
                {
                    using var layout = DirectWriteFactory.CreateTextLayout(timeText, timeFormat, float.MaxValue, float.MaxValue);
                    var metrics = layout.Metrics;
                    timeTextWidth = metrics.WidthIncludingTrailingWhitespace;
                    timeTextHeight = metrics.Height;
                }

                float contentWidth = Math.Max(dateTextWidth, timeTextWidth);
                float contentHeight = timeTextHeight;
                if (dateTextHeight > 0f)
                {
                    contentHeight = dateTextHeight + rowSpacing + timeTextHeight;
                }

                int width = (int)Math.Ceiling(contentWidth + ContentPadding.Horizontal);
                int height = (int)Math.Ceiling(contentHeight + ContentPadding.Vertical);
                UpdatePreferredSize(new Size(width, height));
            }
        }
    }
}
