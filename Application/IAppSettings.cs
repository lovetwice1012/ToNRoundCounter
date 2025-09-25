using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;

namespace ToNRoundCounter.Application
{
    public interface IAppSettings
    {
        int OSCPort { get; set; }
        bool OSCPortChanged { get; set; }
        Color BackgroundColor_InfoPanel { get; set; }
        Color BackgroundColor_Stats { get; set; }
        Color BackgroundColor_Log { get; set; }
        Color FixedTerrorColor { get; set; }
        bool ShowStats { get; set; }
        bool ShowDebug { get; set; }
        bool ShowRoundLog { get; set; }
        bool Filter_RoundType { get; set; }
        bool Filter_Terror { get; set; }
        bool Filter_Appearance { get; set; }
        bool Filter_Survival { get; set; }
        bool Filter_Death { get; set; }
        bool Filter_SurvivalRate { get; set; }
        List<string> AutoSuicideRoundTypes { get; set; }
        Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; }
        List<string> AutoSuicideDetailCustom { get; set; }
        bool AutoSuicideFuzzyMatch { get; set; }
        bool AutoSuicideUseDetail { get; set; }
        List<string> RoundTypeStats { get; set; }
        bool AutoSuicideEnabled { get; set; }
        string apikey { get; set; }
        ThemeType Theme { get; set; }
        string LogFilePath { get; set; }
        string WebSocketIp { get; set; }
        bool AutoLaunchEnabled { get; set; }
        string AutoLaunchExecutablePath { get; set; }
        string AutoLaunchArguments { get; set; }
        bool ItemMusicEnabled { get; set; }
        string ItemMusicItemName { get; set; }
        string ItemMusicSoundPath { get; set; }
        double ItemMusicMinSpeed { get; set; }
        double ItemMusicMaxSpeed { get; set; }
        string DiscordWebhookUrl { get; set; }
        string LastSaveCode { get; set; }
        void Load();
        Task SaveAsync();
    }
}
