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
        bool OverlayShowVelocity { get; set; }
        bool OverlayShowTerror { get; set; }
        bool OverlayShowDamage { get; set; }
        bool OverlayShowNextRound { get; set; }
        bool OverlayShowRoundStatus { get; set; }
        bool OverlayShowRoundHistory { get; set; }
        bool OverlayShowRoundStats { get; set; }
        bool OverlayShowTerrorInfo { get; set; }
        bool OverlayShowShortcuts { get; set; }
        bool OverlayShowAngle { get; set; }
        bool OverlayShowClock { get; set; }
        bool OverlayShowInstanceTimer { get; set; }
        bool OverlayShowUnboundTerrorDetails { get; set; }
        double OverlayOpacity { get; set; }
        int OverlayRoundHistoryLength { get; set; }
        Dictionary<string, Point> OverlayPositions { get; set; }
        Dictionary<string, float> OverlayScaleFactors { get; set; }
        Dictionary<string, Size> OverlaySizes { get; set; }
        List<string> AutoSuicideRoundTypes { get; set; }
        Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; }
        List<string> AutoSuicideDetailCustom { get; set; }
        bool AutoSuicideFuzzyMatch { get; set; }
        bool AutoSuicideUseDetail { get; set; }
        List<string> RoundTypeStats { get; set; }
        bool AutoSuicideEnabled { get; set; }
        string apikey { get; set; }
        string ThemeKey { get; set; }
        string LogFilePath { get; set; }
        string WebSocketIp { get; set; }
        bool AutoLaunchEnabled { get; set; }
        List<AutoLaunchEntry> AutoLaunchEntries { get; set; }
        string AutoLaunchExecutablePath { get; set; }
        string AutoLaunchArguments { get; set; }
        bool ItemMusicEnabled { get; set; }
        List<ItemMusicEntry> ItemMusicEntries { get; set; }
        bool RoundBgmEnabled { get; set; }
        List<RoundBgmEntry> RoundBgmEntries { get; set; }
        RoundBgmItemConflictBehavior RoundBgmItemConflictBehavior { get; set; }
        string ItemMusicItemName { get; set; }
        string ItemMusicSoundPath { get; set; }
        double ItemMusicMinSpeed { get; set; }
        double ItemMusicMaxSpeed { get; set; }
        string DiscordWebhookUrl { get; set; }
        string LastSaveCode { get; set; }
        bool AfkSoundCancelEnabled { get; set; }
        bool CoordinatedAutoSuicideBrainEnabled { get; set; }
        void Load();
        Task SaveAsync();
    }
}
