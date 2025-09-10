using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ToNRoundCounter.UI
{
    public class TerrorInfoPanel : UserControl
    {

        private const int CellWidth = 240;
        private FlowLayoutPanel flow;

        public TerrorInfoPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Theme.Current.PanelBackground;

            this.AutoSize = false;
            this.Visible = false;
            this.Height = 0;

            flow = new FlowLayoutPanel();
            flow.AutoSize = false;
            flow.BackColor = Theme.Current.PanelBackground;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.WrapContents = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Dock = DockStyle.Top;
            this.Controls.Add(flow);
        }

        public void UpdateInfo(List<string> names, JObject data, int width)
        {
            flow.Controls.Clear();
            this.Width = width;


            if (names == null || names.Count == 0)
            {
                this.Visible = false;
                this.Height = 0;

                return;
            }

            this.Visible = true;

            foreach (string name in names)
            {
                var panel = CreateCell(name, data?[name] as JArray);
                panel.Margin = new Padding(5);
                flow.Controls.Add(panel);
            }


            int margin = 5;
            int cellWidth = CellWidth + margin * 2;
            int neededWidth = names.Count * cellWidth;

            bool fits = width >= neededWidth;
            if (fits)
            {
                flow.FlowDirection = FlowDirection.LeftToRight;
                flow.WrapContents = false;
                flow.Width = neededWidth;
            }
            else
            {
                flow.FlowDirection = FlowDirection.TopDown;
                flow.WrapContents = false;
                flow.Width = cellWidth;
            }

            flow.Location = new Point((width - flow.Width) / 2, 0);
            flow.Height = flow.PreferredSize.Height;
            this.Height = flow.Height;

        }

        private Control CreateCell(string name, JArray infoArray)
        {
            var cell = new TableLayoutPanel();
            cell.AutoSize = true;
            cell.BackColor = Theme.Current.PanelBackground;

            cell.MaximumSize = new Size(CellWidth, 0);
            cell.MinimumSize = new Size(CellWidth, 0);
            cell.Width = CellWidth;

            cell.Dock = DockStyle.Fill;
            cell.ColumnCount = 2;
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var title = new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Current.Foreground,

                AutoSize = true,
                MaximumSize = new Size(CellWidth, 0)

            };
            cell.RowCount = 1;
            cell.Controls.Add(title, 0, 0);
            cell.SetColumnSpan(title, 2);

            if (infoArray != null)
            {
                int row = 1;
                foreach (JObject obj in infoArray.OfType<JObject>())
                {
                    var prop = obj.Properties().FirstOrDefault();
                    if (prop == null) continue;

                    cell.RowCount = row + 1;
                    var keyLabel = new Label
                    {
                        Text = prop.Name,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        ForeColor = Theme.Current.Foreground,
                        AutoSize = true,
                        MaximumSize = new Size(CellWidth / 2, 0)
                    };
                    var valLabel = new Label
                    {
                        Text = prop.Value.ToString(),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = Theme.Current.Foreground,
                        AutoSize = true,
                        MaximumSize = new Size(CellWidth / 2, 0)
                    };
                    cell.Controls.Add(keyLabel, 0, row);
                    cell.Controls.Add(valLabel, 1, row);
                    row++;
                }
            }

            return cell;
        }
    }
}

