using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Shortcut overlay redesigned to use clear iconography, tooltips,
    /// and per-state coloring so that operators can see which actions
    /// are active at a glance.
    /// </summary>
    public class OverlayShortcutForm : OverlaySectionForm
    {
        private readonly Dictionary<ShortcutButton, ShortcutButtonControl> buttons = new();
        private readonly Dictionary<ShortcutButton, bool> toggleStates = new();
        private readonly HashSet<ShortcutButton> pulsingButtons = new();
        private readonly Dictionary<ShortcutButton, Timer> pulseTimers = new();
        private readonly ToolTip toolTip;

        public OverlayShortcutForm(string title)
            : base(title, CreateLayout())
        {
            toolTip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 350,
                ReshowDelay = 200,
                ShowAlways = true,
                BackColor = OverlayTheme.SurfaceElevated,
                ForeColor = OverlayTheme.TextPrimary,
                OwnerDraw = false,
            };

            if (ContentControl is TableLayoutPanel layout)
            {
                ConfigureLayout(layout);

                AddButton(layout, ShortcutButton.AutoSuicideToggle,
                    glyph: "\u2620",
                    label: "自動自殺",
                    tooltip: "自動自殺機能の有効/無効を切り替えます",
                    kind: ButtonKind.Toggle);
                AddButton(layout, ShortcutButton.AutoSuicideCancel,
                    glyph: "\u2715",
                    label: "キャンセル",
                    tooltip: "予約中の自動自殺をキャンセルします (WIP)",
                    kind: ButtonKind.Cancel);
                AddButton(layout, ShortcutButton.AutoSuicideDelay,
                    glyph: "\u29D6",
                    label: "遅延化",
                    tooltip: "予約中の自動自殺の発火を遅らせます (WIP)",
                    kind: ButtonKind.Action);
                AddButton(layout, ShortcutButton.ManualSuicide,
                    glyph: "\u26A0",
                    label: "手動自殺",
                    tooltip: "今すぐ自殺操作を実行します",
                    kind: ButtonKind.Danger);
                AddButton(layout, ShortcutButton.AllRoundsModeToggle,
                    glyph: "\u29BF",
                    label: "全ラウンド",
                    tooltip: "全ラウンドで自動自殺を有効化します",
                    kind: ButtonKind.Toggle);
                AddButton(layout, ShortcutButton.CoordinatedBrainToggle,
                    glyph: "\u2699",
                    label: "統率自殺",
                    tooltip: "統率された自動自殺ロジックを使用します (WIP)",
                    kind: ButtonKind.Toggle);
                AddButton(layout, ShortcutButton.AfkDetectionToggle,
                    glyph: "\u23F1",
                    label: "AFK検知",
                    tooltip: "AFK検知の有効/無効を切り替えます",
                    kind: ButtonKind.Toggle);
                AddButton(layout, ShortcutButton.HideUntilRoundEnd,
                    glyph: "\u25D1",
                    label: "ラウンド終了まで隠す",
                    tooltip: "次のラウンド終了までオーバーレイを非表示にします",
                    kind: ButtonKind.Toggle);
                AddButton(layout, ShortcutButton.EditModeToggle,
                    glyph: "\u270E",
                    label: "編集モード",
                    tooltip: "オーバーレイの位置・サイズを変更できる編集モードを切り替えます",
                    kind: ButtonKind.Toggle);
            }

            MinimumSize = new Size(420, 220);
        }

        public event EventHandler<ShortcutButtonEventArgs>? ShortcutClicked;

        public void SetToggleState(ShortcutButton button, bool active)
        {
            if (toggleStates.TryGetValue(button, out var current) && current == active)
            {
                return;
            }

            toggleStates[button] = active;
            ApplyButtonVisuals(button);
        }

        public void SetButtonEnabled(ShortcutButton button, bool enabled)
        {
            if (!buttons.TryGetValue(button, out var ctl))
            {
                return;
            }

            if (ctl.Enabled == enabled)
            {
                return;
            }

            ctl.Enabled = enabled;
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

        private static TableLayoutPanel CreateLayout()
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoSize = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private static void ConfigureLayout(TableLayoutPanel layout)
        {
            layout.SuspendLayout();
            layout.ColumnCount = 3;
            layout.RowCount = 3;
            layout.AutoSize = false;
            layout.Dock = DockStyle.Fill;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.ColumnStyles.Clear();
            layout.RowStyles.Clear();
            for (int i = 0; i < 3; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
            }
            for (int i = 0; i < 3; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3f));
            }
            layout.ResumeLayout(false);
        }

        private void AddButton(TableLayoutPanel layout, ShortcutButton id, string glyph, string label, string tooltip, ButtonKind kind)
        {
            var button = new ShortcutButtonControl(id, glyph, label, kind)
            {
                Margin = new Padding(4),
                Dock = DockStyle.Fill,
                MinimumSize = new Size(120, 56),
            };
            button.Click += (s, e) => OnButtonClicked(id);
            layout.Controls.Add(button);
            buttons[id] = button;
            toggleStates[id] = false;
            toolTip.SetToolTip(button, tooltip);
            ApplyButtonVisuals(id);
        }

        private void OnButtonClicked(ShortcutButton id)
        {
            ShortcutClicked?.Invoke(this, new ShortcutButtonEventArgs(id));
        }

        private void ApplyButtonVisuals(ShortcutButton id)
        {
            if (!buttons.TryGetValue(id, out var btn))
            {
                return;
            }

            bool active = pulsingButtons.Contains(id) || (toggleStates.TryGetValue(id, out var on) && on);
            btn.SetState(active);
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
            EditModeToggle,
        }

        private enum ButtonKind
        {
            Toggle,
            Action,
            Cancel,
            Danger,
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
                toolTip.Dispose();
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

        /// <summary>
        /// Custom owner-drawn shortcut button. Renders a glyph, label and a
        /// state dot in the corner with state-driven theming.
        /// </summary>
        private sealed class ShortcutButtonControl : Control
        {
            private readonly ShortcutButton id;
            private readonly string glyph;
            private readonly string label;
            private readonly ButtonKind kind;
            private bool isHovered;
            private bool isPressed;
            private bool isActive;

            public ShortcutButtonControl(ShortcutButton id, string glyph, string label, ButtonKind kind)
            {
                this.id = id;
                this.glyph = glyph ?? string.Empty;
                this.label = label ?? string.Empty;
                this.kind = kind;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor,
                    true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f, FontStyle.Bold);
            }

            public ShortcutButton ButtonId => id;

            public void SetState(bool active)
            {
                isActive = active;
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                isHovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                isHovered = false;
                isPressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left)
                {
                    isPressed = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (isPressed)
                {
                    isPressed = false;
                    Invalidate();
                }
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            private static readonly Font s_glyphFont = new Font(SystemFonts.DefaultFont.FontFamily, 16f, FontStyle.Bold);
            private static readonly Font s_fallbackLabelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f, FontStyle.Bold);
            private static readonly SolidBrush s_stateOnBrush = new SolidBrush(OverlayTheme.StateOn);
            private static readonly SolidBrush s_stateOffBrush = new SolidBrush(OverlayTheme.StateOff);
            private static readonly SolidBrush s_statePendingBrush = new SolidBrush(OverlayTheme.StatePending);
            private static readonly StringFormat s_labelFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.LineLimit
            };

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width <= 0 || Height <= 0)
                {
                    return;
                }

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Color bg = GetBackgroundColor();
                Color border = GetBorderColor();
                Color textColor = GetTextColor();
                Color glyphColor = GetAccentColor();

                var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = OverlayTheme.CreateRoundedPath(bounds, 8))
                using (var bgBrush = new SolidBrush(bg))
                using (var borderPen = new Pen(border, isActive ? 1.6f : 1.0f))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }

                Font labelFont = Font ?? s_fallbackLabelFont;
                using var glyphBrush = new SolidBrush(glyphColor);
                using var textBrush = new SolidBrush(textColor);

                var glyphSize = g.MeasureString(glyph, s_glyphFont);
                float glyphX = 10f;
                float glyphY = (Height - glyphSize.Height) / 2f;
                g.DrawString(glyph, s_glyphFont, glyphBrush, glyphX, glyphY);

                float labelX = glyphX + glyphSize.Width + 6f;
                float labelMaxWidth = Math.Max(0f, Width - labelX - 18f);
                var labelRect = new RectangleF(labelX, 4f, labelMaxWidth, Height - 8f);
                g.DrawString(label, labelFont, textBrush, labelRect, s_labelFormat);

                if (kind == ButtonKind.Toggle)
                {
                    var dotBrush = isActive ? s_stateOnBrush : s_stateOffBrush;
                    g.FillEllipse(dotBrush, Width - 14, 6, 7, 7);
                }
                else if ((kind == ButtonKind.Cancel || kind == ButtonKind.Action) && Enabled)
                {
                    g.FillEllipse(s_statePendingBrush, Width - 14, 6, 7, 7);
                }
            }

            private Color GetBackgroundColor()
            {
                Color baseBg = isActive ? OverlayTheme.Blend(OverlayTheme.SurfaceElevated, GetAccentColor(), 0.18f)
                                         : OverlayTheme.SurfaceElevated;
                if (!Enabled)
                {
                    return OverlayTheme.Blend(baseBg, OverlayTheme.Surface, 0.4f);
                }
                if (isPressed)
                {
                    return OverlayTheme.Blend(baseBg, GetAccentColor(), 0.3f);
                }
                if (isHovered)
                {
                    return OverlayTheme.Blend(baseBg, GetAccentColor(), 0.12f);
                }
                return baseBg;
            }

            private Color GetBorderColor()
            {
                if (!Enabled)
                {
                    return OverlayTheme.WithAlpha(OverlayTheme.BorderLocked, 160);
                }
                if (isActive)
                {
                    return OverlayTheme.WithAlpha(GetAccentColor(), 230);
                }
                if (isHovered)
                {
                    return OverlayTheme.WithAlpha(GetAccentColor(), 150);
                }
                return OverlayTheme.WithAlpha(OverlayTheme.BorderSubtle, 200);
            }

            private Color GetTextColor()
            {
                if (!Enabled)
                {
                    return OverlayTheme.TextMuted;
                }
                return isActive ? OverlayTheme.TextPrimary : OverlayTheme.TextSecondary;
            }

            private Color GetAccentColor()
            {
                return kind switch
                {
                    ButtonKind.Danger => OverlayTheme.StateDanger,
                    ButtonKind.Cancel => OverlayTheme.StatePending,
                    ButtonKind.Action => OverlayTheme.StateInfo,
                    _ => isActive ? OverlayTheme.StateOn : OverlayTheme.StateInfo,
                };
            }
        }
    }
}
