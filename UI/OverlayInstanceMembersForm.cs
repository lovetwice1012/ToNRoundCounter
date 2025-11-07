using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Instance members overlay showing player states, damage, and survival desires
    /// </summary>
    public class OverlayInstanceMembersForm : OverlaySectionForm
    {
        private readonly TableLayoutPanel contentPanel;
        private List<InstanceMemberInfo> members = new List<InstanceMemberInfo>();
        private List<string> desirePlayers = new List<string>();

        public OverlayInstanceMembersForm(string title) : base(title, null!)
        {
            contentPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };

            // Remove default content and add our custom panel
            ContentControl.Controls.Clear();
            ContentControl.Controls.Add(contentPanel);
        }

        public void UpdateMembers(List<InstanceMemberInfo> newMembers, List<string> desirePlayerIds)
        {
            members = newMembers ?? new List<InstanceMemberInfo>();
            desirePlayers = desirePlayerIds ?? new List<string>();
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();
            contentPanel.RowStyles.Clear();

            if (members.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "インスタンスメンバーなし",
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, 12f, FontStyle.Regular),
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 2, 0, 2),
                };
                contentPanel.Controls.Add(emptyLabel);
                contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                contentPanel.ResumeLayout();
                return;
            }

            // Separate members into desire and others
            var desireMembers = members.Where(m => desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();
            var otherMembers = members.Where(m => !desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();

            // Show desire members first if any exist
            if (desireMembers.Count > 0)
            {
                var desireHeader = new Label
                {
                    Text = "【生存希望者】",
                    ForeColor = Color.Yellow,
                    Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 2, 0, 2),
                };
                contentPanel.Controls.Add(desireHeader);
                contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                foreach (var member in desireMembers)
                {
                    AddMemberLabel(member);
                }

                // Add separator if there are other members
                if (otherMembers.Count > 0)
                {
                    var separator = new Label
                    {
                        Text = "―――――――――",
                        ForeColor = Color.Gray,
                        Font = new Font(Font.FontFamily, 9f, FontStyle.Regular),
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0, 4, 0, 4),
                    };
                    contentPanel.Controls.Add(separator);
                    contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }
            }

            // Show other members
            if (otherMembers.Count > 0)
            {
                if (desireMembers.Count > 0)
                {
                    var otherHeader = new Label
                    {
                        Text = "【その他】",
                        ForeColor = Color.LightGray,
                        Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0, 2, 0, 2),
                    };
                    contentPanel.Controls.Add(otherHeader);
                    contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }

                foreach (var member in otherMembers)
                {
                    AddMemberLabel(member);
                }
            }

            contentPanel.ResumeLayout();
        }

        private void AddMemberLabel(InstanceMemberInfo member)
        {
            var label = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 1, 0, 1),
                Font = new Font(Font.FontFamily, 12f, FontStyle.Regular),
            };

            // Build display text
            string itemText = string.IsNullOrEmpty(member.CurrentItem) ? "" : $"({member.CurrentItem})";
            string displayText = $"{member.PlayerName}{itemText}: ";

            // Create rich text label for colored damage
            var richLabel = new RichTextBox
            {
                ReadOnly = true,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.None,
                Margin = new Padding(0, 1, 0, 1),
                Font = new Font(Font.FontFamily, 12f, FontStyle.Regular),
                Height = 20,
                Width = 300,
            };

            richLabel.SelectionStart = 0;
            richLabel.SelectionLength = 0;
            richLabel.SelectionColor = GetPlayerNameColor(member);
            richLabel.AppendText(displayText);

            // Add damage with appropriate color
            Color damageColor = GetDamageColor(member);
            richLabel.SelectionColor = damageColor;
            richLabel.AppendText($"{member.Damage} dmg");

            contentPanel.Controls.Add(richLabel);
            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        private Color GetPlayerNameColor(InstanceMemberInfo member)
        {
            if (member.IsDead)
            {
                return Color.Gray;
            }
            return Color.LimeGreen; // Alive players are green
        }

        private Color GetDamageColor(InstanceMemberInfo member)
        {
            if (member.IsDead)
            {
                return Color.Gray;
            }

            if (member.Damage > 50)
            {
                return Color.Red;
            }
            else if (member.Damage > 25)
            {
                return Color.Yellow;
            }

            return Color.White;
        }
    }

    /// <summary>
    /// Instance member information for overlay display
    /// </summary>
    public class InstanceMemberInfo
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public int Damage { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public bool IsDead { get; set; }
    }
}
