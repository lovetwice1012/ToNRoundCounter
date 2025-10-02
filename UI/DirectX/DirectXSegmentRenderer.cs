using System;
using System.Collections.Generic;
using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;

namespace ToNRoundCounter.UI.DirectX
{
    internal static class DirectXSegmentRenderer
    {
        [Flags]
        private enum SegmentFlags
        {
            None = 0,
            A = 1 << 0,
            B = 1 << 1,
            C = 1 << 2,
            D = 1 << 3,
            E = 1 << 4,
            F = 1 << 5,
            G = 1 << 6,
            Decimal = 1 << 7,
        }

        private struct Glyph
        {
            public char Symbol { get; set; }

            public bool IncludeDecimal { get; set; }
        }

        private static readonly Dictionary<char, SegmentFlags> SegmentMap = new()
        {
            ['0'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.C | SegmentFlags.D | SegmentFlags.E | SegmentFlags.F,
            ['1'] = SegmentFlags.B | SegmentFlags.C,
            ['2'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.D | SegmentFlags.E | SegmentFlags.G,
            ['3'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.C | SegmentFlags.D | SegmentFlags.G,
            ['4'] = SegmentFlags.B | SegmentFlags.C | SegmentFlags.F | SegmentFlags.G,
            ['5'] = SegmentFlags.A | SegmentFlags.C | SegmentFlags.D | SegmentFlags.F | SegmentFlags.G,
            ['6'] = SegmentFlags.A | SegmentFlags.C | SegmentFlags.D | SegmentFlags.E | SegmentFlags.F | SegmentFlags.G,
            ['7'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.C,
            ['8'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.C | SegmentFlags.D | SegmentFlags.E | SegmentFlags.F | SegmentFlags.G,
            ['9'] = SegmentFlags.A | SegmentFlags.B | SegmentFlags.C | SegmentFlags.D | SegmentFlags.F | SegmentFlags.G,
            ['-'] = SegmentFlags.G,
            [' '] = SegmentFlags.None,
        };

        private const float ColonWidthFactor = 0.4f;

        public static float Measure(string text, float digitWidth, float digitSpacing)
        {
            var glyphs = BuildGlyphs(text);
            if (glyphs.Count == 0)
            {
                return digitWidth;
            }

            float width = 0f;
            foreach (var glyph in glyphs)
            {
                width += glyph.Symbol == ':' ? GetColonWidth(digitWidth) : digitWidth;
                width += digitSpacing;
            }

            if (width > 0f)
            {
                width -= digitSpacing;
            }

            return width;
        }

        public static void Draw(WindowRenderTarget target, string text, RawVector2 origin, float digitWidth, float digitHeight, float digitSpacing, SolidColorBrush onBrush, SolidColorBrush offBrush)
        {
            var glyphs = BuildGlyphs(text);
            if (glyphs.Count == 0)
            {
                return;
            }

            float x = origin.X;
            float y = origin.Y;
            foreach (var glyph in glyphs)
            {
                if (glyph.Symbol == ':')
                {
                    DrawColon(target, x, y, digitWidth, digitHeight, onBrush);
                    x += GetColonWidth(digitWidth) + digitSpacing;
                    continue;
                }

                SegmentFlags segments = GetSegmentsForSymbol(glyph.Symbol);
                if (glyph.IncludeDecimal)
                {
                    segments |= SegmentFlags.Decimal;
                }

                DrawSegments(target, x, y, digitWidth, digitHeight, segments, onBrush, offBrush);
                x += digitWidth + digitSpacing;
            }
        }

        private static void DrawSegments(WindowRenderTarget target, float offsetX, float offsetY, float digitWidth, float digitHeight, SegmentFlags segments, SolidColorBrush onBrush, SolidColorBrush offBrush)
        {
            float thickness = Math.Max(2f, Math.Min(digitWidth, digitHeight) / 6f);
            float halfThickness = thickness / 2f;
            float horizontalLength = digitWidth - thickness;
            float verticalLength = (digitHeight - (3f * thickness)) / 2f;
            float middleY = offsetY + thickness + verticalLength;

            var segmentA = new RawRectangleF(offsetX + halfThickness, offsetY, offsetX + halfThickness + horizontalLength, offsetY + thickness);
            var segmentB = new RawRectangleF(offsetX + digitWidth - thickness, offsetY + thickness, offsetX + digitWidth, offsetY + thickness + verticalLength);
            var segmentC = new RawRectangleF(offsetX + digitWidth - thickness, middleY + thickness, offsetX + digitWidth, middleY + thickness + verticalLength);
            var segmentD = new RawRectangleF(offsetX + halfThickness, offsetY + digitHeight - thickness, offsetX + halfThickness + horizontalLength, offsetY + digitHeight);
            var segmentE = new RawRectangleF(offsetX, middleY + thickness, offsetX + thickness, middleY + thickness + verticalLength);
            var segmentF = new RawRectangleF(offsetX, offsetY + thickness, offsetX + thickness, offsetY + thickness + verticalLength);
            var segmentG = new RawRectangleF(offsetX + halfThickness, middleY, offsetX + halfThickness + horizontalLength, middleY + thickness);
            var decimalRect = new RawRectangleF(offsetX + digitWidth - thickness, offsetY + digitHeight - thickness, offsetX + digitWidth, offsetY + digitHeight);

            FillSegment(target, segmentA, segments.HasFlag(SegmentFlags.A), onBrush, offBrush);
            FillSegment(target, segmentB, segments.HasFlag(SegmentFlags.B), onBrush, offBrush);
            FillSegment(target, segmentC, segments.HasFlag(SegmentFlags.C), onBrush, offBrush);
            FillSegment(target, segmentD, segments.HasFlag(SegmentFlags.D), onBrush, offBrush);
            FillSegment(target, segmentE, segments.HasFlag(SegmentFlags.E), onBrush, offBrush);
            FillSegment(target, segmentF, segments.HasFlag(SegmentFlags.F), onBrush, offBrush);
            FillSegment(target, segmentG, segments.HasFlag(SegmentFlags.G), onBrush, offBrush);

            if (segments.HasFlag(SegmentFlags.Decimal))
            {
                target.FillEllipse(new Ellipse(new RawVector2(decimalRect.Right - ((decimalRect.Right - decimalRect.Left) / 2f), decimalRect.Bottom - ((decimalRect.Bottom - decimalRect.Top) / 2f)), (decimalRect.Right - decimalRect.Left) / 2f, (decimalRect.Bottom - decimalRect.Top) / 2f), onBrush);
            }
        }

        private static void FillSegment(WindowRenderTarget target, RawRectangleF rect, bool active, SolidColorBrush onBrush, SolidColorBrush offBrush)
        {
            target.FillRectangle(rect, offBrush);
            if (active)
            {
                target.FillRectangle(rect, onBrush);
            }
        }

        private static void DrawColon(WindowRenderTarget target, float offsetX, float offsetY, float digitWidth, float digitHeight, SolidColorBrush brush)
        {
            float colonWidth = GetColonWidth(digitWidth);
            float colonHeight = digitHeight;
            float dotRadius = Math.Max(2f, Math.Min(colonWidth, colonHeight) / 6f);
            float centerX = offsetX + colonWidth / 2f;
            float topY = offsetY + digitHeight / 3f;
            float bottomY = offsetY + (2f * digitHeight / 3f);

            var topDot = new Ellipse(new RawVector2(centerX, topY), dotRadius, dotRadius);
            var bottomDot = new Ellipse(new RawVector2(centerX, bottomY), dotRadius, dotRadius);

            target.FillEllipse(topDot, brush);
            target.FillEllipse(bottomDot, brush);
        }

        private static SegmentFlags GetSegmentsForSymbol(char symbol)
        {
            if (SegmentMap.TryGetValue(symbol, out SegmentFlags value))
            {
                return value;
            }

            return SegmentFlags.None;
        }

        private static List<Glyph> BuildGlyphs(string text)
        {
            var glyphs = new List<Glyph>(text?.Length ?? 0);
            if (string.IsNullOrEmpty(text))
            {
                return glyphs;
            }

            foreach (char c in text!)
            {
                if (c == '.')
                {
                    if (glyphs.Count > 0)
                    {
                        int previousIndex = glyphs.Count - 1;
                        Glyph previous = glyphs[previousIndex];
                        previous.IncludeDecimal = true;
                        glyphs[previousIndex] = previous;
                    }

                    continue;
                }

                glyphs.Add(new Glyph { Symbol = c });
            }

            return glyphs;
        }

        private static float GetColonWidth(float digitWidth) => digitWidth * ColonWidthFactor;
    }
}
