using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Voting system UI panel
    /// </summary>
    public partial class VotingPanelForm : Form
    {
        private readonly CloudWebSocketClient? _cloudClient;
        private readonly string? _currentInstanceId;
        private readonly string? _playerId;
        private System.Windows.Forms.Timer? _refreshTimer;
        private string? _activeCampaignId;
        private bool _hasVoted;

        // UI Controls
        private Panel mainPanel = null!;
        private Label titleLabel = null!;
        private Label statusLabel = null!;
        private Button startVotingButton = null!;
        private TextBox terrorNameTextBox = null!;
        private DateTimePicker expiresAtPicker = null!;
        private Label campaignInfoLabel = null!;
        private Button proceedButton = null!;
        private Button cancelButton = null!;
        private ProgressBar votingProgressBar = null!;
        private Label votesLabel = null!;

        public VotingPanelForm(CloudWebSocketClient? cloudClient, string? instanceId, string? playerId)
        {
            _cloudClient = cloudClient;
            _currentInstanceId = instanceId;
            _playerId = playerId;
            
            InitializeComponent();
            InitializeCustomComponents();
            
            if (_cloudClient != null && !string.IsNullOrEmpty(_currentInstanceId))
            {
                // Poll for active voting campaigns every 2 seconds
                _refreshTimer = new System.Windows.Forms.Timer();
                _refreshTimer.Interval = Infrastructure.Constants.Network.DefaultRefreshIntervalMs;
                _refreshTimer.Tick += RefreshTimer_Tick;
                _refreshTimer.Start();
            }
        }

        private void InitializeComponent()
        {
            this.Text = "投票システム - Voting System";
            this.Size = new Size(500, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
        }

        private void InitializeCustomComponents()
        {
            mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15),
                BackColor = Color.FromArgb(240, 240, 245)
            };

            titleLabel = new Label
            {
                Text = "協調投票システム",
                Font = new Font("Yu Gothic UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 15)
            };

            // Start Voting Section
            var startVotingGroupBox = new GroupBox
            {
                Text = "新規投票を開始",
                Location = new Point(15, 55),
                Size = new Size(450, 160),
                Font = new Font("Yu Gothic UI", 10)
            };

            var terrorLabel = new Label
            {
                Text = "Terror Name:",
                Location = new Point(15, 30),
                AutoSize = true
            };

            terrorNameTextBox = new TextBox
            {
                Location = new Point(15, 55),
                Size = new Size(410, 25)
                // PlaceholderText not available in .NET Framework 4.8
            };

            var expiresLabel = new Label
            {
                Text = "投票期限:",
                Location = new Point(15, 90),
                AutoSize = true
            };

            expiresAtPicker = new DateTimePicker
            {
                Location = new Point(15, 115),
                Size = new Size(Infrastructure.Constants.UI.StandardControlWidth, 25),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy/MM/dd HH:mm:ss",
                Value = DateTime.Now.AddMinutes(5)
            };

            startVotingButton = new Button
            {
                Text = "投票開始",
                Location = new Point(325, 115),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            startVotingButton.Click += StartVotingButton_Click;

            startVotingGroupBox.Controls.AddRange(new Control[] { terrorLabel, terrorNameTextBox, expiresLabel, expiresAtPicker, startVotingButton });

            // Active Voting Section
            var activeVotingGroupBox = new GroupBox
            {
                Text = "アクティブな投票",
                Location = new Point(15, 230),
                Size = new Size(450, 310),
                Font = new Font("Yu Gothic UI", 10)
            };

            statusLabel = new Label
            {
                Text = "投票キャンペーンはありません",
                Location = new Point(15, 30),
                Size = new Size(410, 25),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter
            };

            campaignInfoLabel = new Label
            {
                Text = "",
                Location = new Point(15, 60),
                Size = new Size(410, 80),
                Font = new Font("Yu Gothic UI", 9)
            };

            votesLabel = new Label
            {
                Text = "賛成: 0 | 反対: 0",
                Location = new Point(15, 150),
                Size = new Size(410, 25),
                Font = new Font("Yu Gothic UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            votingProgressBar = new ProgressBar
            {
                Location = new Point(15, 185),
                Size = new Size(410, 25),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            proceedButton = new Button
            {
                Text = "賛成 (Proceed)",
                Location = new Point(15, 230),
                Size = new Size(195, 50),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Yu Gothic UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            proceedButton.Click += ProceedButton_Click;

            cancelButton = new Button
            {
                Text = "反対 (Cancel)",
                Location = new Point(230, 230),
                Size = new Size(195, 50),
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Yu Gothic UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            cancelButton.Click += CancelButton_Click;

            activeVotingGroupBox.Controls.AddRange(new Control[] { statusLabel, campaignInfoLabel, votesLabel, votingProgressBar, proceedButton, cancelButton });

            mainPanel.Controls.AddRange(new Control[] { titleLabel, startVotingGroupBox, activeVotingGroupBox });
            this.Controls.Add(mainPanel);
        }

        private async void StartVotingButton_Click(object? sender, EventArgs e)
        {
            if (_cloudClient == null || string.IsNullOrEmpty(_currentInstanceId))
            {
                DialogHelper.ShowError("Cloud接続またはインスタンスIDが無効です");
                return;
            }

            var terrorName = terrorNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(terrorName))
            {
                DialogHelper.ShowInputError("Terror Nameを入力してください");
                return;
            }

            // Validate terror name length and content
            if (terrorName.Length < 2 || terrorName.Length > 100)
            {
                DialogHelper.ShowInputError("Terror Nameは2文字以上100文字以内で入力してください");
                return;
            }

            startVotingButton.Enabled = false;
            startVotingButton.Text = "開始中...";

            try
            {
                var result = await _cloudClient.StartVotingAsync(
                    _currentInstanceId!, // Non-null assertion
                    terrorName,
                    expiresAtPicker.Value,
                    CancellationToken.None
                );

                if (result.ContainsKey("campaign_id"))
                {
                    _activeCampaignId = result["campaign_id"].ToString();
                    _hasVoted = false;
                    DialogHelper.ShowSuccess("投票キャンペーンを開始しました!");
                    await RefreshActiveCampaignAsync();
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowException("投票開始", ex);
            }
            finally
            {
                startVotingButton.Enabled = true;
                startVotingButton.Text = "投票開始";
            }
        }

        private async void ProceedButton_Click(object? sender, EventArgs e)
        {
            try
            {
                await SubmitVoteAsync("Proceed");
            }
            catch (Exception ex)
            {
                DialogHelper.ShowException("投票送信", ex);
            }
        }

        private async void CancelButton_Click(object? sender, EventArgs e)
        {
            try
            {
                await SubmitVoteAsync("Cancel");
            }
            catch (Exception ex)
            {
                DialogHelper.ShowException("投票送信", ex);
            }
        }

        private async Task SubmitVoteAsync(string decision)
        {
            if (_cloudClient == null || string.IsNullOrEmpty(_activeCampaignId) || string.IsNullOrEmpty(_playerId))
            {
                return;
            }

            proceedButton.Enabled = false;
            cancelButton.Enabled = false;

            try
            {
                await _cloudClient.SubmitVoteAsync(_activeCampaignId!, _playerId!, decision, CancellationToken.None);
                _hasVoted = true;
                DialogHelper.ShowInfo($"投票しました: {decision}", "投票完了");
                await RefreshActiveCampaignAsync();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowException("投票", ex);
                proceedButton.Enabled = !_hasVoted;
                cancelButton.Enabled = !_hasVoted;
            }
        }

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                await RefreshActiveCampaignAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing campaign: {ex.Message}");
            }
        }

        private async Task RefreshActiveCampaignAsync()
        {
            if (_cloudClient == null || string.IsNullOrEmpty(_activeCampaignId))
            {
                return;
            }

            try
            {
                var campaign = await _cloudClient.GetVotingCampaignAsync(_activeCampaignId!, CancellationToken.None);

                if (campaign.ContainsKey("terror_name") && campaign.ContainsKey("expires_at"))
                {
                    var terrorName = campaign["terror_name"]?.ToString() ?? "不明";
                    var expiresAt = campaign["expires_at"]?.ToString() ?? "不明";
                    var proceedCount = campaign.ContainsKey("proceed_count") ? Convert.ToInt32(campaign["proceed_count"]) : 0;
                    var cancelCount = campaign.ContainsKey("cancel_count") ? Convert.ToInt32(campaign["cancel_count"]) : 0;
                    var totalVotes = proceedCount + cancelCount;

                    statusLabel.Text = "投票中 - Active Campaign";
                    statusLabel.ForeColor = Color.Green;

                    campaignInfoLabel.Text = $"Terror: {terrorName}\n期限: {expiresAt}\n総投票数: {totalVotes}";
                    votesLabel.Text = $"賛成: {proceedCount} | 反対: {cancelCount}";

                    if (totalVotes > 0)
                    {
                        votingProgressBar.Value = (int)((double)proceedCount / totalVotes * 100);
                    }

                    proceedButton.Enabled = !_hasVoted;
                    cancelButton.Enabled = !_hasVoted;
                }
            }
            catch (Exception ex)
            {
                // Campaign might have expired or been deleted, or network error occurred
                System.Diagnostics.Debug.WriteLine($"Failed to retrieve campaign data: {ex.Message}");
                _activeCampaignId = null;
                _hasVoted = false;
                statusLabel.Text = "投票キャンペーンはありません";
                statusLabel.ForeColor = Color.Gray;
                campaignInfoLabel.Text = "";
                votesLabel.Text = "賛成: 0 | 反対: 0";
                votingProgressBar.Value = 0;
                proceedButton.Enabled = false;
                cancelButton.Enabled = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
