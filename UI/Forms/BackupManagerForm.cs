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
    /// Backup and restore management UI form
    /// </summary>
    public partial class BackupManagerForm : Form
    {
        private readonly Infrastructure.Cloud.CloudClientZero? _cloudClient;
        private readonly string _userId;
        private List<Dictionary<string, object>>? _backups;

        // UI Controls
        private Panel mainPanel = null!;
        private Label titleLabel = null!;
        private GroupBox createBackupGroup = null!;
        private Label backupNameLabel = null!;
        private TextBox backupNameTextBox = null!;
        private CheckBox includeSettingsCheckBox = null!;
        private CheckBox includeStatsCheckBox = null!;
        private CheckBox includeRoundsCheckBox = null!;
        private Button createBackupButton = null!;
        private GroupBox backupsListGroup = null!;
        private ListView backupsListView = null!;
        private Button refreshButton = null!;
        private Button restoreButton = null!;
        private Button deleteButton = null!;
        private Button exportButton = null!;
        private ProgressBar progressBar = null!;
        private Label statusLabel = null!;
        private Button closeButton = null!;

        public BackupManagerForm(Infrastructure.Cloud.CloudClientZero? cloudClient, string userId)
        {
            _cloudClient = cloudClient;
            _userId = userId ?? Environment.UserName;
            
            InitializeComponent();
            InitializeCustomComponents();
            
            this.Load += async (s, e) => await LoadBackupsAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "バックアップ管理 - Backup Manager";
            this.Size = new Size(800, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(800, 700);
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
                Text = "クラウドバックアップ管理",
                Font = new Font("Yu Gothic UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 15)
            };

            // Create Backup Group
            createBackupGroup = new GroupBox
            {
                Text = "新規バックアップ作成",
                Location = new Point(15, 55),
                Size = new Size(750, 180),
                Font = new Font("Yu Gothic UI", 10)
            };

            backupNameLabel = new Label
            {
                Text = "バックアップ名:",
                Location = new Point(15, 30),
                AutoSize = true
            };

            backupNameTextBox = new TextBox
            {
                Location = new Point(15, 55),
                Size = new Size(710, 25),
                Text = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            includeSettingsCheckBox = new CheckBox
            {
                Text = "設定を含める",
                Location = new Point(15, 95),
                AutoSize = true,
                Checked = true
            };

            includeStatsCheckBox = new CheckBox
            {
                Text = "統計情報を含める",
                Location = new Point(200, 95),
                AutoSize = true,
                Checked = true
            };

            includeRoundsCheckBox = new CheckBox
            {
                Text = "ラウンド履歴を含める",
                Location = new Point(385, 95),
                AutoSize = true,
                Checked = true
            };

            createBackupButton = new Button
            {
                Text = "バックアップ作成",
                Location = new Point(575, 130),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Yu Gothic UI", 10, FontStyle.Bold)
            };
            createBackupButton.Click += async (s, e) => await CreateBackupAsync();

            createBackupGroup.Controls.AddRange(new Control[] { 
                backupNameLabel, backupNameTextBox, 
                includeSettingsCheckBox, includeStatsCheckBox, includeRoundsCheckBox,
                createBackupButton 
            });

            // Backups List Group
            backupsListGroup = new GroupBox
            {
                Text = "バックアップ一覧",
                Location = new Point(15, 245),
                Size = new Size(750, 340),
                Font = new Font("Yu Gothic UI", 10)
            };

            backupsListView = new ListView
            {
                Location = new Point(15, 25),
                Size = new Size(720, 260),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            backupsListView.Columns.Add("バックアップ名", 300);
            backupsListView.Columns.Add("作成日時", 180);
            backupsListView.Columns.Add("サイズ", 100);
            backupsListView.Columns.Add("種類", 120);
            backupsListView.SelectedIndexChanged += BackupsListView_SelectedIndexChanged;

            refreshButton = new Button
            {
                Text = "更新",
                Location = new Point(15, 295),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            refreshButton.Click += async (s, e) => await LoadBackupsAsync();

            restoreButton = new Button
            {
                Text = "復元",
                Location = new Point(425, 295),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(230, 126, 34),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            restoreButton.Click += async (s, e) => await RestoreBackupAsync();

            deleteButton = new Button
            {
                Text = "削除",
                Location = new Point(535, 295),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            deleteButton.Click += DeleteButton_Click;

            exportButton = new Button
            {
                Text = "エクスポート",
                Location = new Point(645, 295),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            exportButton.Click += ExportButton_Click;

            backupsListGroup.Controls.AddRange(new Control[] { 
                backupsListView, refreshButton, restoreButton, deleteButton, exportButton 
            });

            // Progress Bar
            progressBar = new ProgressBar
            {
                Location = new Point(15, 595),
                Size = new Size(750, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };

            // Status Label
            statusLabel = new Label
            {
                Text = "準備完了",
                Location = new Point(15, 615),
                Size = new Size(650, 25),
                Font = new Font("Yu Gothic UI", 9),
                ForeColor = Color.Gray
            };

            // Close Button
            closeButton = new Button
            {
                Text = "閉じる",
                Location = new Point(665, 615),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) => this.Close();

            mainPanel.Controls.AddRange(new Control[] { 
                titleLabel, createBackupGroup, backupsListGroup, 
                progressBar, statusLabel, closeButton 
            });
            this.Controls.Add(mainPanel);
        }

        private async Task LoadBackupsAsync()
        {
            if (_cloudClient == null)
            {
                MessageBox.Show("Cloud接続が無効です", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            progressBar.Visible = true;
            refreshButton.Enabled = false;
            statusLabel.Text = "バックアップ一覧を読み込み中...";

            try
            {
                _backups = await _cloudClient.ListBackupsAsync(_userId, CancellationToken.None);

                backupsListView.Items.Clear();

                if (_backups != null && _backups.Count > 0)
                {
                    foreach (var backup in _backups)
                    {
                        var name = backup.TryGetValue("backup_name", out var n) ? n?.ToString() : "Unknown";
                        var createdAt = backup.TryGetValue("created_at", out var ca) ? ca?.ToString() : "";
                        var size = backup.TryGetValue("size_bytes", out var sz) ? FormatFileSize(Convert.ToInt64(sz)) : "N/A";
                        var type = backup.TryGetValue("backup_type", out var bt) ? bt?.ToString() : "Full";

                        var item = new ListViewItem(name ?? "Unknown");
                        item.SubItems.Add(createdAt);
                        item.SubItems.Add(size);
                        item.SubItems.Add(type);
                        item.Tag = backup; // Store full backup data
                        backupsListView.Items.Add(item);
                    }

                    statusLabel.Text = $"{_backups.Count}個のバックアップが見つかりました";
                }
                else
                {
                    statusLabel.Text = "バックアップがありません";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"バックアップ一覧の取得に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "読み込み失敗";
            }
            finally
            {
                progressBar.Visible = false;
                refreshButton.Enabled = true;
            }
        }

        private async Task CreateBackupAsync()
        {
            if (_cloudClient == null) return;

            if (string.IsNullOrWhiteSpace(backupNameTextBox.Text))
            {
                MessageBox.Show("バックアップ名を入力してください", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            progressBar.Visible = true;
            createBackupButton.Enabled = false;
            statusLabel.Text = "バックアップを作成中...";

            try
            {
                var includes = new List<string>();
                if (includeSettingsCheckBox.Checked) includes.Add("settings");
                if (includeStatsCheckBox.Checked) includes.Add("stats");
                if (includeRoundsCheckBox.Checked) includes.Add("rounds");

                var backupType = includes.Count == 3 ? "FULL" : "PARTIAL";
                var description = $"{backupNameTextBox.Text} - {string.Join(", ", includes)}";

                var result = await _cloudClient.CreateBackupAsync(
                    backupType,
                    compress: true,
                    encrypt: false,
                    description: description,
                    cancellationToken: CancellationToken.None
                );

                MessageBox.Show("バックアップを作成しました", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusLabel.Text = "バックアップ作成完了";

                // Generate new default name for next backup
                backupNameTextBox.Text = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

                await LoadBackupsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"バックアップの作成に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "バックアップ作成失敗";
            }
            finally
            {
                progressBar.Visible = false;
                createBackupButton.Enabled = true;
            }
        }

        private async Task RestoreBackupAsync()
        {
            if (_cloudClient == null || backupsListView.SelectedItems.Count == 0) return;

            var selectedBackup = backupsListView.SelectedItems[0].Tag as Dictionary<string, object>;
            if (selectedBackup == null) return;

            var backupId = selectedBackup.TryGetValue("backup_id", out var bid) ? bid?.ToString() : null;
            if (string.IsNullOrEmpty(backupId))
            {
                MessageBox.Show("バックアップIDが無効です", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                "選択したバックアップから復元します。現在のデータは上書きされます。\n続行しますか?",
                "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes) return;

            progressBar.Visible = true;
            restoreButton.Enabled = false;
            statusLabel.Text = "バックアップを復元中...";

            try
            {
                await _cloudClient.RestoreBackupAsync(
                    backupId!, // Non-null assertion since we checked above
                    validateBeforeRestore: true,
                    createBackupBeforeRestore: true,
                    cancellationToken: CancellationToken.None
                );
                
                MessageBox.Show("バックアップから復元しました", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusLabel.Text = "復元完了";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"復元に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "復元失敗";
            }
            finally
            {
                progressBar.Visible = false;
                restoreButton.Enabled = true;
            }
        }

        private void DeleteButton_Click(object? sender, EventArgs e)
        {
            if (backupsListView.SelectedItems.Count == 0) return;

            var result = MessageBox.Show(
                "選択したバックアップを削除します。この操作は元に戻せません。\n続行しますか?",
                "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                MessageBox.Show("バックアップ削除機能は未実装です", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportButton_Click(object? sender, EventArgs e)
        {
            if (backupsListView.SelectedItems.Count == 0) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "バックアップをエクスポート"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var selectedBackup = backupsListView.SelectedItems[0].Tag as Dictionary<string, object>;
                    var json = JsonSerializer.Serialize(selectedBackup, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(saveDialog.FileName, json);
                    
                    MessageBox.Show("エクスポート完了", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"エクスポートに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BackupsListView_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var hasSelection = backupsListView.SelectedItems.Count > 0;
            restoreButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
            exportButton.Enabled = hasSelection;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
