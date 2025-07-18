using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using ToNRoundCounter.Utils;

namespace ToNRoundCounter
{
    public static class AppSettings
    {
        private static readonly string settingsFile = "appsettings.json";

        public static int OSCPort { get; set; } = 9001;
        public static bool OSCPortChanged { get; set; } = false;

        public static Color BackgroundColor_InfoPanel { get; set; } = Color.DarkGray;
        public static Color BackgroundColor_Stats { get; set; } = Color.DarkGray;
        public static Color BackgroundColor_Log { get; set; } = Color.DarkGray;
        public static Color FixedTerrorColor { get; set; } = Color.Empty; // 未指定の場合は空（固定しない）

        public static bool ShowStats { get; set; } = true;
        public static bool ShowDebug { get; set; } = false;
        public static bool ShowRoundLog { get; set; } = true;

        public static bool Filter_RoundType { get; set; } = true;
        public static bool Filter_Terror { get; set; } = true;
        public static bool Filter_Appearance { get; set; } = true;
        public static bool Filter_Survival { get; set; } = true;
        public static bool Filter_Death { get; set; } = true;
        public static bool Filter_SurvivalRate { get; set; } = true;

        public static List<string> AutoSuicideRoundTypes { get; set; } = new List<string>();

        public static List<string> RoundTypeStats { get; set; } = new List<string>()
        {
            "クラシック", "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "走れ！", "コールドナイト", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス", "霧", "8ページ", "狂気", "ゴースト", "ダブル・トラブル", "EX", "アンバウンド"
        };
        public static bool AutoSuicideEnabled { get; internal set; }

        public static string apikey { get; set; } = string.Empty; // APIキーの初期値は空文字列

        public static void Load()
        {
            if (File.Exists(settingsFile))
            {
                try
                {
                    string json = File.ReadAllText(settingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettingsData>(json);
                    if (settings != null)
                    {
                        OSCPort = int.TryParse(settings.OSCPort.ToString(), out int port) ? port : 9001;
                        OSCPortChanged = bool.TryParse(settings.OSCPortChanged.ToString(), out bool portChanged) ? portChanged : false;
                        BackgroundColor_InfoPanel = !string.IsNullOrEmpty(settings.BackgroundColor_InfoPanel) ? ColorTranslator.FromHtml(settings.BackgroundColor_InfoPanel) : Color.DarkGray;
                        BackgroundColor_Stats = !string.IsNullOrEmpty(settings.BackgroundColor_Stats) ? ColorTranslator.FromHtml(settings.BackgroundColor_Stats) : Color.DarkGray;
                        BackgroundColor_Log = !string.IsNullOrEmpty(settings.BackgroundColor_Log) ? ColorTranslator.FromHtml(settings.BackgroundColor_Log) : Color.DarkGray;
                        FixedTerrorColor = !string.IsNullOrEmpty(settings.FixedTerrorColor) ? ColorTranslator.FromHtml(settings.FixedTerrorColor) : Color.Empty;
                        ShowStats = bool.TryParse(settings.ShowStats.ToString(), out bool showStats) ? showStats : true;
                        ShowDebug = bool.TryParse(settings.ShowDebug.ToString(), out bool showDebug) ? showDebug : false;
                        ShowRoundLog = bool.TryParse(settings.ShowRoundLog.ToString(), out bool showRoundLog) ? showRoundLog : true;
                        Filter_RoundType = bool.TryParse(settings.Filter_RoundType.ToString(), out bool filterRoundType) ? filterRoundType : true;
                        Filter_Terror = bool.TryParse(settings.Filter_Terror.ToString(), out bool filterTerror) ? filterTerror : true;
                        Filter_Appearance = bool.TryParse(settings.Filter_Appearance.ToString(), out bool filterAppearance) ? filterAppearance : true;
                        Filter_Survival = bool.TryParse(settings.Filter_Survival.ToString(), out bool filterSurvival) ? filterSurvival : true;
                        Filter_Death = bool.TryParse(settings.Filter_Death.ToString(), out bool filterDeath) ? filterDeath : true;
                        Filter_SurvivalRate = bool.TryParse(settings.Filter_SurvivalRate.ToString(), out bool filterSurvivalRate) ? filterSurvivalRate : true;
                        RoundTypeStats = settings.RoundTypeStats ?? new List<string>()
                        {
                            "クラシック", "オルタネイト", "パニッシュ", "サボタージュ", "ブラッドバス", "ミッドナイト", "走れ！", "コールドナイト", "ミスティックムーン", "ブラッドムーン", "トワイライト", "ソルスティス", "霧", "8ページ", "狂気", "ゴースト", "ダブルトラブル", "EX", "アンバウンド"
                        };
                        AutoSuicideEnabled = bool.TryParse(settings.AutoSuicideEnabled.ToString(), out bool autoSuicideEnabled) ? autoSuicideEnabled : false;
                        AutoSuicideRoundTypes = settings.AutoSuicideRoundTypes ?? new List<string>();
                        apikey = !string.IsNullOrEmpty(settings.apikey) ? settings.apikey : string.Empty; // APIキーの読み込み
                        EventLogger.LogEvent("AppSettings", "Settings loaded successfully from " + settingsFile);


                    }
                }
                catch (Exception ex)
                {
                    // エラー発生時はデフォルト値を維持
                    EventLogger.LogEvent("Error", "Failed to load app settings. Using default values.");
                    EventLogger.LogEvent("Error", "Exception: " + ex.Message);
                }
            }
        }

        public static void Save()
        {
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
                apikey = apikey // APIキーの保存
            };
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsFile, json);
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
        public bool AutoSuicideEnabled { get; internal set; }
        public List<string> AutoSuicideRoundTypes { get; internal set; }

        public string apikey { get; set; }
    }
}
