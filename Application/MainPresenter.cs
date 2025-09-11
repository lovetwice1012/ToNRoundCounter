using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Presenter coordinating between the main view and application services.
    /// </summary>
    public class MainPresenter
    {
        private readonly StateService _stateService;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly IHttpClient _httpClient;
        private IMainView _view;

        public MainPresenter(StateService stateService, IAppSettings settings, IEventLogger logger, IHttpClient httpClient)
        {
            _stateService = stateService;
            _settings = settings;
            _logger = logger;
            _httpClient = httpClient;
        }

        public void AttachView(IMainView view)
        {
            _view = view;
        }

        public void AppendRoundLog(Round round, string status)
        {
            string items = round.ItemNames.Count > 0 ? string.Join("、", round.ItemNames) : "アイテム未使用";
            string logEntry = string.Format("ラウンドタイプ: {0}, テラー: {1}, MAP: {2}, アイテム: {3}, ダメージ: {4}, 生死: {5}",
                round.RoundType, round.TerrorKey, round.MapName, items, round.Damage, status);
            _stateService.AddRoundLog(round, logEntry);
            _view?.UpdateRoundLog(_stateService.GetRoundLogHistory().Select(e => e.Item2));
        }

        public async Task UploadRoundLogAsync(Round round, string status)
        {
            if (string.IsNullOrEmpty(_settings.apikey))
            {
                _logger.LogEvent("RoundLogUpload", "APIキーが設定されていません。アップロードをスキップします。");
                return;
            }

            var terrors = (round.TerrorKey ?? string.Empty).Split('&');
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
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            try
            {
                var url = "https://toncloud.sprink.cloud/api/roundlogs/create/" + _settings.apikey;
                var response = await _httpClient.PostAsync(url, content, System.Threading.CancellationToken.None);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogEvent("RoundLogUpload", "ラウンドログのアップロードに成功しました。");
                }
                else
                {
                    _logger.LogEvent("RoundLogUploadError", $"ラウンドログのアップロードに失敗しました: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("RoundLogUploadError", $"ラウンドログアップロード中にエラーが発生しました: {ex.Message}");
            }

            _logger.LogEvent("RoundLogUpload", $"Round: {round.RoundType}, Status: {status}, Map: {round.MapName}");
        }
    }
}

