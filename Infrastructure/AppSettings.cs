using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;

namespace ToNRoundCounter.Infrastructure
{
    public class AppSettings : IAppSettings
    {
        private readonly string settingsFile = "appsettings.json";
        private readonly IEventLogger _logger;
        private readonly IEventBus _bus;

        public AppSettings(IEventLogger logger, IEventBus bus)
        {
            _logger = logger;
            _bus = bus;
            Load();
        }

        public int OSCPort { get; set; } = 9001;
        public bool OSCPortChanged { get; set; } = false;
        public Color BackgroundColor_InfoPanel { get; set; } = Color.Gainsboro;
        public Color BackgroundColor_Stats { get; set; } = Color.Gainsboro;
        public Color BackgroundColor_Log { get; set; } = Color.Gainsboro;
        public Color FixedTerrorColor { get; set; } = Color.Empty;
        public bool ShowStats { get; set; } = true;
        public bool ShowDebug { get; set; } = false;
        public bool ShowRoundLog { get; set; } = true;
        public bool Filter_RoundType { get; set; } = true;
        public bool Filter_Terror { get; set; } = true;
        public bool Filter_Appearance { get; set; } = true;
        public bool Filter_Survival { get; set; } = true;
        public bool Filter_Death { get; set; } = true;
        public bool Filter_SurvivalRate { get; set; } = true;
        public List<string> AutoSuicideRoundTypes { get; set; } = new List<string>();
        public Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; } = new Dictionary<string, AutoSuicidePreset>();
        public List<string> AutoSuicideDetailCustom { get; set; } = new List<string>();
        public bool AutoSuicideFuzzyMatch { get; set; } = false;
        public bool AutoSuicideUseDetail { get; set; } = false;
        public List<string> RoundTypeStats { get; set; } = new List<string>()
        {
            "クラシック", "走れ！", "オルタネイト", "パニッシュ", "狂気", "サボタージュ", "霧", "ブラッドバス", "ダブルトラブル",
            "EX", "ミッドナイト", "ゴースト", "8ページ", "アンバウンド", "寒い夜", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス"
        };
        public bool AutoSuicideEnabled { get; set; }
        public string apikey { get; set; } = string.Empty;
        public ThemeType Theme { get; set; } = ThemeType.Light;
        public string LogFilePath { get; set; } = "logs/log-.txt";
        public string WebSocketIp { get; set; } = "127.0.0.1";
        public bool AutoLaunchEnabled { get; set; }
        public string AutoLaunchExecutablePath { get; set; } = string.Empty;
        public string AutoLaunchArguments { get; set; } = string.Empty;
        public bool ItemMusicEnabled { get; set; }
        public string ItemMusicItemName { get; set; } = string.Empty;
        public string ItemMusicSoundPath { get; set; } = string.Empty;
        public double ItemMusicMinSpeed { get; set; }
        public double ItemMusicMaxSpeed { get; set; }
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string LastSaveCode { get; set; } = string.Empty;

        public void Load()
        {
            try
            {
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    JsonConvert.PopulateObject(json, this);
                }
                var errors = Validate();
                if (errors.Count > 0)
                {
                    foreach (var err in errors)
                    {
                        _logger.LogEvent("SettingsValidation", err, Serilog.Events.LogEventLevel.Error);
                    }
                    _bus.Publish(new SettingsValidationFailed(errors));
                }
                else
                {
                    _logger.LogEvent("AppSettings", "Settings loaded successfully.");
                }

                AutoLaunchExecutablePath ??= string.Empty;
                AutoLaunchArguments ??= string.Empty;
                ItemMusicItemName ??= string.Empty;
                ItemMusicSoundPath ??= string.Empty;
                DiscordWebhookUrl ??= string.Empty;
                LastSaveCode ??= string.Empty;
                NormalizeItemMusicSpeeds();
            }
            catch (Exception ex)
            {
                _logger.LogEvent("Error", "Failed to bind app settings: " + ex.Message, Serilog.Events.LogEventLevel.Error);
                _bus.Publish(new SettingsValidationFailed(new[] { ex.Message }));
            }
        }

        private void NormalizeItemMusicSpeeds()
        {
            if (double.IsNaN(ItemMusicMinSpeed) || double.IsInfinity(ItemMusicMinSpeed) || ItemMusicMinSpeed < 0)
            {
                ItemMusicMinSpeed = 0;
            }

            if (double.IsNaN(ItemMusicMaxSpeed) || double.IsInfinity(ItemMusicMaxSpeed) || ItemMusicMaxSpeed < 0)
            {
                ItemMusicMaxSpeed = ItemMusicMinSpeed;
            }

            if (ItemMusicMaxSpeed < ItemMusicMinSpeed)
            {
                ItemMusicMaxSpeed = ItemMusicMinSpeed;
            }
        }

        private List<string> Validate()
        {
            var errors = new List<string>();
            if (OSCPort <= 0 || OSCPort > 65535)
            {
                errors.Add("Invalid OSCPort value.");
            }
            return errors;
        }

        public async Task SaveAsync()
        {
            NormalizeItemMusicSpeeds();
            var settings = new AppSettingsData
            {
                OSCPort = OSCPort,
                OSCPortChanged = OSCPortChanged,
                BackgroundColor_InfoPanel = ColorTranslator.ToHtml(BackgroundColor_InfoPanel),
                BackgroundColor_Stats = ColorTranslator.ToHtml(BackgroundColor_Stats),
                BackgroundColor_Log = ColorTranslator.ToHtml(BackgroundColor_Log),
                FixedTerrorColor = FixedTerrorColor == Color.Empty ? "" : ColorTranslator.ToHtml(FixedTerrorColor),
                ShowStats = ShowStats,
                ShowDebug = ShowDebug,
                ShowRoundLog = ShowRoundLog,
                Filter_RoundType = Filter_RoundType,
                Filter_Terror = Filter_Terror,
                Filter_Appearance = Filter_Appearance,
                Filter_Survival = Filter_Survival,
                Filter_Death = Filter_Death,
                Filter_SurvivalRate = Filter_SurvivalRate,
                RoundTypeStats = RoundTypeStats,
                AutoSuicideEnabled = AutoSuicideEnabled,
                AutoSuicideRoundTypes = AutoSuicideRoundTypes,
                AutoSuicidePresets = AutoSuicidePresets,
                AutoSuicideDetailCustom = AutoSuicideDetailCustom,
                AutoSuicideFuzzyMatch = AutoSuicideFuzzyMatch,
                AutoSuicideUseDetail = AutoSuicideUseDetail,
                apikey = apikey,
                Theme = Theme,
                LogFilePath = LogFilePath,
                WebSocketIp = WebSocketIp,
                AutoLaunchEnabled = AutoLaunchEnabled,
                AutoLaunchExecutablePath = AutoLaunchExecutablePath,
                AutoLaunchArguments = AutoLaunchArguments,
                ItemMusicEnabled = ItemMusicEnabled,
                ItemMusicItemName = ItemMusicItemName,
                ItemMusicSoundPath = ItemMusicSoundPath,
                ItemMusicMinSpeed = ItemMusicMinSpeed,
                ItemMusicMaxSpeed = ItemMusicMaxSpeed,
                DiscordWebhookUrl = DiscordWebhookUrl,
                LastSaveCode = LastSaveCode
            };

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            try
            {
                using (var writer = new StreamWriter(settingsFile, false))
                {
                    await writer.WriteAsync(json).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("Error", "Failed to save app settings: " + ex.Message, Serilog.Events.LogEventLevel.Error);
                throw;
            }
        }
    }

    public class AppSettingsData
    {
        public int OSCPort { get; set; }
        public bool OSCPortChanged { get; set; }
        public string BackgroundColor_InfoPanel { get; set; }
        public string BackgroundColor_Stats { get; set; }
        public string BackgroundColor_Log { get; set; }
        public string FixedTerrorColor { get; set; }
        public bool ShowStats { get; set; }
        public bool ShowDebug { get; set; }
        public bool ShowRoundLog { get; set; }
        public bool Filter_RoundType { get; set; }
        public bool Filter_Terror { get; set; }
        public bool Filter_Appearance { get; set; }
        public bool Filter_Survival { get; set; }
        public bool Filter_Death { get; set; }
        public bool Filter_SurvivalRate { get; set; }
        public List<string> RoundTypeStats { get; set; }
        public bool AutoSuicideEnabled { get; set; }
        public List<string> AutoSuicideRoundTypes { get; set; }
        public Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; }
        public List<string> AutoSuicideDetailCustom { get; set; }
        public bool AutoSuicideFuzzyMatch { get; set; }
        public bool AutoSuicideUseDetail { get; set; }
        public string apikey { get; set; }
        public ThemeType Theme { get; set; }
        public string LogFilePath { get; set; }
        public string WebSocketIp { get; set; }
        public bool AutoLaunchEnabled { get; set; }
        public string AutoLaunchExecutablePath { get; set; } = string.Empty;
        public string AutoLaunchArguments { get; set; } = string.Empty;
        public bool ItemMusicEnabled { get; set; }
        public string ItemMusicItemName { get; set; } = string.Empty;
        public string ItemMusicSoundPath { get; set; } = string.Empty;
        public double ItemMusicMinSpeed { get; set; }
        public double ItemMusicMaxSpeed { get; set; }
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string LastSaveCode { get; set; } = string.Empty;
    }
}
