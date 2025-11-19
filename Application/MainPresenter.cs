using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using Serilog.Events;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Presenter coordinating between the main view and application services.
    /// </summary>
    public class MainPresenter
    {
        private static readonly char[] TerrorSeparators = new[] { '&', '＆' };
        private readonly StateService _stateService;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly CloudWebSocketClient? _cloudClient;
        private IMainView? _view;
        private string? _currentRoundId;
        private DateTime _currentRoundStartTime;

        public MainPresenter(StateService stateService, IAppSettings settings, IEventLogger logger, IHttpClient httpClient, CloudWebSocketClient? cloudClient = null)
        {
            _stateService = stateService;
            _settings = settings;
            _logger = logger;
            _httpClient = httpClient;
            _cloudClient = cloudClient;
            _stateService.RoundLogAdded += OnRoundLogAdded;
        }

        public void AttachView(IMainView view)
        {
            _view = view;
            _logger.LogEvent("MainPresenter", () => $"View attached: {view.GetType().FullName}");
            var history = _stateService.GetRoundLogHistory().Select(e => e.Item2);
            _view.UpdateRoundLog(history);
        }

        public void AppendRoundLog(Round round, string status)
        {
            string? mapName = round.MapName;
            string? initialMapName = mapName;
            string? terrorMapCandidate = null;
            string? roundTypeMapCandidate = null;

            if (string.IsNullOrWhiteSpace(mapName) && !string.IsNullOrWhiteSpace(round.RoundType) && !string.IsNullOrWhiteSpace(round.TerrorKey))
            {
                terrorMapCandidate = _stateService.GetTerrorMapName(round.RoundType!, round.TerrorKey!);
                mapName = terrorMapCandidate;
            }

            if (string.IsNullOrWhiteSpace(mapName) && !string.IsNullOrWhiteSpace(round.RoundType))
            {
                roundTypeMapCandidate = _stateService.GetRoundMapName(round.RoundType!);
                mapName = roundTypeMapCandidate;
            }

            var previousRound = _stateService.PreviousRound;
            string? previousRoundMapName = previousRound?.MapName;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                if (!string.IsNullOrWhiteSpace(previousRoundMapName))
                {
                    mapName = previousRoundMapName;
                }
            }

            if (!string.IsNullOrWhiteSpace(mapName) && mapName != round.MapName)
            {
                round.MapName = mapName;
            }

            if (string.IsNullOrWhiteSpace(mapName))
            {
                LogMapResolutionDebug(round, initialMapName, terrorMapCandidate, roundTypeMapCandidate, previousRoundMapName, mapName);
            }

            string items = round.ItemNames.Count > 0 ? string.Join("、", round.ItemNames) : "アイテム未使用";
            string displayMapName = mapName ?? string.Empty;
            string logEntry = string.Format("ラウンドタイプ: {0}, テラー: {1}, MAP: {2}, アイテム: {3}, ダメージ: {4}, 生死: {5}",
                round.RoundType, round.TerrorKey, displayMapName, items, round.Damage, status);
            _stateService.AddRoundLog(round, logEntry);
            _logger.LogEvent("MainPresenter", () => $"Round log appended: {round.RoundType} ({status}).");
        }

        public async Task OnRoundStartAsync(Round round, string? instanceId = null)
        {
            if (_cloudClient != null && _settings.CloudSyncEnabled && _cloudClient.IsConnected)
            {
                try
                {
                    var playerName = string.IsNullOrWhiteSpace(_settings.CloudPlayerName) 
                        ? Environment.UserName 
                        : _settings.CloudPlayerName;
                    
                    _currentRoundStartTime = DateTime.Now;
                    _currentRoundId = await _cloudClient.GameRoundStartAsync(
                        instanceId,
                        playerName,
                        round.RoundType ?? "Unknown",
                        round.MapName,
                        System.Threading.CancellationToken.None
                    ).ConfigureAwait(false);
                    
                    _logger.LogEvent("CloudSync", $"Round started on cloud: {_currentRoundId} (Instance: {instanceId ?? "None"})");
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("CloudSync", () => $"Failed to start round on cloud: {ex.Message}", LogEventLevel.Warning);
                }
            }
        }

        public async Task OnRoundEndAsync(Round round, string status)
        {
            if (_cloudClient != null && _settings.CloudSyncEnabled && _cloudClient.IsConnected && !string.IsNullOrEmpty(_currentRoundId))
            {
                try
                {
                    var survived = status != "死亡" && !round.IsDeath;
                    var duration = (int)(DateTime.Now - _currentRoundStartTime).TotalSeconds;
                    
                    var result = await _cloudClient.GameRoundEndAsync(
                        _currentRoundId!,
                        survived,
                        duration,
                        round.Damage,
                        round.ItemNames?.ToArray(),
                        round.TerrorKey,
                        System.Threading.CancellationToken.None
                    ).ConfigureAwait(false);
                    
                    // Log the returned stats from cloud
                    if (result.ContainsKey("stats"))
                    {
                        _logger.LogEvent("CloudSync", $"Round ended on cloud: {_currentRoundId} | Stats updated on server");
                    }
                    else
                    {
                        _logger.LogEvent("CloudSync", $"Round ended on cloud: {_currentRoundId}");
                    }
                    
                    _currentRoundId = null;
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("CloudSync", () => $"Failed to end round on cloud: {ex.Message}", LogEventLevel.Warning);
                }
            }
        }

        public async Task UploadRoundLogAsync(Round round, string status)
        {
            _logger.LogEvent("MainPresenter", () => $"Initiating round log upload for {round.RoundType} ({status}).");
            await TryUploadRoundLogToCloudAsync(round, status).ConfigureAwait(false);
            await SendDiscordWebhookAsync(round, status).ConfigureAwait(false);
            _logger.LogEvent("MainPresenter", "Round log upload operations completed.");
        }

        private async Task TryUploadRoundLogToCloudAsync(Round round, string status)
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogEvent("RoundLogUpload", "APIキーが設定されていません。アップロードをスキップします。");
                return;
            }

            var terrors = (round.TerrorKey ?? string.Empty).Split(TerrorSeparators, StringSplitOptions.None);
            var payload = new
            {
                roundType = round.RoundType,
                terror1 = terrors.ElementAtOrDefault(0)?.Trim(),
                terror2 = terrors.ElementAtOrDefault(1)?.Trim(),
                terror3 = terrors.ElementAtOrDefault(2)?.Trim(),
                map = round.MapName,
                item = round.ItemNames.Count > 0 ? string.Join("、", round.ItemNames) : "アイテム未使用",
                damage = round.Damage,
                isAlive = !round.IsDeath,
                instanceSize = round.InstancePlayersCount
            };

            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            try
            {
                var url = "https://toncloud.sprink.cloud/api/roundlogs/create";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                request.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");
                using var response = await _httpClient.SendAsync(request, System.Threading.CancellationToken.None).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogEvent("RoundLogUpload", "ラウンドログのアップロードに成功しました。");
                }
                else
                {
                    _logger.LogEvent("RoundLogUploadError", () => $"ラウンドログのアップロードに失敗しました: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("RoundLogUploadError", () => $"ラウンドログアップロード中にエラーが発生しました: {ex.Message}");
            }

            _logger.LogEvent("RoundLogUpload", () => $"Round: {round.RoundType}, Status: {status}, Map: {round.MapName}");
        }

        private async Task SendDiscordWebhookAsync(Round round, string status)
        {
            if (string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl))
            {
                return;
            }

            static string FormatInline(string? value, string fallback)
            {
                var text = string.IsNullOrWhiteSpace(value) ? fallback : value!;
                return text.Replace("`", "\\`");
            }

            string GetItems()
            {
                if (round.ItemNames != null && round.ItemNames.Count > 0)
                {
                    return string.Join("、", round.ItemNames);
                }

                return "アイテム未使用";
            }

            string statusSymbol = string.IsNullOrWhiteSpace(status)
                ? (round.IsDeath ? "☠" : "✅")
                : status;

            var descriptionBuilder = new StringBuilder();
            descriptionBuilder.AppendLine($"**ラウンドタイプ: **`{FormatInline(round.RoundType, "不明")}`");
            descriptionBuilder.AppendLine($"**テラー: **`{FormatInline(round.TerrorKey, "なし")}`");
            descriptionBuilder.AppendLine($"**MAP: **`{FormatInline(round.MapName, "不明")}`");
            descriptionBuilder.AppendLine($"**アイテム: **`{FormatInline(GetItems(), "アイテム未使用")}`");
            descriptionBuilder.AppendLine($"**ダメージ: **`{round.Damage}`");
            descriptionBuilder.AppendLine($"**生死: **`{FormatInline(statusSymbol, statusSymbol)}`");

            var description = descriptionBuilder.ToString().TrimEnd();
            int color = (round.RoundColor ?? 0xFFFFFF) & 0xFFFFFF;
            var embed = new
            {
                title = string.IsNullOrWhiteSpace(round.RoundType) ? "ラウンド結果" : round.RoundType,
                description,
                color,
                timestamp = DateTime.UtcNow
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            try
            {
                using var response = await _httpClient.PostAsync(_settings.DiscordWebhookUrl, content, System.Threading.CancellationToken.None).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogEvent("DiscordWebhook", "Discordへのラウンドログ送信に成功しました。");
                }
                else
                {
                    _logger.LogEvent("DiscordWebhookError", () => $"Discordへの送信に失敗しました: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("DiscordWebhookError", () => $"Discord送信中にエラーが発生しました: {ex.Message}");
            }

            _logger.LogEvent("DiscordWebhook", () => $"Webhook payload sent. Round: {round.RoundType}, Status: {status}");
        }

        private void OnRoundLogAdded(Round round, string logEntry)
        {
            var view = _view;
            if (view == null)
            {
                return;
            }

            view.AppendRoundLogEntry(logEntry);
        }

        private void LogMapResolutionDebug(Round round, string? initialMapName, string? terrorMapCandidate, string? roundTypeMapCandidate, string? previousRoundMapName, string? finalMapName)
        {
            if (!_logger.IsEnabled(LogEventLevel.Debug))
            {
                return;
            }

            try
            {
                var builder = new StringBuilder();
                builder.AppendLine("Map resolution debug information:");
                builder.AppendLine($"- Round ID: {round.Id}");
                builder.AppendLine($"- Round type: '{round.RoundType ?? "<null>"}'");
                builder.AppendLine($"- Terror key: '{round.TerrorKey ?? "<null>"}'");
                builder.AppendLine($"- Initial map name: '{initialMapName ?? "<null>"}'");
                builder.AppendLine($"- Terror map candidate: '{terrorMapCandidate ?? "<null>"}'");
                builder.AppendLine($"- Round type map candidate: '{roundTypeMapCandidate ?? "<null>"}'");
                builder.AppendLine($"- Previous round map: '{previousRoundMapName ?? "<null>"}'");

                var mapSnapshot = _stateService.GetRoundMapNames();
                builder.AppendLine("- Stored round map snapshot:");
                if (mapSnapshot.Count == 0)
                {
                    builder.AppendLine("  (empty)");
                }
                else
                {
                    foreach (var entry in mapSnapshot.OrderBy(e => e.Key))
                    {
                        builder.AppendLine($"  {entry.Key}: '{entry.Value ?? "<null>"}'");
                    }
                }

                builder.AppendLine($"- Final resolved map name: '{finalMapName ?? "<null>"}'");
                _logger.LogEvent("MainPresenter", builder.ToString(), LogEventLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("MainPresenter", () => $"Failed to record map resolution debug information: {ex}", LogEventLevel.Debug);
            }
        }
    }
}

