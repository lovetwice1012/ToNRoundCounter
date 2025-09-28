using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class OverlayRoundStatsForm : OverlaySectionForm
    {
        public readonly struct RoundStatEntry
        {
            public RoundStatEntry(string roundName, int total, int survival, int death)
            {
                RoundName = roundName ?? string.Empty;
                Total = Math.Max(0, total);
                Survival = Math.Max(0, survival);
                Death = Math.Max(0, death);
            }

            public string RoundName { get; }
            public int Total { get; }
            public int Survival { get; }
            public int Death { get; }
            public double SurvivalRate => Total == 0 ? 0d : Survival * 100d / Total;
        }

        private static readonly Font SummaryFont = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f, FontStyle.Regular);
        private static readonly Font RoundNameFont = new Font(SystemFonts.DefaultFont.FontFamily, 10.5f, FontStyle.Bold);
        private static readonly Font RoundTotalFont = new Font(SystemFonts.DefaultFont.FontFamily, 22f, FontStyle.Bold);
        private static readonly Font RoundDetailFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);

        private static readonly Dictionary<string, string> RoundNameTranslations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["クラシック"] = "Classic",
            ["走れ！"] = "RUN",
            ["オルタネイト"] = "Alternate",
            ["パニッシュ"] = "Punish",
            ["狂気"] = "Cracked",
            ["サボタージュ"] = "Sabotage",
            ["霧"] = "Fog",
            ["ブラッドバス"] = "Bloodbath",
            ["ダブルトラブル"] = "Double Trouble",
            ["ミッドナイト"] = "Midnight",
            ["ゴースト"] = "Ghost",
            ["8ページ"] = "8 Page",
            ["アンバウンド"] = "Unbound",
            ["寒い夜"] = "Cold Night",
            ["ミスティックムーン"] = "Mystic Moon",
            ["ブラッドムーン"] = "Blood Moon",
            ["トワイライト"] = "Twilight",
            ["ソルスティス"] = "Solstice",
            ["ミートボール"] = "Meatball",
            ["EX"] = "EX"
        };

        private static readonly Dictionary<string, Color> RoundAccentColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Classic"] = Color.White,
            ["RUN"] = Color.FromArgb(80, 200, 255),
            ["Alternate"] = Color.FromArgb(255, 102, 255),
            ["Punish"] = Color.FromArgb(255, 153, 0),
            ["Cracked"] = Color.FromArgb(255, 111, 97),
            ["Sabotage"] = Color.FromArgb(255, 206, 86),
            ["Fog"] = Color.FromArgb(179, 196, 216),
            ["Bloodbath"] = Color.FromArgb(220, 53, 69),
            ["Double Trouble"] = Color.FromArgb(0, 188, 212),
            ["EX"] = Color.FromArgb(102, 204, 255),
            ["Midnight"] = Color.FromArgb(173, 147, 255),
            ["Ghost"] = Color.FromArgb(156, 220, 254),
            ["8 Page"] = Color.FromArgb(255, 193, 7),
            ["Unbound"] = Color.FromArgb(72, 201, 176),
            ["Cold Night"] = Color.FromArgb(135, 196, 255),
            ["Mystic Moon"] = Color.FromArgb(111, 207, 255),
            ["Blood Moon"] = Color.FromArgb(255, 99, 132),
            ["Twilight"] = Color.FromArgb(214, 162, 232),
            ["Solstice"] = Color.FromArgb(255, 193, 59),
            ["Meatball"] = Color.FromArgb(255, 159, 64)
        };

        private readonly FlowLayoutPanel statsPanel;
        private readonly Label totalRoundsLabel;

        public OverlayRoundStatsForm(string title)
            : base(title, CreateLayout(out FlowLayoutPanel panel, out Label summaryLabel))
        {
            statsPanel = panel;
            totalRoundsLabel = summaryLabel;
            totalRoundsLabel.Font = SummaryFont;
            totalRoundsLabel.ForeColor = Color.Gainsboro;
            totalRoundsLabel.Text = string.Empty;
            RegisterDragEvents(totalRoundsLabel);
        }

        private static Control CreateLayout(out FlowLayoutPanel panel, out Label summaryLabel)
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            summaryLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8),
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(summaryLabel, 0, 0);

            panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.Controls.Add(panel, 0, 1);

            return layout;
        }

        public void SetStats(IReadOnlyList<RoundStatEntry> entries, int totalRounds)
        {
            totalRoundsLabel.Text = $"Total Rounds: {Math.Max(0, totalRounds)}";

            statsPanel.SuspendLayout();
            statsPanel.Controls.Clear();

            if (entries == null || entries.Count == 0)
            {
                var emptyLabel = new Label
                {
                    AutoSize = true,
                    ForeColor = Color.Gainsboro,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0),
                    Text = "No statistics available",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                statsPanel.Controls.Add(emptyLabel);
                RegisterDragEvents(emptyLabel);
            }
            else
            {
                foreach (var entry in entries)
                {
                    var card = CreateStatCard(entry);
                    statsPanel.Controls.Add(card);
                    RegisterDragEventsRecursive(card);
                }
            }

            statsPanel.ResumeLayout(true);
            AdjustSizeToContent();
        }

        private Control CreateStatCard(RoundStatEntry entry)
        {
            var displayName = TranslateRoundName(entry.RoundName);
            var accent = GetAccentColor(displayName);
            var card = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6),
                Padding = new Padding(10, 8, 10, 8),
                BackColor = GetCardBackgroundColor(accent),
                MinimumSize = new Size(140, 0)
            };

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var nameLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = accent,
                Font = RoundNameFont,
                Margin = new Padding(0),
                Text = string.IsNullOrWhiteSpace(displayName) ? "?" : displayName,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var totalLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = RoundTotalFont,
                Margin = new Padding(0, 4, 0, 0),
                Text = entry.Total.ToString("000"),
                TextAlign = ContentAlignment.MiddleCenter
            };

            string detailText = $"Survived {entry.Survival} / Died {entry.Death} ({entry.SurvivalRate:F1}%)";
            var detailLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.Gainsboro,
                Font = RoundDetailFont,
                Margin = new Padding(0, 6, 0, 0),
                Text = detailText,
                TextAlign = ContentAlignment.MiddleCenter
            };

            layout.Controls.Add(nameLabel, 0, 0);
            layout.Controls.Add(totalLabel, 0, 1);
            layout.Controls.Add(detailLabel, 0, 2);
            layout.Dock = DockStyle.Fill;

            card.Controls.Add(layout);
            return card;
        }

        private static string TranslateRoundName(string roundName)
        {
            if (string.IsNullOrWhiteSpace(roundName))
            {
                return string.Empty;
            }

            var trimmed = roundName.Trim();
            if (RoundNameTranslations.TryGetValue(trimmed, out var translated))
            {
                return translated;
            }

            return trimmed;
        }

        private static Color GetAccentColor(string roundName)
        {
            if (!string.IsNullOrWhiteSpace(roundName) && RoundAccentColors.TryGetValue(roundName.Trim(), out var color))
            {
                return color;
            }

            return Color.FromArgb(200, 200, 200);
        }

        private static Color GetCardBackgroundColor(Color accent)
        {
            var baseColor = Color.FromArgb(32, 32, 36);
            return MixColors(baseColor, accent, 0.2f);
        }

        private static Color MixColors(Color baseColor, Color accent, float accentWeight)
        {
            accentWeight = Clamp01(accentWeight);
            float baseWeight = 1f - accentWeight;

            int r = ClampToByte(baseColor.R * baseWeight + accent.R * accentWeight);
            int g = ClampToByte(baseColor.G * baseWeight + accent.G * accentWeight);
            int b = ClampToByte(baseColor.B * baseWeight + accent.B * accentWeight);

            return Color.FromArgb(r, g, b);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        private static int ClampToByte(float value)
        {
            if (value < 0f)
            {
                return 0;
            }

            if (value > 255f)
            {
                return 255;
            }

            return (int)Math.Round(value);
        }

        private void RegisterDragEventsRecursive(Control control)
        {
            RegisterDragEvents(control);
            foreach (Control child in control.Controls.Cast<Control>())
            {
                RegisterDragEventsRecursive(child);
            }
        }
    }
}
