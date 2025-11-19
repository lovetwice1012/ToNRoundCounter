using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ToNRoundCounter.Infrastructure;
using ToNRoundCounter.Application;
using System.Text.Json;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Cloud settings synchronization UI form
    /// </summary>
    public partial class SettingsSyncForm : Form
    {
        private readonly CloudWebSocketClient? _cloudClient;
        private readonly string _userId;
        private readonly IAppSettings _localSettings;
        private Dictionary<string, object>? _remoteSettings;
        private int _remoteVersion = 0;
        private int _localVersion = 0;

        // UI Controls
        private Panel mainPanel = null!;
        private Label titleLabel = null!;
        private GroupBox syncStatusGroup = null!;
        private Label localVersionLabel = null!;
        private Label remoteVersionLabel = null!;
        private Label syncStatusLabel = null!;
        private PictureBox syncStatusIcon = null!;
        private GroupBox actionsGroup = null!;
        private Button uploadButton = null!;
        private Button downloadButton = null!;
        private Button syncButton = null!;
        private Button getRemoteButton = null!;
        private GroupBox settingsPreviewGroup = null!;
        private TabControl settingsTabControl = null!;
        private TabPage localTab = null!;
        private TabPage remoteTab = null!;
        private TextBox localSettingsTextBox = null!;
        private TextBox remoteSettingsTextBox = null!;
        private ProgressBar progressBar = null!;
        private Button closeButton = null!;

        public SettingsSyncForm(CloudWebSocketClient? cloudClient, string userId, IAppSettings localSettings)
        {
            _cloudClient = cloudClient;
            _userId = userId ?? Environment.UserName;
            _localSettings = localSettings;
            
            InitializeComponent();
            InitializeCustomComponents();
            
            this.Load += async (s, e) => await LoadSettingsAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "設定同期 - Settings Sync";
            this.Size = new Size(700, 750);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(700, 750);
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
                Text = "クラウド設定同期",
                Font = new Font("Yu Gothic UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 15)
            };

            // Sync Status Group
            syncStatusGroup = new GroupBox
            {
                Text = "同期状態",
                Location = new Point(15, 55),
                Size = new Size(650, 120),
                Font = new Font("Yu Gothic UI", 10)
            };

            localVersionLabel = new Label
            {
                Text = "ローカルバージョン: 0",
                Location = new Point(15, 30),
                Size = new Size(Infrastructure.Constants.UI.StandardControlWidth, 25),
                Font = new Font("Yu Gothic UI", 9)
            };

            remoteVersionLabel = new Label
            {
                Text = "リモートバージョン: 0",
                Location = new Point(15, 60),
                Size = new Size(Infrastructure.Constants.UI.StandardControlWidth, 25),
                Font = new Font("Yu Gothic UI", 9)
            };

            syncStatusLabel = new Label
            {
                Text = "同期状態: 未確認",
                Location = new Point(350, 30),
                Size = new Size(250, 25),
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };

            syncStatusIcon = new PictureBox
            {
                Location = new Point(600, 30),
                Size = new Size(30, 30),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            syncStatusGroup.Controls.AddRange(new Control[] { 
                localVersionLabel, remoteVersionLabel, syncStatusLabel, syncStatusIcon 
            });

            // Actions Group
            actionsGroup = new GroupBox
            {
                Text = "同期アクション",
                Location = new Point(15, 185),
                Size = new Size(650, 100),
                Font = new Font("Yu Gothic UI", 10)
            };

            getRemoteButton = new Button
            {
                Text = "リモート設定取得",
                Location = new Point(15, 30),
                Size = new Size(140, 50),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            getRemoteButton.Click += async (s, e) => await GetRemoteSettingsAsync();

            uploadButton = new Button
            {
                Text = "↑ アップロード",
                Location = new Point(170, 30),
                Size = new Size(140, 50),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };
            uploadButton.Click += async (s, e) => await UploadSettingsAsync();

            downloadButton = new Button
            {
                Text = "↓ ダウンロード",
                Location = new Point(325, 30),
                Size = new Size(140, 50),
                BackColor = Color.FromArgb(230, 126, 34),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };
            downloadButton.Click += async (s, e) => await DownloadSettingsAsync();

            syncButton = new Button
            {
                Text = "⇄ 自動同期",
                Location = new Point(480, 30),
                Size = new Size(140, 50),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Yu Gothic UI", 9, FontStyle.Bold)
            };
            syncButton.Click += async (s, e) => await AutoSyncSettingsAsync();

            actionsGroup.Controls.AddRange(new Control[] { 
                getRemoteButton, uploadButton, downloadButton, syncButton 
            });

            // Settings Preview Group
            settingsPreviewGroup = new GroupBox
            {
                Text = "設定プレビュー",
                Location = new Point(15, 295),
                Size = new Size(650, 360),
                Font = new Font("Yu Gothic UI", 10)
            };

            settingsTabControl = new TabControl
            {
                Location = new Point(10, 25),
                Size = new Size(630, 320)
            };

            localTab = new TabPage("ローカル設定");
            remoteTab = new TabPage("リモート設定");

            localSettingsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.White
            };

            remoteSettingsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.White
            };

            localTab.Controls.Add(localSettingsTextBox);
            remoteTab.Controls.Add(remoteSettingsTextBox);
            settingsTabControl.TabPages.AddRange(new TabPage[] { localTab, remoteTab });
            settingsPreviewGroup.Controls.Add(settingsTabControl);

            // Progress Bar
            progressBar = new ProgressBar
            {
                Location = new Point(15, 665),
                Size = new Size(650, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            // Close Button
            closeButton = new Button
            {
                Text = "閉じる",
                Location = new Point(565, 685),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) => this.Close();

            mainPanel.Controls.AddRange(new Control[] { 
                titleLabel, syncStatusGroup, actionsGroup, settingsPreviewGroup, 
                progressBar, closeButton 
            });
            this.Controls.Add(mainPanel);
        }

        private async Task LoadSettingsAsync()
        {
            // Load local settings preview
            try
            {
                var localDict = new Dictionary<string, object>
                {
                    ["CloudSyncEnabled"] = _localSettings.CloudSyncEnabled,
                    ["CloudPlayerName"] = _localSettings.CloudPlayerName ?? "",
                    ["AutoSuicideEnabled"] = _localSettings.AutoSuicideEnabled
                    // Add more settings as needed
                };

                localSettingsTextBox.Text = JsonSerializer.Serialize(localDict, new JsonSerializerOptions { WriteIndented = true });
                _localVersion = 1; // Placeholder
                localVersionLabel.Text = $"ローカルバージョン: {_localVersion}";
            }
            catch (Exception ex)
            {
                localSettingsTextBox.Text = $"エラー: {ex.Message}";
            }

            // Try to load remote settings
            await GetRemoteSettingsAsync();
        }

        private async Task GetRemoteSettingsAsync()
        {
            if (_cloudClient == null)
            {
                DialogHelper.ShowError("Cloud接続が無効です");
                return;
            }

            progressBar.Visible = true;
            getRemoteButton.Enabled = false;

            try
            {
                _remoteSettings = await _cloudClient.GetSettingsAsync(_userId, CancellationToken.None);

                if (_remoteSettings != null)
                {
                    remoteSettingsTextBox.Text = JsonSerializer.Serialize(_remoteSettings, new JsonSerializerOptions { WriteIndented = true });
                    
                    if (_remoteSettings.TryGetValue("version", out var version))
                    {
                        _remoteVersion = Convert.ToInt32(version);
                    }

                    remoteVersionLabel.Text = $"リモートバージョン: {_remoteVersion}";
                    UpdateSyncStatus();
                }
            }
            catch (Exception ex)
            {
                remoteSettingsTextBox.Text = $"取得失敗: {ex.Message}";
                DialogHelper.ShowException("リモート設定の取得", ex);
            }
            finally
            {
                progressBar.Visible = false;
                getRemoteButton.Enabled = true;
            }
        }

        private async Task UploadSettingsAsync()
        {
            if (_cloudClient == null) return;

            var result = MessageBox.Show(
                "ローカル設定をクラウドにアップロードします。リモート設定は上書きされます。\n続行しますか?",
                "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes) return;

            progressBar.Visible = true;
            uploadButton.Enabled = false;

            try
            {
                var localDict = new Dictionary<string, object>
                {
                    ["CloudSyncEnabled"] = _localSettings.CloudSyncEnabled,
                    ["CloudPlayerName"] = _localSettings.CloudPlayerName ?? "",
                    ["AutoSuicideEnabled"] = _localSettings.AutoSuicideEnabled
                };

                await _cloudClient.UpdateSettingsAsync(_userId, localDict, CancellationToken.None);
                MessageBox.Show("設定をアップロードしました", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                await GetRemoteSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アップロードに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar.Visible = false;
                uploadButton.Enabled = true;
            }
        }

        private async Task DownloadSettingsAsync()
        {
            if (_cloudClient == null || _remoteSettings == null) return;

            var result = MessageBox.Show(
                "リモート設定をダウンロードしてローカルに適用します。ローカル設定は上書きされます。\n続行しますか?",
                "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes) return;

            progressBar.Visible = true;
            downloadButton.Enabled = false;

            try
            {
                // Apply remote settings to local
                if (_remoteSettings.TryGetValue("CloudSyncEnabled", out var cloudSync))
                {
                    _localSettings.CloudSyncEnabled = Convert.ToBoolean(cloudSync);
                }
                if (_remoteSettings.TryGetValue("CloudPlayerName", out var playerName) && playerName != null)
                {
                    _localSettings.CloudPlayerName = playerName.ToString();
                }
                // Apply more settings as needed

                MessageBox.Show("設定をダウンロードして適用しました", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                await LoadSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ダウンロードに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar.Visible = false;
                downloadButton.Enabled = true;
            }
        }

        private async Task AutoSyncSettingsAsync()
        {
            if (_cloudClient == null) return;

            progressBar.Visible = true;
            syncButton.Enabled = false;

            try
            {
                var localDict = new Dictionary<string, object>
                {
                    ["CloudSyncEnabled"] = _localSettings.CloudSyncEnabled,
                    ["CloudPlayerName"] = _localSettings.CloudPlayerName ?? "",
                    ["AutoSuicideEnabled"] = _localSettings.AutoSuicideEnabled
                };

                var result = await _cloudClient.SyncSettingsAsync(_userId, localDict, _localVersion, CancellationToken.None);

                if (result.TryGetValue("merged_settings", out var merged))
                {
                    MessageBox.Show("設定を自動同期しました(マージ完了)", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                await LoadSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自動同期に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar.Visible = false;
                syncButton.Enabled = true;
            }
        }

        private void UpdateSyncStatus()
        {
            if (_remoteVersion == _localVersion)
            {
                syncStatusLabel.Text = "同期状態: 同期済み ✓";
                syncStatusLabel.ForeColor = Color.Green;
            }
            else if (_remoteVersion > _localVersion)
            {
                syncStatusLabel.Text = "同期状態: リモートが新しい";
                syncStatusLabel.ForeColor = Color.Orange;
            }
            else
            {
                syncStatusLabel.Text = "同期状態: ローカルが新しい";
                syncStatusLabel.ForeColor = Color.Blue;
            }
        }
    }
}
