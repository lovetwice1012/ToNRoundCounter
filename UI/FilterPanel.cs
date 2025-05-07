using System.Drawing;
using System.Windows.Forms;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter.UI
{
    public class FilterPanel : Panel
    {
        public CheckBox RoundTypeCheckBox { get; private set; }
        public CheckBox TerrorCheckBox { get; private set; }
        public CheckBox AppearanceCountCheckBox { get; private set; }
        public CheckBox SurvivalCountCheckBox { get; private set; }
        public CheckBox DeathCountCheckBox { get; private set; }
        public CheckBox SurvivalRateCheckBox { get; private set; }

        public FilterPanel()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(560, 70);

            int margin = 10;
            int currentY = margin;

            Label title = new Label();
            title.Text = LanguageManager.Translate("フィルター");
            title.AutoSize = true;
            title.Location = new Point(margin, currentY);
            this.Controls.Add(title);

            currentY += title.Height + margin;

            RoundTypeCheckBox = new CheckBox();
            RoundTypeCheckBox.Text = LanguageManager.Translate("ラウンドタイプ");
            RoundTypeCheckBox.AutoSize = true;
            RoundTypeCheckBox.Location = new Point(margin, currentY);
            this.Controls.Add(RoundTypeCheckBox);

            TerrorCheckBox = new CheckBox();
            TerrorCheckBox.Text = LanguageManager.Translate("テラー");
            TerrorCheckBox.AutoSize = true;
            TerrorCheckBox.Location = new Point(RoundTypeCheckBox.Right + margin, currentY);
            this.Controls.Add(TerrorCheckBox);

            AppearanceCountCheckBox = new CheckBox();
            AppearanceCountCheckBox.Text = LanguageManager.Translate("出現回数");
            AppearanceCountCheckBox.AutoSize = true;
            AppearanceCountCheckBox.Location = new Point(TerrorCheckBox.Right + margin, currentY);
            this.Controls.Add(AppearanceCountCheckBox);

            SurvivalCountCheckBox = new CheckBox();
            SurvivalCountCheckBox.Text = LanguageManager.Translate("生存回数");
            SurvivalCountCheckBox.AutoSize = true;
            SurvivalCountCheckBox.Location = new Point(AppearanceCountCheckBox.Right + margin, currentY);
            this.Controls.Add(SurvivalCountCheckBox);

            DeathCountCheckBox = new CheckBox();
            DeathCountCheckBox.Text = LanguageManager.Translate("死亡回数");
            DeathCountCheckBox.AutoSize = true;
            DeathCountCheckBox.Location = new Point(SurvivalCountCheckBox.Right + margin, currentY);
            this.Controls.Add(DeathCountCheckBox);

            SurvivalRateCheckBox = new CheckBox();
            SurvivalRateCheckBox.Text = LanguageManager.Translate("生存率");
            SurvivalRateCheckBox.AutoSize = true;
            SurvivalRateCheckBox.Location = new Point(DeathCountCheckBox.Right + margin, currentY);
            this.Controls.Add(SurvivalRateCheckBox);
        }
    }
}
