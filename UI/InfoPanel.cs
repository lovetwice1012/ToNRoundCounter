using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter.UI
{
    public class InfoPanel : Panel
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
            this.BackColor = Color.DarkGray;
            this.Size = new Size(560, 240);

            int margin = 10;
            int currentY = margin;
            int labelWidth = 100;

            RoundTypeTitle = new Label();
            RoundTypeTitle.Text = LanguageManager.Translate("ラウンドタイプ:");
            RoundTypeTitle.AutoSize = true;
            RoundTypeTitle.ForeColor = Color.White;
            RoundTypeTitle.Location = new Point(margin, currentY);
            this.Controls.Add(RoundTypeTitle);

            RoundTypeValue = new Label();
            RoundTypeValue.Text = "";
            RoundTypeValue.AutoSize = true;
            RoundTypeValue.ForeColor = Color.White;
            RoundTypeValue.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(RoundTypeValue);
            currentY += RoundTypeTitle.Height + margin;

            MapTitle = new Label();
            MapTitle.Text = LanguageManager.Translate("MAP:");
            MapTitle.AutoSize = true;
            MapTitle.ForeColor = Color.White;
            MapTitle.Location = new Point(margin, currentY);
            this.Controls.Add(MapTitle);

            MapValue = new Label();
            MapValue.Text = "";
            MapValue.AutoSize = true;
            MapValue.ForeColor = Color.White;
            MapValue.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(MapValue);
            currentY += MapTitle.Height + margin;

            TerrorTitle = new Label();
            TerrorTitle.Text = LanguageManager.Translate("テラー:");
            TerrorTitle.AutoSize = true;
            TerrorTitle.ForeColor = Color.White;
            TerrorTitle.Location = new Point(margin, currentY);
            this.Controls.Add(TerrorTitle);

            TerrorValue = new Label();
            TerrorValue.Text = "";
            TerrorValue.AutoSize = true;
            TerrorValue.ForeColor = Color.White;
            TerrorValue.Location = new Point(labelWidth + margin, currentY);
            TerrorValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(TerrorValue);
            currentY += TerrorTitle.Height + margin;

            ItemTitle = new Label();
            ItemTitle.Text = LanguageManager.Translate("アイテム:");
            ItemTitle.AutoSize = true;
            ItemTitle.ForeColor = Color.White;
            ItemTitle.Location = new Point(margin, currentY);
            this.Controls.Add(ItemTitle);

            ItemValue = new Label();
            ItemValue.Text = "";
            ItemValue.AutoSize = true;
            ItemValue.ForeColor = Color.White;
            ItemValue.Location = new Point(labelWidth + margin, currentY);
            ItemValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(ItemValue);
            currentY += ItemTitle.Height + margin;

            DamageTitle = new Label();
            DamageTitle.Text = LanguageManager.Translate("ダメージ:");
            DamageTitle.AutoSize = true;
            DamageTitle.ForeColor = Color.White;
            DamageTitle.Location = new Point(margin, currentY);
            this.Controls.Add(DamageTitle);

            DamageValue = new Label();
            DamageValue.Text = "0";
            DamageValue.AutoSize = true;
            DamageValue.ForeColor = Color.White;
            DamageValue.Location = new Point(labelWidth + margin, currentY);
            DamageValue.MaximumSize = new Size(400, 0);
            this.Controls.Add(DamageValue);
            currentY += DamageTitle.Height + margin;

            NextRoundType = new Label();
            NextRoundType.Text = "???";
            NextRoundType.AutoSize = true;
            NextRoundType.ForeColor = Color.White;
            NextRoundType.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(NextRoundType);
            currentY += NextRoundType.Height + margin;

            IdleTimeLabel = new Label();
            IdleTimeLabel.Text = "";
            IdleTimeLabel.AutoSize = true;
            IdleTimeLabel.ForeColor = Color.White;
            IdleTimeLabel.Location = new Point(labelWidth + margin, currentY);
            this.Controls.Add(IdleTimeLabel);
            currentY += IdleTimeLabel.Height + margin;

            this.Height = currentY + margin;
        }
    }
}
