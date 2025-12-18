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
        private Dictionary<string, RichTextBox> memberControls = new Dictionary<string, RichTextBox>();
        private bool needsFullRebuild = true;
        private HashSet<string> lastDesirePlayerSet = new HashSet<string>();

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
            
            // Debug log with timestamp
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            System.Diagnostics.Debug.WriteLine($"[{timestamp}] UpdateMembers called: {members.Count} members, {desirePlayers.Count} desire players");
            if (desirePlayers.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] Desire players: {string.Join(", ", desirePlayers)}");
            }
            
            // Check if we need full rebuild
            var newMemberIds = new HashSet<string>(members.Select(m => m.PlayerId));
            var oldMemberIds = new HashSet<string>(memberControls.Keys);
            var newDesireSet = new HashSet<string>(desirePlayers);
            
            // Rebuild if: member list changed OR desire player list changed
            needsFullRebuild = !newMemberIds.SetEquals(oldMemberIds) || !newDesireSet.SetEquals(lastDesirePlayerSet);
            
            if (needsFullRebuild)
            {
                lastDesirePlayerSet = new HashSet<string>(desirePlayers);
                System.Diagnostics.Debug.WriteLine("Full rebuild triggered");
            }
            
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (members.Count == 0)
            {
                if (memberControls.Count > 0 || contentPanel.Controls.Count > 0)
                {
                    contentPanel.SuspendLayout();
                    contentPanel.Controls.Clear();
                    contentPanel.RowStyles.Clear();
                    memberControls.Clear();
                    
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
                }
                return;
            }

            // If full rebuild needed, clear everything
            if (needsFullRebuild)
            {
                RebuildDisplay();
                needsFullRebuild = false;
            }
            else
            {
                // Just update existing controls
                UpdateExistingControls();
            }
        }

        private void RebuildDisplay()
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();
            contentPanel.RowStyles.Clear();
            memberControls.Clear();

            System.Diagnostics.Debug.WriteLine($"RebuildDisplay: {members.Count} members, {desirePlayers.Count} desire players");

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
            List<InstanceMemberInfo> desireMembers;
            List<InstanceMemberInfo> otherMembers;
            
            // Write to file for debugging
            try
            {
                var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ToNRoundCounter_DesireDebug.log");
                var logLines = new List<string>
                {
                    $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
                    $"Desire player IDs: [{string.Join(", ", desirePlayers.Select(d => $"\"{d}\""))}]",
                    $"All member IDs: [{string.Join(", ", members.Select(m => $"\"{m.PlayerId}\""))}]",
                    $"All member Names: [{string.Join(", ", members.Select(m => $"\"{m.PlayerName}\""))}]"
                };
                
                desireMembers = members.Where(m => desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();
                otherMembers = members.Where(m => !desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();
                
                logLines.Add($"Matched: {desireMembers.Count} desire members, {otherMembers.Count} other members");
                
                if (desireMembers.Count == 0 && desirePlayers.Count > 0)
                {
                    logLines.Add("WARNING: desirePlayers is not empty but no members matched!");
                    foreach (var member in members)
                    {
                        foreach (var desireId in desirePlayers)
                        {
                            logLines.Add($"  Compare: \"{member.PlayerId}\" == \"{desireId}\" ? {member.PlayerId == desireId}");
                            logLines.Add($"    PlayerName: \"{member.PlayerName}\"");
                            if (member.PlayerId != desireId)
                            {
                                logLines.Add($"    Length: {member.PlayerId.Length} vs {desireId.Length}");
                            }
                        }
                    }
                }
                
                System.IO.File.AppendAllLines(logPath, logLines);
                System.Diagnostics.Debug.WriteLine($"Debug log written to: {logPath}");
            }
            catch 
            {
                desireMembers = members.Where(m => desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();
                otherMembers = members.Where(m => !desirePlayers.Contains(m.PlayerId)).OrderBy(m => m.PlayerName).ToList();
            }

            // Show desire members first if any exist
            if (desireMembers.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Adding {desireMembers.Count} desire members to UI");
                
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
                    System.Diagnostics.Debug.WriteLine($"  Adding desire member: {member.PlayerName} ({member.PlayerId})");
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

        private void UpdateExistingControls()
        {
            // Update each member's display without recreating controls
            foreach (var member in members)
            {
                if (memberControls.TryGetValue(member.PlayerId, out var richLabel))
                {
                    UpdateMemberLabel(richLabel, member);
                }
            }
        }

        private void AddMemberLabel(InstanceMemberInfo member)
        {
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
                Width = 450,
            };

            UpdateMemberLabel(richLabel, member);
            
            memberControls[member.PlayerId] = richLabel;
            contentPanel.Controls.Add(richLabel);
            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        private void UpdateMemberLabel(RichTextBox richLabel, InstanceMemberInfo member)
        {
            richLabel.Clear();
            
            // Build display text
            string itemText = string.IsNullOrEmpty(member.CurrentItem) ? "" : $"({member.CurrentItem})";
            string displayText = $"{member.PlayerName}{itemText}: ";

            richLabel.SelectionStart = 0;
            richLabel.SelectionLength = 0;
            richLabel.SelectionColor = GetPlayerNameColor(member);
            richLabel.AppendText(displayText);

            // Add damage with appropriate color
            Color damageColor = GetDamageColor(member);
            richLabel.SelectionColor = damageColor;
            richLabel.AppendText($"{member.Damage} dmg");

            // Add velocity
            richLabel.SelectionColor = Color.LightGray;
            richLabel.AppendText($" | {member.Velocity:F2} m/s");

            // Add AFK status if applicable (velocity < 1 for >= 3 seconds)
            if (member.AfkDuration >= 3 && member.Velocity < 1)
            {
                richLabel.SelectionColor = Color.Orange;
                richLabel.AppendText($" (AFK: {(int)member.AfkDuration}秒)");
            }
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
        public double Velocity { get; set; }
        public double AfkDuration { get; set; }
    }
}
