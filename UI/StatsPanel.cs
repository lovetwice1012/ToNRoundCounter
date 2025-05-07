using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter.UI
{
    public class StatsPanel : Panel
    {
        public CheckBox ShowStatsCheckBox { get; private set; }
        public CheckBox DebugInfoCheckBox { get; private set; }

        public StatsPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(560, 40);

            int margin = 10;
            ShowStatsCheckBox = new CheckBox();
            ShowStatsCheckBox.Text = LanguageManager.Translate("統計情報を表示する");
            ShowStatsCheckBox.AutoSize = true;
            ShowStatsCheckBox.Location = new Point(margin, margin);
            this.Controls.Add(ShowStatsCheckBox);

            DebugInfoCheckBox = new CheckBox();
            DebugInfoCheckBox.Text = LanguageManager.Translate("デバッグ情報表示");
            DebugInfoCheckBox.AutoSize = true;
            DebugInfoCheckBox.Location = new Point(ShowStatsCheckBox.Right + margin, margin);
            this.Controls.Add(DebugInfoCheckBox);
        }
    }
}
