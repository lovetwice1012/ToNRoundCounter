using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class OverlayShortcutForm : OverlaySectionForm
    {
        private readonly Dictionary<ShortcutButton, Button> buttons = new();
        private readonly Color overlayBaseColor;
        private readonly Color overlayTextColor = Color.White;
        private readonly Color activeBackgroundColor = Color.White;
        private readonly Dictionary<ShortcutButton, bool> toggleStates = new();
        private readonly HashSet<ShortcutButton> pulsingButtons = new();
        private readonly Dictionary<ShortcutButton, Timer> pulseTimers = new();

        public OverlayShortcutForm(string title)
            : base(title, CreateLayout())
        {
            overlayBaseColor = MakeOpaque(BackColor);

            if (ContentControl is TableLayoutPanel layout)
            {
                layout.ColumnCount = 4;
                layout.RowCount = 2;
                layout.AutoSize = true;
                layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                layout.Dock = DockStyle.Fill;
                layout.Margin = new Padding(0);
                layout.Padding = new Padding(0);
                layout.ColumnStyles.Clear();
                layout.RowStyles.Clear();
                for (int i = 0; i < 4; i++)
                {
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                }
                for (int i = 0; i < 2; i++)
                {
                    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }

                AddButton(layout, ShortcutButton.AutoSuicideToggle, "自動自殺\nオン/オフ");
                AddButton(layout, ShortcutButton.AutoSuicideCancel, "自動自殺\nキャンセル");
                AddButton(layout, ShortcutButton.AutoSuicideDelay, "自動自殺\n遅延化");
                AddButton(layout, ShortcutButton.ManualSuicide, "手動自殺");
                AddButton(layout, ShortcutButton.AllRoundsModeToggle, "全ラウンド\n自殺モード");
                AddButton(layout, ShortcutButton.CoordinatedBrainToggle, "統率された\n自爆脳");
                AddButton(layout, ShortcutButton.AfkDetectionToggle, "AFK検知");
                AddButton(layout, ShortcutButton.HideUntilRoundEnd, "ラウンド終わる\nまで隠す");
            }

            MinimumSize = new Size(520, 260);
        }

        public event EventHandler<ShortcutButtonEventArgs>? ShortcutClicked;

        public void SetToggleState(ShortcutButton button, bool active)
        {
            toggleStates[button] = active;
            ApplyButtonVisuals(button);
        }

        public void SetButtonEnabled(ShortcutButton button, bool enabled)
        {
            if (!buttons.TryGetValue(button, out var btn))
            {
                return;
            }

            btn.Enabled = enabled;
            ApplyButtonVisuals(button);
        }

        public void PulseButton(ShortcutButton button, TimeSpan? duration = null)
        {
            if (!buttons.ContainsKey(button))
            {
                return;
            }

            duration ??= TimeSpan.FromMilliseconds(280);
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            if (pulseTimers.TryGetValue(button, out var existing))
            {
                existing.Stop();
                existing.Tick -= PulseTimer_Tick;
                existing.Dispose();
                pulseTimers.Remove(button);
            }

            pulsingButtons.Add(button);
            ApplyButtonVisuals(button);

            var timer = new Timer
            {
                Interval = Math.Max(50, (int)Math.Round(duration.Value.TotalMilliseconds)),
                Tag = button
            };
            timer.Tick += PulseTimer_Tick;
            pulseTimers[button] = timer;
            timer.Start();
        }

        private static TableLayoutPanel CreateLayout()
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private void AddButton(TableLayoutPanel layout, ShortcutButton id, string text)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = overlayBaseColor,
                ForeColor = overlayTextColor,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
                Margin = new Padding(8),
                Padding = new Padding(6),
                AutoSize = false,
                MinimumSize = new Size(110, 110),
                Tag = id,
                UseVisualStyleBackColor = false,
            };
            button.FlatAppearance.BorderSize = 2;
            button.FlatAppearance.BorderColor = overlayTextColor;
            button.Click += Button_Click;
            button.Resize += (_, _) => UpdateButtonRegion(button);
            layout.Controls.Add(button);
            buttons[id] = button;
            toggleStates[id] = false;
            UpdateButtonRegion(button);
            ApplyButtonVisuals(id);
        }

        private void UpdateButtonRegion(Button button)
        {
            int diameter = Math.Min(button.Width, button.Height);
            if (diameter <= 0)
            {
                return;
            }

            var bounds = new Rectangle((button.Width - diameter) / 2, (button.Height - diameter) / 2, diameter, diameter);
            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            button.Region?.Dispose();
            button.Region = new Region(path);
        }

        private void Button_Click(object? sender, EventArgs e)
        {
            if (sender is not Button button || button.Tag is not ShortcutButton id)
            {
                return;
            }

            ShortcutClicked?.Invoke(this, new ShortcutButtonEventArgs(id));
        }

        private void ApplyButtonVisuals(ShortcutButton id)
        {
            if (!buttons.TryGetValue(id, out var button))
            {
                return;
            }

            bool isActive = pulsingButtons.Contains(id) || (toggleStates.TryGetValue(id, out var active) && active);
            Color backgroundColor = isActive ? activeBackgroundColor : overlayBaseColor;
            Color textColor = isActive ? overlayBaseColor : overlayTextColor;

            if (!button.Enabled)
            {
                Color disabledBlendTarget = isActive ? overlayBaseColor : overlayTextColor;
                backgroundColor = Blend(backgroundColor, disabledBlendTarget, 0.35f);
                textColor = Blend(textColor, overlayTextColor, 0.4f);
            }

            button.BackColor = backgroundColor;
            button.ForeColor = textColor;
            button.FlatAppearance.BorderColor = textColor;
            button.FlatAppearance.MouseOverBackColor = Blend(backgroundColor, textColor, 0.15f);
            button.FlatAppearance.MouseDownBackColor = Blend(backgroundColor, textColor, 0.3f);
        }

        public void ResetButtons(params ShortcutButton[] buttonIds)
        {
            foreach (var buttonId in buttonIds)
            {
                if (!toggleStates.ContainsKey(buttonId))
                {
                    continue;
                }

                toggleStates[buttonId] = false;
                if (pulseTimers.TryGetValue(buttonId, out var timer))
                {
                    timer.Stop();
                    timer.Tick -= PulseTimer_Tick;
                    timer.Dispose();
                    pulseTimers.Remove(buttonId);
                }
                pulsingButtons.Remove(buttonId);
                ApplyButtonVisuals(buttonId);
            }
        }

        private void PulseTimer_Tick(object? sender, EventArgs e)
        {
            if (sender is not Timer timer || timer.Tag is not ShortcutButton id)
            {
                return;
            }

            timer.Stop();
            timer.Tick -= PulseTimer_Tick;
            timer.Dispose();
            pulseTimers.Remove(id);
            pulsingButtons.Remove(id);
            ApplyButtonVisuals(id);
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            int r = (int)Math.Round(from.R + (to.R - from.R) * amount);
            int g = (int)Math.Round(from.G + (to.G - from.G) * amount);
            int b = (int)Math.Round(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(255, r, g, b);
        }

        private static Color MakeOpaque(Color color)
        {
            return Color.FromArgb(255, color.R, color.G, color.B);
        }

        public enum ShortcutButton
        {
            AutoSuicideToggle,
            AutoSuicideCancel,
            AutoSuicideDelay,
            ManualSuicide,
            AllRoundsModeToggle,
            CoordinatedBrainToggle,
            AfkDetectionToggle,
            HideUntilRoundEnd,
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var timer in pulseTimers.Values)
                {
                    timer.Stop();
                    timer.Tick -= PulseTimer_Tick;
                    timer.Dispose();
                }
                pulseTimers.Clear();
                pulsingButtons.Clear();
            }

            base.Dispose(disposing);
        }

        public sealed class ShortcutButtonEventArgs : EventArgs
        {
            public ShortcutButtonEventArgs(ShortcutButton button)
            {
                Button = button;
            }

            public ShortcutButton Button { get; }
        }
    }
}
