using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Centralized visual theme for overlay surfaces. Provides a consistent
    /// look-and-feel (colors, radii, snap behavior, iconography) across every
    /// overlay window so that the redesign stays coherent.
    /// </summary>
    public static class OverlayTheme
    {
        // --- Surface palette ---------------------------------------------------
        public static readonly Color Surface = Color.FromArgb(18, 20, 26);
        public static readonly Color SurfaceElevated = Color.FromArgb(28, 31, 39);
        public static readonly Color SurfaceHeader = Color.FromArgb(34, 38, 48);
        public static readonly Color BorderLocked = Color.FromArgb(58, 64, 78);
        public static readonly Color BorderEditing = Color.FromArgb(108, 184, 255);
        public static readonly Color BorderSubtle = Color.FromArgb(70, 76, 90);
        public static readonly Color GripColor = Color.FromArgb(160, 200, 210, 230);

        // --- Text --------------------------------------------------------------
        public static readonly Color TextPrimary = Color.FromArgb(245, 247, 250);
        public static readonly Color TextSecondary = Color.FromArgb(205, 211, 225);
        public static readonly Color TextMuted = Color.FromArgb(150, 158, 174);

        // --- State (used by shortcut buttons / status badges) ------------------
        public static readonly Color StateOn = Color.FromArgb(76, 217, 100);
        public static readonly Color StateOff = Color.FromArgb(74, 80, 96);
        public static readonly Color StateDanger = Color.FromArgb(255, 95, 86);
        public static readonly Color StatePending = Color.FromArgb(255, 204, 64);
        public static readonly Color StateInfo = Color.FromArgb(108, 184, 255);
        public static readonly Color StateNeutral = Color.FromArgb(150, 158, 174);

        // --- Geometry / behavior ----------------------------------------------
        public const int CornerRadius = 12;
        public const int HeaderHeight = 22;
        public const int AccentStripHeight = 3;
        public const int SnapDistance = 14;
        public const int ResizeGripSize = 16;
        public const int OuterPadding = 12;

        // --- Section color tokens ---------------------------------------------
        private static readonly Dictionary<string, Color> SectionAccents = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Velocity"] = Color.FromArgb(255, 180, 70),
            ["Angle"] = Color.FromArgb(180, 220, 255),
            ["Terror"] = Color.FromArgb(255, 99, 132),
            ["TerrorInfo"] = Color.FromArgb(255, 145, 165),
            ["Damage"] = Color.FromArgb(255, 122, 89),
            ["NextRound"] = Color.FromArgb(108, 184, 255),
            ["RoundStatus"] = Color.FromArgb(140, 220, 200),
            ["RoundHistory"] = Color.FromArgb(174, 196, 255),
            ["RoundStats"] = Color.FromArgb(72, 201, 176),
            ["Shortcuts"] = Color.FromArgb(255, 204, 64),
            ["Clock"] = Color.FromArgb(120, 200, 255),
            ["InstanceTimer"] = Color.FromArgb(186, 156, 255),
            ["InstanceMembers"] = Color.FromArgb(176, 224, 230),
        };

        public static Color GetSectionAccent(string sectionKey)
        {
            if (!string.IsNullOrEmpty(sectionKey) && SectionAccents.TryGetValue(sectionKey, out var color))
            {
                return color;
            }
            return StateInfo;
        }

        /// <summary>
        /// Per-section short label used as a glanceable identifier in the header.
        /// Falls back to the first character of the supplied title if unknown.
        /// </summary>
        private static readonly Dictionary<string, string> SectionGlyphs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Velocity"] = "VEL",
            ["Angle"] = "ANG",
            ["Terror"] = "TER",
            ["TerrorInfo"] = "INF",
            ["Damage"] = "DMG",
            ["NextRound"] = "NXT",
            ["RoundStatus"] = "ROUND",
            ["RoundHistory"] = "HIST",
            ["RoundStats"] = "STAT",
            ["Shortcuts"] = "CTRL",
            ["Clock"] = "TIME",
            ["InstanceTimer"] = "STAY",
            ["InstanceMembers"] = "MBR",
        };

        public static string GetSectionGlyph(string sectionKey)
        {
            if (!string.IsNullOrEmpty(sectionKey) && SectionGlyphs.TryGetValue(sectionKey, out var glyph))
            {
                return glyph;
            }
            return string.Empty;
        }

        // --- Drawing helpers ---------------------------------------------------
        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            int r = (int)Math.Round(from.R + (to.R - from.R) * amount);
            int g = (int)Math.Round(from.G + (to.G - from.G) * amount);
            int b = (int)Math.Round(from.B + (to.B - from.B) * amount);
            int a = (int)Math.Round(from.A + (to.A - from.A) * amount);
            return Color.FromArgb(a, r, g, b);
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            alpha = Math.Max(0, Math.Min(255, alpha));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
