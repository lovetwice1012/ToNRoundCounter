using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
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
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private System.Windows.Forms.Timer? _refreshTimer;
        private string? _activeCampaignId;
        private bool _hasVoted;

        // UI Controls
        private Panel mainPanel = null!;
        private Label titleLabel = null!;
        private Label statusLabel = null!;
        private Button startVotingButton = null!;
        private TextBox terrorNameTextBox = null!;
        private TextBox roundKeyTextBox = null!;
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
                _refreshTimer.Interval = 2000;
                _refreshTimer.Tick += RefreshTimer_Tick;
                _refreshTimer.Start();
            }
        }

        public void ApplyVotingStartedStream(JsonElement data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyVotingStartedStream(data)));
                return;
            }

            try
            {
                if (data.TryGetProperty("campaign_id", out var campaignIdElement))
                {
                    _activeCampaignId = campaignIdElement.GetString();
                }

                _hasVoted = false;
                statusLabel.Text = "投票中 - Active Campaign";
                statusLabel.ForeColor = Color.Green;
                proceedButton.Enabled = true;
                cancelButton.Enabled = true;

                _ = RefreshActiveCampaignAsync();
            }
            catch
            {
                // Stream parse failure should not break the panel.
            }
        }

        public void ApplyVotingResolvedStream(JsonElement data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyVotingResolvedStream(data)));
                return;
            }

            try
            {
                var finalDecision = data.TryGetProperty("final_decision", out var decisionElement)
                    ? decisionElement.GetString() ?? "Unknown"
                    : "Unknown";

                var continueCount = 0;
                var skipCount = 0;
                if (data.TryGetProperty("vote_count", out var voteCountElement) && voteCountElement.ValueKind == JsonValueKind.Object)
                {
                    if (voteCountElement.TryGetProperty("continue", out var continueElement) && continueElement.TryGetInt32(out var continueValue))
                    {
                        continueCount = continueValue;
                    }
                    else if (voteCountElement.TryGetProperty("proceed", out var proceedElement) && proceedElement.TryGetInt32(out var proceedValue))
                    {
                        continueCount = proceedValue;
                    }

                    if (voteCountElement.TryGetProperty("skip", out var skipElement) && skipElement.TryGetInt32(out var skipValue))
                    {
                        skipCount = skipValue;
                    }
                    else if (voteCountElement.TryGetProperty("cancel", out var cancelElement) && cancelElement.TryGetInt32(out var cancelValue))
                    {
                        skipCount = cancelValue;
                    }
                }

                var totalVotes = continueCount + skipCount;
                statusLabel.Text = "投票終了 - Resolved";
                statusLabel.ForeColor = Color.Gray;
                campaignInfoLabel.Text = $"最終決定: {finalDecision}\n総投票数: {totalVotes}";
                votesLabel.Text = $"続行: {continueCount} | スキップ: {skipCount}";
                votingProgressBar.Value = totalVotes > 0
                    ? Math.Max(0, Math.Min(100, (int)((double)continueCount / totalVotes * 100)))
                    : 0;

                proceedButton.Enabled = false;
                cancelButton.Enabled = false;

                _activeCampaignId = null;
                _hasVoted = false;
            }
            catch
            {
                // Stream parse failure should not break the panel.
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
                Size = new Size(450, 200),
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

            var roundKeyLabel = new Label
            {
                Text = "ラウンド名:",
                Location = new Point(15, 90),
                AutoSize = true
            };

            roundKeyTextBox = new TextBox
            {
                Location = new Point(15, 115),
                Size = new Size(410, 25)
            };

            var expiresLabel = new Label
            {
                Text = "投票期限:",
                Location = new Point(15, 150),
                AutoSize = true
            };

            expiresAtPicker = new DateTimePicker
            {
                Location = new Point(15, 170),
                Size = new Size(300, 25),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy/MM/dd HH:mm:ss",
                Value = DateTime.Now.AddMinutes(5)
            };

            startVotingButton = new Button
            {
                Text = "投票開始",
                Location = new Point(325, 170),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            startVotingButton.Click += StartVotingButton_Click;

            startVotingGroupBox.Controls.AddRange(new Control[] { terrorLabel, terrorNameTextBox, roundKeyLabel, roundKeyTextBox, expiresLabel, expiresAtPicker, startVotingButton });

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
                Text = "続行: 0 | スキップ: 0",
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
                Text = "続行 (Continue)",
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
                Text = "スキップ (Skip)",
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
                MessageBox.Show("Cloud接続またはインスタンスIDが無効です", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(terrorNameTextBox.Text))
            {
                MessageBox.Show("Terror Nameを入力してください", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            startVotingButton.Enabled = false;
            startVotingButton.Text = "開始中...";

            try
            {
                var result = await _cloudClient.StartVotingAsync(
                    _currentInstanceId!, // Non-null assertion
                    terrorNameTextBox.Text.Trim(),
                    expiresAtPicker.Value,
                    string.IsNullOrWhiteSpace(roundKeyTextBox.Text) ? null : roundKeyTextBox.Text.Trim(),
                    _lifetimeCts.Token
                );

                if (result.ContainsKey("campaign_id"))
                {
                    _activeCampaignId = result["campaign_id"].ToString();
                    _hasVoted = false;
                    MessageBox.Show("投票キャンペーンを開始しました!", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await RefreshActiveCampaignAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"投票開始に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                startVotingButton.Enabled = true;
                startVotingButton.Text = "投票開始";
            }
        }

        private async void ProceedButton_Click(object? sender, EventArgs e)
        {
            await SubmitVoteAsync("Continue");
        }

        private async void CancelButton_Click(object? sender, EventArgs e)
        {
            await SubmitVoteAsync("Skip");
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
                await _cloudClient.SubmitVoteAsync(_activeCampaignId!, _playerId!, decision, _lifetimeCts.Token);
                _hasVoted = true;
                MessageBox.Show($"投票しました: {decision}", "投票完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await RefreshActiveCampaignAsync();
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation when form is closing.
            }
            catch (Exception ex)
            {
                MessageBox.Show($"投票に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                proceedButton.Enabled = !_hasVoted;
                cancelButton.Enabled = !_hasVoted;
            }
        }

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshActiveCampaignAsync();
        }

        private async Task RefreshActiveCampaignAsync()
        {
            if (_cloudClient == null || string.IsNullOrEmpty(_currentInstanceId))
            {
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(_activeCampaignId))
                {
                    var activeCampaign = await _cloudClient.GetActiveVotingCampaignAsync(_currentInstanceId!, _lifetimeCts.Token);
                    if (activeCampaign == null)
                    {
                        throw new InvalidOperationException("Active campaign not found");
                    }

                    _activeCampaignId = activeCampaign.ContainsKey("campaign_id")
                        ? activeCampaign["campaign_id"]?.ToString()
                        : null;
                }

                if (string.IsNullOrEmpty(_activeCampaignId))
                {
                    throw new InvalidOperationException("Campaign ID unavailable");
                }

                var campaign = await _cloudClient.GetVotingCampaignAsync(_activeCampaignId!, _lifetimeCts.Token);

                if (campaign.ContainsKey("terror_name") && campaign.ContainsKey("expires_at"))
                {
                    var terrorName = GetDictionaryString(campaign, "terror_name") ?? "不明";
                    var expiresAt = GetDictionaryString(campaign, "expires_at") ?? "不明";
                    var roundKey = GetDictionaryString(campaign, "round_key") ?? string.Empty;
                    var continueCount = campaign.ContainsKey("continue_count")
                        ? GetDictionaryInt(campaign, "continue_count")
                        : GetDictionaryInt(campaign, "proceed_count");
                    var skipCount = campaign.ContainsKey("skip_count")
                        ? GetDictionaryInt(campaign, "skip_count")
                        : GetDictionaryInt(campaign, "cancel_count");
                    var totalVotes = continueCount + skipCount;
                    var myVote = GetDictionaryString(campaign, "my_vote");

                    statusLabel.Text = "投票中 - Active Campaign";
                    statusLabel.ForeColor = Color.Green;

                    campaignInfoLabel.Text = $"Terror: {terrorName}\nラウンド: {(string.IsNullOrWhiteSpace(roundKey) ? "現在のラウンド" : roundKey)}\n期限: {expiresAt}\n総投票数: {totalVotes}";
                    votesLabel.Text = $"続行: {continueCount} | スキップ: {skipCount}";

                    if (totalVotes > 0)
                    {
                        votingProgressBar.Value = (int)((double)continueCount / totalVotes * 100);
                    }
                    else
                    {
                        votingProgressBar.Value = 0;
                    }

                    _hasVoted = !string.IsNullOrWhiteSpace(myVote);
                    proceedButton.Enabled = !_hasVoted;
                    cancelButton.Enabled = !_hasVoted;
                }
            }
            catch (OperationCanceledException)
            {
                // Form is closing or polling cancelled.
            }
            catch
            {
                // Campaign might have expired or been deleted
                _activeCampaignId = null;
                _hasVoted = false;
                statusLabel.Text = "投票キャンペーンはありません";
                statusLabel.ForeColor = Color.Gray;
                campaignInfoLabel.Text = "";
                votesLabel.Text = "続行: 0 | スキップ: 0";
                votingProgressBar.Value = 0;
                proceedButton.Enabled = false;
                cancelButton.Enabled = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _lifetimeCts.Cancel();
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            _lifetimeCts.Dispose();
            base.OnFormClosing(e);
        }

        private static int GetDictionaryInt(Dictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out var raw) || raw == null)
            {
                return 0;
            }

            if (raw is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var numberValue))
                {
                    return numberValue;
                }

                if (jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out var parsedValue))
                {
                    return parsedValue;
                }

                return 0;
            }

            return int.TryParse(raw.ToString(), out var convertedValue) ? convertedValue : 0;
        }

        private static string? GetDictionaryString(Dictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out var raw) || raw == null)
            {
                return null;
            }

            if (raw is JsonElement jsonElement)
            {
                return jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();
            }

            return raw.ToString();
        }
    }
}
