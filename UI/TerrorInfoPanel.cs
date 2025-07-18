using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ToNRoundCounter.UI
{
    public class TerrorInfoPanel : Panel
    {
        private TableLayoutPanel table;

        public TerrorInfoPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Color.DarkGray;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Visible = false;

            table = new TableLayoutPanel();
            table.AutoSize = true;
            table.Dock = DockStyle.Fill;
            table.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            this.Controls.Add(table);
        }

        public void UpdateInfo(List<string> names, JObject data)
        {
            table.Controls.Clear();
            table.ColumnStyles.Clear();

            if (names == null || names.Count == 0)
            {
                this.Visible = false;
                return;
            }

            this.Visible = true;

            int count = Math.Min(3, names.Count);
            table.ColumnCount = count;
            for (int i = 0; i < count; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
            }

            int col = 0;
            foreach (string name in names.Take(3))
            {
                var panel = CreateCell(name, data?[name] as JArray);
                table.Controls.Add(panel, col, 0);
                col++;
            }
        }

        private Control CreateCell(string name, JArray infoArray)
        {
            var cell = new TableLayoutPanel();
            cell.AutoSize = true;
            cell.Dock = DockStyle.Fill;
            cell.ColumnCount = 2;
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var title = new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                AutoSize = true
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
                        TextAlign = ContentAlignment.MiddleRight,
                        ForeColor = Color.White,
                        AutoSize = true
                    };
                    var valLabel = new Label
                    {
                        Text = prop.Value.ToString(),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = Color.White,
                        AutoSize = true
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

