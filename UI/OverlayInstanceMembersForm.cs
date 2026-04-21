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
        // Shared fonts to avoid per-rebuild Font allocations (previously every rebuild
        // instantiated 3-4 new Font objects per member row, which at 5Hz × 20 players
        // produced hundreds of GDI font handles per second).
        private static readonly FontFamily DefaultFontFamily = Control.DefaultFont.FontFamily;
        private static readonly Font EmptyLabelFont = new Font(DefaultFontFamily, 12f, FontStyle.Regular);
        private static readonly Font HeaderFont = new Font(DefaultFontFamily, 11f, FontStyle.Bold);
        private static readonly Font SeparatorFont = new Font(DefaultFontFamily, 9f, FontStyle.Regular);
        private static readonly Font MemberRowFont = new Font(DefaultFontFamily, 11f, FontStyle.Regular);

        private readonly TableLayoutPanel contentPanel;
        private List<InstanceMemberInfo> members = new List<InstanceMemberInfo>();
        private List<string> desirePlayers = new List<string>();
        private Dictionary<string, RichTextBox> memberControls = new Dictionary<string, RichTextBox>();
        private bool needsFullRebuild = true;
        private HashSet<string> lastDesirePlayerSet = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> lastMemberPlayerIdSet = new HashSet<string>(StringComparer.Ordinal);

        public OverlayInstanceMembersForm(string title) : base(title, BuildContentPanel(out var panel))
        {
            contentPanel = panel;
            ShowEmptyMessage("インスタンスメンバーなし");
        }

        private void ShowEmptyMessage(string message)
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();
            contentPanel.RowStyles.Clear();
            memberControls.Clear();
            lastMemberPlayerIdSet = new HashSet<string>(StringComparer.Ordinal);
            lastDesirePlayerSet = new HashSet<string>(StringComparer.Ordinal);
            currentDesireSet = new HashSet<string>(StringComparer.Ordinal);
            needsFullRebuild = true;

            var emptyLabel = new Label
            {
                Text = message,
                ForeColor = Color.Gray,
                Font = EmptyLabelFont,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 2),
            };
            contentPanel.Controls.Add(emptyLabel);
            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentPanel.ResumeLayout();
            AdjustSizeToContent();
        }

        private static TableLayoutPanel BuildContentPanel(out TableLayoutPanel panel)
        {
            panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            return panel;
        }

        private HashSet<string> currentDesireSet = new HashSet<string>();

        public void UpdateMembers(List<InstanceMemberInfo> newMembers, List<string> desirePlayerIds)
        {
            members = newMembers ?? new List<InstanceMemberInfo>();
            desirePlayers = desirePlayerIds ?? new List<string>();

            // Build desire set once for O(1) lookups in this refresh
            currentDesireSet = new HashSet<string>(desirePlayers, StringComparer.Ordinal);

            // Build current member ID set
            var currentMemberIdSet = new HashSet<string>(members.Select(m => m.PlayerId), StringComparer.Ordinal);

            // Check if member set (IDs) changed
            bool memberSetChanged = !currentMemberIdSet.SetEquals(lastMemberPlayerIdSet);

            // Check if desire set changed
            bool desireSetChanged = !currentDesireSet.SetEquals(lastDesirePlayerSet);

            // Full rebuild needed if either set changed
            needsFullRebuild = memberSetChanged || desireSetChanged;

            // Update last known state
            if (memberSetChanged)
            {
                lastMemberPlayerIdSet = currentMemberIdSet;
            }
            if (desireSetChanged)
            {
                lastDesirePlayerSet = currentDesireSet;
            }

            RefreshDisplay();
        }

        /// <summary>
        /// Sets members from a simple list of names. This is a fallback for when only names are available.
        /// </summary>
        public void SetMembers(IReadOnlyList<string> memberNames)
        {
            // Convert string list to InstanceMemberInfo with minimal data
            var newMembers = new List<InstanceMemberInfo>();
            if (memberNames != null)
            {
                foreach (var name in memberNames)
                {
                    newMembers.Add(new InstanceMemberInfo
                    {
                        PlayerId = name,
                        PlayerName = name,
                        Damage = 0,
                        CurrentItem = string.Empty,
                        IsDead = false,
                        Velocity = 0,
                        AfkDuration = 0
                    });
                }
            }

            // Update without desire players
            UpdateMembers(newMembers, new List<string>());
        }

        private void RefreshDisplay()
        {
            if (members.Count == 0)
            {
                ShowEmptyMessage("インスタンスメンバーなし");
                return;
            }

            if (memberControls.Count == 0)
            {
                needsFullRebuild = true;
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
            SuspendDrawing(this);
            try
            {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();
            contentPanel.RowStyles.Clear();
            memberControls.Clear();

            if (members.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "インスタンスメンバーなし",
                    ForeColor = Color.Gray,
                    Font = EmptyLabelFont,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 2, 0, 2),
                };
                contentPanel.Controls.Add(emptyLabel);
                contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                contentPanel.ResumeLayout();
                AdjustSizeToContent();
                return;
            }

            // Partition into desire and other members in a single pass (O(N) using HashSet lookup)
            var desireMembers = new List<InstanceMemberInfo>(currentDesireSet.Count);
            var otherMembers = new List<InstanceMemberInfo>(Math.Max(0, members.Count - currentDesireSet.Count));
            foreach (var m in members)
            {
                if (currentDesireSet.Contains(m.PlayerId))
                    desireMembers.Add(m);
                else
                    otherMembers.Add(m);
            }
            desireMembers.Sort((a, b) => string.Compare(a.PlayerName, b.PlayerName, StringComparison.Ordinal));
            otherMembers.Sort((a, b) => string.Compare(a.PlayerName, b.PlayerName, StringComparison.Ordinal));

            // Show desire members first if any exist
            if (desireMembers.Count > 0)
            {
                var desireHeader = new Label
                {
                    Text = "【生存希望者】",
                    ForeColor = Color.Yellow,
                    Font = HeaderFont,
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
                        Font = SeparatorFont,
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
                        Font = HeaderFont,
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
            AdjustSizeToContent();
            }
            finally
            {
                ResumeDrawing(this);
            }
        }

        private void UpdateExistingControls()
        {
            // Update each member's display without recreating controls
            contentPanel.SuspendLayout();
            foreach (var member in members)
            {
                if (memberControls.TryGetValue(member.PlayerId, out var richLabel))
                {
                    UpdateMemberLabel(richLabel, member);
                }
            }
            contentPanel.ResumeLayout();

            // Adjust parent form size to content after refreshing display
            AdjustSizeToContent();
        }

        private void AddMemberLabel(InstanceMemberInfo member)
        {
            // Create rich text label for colored damage
            // Note: RichTextBox does not support BackColor = Transparent;
            // use the overlay surface color instead to blend with the background.
            var richLabel = new RichTextBox
            {
                ReadOnly = true,
                BackColor = OverlayTheme.Surface,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.None,
                Margin = new Padding(0, 1, 0, 1),
                Font = MemberRowFont,
                AutoSize = false,
                Dock = DockStyle.Top,
                WordWrap = false,
            };

            UpdateMemberLabel(richLabel, member);
            
            memberControls[member.PlayerId] = richLabel;
            contentPanel.Controls.Add(richLabel);
            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        private void UpdateMemberLabel(RichTextBox richLabel, InstanceMemberInfo member)
        {
            // Build the full plain text first to check if an update is needed
            string itemText = string.IsNullOrEmpty(member.CurrentItem) ? "" : $"({member.CurrentItem})";
            string displayText = $"{member.PlayerName}{itemText}: ";
            string fullText = displayText + $"{member.Damage} dmg | {member.Velocity:F2} m/s";
            if (member.AfkDuration >= 3 && member.Velocity < 1)
                fullText += $" (AFK: {(int)member.AfkDuration}秒)";

            // Skip update if text hasn't changed
            if (richLabel.Text == fullText)
                return;

            SuspendDrawing(richLabel);
            try
            {
                richLabel.Clear();

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

                // Auto-fit height to content
                var textSize = TextRenderer.MeasureText(fullText, richLabel.Font);
                richLabel.Height = Math.Max(20, textSize.Height + 4);
            }
            finally
            {
                ResumeDrawing(richLabel);
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
