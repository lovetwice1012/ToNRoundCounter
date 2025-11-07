using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure;
using System.Text.Json;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Profile management UI form
    /// </summary>
    public partial class ProfileManagerForm : Form
    {
        private readonly CloudWebSocketClient? _cloudClient;
        private readonly string _playerId;
        private Dictionary<string, object>? _currentProfile;
        private bool _isDirty = false;

        // UI Controls
        private Panel mainPanel = null!;
        private Label titleLabel = null!;
        private GroupBox profileInfoGroup = null!;
        private Label playerNameLabel = null!;
        private TextBox playerNameTextBox = null!;
        private Label skillLevelLabel = null!;
        private NumericUpDown skillLevelNumeric = null!;
        private GroupBox statsGroup = null!;
        private Label totalRoundsLabel = null!;
        private Label totalSurvivedLabel = null!;
        private Label survivalRateLabel = null!;
        private Label totalRoundsValueLabel = null!;
        private Label totalSurvivedValueLabel = null!;
        private Label survivalRateValueLabel = null!;
        private GroupBox terrorStatsGroup = null!;
        private ListView terrorStatsListView = null!;
        private Button refreshButton = null!;
        private Button saveButton = null!;
        private Button closeButton = null!;
        private ProgressBar loadingProgressBar = null!;

        public ProfileManagerForm(CloudWebSocketClient? cloudClient, string playerId)
        {
            _cloudClient = cloudClient;
            _playerId = playerId ?? Environment.UserName;
            
            InitializeComponent();
            InitializeCustomComponents();
            
            // Load profile on form load
            this.Load += async (s, e) => await LoadProfileAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "プロフィール管理 - Profile Manager";
            this.Size = new Size(600, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(600, 700);
        }

        private void InitializeCustomComponents()
        {
            mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15),
                BackColor = Color.FromArgb(240, 240, 245),
                AutoScroll = true
            };

            titleLabel = new Label
            {
                Text = "プレイヤープロフィール",
                Font = new Font("Yu Gothic UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 15)
            };

            // Profile Info Group
            profileInfoGroup = new GroupBox
            {
                Text = "基本情報",
                Location = new Point(15, 55),
                Size = new Size(550, 120),
                Font = new Font("Yu Gothic UI", 10)
            };

            playerNameLabel = new Label
            {
                Text = "プレイヤー名:",
                Location = new Point(15, 30),
                AutoSize = true
            };

            playerNameTextBox = new TextBox
            {
                Location = new Point(15, 55),
                Size = new Size(510, 25)
            };
            playerNameTextBox.TextChanged += (s, e) => _isDirty = true;

            skillLevelLabel = new Label
            {
                Text = "スキルレベル (1-10):",
                Location = new Point(15, 90),
                AutoSize = true
            };

            skillLevelNumeric = new NumericUpDown
            {
                Location = new Point(180, 87),
                Size = new Size(80, 25),
                Minimum = 1,
                Maximum = 10,
                Value = 5
            };
            skillLevelNumeric.ValueChanged += (s, e) => _isDirty = true;

            profileInfoGroup.Controls.AddRange(new Control[] { 
                playerNameLabel, playerNameTextBox, skillLevelLabel, skillLevelNumeric 
            });

            // Stats Group
            statsGroup = new GroupBox
            {
                Text = "統計情報",
                Location = new Point(15, 185),
                Size = new Size(550, 120),
                Font = new Font("Yu Gothic UI", 10)
            };

            totalRoundsLabel = new Label
            {
                Text = "総ラウンド数:",
                Location = new Point(15, 30),
                Size = new Size(200, 25),
                Font = new Font("Yu Gothic UI", 9)
            };

            totalRoundsValueLabel = new Label
            {
                Text = "0",
                Location = new Point(220, 30),
                Size = new Size(100, 25),
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };

            totalSurvivedLabel = new Label
            {
                Text = "生存回数:",
                Location = new Point(15, 60),
                Size = new Size(200, 25),
                Font = new Font("Yu Gothic UI", 9)
            };

            totalSurvivedValueLabel = new Label
            {
                Text = "0",
                Location = new Point(220, 60),
                Size = new Size(100, 25),
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };

            survivalRateLabel = new Label
            {
                Text = "生存率:",
                Location = new Point(350, 30),
                Size = new Size(100, 25),
                Font = new Font("Yu Gothic UI", 9)
            };

            survivalRateValueLabel = new Label
            {
                Text = "0.0%",
                Location = new Point(450, 30),
                Size = new Size(80, 25),
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold),
                ForeColor = Color.Green
            };

            statsGroup.Controls.AddRange(new Control[] { 
                totalRoundsLabel, totalRoundsValueLabel,
                totalSurvivedLabel, totalSurvivedValueLabel,
                survivalRateLabel, survivalRateValueLabel
            });

            // Terror Stats Group
            terrorStatsGroup = new GroupBox
            {
                Text = "Terror別統計",
                Location = new Point(15, 315),
                Size = new Size(550, 250),
                Font = new Font("Yu Gothic UI", 10)
            };

            terrorStatsListView = new ListView
            {
                Location = new Point(15, 25),
                Size = new Size(520, 210),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            terrorStatsListView.Columns.Add("Terror Name", 200);
            terrorStatsListView.Columns.Add("遭遇回数", 100);
            terrorStatsListView.Columns.Add("生存回数", 100);
            terrorStatsListView.Columns.Add("生存率", 100);

            terrorStatsGroup.Controls.Add(terrorStatsListView);

            // Loading Progress
            loadingProgressBar = new ProgressBar
            {
                Location = new Point(15, 575),
                Size = new Size(550, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            // Buttons
            refreshButton = new Button
            {
                Text = "再読込",
                Location = new Point(250, 595),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            refreshButton.Click += async (s, e) => await LoadProfileAsync();

            saveButton = new Button
            {
                Text = "保存",
                Location = new Point(360, 595),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            saveButton.Click += async (s, e) => await SaveProfileAsync();

            closeButton = new Button
            {
                Text = "閉じる",
                Location = new Point(470, 595),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) => this.Close();

            mainPanel.Controls.AddRange(new Control[] { 
                titleLabel, profileInfoGroup, statsGroup, terrorStatsGroup, 
                loadingProgressBar, refreshButton, saveButton, closeButton 
            });
            this.Controls.Add(mainPanel);

            // Track changes for save button
            playerNameTextBox.TextChanged += (s, e) => saveButton.Enabled = _isDirty;
            skillLevelNumeric.ValueChanged += (s, e) => saveButton.Enabled = _isDirty;
        }

        private async Task LoadProfileAsync()
        {
            if (_cloudClient == null)
            {
                MessageBox.Show("Cloud接続が無効です", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            loadingProgressBar.Visible = true;
            refreshButton.Enabled = false;

            try
            {
                _currentProfile = await _cloudClient.GetProfileAsync(_playerId, CancellationToken.None);

                if (_currentProfile != null)
                {
                    // Update UI with profile data
                    if (_currentProfile.TryGetValue("player_name", out var playerName))
                    {
                        playerNameTextBox.Text = playerName?.ToString() ?? _playerId;
                    }

                    if (_currentProfile.TryGetValue("skill_level", out var skillLevel))
                    {
                        var skillValue = Convert.ToInt32(skillLevel);
                        skillLevelNumeric.Value = Math.Max(1, Math.Min(10, skillValue));
                    }

                    if (_currentProfile.TryGetValue("total_rounds", out var totalRounds))
                    {
                        totalRoundsValueLabel.Text = totalRounds?.ToString() ?? "0";
                    }

                    if (_currentProfile.TryGetValue("total_survived", out var totalSurvived))
                    {
                        totalSurvivedValueLabel.Text = totalSurvived?.ToString() ?? "0";
                    }

                    // Calculate survival rate
                    var rounds = Convert.ToInt32(_currentProfile.TryGetValue("total_rounds", out var tr) ? tr : 0);
                    var survived = Convert.ToInt32(_currentProfile.TryGetValue("total_survived", out var ts) ? ts : 0);
                    if (rounds > 0)
                    {
                        var rate = (double)survived / rounds * 100;
                        survivalRateValueLabel.Text = $"{rate:F1}%";
                        survivalRateValueLabel.ForeColor = rate >= 50 ? Color.Green : Color.OrangeRed;
                    }

                    // Load terror stats
                    terrorStatsListView.Items.Clear();
                    if (_currentProfile.TryGetValue("terror_stats", out var terrorStatsObj))
                    {
                        var terrorStatsJson = terrorStatsObj.ToString();
                        if (!string.IsNullOrEmpty(terrorStatsJson))
                        {
                            try
                            {
                                var terrorStats = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(terrorStatsJson);
                                if (terrorStats != null)
                                {
                                    foreach (var kvp in terrorStats)
                                    {
                                        var terrorName = kvp.Key;
                                        var stats = kvp.Value;
                                        var encounters = stats.TryGetValue("encounters", out var enc) ? enc : 0;
                                        var survivals = stats.TryGetValue("survived", out var surv) ? surv : 0;
                                        var survRate = encounters > 0 ? (double)survivals / encounters * 100 : 0;

                                        var item = new ListViewItem(terrorName);
                                        item.SubItems.Add(encounters.ToString());
                                        item.SubItems.Add(survivals.ToString());
                                        item.SubItems.Add($"{survRate:F1}%");
                                        terrorStatsListView.Items.Add(item);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    _isDirty = false;
                    saveButton.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロフィール読込に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loadingProgressBar.Visible = false;
                refreshButton.Enabled = true;
            }
        }

        private async Task SaveProfileAsync()
        {
            if (_cloudClient == null)
            {
                return;
            }

            saveButton.Enabled = false;
            loadingProgressBar.Visible = true;

            try
            {
                var result = await _cloudClient.UpdateProfileAsync(
                    _playerId,
                    playerName: playerNameTextBox.Text,
                    skillLevel: (int)skillLevelNumeric.Value,
                    terrorStats: null, // Keep existing terror stats
                    totalRounds: null, // Keep existing rounds
                    totalSurvived: null, // Keep existing survived
                    cancellationToken: CancellationToken.None
                );

                MessageBox.Show("プロフィールを保存しました", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _isDirty = false;
                saveButton.Enabled = false;

                // Reload to get updated data
                await LoadProfileAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロフィール保存に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                saveButton.Enabled = _isDirty;
            }
            finally
            {
                loadingProgressBar.Visible = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(
                    "保存していない変更があります。閉じてもよろしいですか?",
                    "確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }

            base.OnFormClosing(e);
        }
    }
}
