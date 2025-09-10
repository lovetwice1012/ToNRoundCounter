using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    public class InfoPanel : UserControl
    {
        public Label RoundTypeTitle { get; private set; }
        public Label RoundTypeValue { get; private set; }
        public Label MapTitle { get; private set; }
        public Label MapValue { get; private set; }
        public Label TerrorTitle { get; private set; }
        public Label TerrorValue { get; private set; }
        public Label ItemTitle { get; private set; }
        public Label ItemValue { get; private set; }
        public Label DamageTitle { get; private set; }
        public Label DamageValue { get; private set; }
        public Label NextRoundType { get; private set; }
        public Label IdleTimeLabel { get; private set; }

        public InfoPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Theme.Current.PanelBackground;
            this.Size = new Size(560, 240);

            int margin = 10;
            int currentY = margin;
            int labelWidth = 100;

            RoundTypeTitle = new Label();
            RoundTypeTitle.Text = LanguageManager.Translate("InfoPanel_RoundType");
            RoundTypeTitle.AutoSize = true;
            RoundTypeTitle.ForeColor = Theme.Current.Foreground;
            RoundTypeTitle.Location = new Point(margin, currentY);
            this.Controls.Add(RoundTypeTitle);

            RoundTypeValue = new Label();
            RoundTypeValue.Text = "";
            RoundTypeValue.AutoSize = true;
            RoundTypeValue.ForeColor = Theme.Current.Foreground;
            RoundTypeValue.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(RoundTypeValue);
            currentY += RoundTypeTitle.Height + margin;

            MapTitle = new Label();
            MapTitle.Text = LanguageManager.Translate("InfoPanel_Map");
            MapTitle.AutoSize = true;
            MapTitle.ForeColor = Theme.Current.Foreground;
            MapTitle.Location = new Point(margin, currentY);
            this.Controls.Add(MapTitle);

            MapValue = new Label();
            MapValue.Text = "";
            MapValue.AutoSize = true;
            MapValue.ForeColor = Theme.Current.Foreground;
            MapValue.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(MapValue);
            currentY += MapTitle.Height + margin;

            TerrorTitle = new Label();
            TerrorTitle.Text = LanguageManager.Translate("InfoPanel_Terror");
            TerrorTitle.AutoSize = true;
            TerrorTitle.ForeColor = Theme.Current.Foreground;
            TerrorTitle.Location = new Point(margin, currentY);
            this.Controls.Add(TerrorTitle);

            TerrorValue = new Label();
            TerrorValue.Text = "";
            TerrorValue.AutoSize = true;
            TerrorValue.ForeColor = Theme.Current.Foreground;
            TerrorValue.Location = new Point(labelWidth + margin, currentY);
            TerrorValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(TerrorValue);
            currentY += TerrorTitle.Height + margin;

            ItemTitle = new Label();
            ItemTitle.Text = LanguageManager.Translate("InfoPanel_Item");
            ItemTitle.AutoSize = true;
            ItemTitle.ForeColor = Theme.Current.Foreground;
            ItemTitle.Location = new Point(margin, currentY);
            this.Controls.Add(ItemTitle);

            ItemValue = new Label();
            ItemValue.Text = "";
            ItemValue.AutoSize = true;
            ItemValue.ForeColor = Theme.Current.Foreground;
            ItemValue.Location = new Point(labelWidth + margin, currentY);
            ItemValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(ItemValue);
            currentY += ItemTitle.Height + margin;

            DamageTitle = new Label();
            DamageTitle.Text = LanguageManager.Translate("InfoPanel_Damage");
            DamageTitle.AutoSize = true;
            DamageTitle.ForeColor = Theme.Current.Foreground;
            DamageTitle.Location = new Point(margin, currentY);
            this.Controls.Add(DamageTitle);

            DamageValue = new Label();
            DamageValue.Text = "0";
            DamageValue.AutoSize = true;
            DamageValue.ForeColor = Theme.Current.Foreground;
            DamageValue.Location = new Point(labelWidth + margin, currentY);
            DamageValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(DamageValue);
            currentY += DamageTitle.Height + margin;

            NextRoundType = new Label();
            NextRoundType.Text = "???";
            NextRoundType.AutoSize = true;
            NextRoundType.ForeColor = Theme.Current.Foreground;
            NextRoundType.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(NextRoundType);
            currentY += NextRoundType.Height + margin;

            IdleTimeLabel = new Label();
            IdleTimeLabel.Text = "";
            IdleTimeLabel.AutoSize = true;
            IdleTimeLabel.ForeColor = Theme.Current.Foreground;
            IdleTimeLabel.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(IdleTimeLabel);
            currentY += IdleTimeLabel.Height + margin;

            this.Height = currentY + margin;
        }

        public void ApplyTheme()
        {
            this.BackColor = Theme.Current.PanelBackground;
            foreach (Control ctrl in Controls)
            {
                ctrl.ForeColor = Theme.Current.Foreground;
            }
        }
    }
}
