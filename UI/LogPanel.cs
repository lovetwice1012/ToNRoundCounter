using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter.UI
{
    public class LogPanel : Panel
    {
        public RichTextBox AggregateStatsTextBox { get; private set; }
        public RichTextBox RoundLogTextBox { get; private set; }

        public LogPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(560, 340);

            int margin = 50;

            AggregateStatsTextBox = new RichTextBox();
            AggregateStatsTextBox.ReadOnly = true;
            AggregateStatsTextBox.BorderStyle = BorderStyle.FixedSingle;
            AggregateStatsTextBox.Font = new Font("Arial", 10);
            AggregateStatsTextBox.Location = new Point(margin, margin);
            AggregateStatsTextBox.Size = new Size(540, 150);
            this.Controls.Add(AggregateStatsTextBox);

            RoundLogTextBox = new RichTextBox();
            RoundLogTextBox.ReadOnly = true;
            RoundLogTextBox.BorderStyle = BorderStyle.FixedSingle;
            RoundLogTextBox.Font = new Font("Arial", 10);
            RoundLogTextBox.Location = new Point(margin, AggregateStatsTextBox.Bottom + margin);
            RoundLogTextBox.Size = new Size(540, 150);
            this.Controls.Add(RoundLogTextBox);
        }
    }
}
