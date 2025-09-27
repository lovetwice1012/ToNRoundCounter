using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class OverlayAngleForm : OverlaySectionForm
    {
        private readonly AngleOverlayPanel anglePanel;

        public OverlayAngleForm(string title)
            : base(title, new AngleOverlayPanel())
        {
            anglePanel = (AngleOverlayPanel)ContentControl;
            MinimumSize = new Size(220, 240);
        }

        public override void SetValue(string value)
        {
            anglePanel.DisplayText = value ?? string.Empty;
            AdjustSizeToContent();
        }

        public void SetAngle(float angleDegrees)
        {
            anglePanel.Angle = angleDegrees;
        }

        private sealed class AngleOverlayPanel : TableLayoutPanel
        {
            private readonly Label valueLabel;
            private readonly AngleIndicatorControl indicator;

            public AngleOverlayPanel()
            {
                ColumnCount = 1;
                RowCount = 2;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                BackColor = Color.Transparent;
                Margin = new Padding(0);
                Padding = new Padding(0);

                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RowStyles.Add(new RowStyle(SizeType.AutoSize));

                valueLabel = new Label
                {
                    AutoSize = true,
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 16f, FontStyle.Bold),
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, 0, 8),
                };
                valueLabel.Anchor = AnchorStyles.Top;

                indicator = new AngleIndicatorControl
                {
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                };
                indicator.Anchor = AnchorStyles.Top;

                Controls.Add(valueLabel, 0, 0);
                Controls.Add(indicator, 0, 1);
            }

            public string DisplayText
            {
                get => valueLabel.Text;
                set => valueLabel.Text = value ?? string.Empty;
            }

            public float Angle
            {
                get => indicator.Angle;
                set => indicator.Angle = value;
            }
        }

        private sealed class AngleIndicatorControl : Control
        {
            private float angle;

            public AngleIndicatorControl()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor,
                    true);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                BackColor = Color.Transparent;
                Size = new Size(180, 180);
                MinimumSize = new Size(140, 140);
            }

            public float Angle
            {
                get => angle;
                set
                {
                    angle = value % 360f;
                    if (angle < 0)
                    {
                        angle += 360f;
                    }
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = ClientRectangle;
                float centerX = rect.Width / 2f;
                float centerY = rect.Height / 2f;
                float radius = Math.Min(rect.Width, rect.Height) / 2f - 10f;
                if (radius <= 0)
                {
                    return;
                }

                using (var tickPen = new Pen(Color.FromArgb(200, Color.White), 2f))
                {
                    tickPen.StartCap = LineCap.Round;
                    tickPen.EndCap = LineCap.Round;
                    for (int i = 0; i < 12; i++)
                    {
                        float tickAngle = (float)(i * 30f * Math.PI / 180f);
                        float inner = radius - 14f;
                        float outer = radius;
                        var innerPoint = new PointF(
                            centerX + inner * (float)Math.Sin(tickAngle),
                            centerY - inner * (float)Math.Cos(tickAngle));
                        var outerPoint = new PointF(
                            centerX + outer * (float)Math.Sin(tickAngle),
                            centerY - outer * (float)Math.Cos(tickAngle));
                        g.DrawLine(tickPen, innerPoint, outerPoint);
                    }
                }

                using var crossPen = new Pen(Color.FromArgb(255, 255, 170, 60), 8f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                using var pointerPen = new Pen(Color.White, 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                var state = g.Save();
                g.TranslateTransform(centerX, centerY);
                g.RotateTransform(angle);
                g.RotateTransform(45f);

                float crossLength = radius * 0.85f;
                g.DrawLine(crossPen, -crossLength, 0, crossLength, 0);
                g.DrawLine(crossPen, 0, -crossLength, 0, crossLength);
                g.Restore(state);

                state = g.Save();
                g.TranslateTransform(centerX, centerY);
                float pointerLength = radius * 0.9f;
                g.DrawLine(pointerPen, 0, 0, 0, -pointerLength);
                g.Restore(state);

                using var centerBrush = new SolidBrush(Color.White);
                float hubSize = 8f;
                g.FillEllipse(centerBrush, centerX - hubSize / 2f, centerY - hubSize / 2f, hubSize, hubSize);
            }
        }
    }
}
