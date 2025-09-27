using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    public class OverlayRoundHistoryForm : OverlaySectionForm
    {
        private readonly TableLayoutPanel historyLayout;

        public OverlayRoundHistoryForm(string title)
            : base(title, CreateLayout())
        {
            historyLayout = (TableLayoutPanel)ContentControl;
        }

        private static TableLayoutPanel CreateLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 0,
                RowCount = 2,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0),
                Margin = new Padding(0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        public void SetHistory(IReadOnlyList<(string Label, string Status)> entries)
        {
            historyLayout.SuspendLayout();
            historyLayout.Controls.Clear();
            historyLayout.ColumnStyles.Clear();
            historyLayout.ColumnCount = Math.Max(entries.Count, 1);

            if (entries.Count == 0)
            {
                historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var emptyLabel = CreateTypeLabel(string.Empty);
                historyLayout.Controls.Add(emptyLabel, 0, 0);
                var emptyStatus = CreateStatusLabel(string.Empty);
                historyLayout.Controls.Add(emptyStatus, 0, 1);
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    var (label, status) = entries[i];
                    var typeLabel = CreateTypeLabel(label);
                    historyLayout.Controls.Add(typeLabel, i, 0);
                    RegisterDragEventsRecursive(typeLabel);

                    var statusLabel = CreateStatusLabel(status);
                    historyLayout.Controls.Add(statusLabel, i, 1);
                    RegisterDragEventsRecursive(statusLabel);
                }
            }

            historyLayout.ResumeLayout(true);
            AdjustSizeToContent();
        }

        private static Label CreateTypeLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Margin = new Padding(4, 0, 4, 0),
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 15f, FontStyle.Regular),
            };
        }

        private static Label CreateStatusLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Margin = new Padding(4, 6, 4, 0),
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f, FontStyle.Bold),
            };
        }

        private void RegisterDragEventsRecursive(Control control)
        {
            RegisterDragEvents(control);
            foreach (Control child in control.Controls)
            {
                RegisterDragEventsRecursive(child);
            }
        }
    }
}
