using System.Drawing;

namespace ToNRoundCounter.UI
{
    public enum ThemeType
    {
        Dark,
        Light
    }

    public class ThemeColors
    {
        public Color Background { get; init; }
        public Color Foreground { get; init; }
        public Color PanelBackground { get; init; }
    }

    public static class Theme
    {
        public static readonly ThemeColors Dark = new ThemeColors
        {
            Background = Color.FromArgb(45, 45, 48),
            Foreground = Color.White,
            PanelBackground = Color.FromArgb(60, 60, 64)
        };

        public static readonly ThemeColors Light = new ThemeColors
        {
            Background = Color.White,
            Foreground = Color.Black,
            PanelBackground = Color.Gainsboro
        };

        public static ThemeColors Current { get; private set; } = Dark;

        public static void SetTheme(ThemeType type)
        {
            Current = type == ThemeType.Light ? Light : Dark;
        }
    }
}
