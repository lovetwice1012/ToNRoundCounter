using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class ThemeColors
    {
        public Color Background { get; init; }
        public Color Foreground { get; init; }
        public Color PanelBackground { get; init; }
    }

    public sealed class ThemeDescriptor
    {
        public ThemeDescriptor(string key, string displayName, ThemeColors colors, Action<ThemeApplicationContext>? applyAction = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Theme key must be provided", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Theme display name must be provided", nameof(displayName));
            }

            Key = key;
            DisplayName = displayName;
            Colors = colors ?? throw new ArgumentNullException(nameof(colors));
            ApplyAction = applyAction;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public ThemeColors Colors { get; }

        public Action<ThemeApplicationContext>? ApplyAction { get; }
    }

    public sealed class ThemeApplicationContext
    {
        public static readonly ThemeApplicationContext Empty = new ThemeApplicationContext(null, null);

        public ThemeApplicationContext(Form? mainForm, IServiceProvider? serviceProvider)
        {
            MainForm = mainForm;
            ServiceProvider = serviceProvider;
        }

        public Form? MainForm { get; }

        public IServiceProvider? ServiceProvider { get; }
    }

    public static class Theme
    {
        private static readonly Dictionary<string, ThemeDescriptor> _themes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["light"] = new ThemeDescriptor(
                "light",
                "Light",
                new ThemeColors
                {
                    Background = SystemColors.Control,
                    Foreground = Color.Black,
                    PanelBackground = SystemColors.Control
                }),
            ["dark"] = new ThemeDescriptor(
                "dark",
                "Dark",
                new ThemeColors
                {
                    Background = Color.FromArgb(45, 45, 48),
                    Foreground = Color.White,
                    PanelBackground = Color.FromArgb(60, 60, 64)
                })
        };

        private static ThemeDescriptor _current = _themes["light"];

        public static string DefaultThemeKey => "light";

        public static ThemeColors Current => _current.Colors;

        public static ThemeDescriptor CurrentDescriptor => _current;

        public static IReadOnlyCollection<ThemeDescriptor> RegisteredThemes => _themes.Values;

        public static ThemeDescriptor RegisterTheme(ThemeDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _themes[descriptor.Key] = descriptor;
            return descriptor;
        }

        public static ThemeDescriptor RegisterTheme(string key, string displayName, ThemeColors colors, Action<ThemeApplicationContext>? applyAction = null)
        {
            return RegisterTheme(new ThemeDescriptor(key, displayName, colors, applyAction));
        }

        public static ThemeDescriptor EnsureTheme(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return _themes[DefaultThemeKey];
            }

            return _themes.TryGetValue(key, out var descriptor)
                ? descriptor
                : _themes[DefaultThemeKey];
        }

        public static ThemeDescriptor SetTheme(string key, ThemeApplicationContext? context = null)
        {
            _current = EnsureTheme(key);
            _current.ApplyAction?.Invoke(context ?? ThemeApplicationContext.Empty);
            return _current;
        }
    }
}
