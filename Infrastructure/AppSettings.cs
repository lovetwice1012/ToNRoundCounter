using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using ToNRoundCounter.UI;
using Newtonsoft.Json.Linq;

namespace ToNRoundCounter.Infrastructure
{
    public class AppSettings : IAppSettings
    {
        private readonly string settingsFile = "appsettings.json";
        private readonly IEventLogger _logger;
        private readonly IEventBus _bus;
        private readonly ISettingsRepository? _settingsRepository;

        public AppSettings(IEventLogger logger, IEventBus bus, ISettingsRepository? settingsRepository = null)
        {
            _logger = logger;
            _bus = bus;
            _settingsRepository = settingsRepository;
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
        public bool OverlayShowVelocity { get; set; } = true;
        public bool OverlayShowTerror { get; set; } = true;
        public bool OverlayShowDamage { get; set; } = true;
        public bool OverlayShowNextRound { get; set; } = true;
        public bool OverlayShowRoundStatus { get; set; } = true;
        public bool OverlayShowRoundHistory { get; set; } = true;
        public bool OverlayShowRoundStats { get; set; } = true;
        public bool OverlayShowTerrorInfo { get; set; } = true;
        public bool OverlayShowShortcuts { get; set; } = true;
        public bool OverlayShowAngle { get; set; } = true;
        public bool OverlayShowClock { get; set; } = true;
        public bool OverlayShowInstanceTimer { get; set; } = true;
        public bool OverlayShowUnboundTerrorDetails { get; set; } = true;
        public double OverlayOpacity { get; set; } = 0.95d;
        public int OverlayRoundHistoryLength { get; set; } = 3;
        public Dictionary<string, Point> OverlayPositions { get; set; } = new Dictionary<string, Point>();
        public Dictionary<string, float> OverlayScaleFactors { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, Size> OverlaySizes { get; set; } = new Dictionary<string, Size>();
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
        public string ThemeKey { get; set; } = Theme.DefaultThemeKey;
        public string Language { get; set; } = LanguageManager.DefaultCulture;
        public string LogFilePath { get; set; } = "logs/log-.txt";
        public string WebSocketIp { get; set; } = "127.0.0.1";
        public string CloudWebSocketUrl { get; set; } = string.Empty;
        public bool CloudSyncEnabled { get; set; }
        public string CloudPlayerName { get; set; } = string.Empty;
        public bool AutoLaunchEnabled { get; set; }
        public List<AutoLaunchEntry> AutoLaunchEntries { get; set; } = new List<AutoLaunchEntry>();
        // Legacy properties retained for migration of single-entry settings
        public string AutoLaunchExecutablePath { get; set; } = string.Empty;
        public string AutoLaunchArguments { get; set; } = string.Empty;
        public bool ItemMusicEnabled { get; set; }
        public List<ItemMusicEntry> ItemMusicEntries { get; set; } = new List<ItemMusicEntry>();
        public bool RoundBgmEnabled { get; set; }
        public List<RoundBgmEntry> RoundBgmEntries { get; set; } = new List<RoundBgmEntry>();
        public RoundBgmItemConflictBehavior RoundBgmItemConflictBehavior { get; set; } = RoundBgmItemConflictBehavior.PlayBoth;
        public string ItemMusicItemName { get; set; } = string.Empty;
        public string ItemMusicSoundPath { get; set; } = string.Empty;
        public double ItemMusicMinSpeed { get; set; }
        public double ItemMusicMaxSpeed { get; set; }
        public bool AutoRecordingEnabled { get; set; }
        public string AutoRecordingWindowTitle { get; set; } = "VRChat";
        public string AutoRecordingCommand { get; set; } = string.Empty;
        public int AutoRecordingFrameRate { get; set; } = 30;
        public string AutoRecordingResolution { get; set; } = AutoRecordingService.DefaultResolutionOptionId;
        public string AutoRecordingArguments { get; set; } = string.Empty;
        public string AutoRecordingOutputDirectory { get; set; } = "recordings";
        public string AutoRecordingOutputExtension { get; set; } = "avi";
        public string AutoRecordingVideoCodec { get; set; } = AutoRecordingService.DefaultCodec;
        public int AutoRecordingVideoBitrate { get; set; }
        public int AutoRecordingAudioBitrate { get; set; }
        public string AutoRecordingHardwareEncoder { get; set; } = AutoRecordingService.DefaultHardwareEncoderOptionId;
        public bool AutoRecordingIncludeOverlay { get; set; }
        public List<string> AutoRecordingRoundTypes { get; set; } = new List<string>();
        public List<string> AutoRecordingTerrors { get; set; } = new List<string>();
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string LastSaveCode { get; set; } = string.Empty;
        public bool AfkSoundCancelEnabled { get; set; } = true;
        public bool CoordinatedAutoSuicideBrainEnabled { get; set; } = true;
        public bool NetworkAnalyzerConsentGranted { get; set; }
        public DateTimeOffset? NetworkAnalyzerConsentTimestamp { get; set; }
        public string? NetworkAnalyzerConsentMarkerId { get; set; }
        public int NetworkAnalyzerProxyPort { get; set; } = 8890;

        public void Load()
        {
            _logger.LogEvent("AppSettings", $"Loading settings from {Path.GetFullPath(settingsFile)}.");
            _bus.Publish(new SettingsLoading(this));
            bool success = false;
            var validationErrors = new List<string>();
            string? json = null;
            bool loadedFromRepository = false;

            try
            {
                if (File.Exists(settingsFile))
                {
                    _logger.LogEvent("AppSettings", "Settings file found. Reading contents.");
                    json = File.ReadAllText(settingsFile);
                }
                else
                {
                    _logger.LogEvent("AppSettings", "Settings file not found. Attempting to load from SQLite snapshot.", Serilog.Events.LogEventLevel.Warning);
                    if (_settingsRepository != null)
                    {
                        json = _settingsRepository.LoadLatest();
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            loadedFromRepository = true;
                            _logger.LogEvent("AppSettings", "Settings snapshot loaded from SQLite repository.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogEvent("AppSettings", "No persisted settings snapshot found. Using defaults.", Serilog.Events.LogEventLevel.Warning);
                    }
                }

                if (!string.IsNullOrWhiteSpace(json))
                {
                    string? jsonContent = json;

                    if (jsonContent != null)
                    {
                        try
                        {
                            var token = JObject.Parse(jsonContent);
                            if (token.TryGetValue("ThemeKey", out var themeKeyToken))
                            {
                                ThemeKey = NormalizeThemeKey(themeKeyToken?.Value<string>());
                            }
                            else if (token.TryGetValue("Theme", out var legacyThemeToken))
                            {
                                ThemeKey = NormalizeLegacyTheme(legacyThemeToken);
                            }
                        }
                        catch (JsonException)
                        {
                            // Ignore malformed theme information and fall back to defaults.
                        }

                        JsonConvert.PopulateObject(jsonContent, this);

                        if (loadedFromRepository && !File.Exists(settingsFile))
                        {
                            TryRegenerateSettingsFile(jsonContent);
                        }
                        else if (!loadedFromRepository)
                        {
                            PersistSettingsSnapshot(jsonContent);
                        }
                    }
                }

                ThemeKey = NormalizeThemeKey(ThemeKey);
                Language = LanguageManager.NormalizeCulture(Language);
                LanguageManager.SetLanguage(Language);
                _logger.LogEvent("AppSettings", $"Theme normalized to '{ThemeKey}'.");
                _bus.Publish(new SettingsValidating(this, validationErrors));
                var errors = Validate();
                validationErrors.AddRange(errors);
                if (validationErrors.Count > 0)
                {
                    foreach (var err in validationErrors)
                    {
                        _logger.LogEvent("SettingsValidation", err, Serilog.Events.LogEventLevel.Error);
                    }
                    _bus.Publish(new SettingsValidationFailed(this, validationErrors));
                }
                else
                {
                    _logger.LogEvent("AppSettings", "Settings loaded successfully.");
                    _bus.Publish(new SettingsValidated(this));
                }

                OverlayPositions ??= new Dictionary<string, Point>();
                OverlayScaleFactors ??= new Dictionary<string, float>();
                OverlaySizes ??= new Dictionary<string, Size>();
                OverlayOpacity = NormalizeOverlayOpacity(OverlayOpacity);
                if (OverlayRoundHistoryLength <= 0)
                {
                    OverlayRoundHistoryLength = 3;
                }
                AutoLaunchExecutablePath ??= string.Empty;
                AutoLaunchArguments ??= string.Empty;
                ItemMusicItemName ??= string.Empty;
                ItemMusicSoundPath ??= string.Empty;
                DiscordWebhookUrl ??= string.Empty;
                LastSaveCode ??= string.Empty;
                NormalizeAutoLaunchEntries();
                NormalizeItemMusicEntries();
                NormalizeRoundBgmEntries();
                NormalizeRoundBgmPreferences();
                NormalizeAutoRecordingSettings();
                _logger.LogEvent("AppSettings", "Normalization of complex settings completed.");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("Error", "Failed to bind app settings: " + ex.Message, Serilog.Events.LogEventLevel.Error);
                validationErrors.Add(ex.Message);
                _bus.Publish(new SettingsValidationFailed(this, validationErrors));
            }
            finally
            {
                if (success)
                {
                    _bus.Publish(new SettingsLoaded(this));
                }
            }
        }

        private void PersistSettingsSnapshot(string? json)
        {
            if (_settingsRepository == null || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                _settingsRepository.SaveSnapshot(json!, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("AppSettings", $"Failed to persist settings snapshot: {ex.Message}", Serilog.Events.LogEventLevel.Warning);
            }
        }

        private void TryRegenerateSettingsFile(string? json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                File.WriteAllText(settingsFile, json);
                _logger.LogEvent("AppSettings", "Settings file regenerated from SQLite snapshot.");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("AppSettings", $"Failed to regenerate settings file from SQLite snapshot: {ex.Message}", Serilog.Events.LogEventLevel.Warning);
            }
        }

        private double NormalizeOverlayOpacity(double opacity)
        {
            if (opacity <= 0d)
            {
                return 0.95d;
            }

            if (opacity < 0.2d)
            {
                return 0.2d;
            }

            if (opacity > 1d)
            {
                return 1d;
            }

            return opacity;
        }

        private void NormalizeAutoLaunchEntries()
        {
            var entries = AutoLaunchEntries ?? new List<AutoLaunchEntry>();
            var normalizedEntries = new List<AutoLaunchEntry>(entries.Count + 1);
            var deduplicationMap = new Dictionary<AutoLaunchKey, AutoLaunchEntry>();

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var normalizedPath = (entry.ExecutablePath ?? string.Empty).Trim();
                var normalizedArguments = (entry.Arguments ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var key = new AutoLaunchKey(normalizedPath, normalizedArguments);
                if (deduplicationMap.TryGetValue(key, out var existing))
                {
                    existing.Enabled |= entry.Enabled;
                    continue;
                }

                var normalizedEntry = new AutoLaunchEntry
                {
                    Enabled = entry.Enabled,
                    ExecutablePath = normalizedPath,
                    Arguments = normalizedArguments
                };

                deduplicationMap[key] = normalizedEntry;
                normalizedEntries.Add(normalizedEntry);
            }

            if (!string.IsNullOrWhiteSpace(AutoLaunchExecutablePath))
            {
                var fallbackPath = AutoLaunchExecutablePath.Trim();
                var fallbackArguments = (AutoLaunchArguments ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(fallbackPath))
                {
                    var fallbackKey = new AutoLaunchKey(fallbackPath, fallbackArguments);
                    if (deduplicationMap.TryGetValue(fallbackKey, out var existing))
                    {
                        existing.Enabled |= AutoLaunchEnabled;
                    }
                    else
                    {
                        var fallbackEntry = new AutoLaunchEntry
                        {
                            Enabled = AutoLaunchEnabled,
                            ExecutablePath = fallbackPath,
                            Arguments = fallbackArguments
                        };

                        deduplicationMap[fallbackKey] = fallbackEntry;
                        normalizedEntries.Add(fallbackEntry);
                    }
                }

                AutoLaunchExecutablePath = string.Empty;
                AutoLaunchArguments = string.Empty;
            }

            AutoLaunchEntries = normalizedEntries;
        }

        private struct AutoLaunchKey : IEquatable<AutoLaunchKey>
        {
            public AutoLaunchKey(string path, string arguments)
            {
                Path = path;
                Arguments = arguments;
            }

            public string Path { get; }

            public string Arguments { get; }

            public bool Equals(AutoLaunchKey other)
            {
                return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Arguments, other.Arguments, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is AutoLaunchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
                    hash = hash * 23 + StringComparer.Ordinal.GetHashCode(Arguments);
                    return hash;
                }
            }
        }

        private void NormalizeItemMusicEntries()
        {
            ItemMusicEntries ??= new List<ItemMusicEntry>();

            foreach (var entry in ItemMusicEntries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.ItemName ??= string.Empty;
                entry.SoundPath ??= string.Empty;
                NormalizeItemMusicSpeeds(entry);
            }

            if (!string.IsNullOrWhiteSpace(ItemMusicItemName) || !string.IsNullOrWhiteSpace(ItemMusicSoundPath))
            {
                var legacyEntry = new ItemMusicEntry
                {
                    Enabled = ItemMusicEnabled,
                    ItemName = ItemMusicItemName ?? string.Empty,
                    SoundPath = ItemMusicSoundPath ?? string.Empty,
                    MinSpeed = ItemMusicMinSpeed,
                    MaxSpeed = ItemMusicMaxSpeed
                };

                NormalizeItemMusicSpeeds(legacyEntry);
                ItemMusicEntries.Add(legacyEntry);

                ItemMusicItemName = string.Empty;
                ItemMusicSoundPath = string.Empty;
                ItemMusicMinSpeed = 0;
                ItemMusicMaxSpeed = 0;
            }
        }

        private void NormalizeRoundBgmEntries()
        {
            RoundBgmEntries ??= new List<RoundBgmEntry>();

            foreach (var entry in RoundBgmEntries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.RoundType ??= string.Empty;
                entry.TerrorType ??= string.Empty;
                entry.SoundPath ??= string.Empty;
            }
        }

        private void NormalizeRoundBgmPreferences()
        {
            if (!Enum.IsDefined(typeof(RoundBgmItemConflictBehavior), RoundBgmItemConflictBehavior))
            {
                RoundBgmItemConflictBehavior = RoundBgmItemConflictBehavior.PlayBoth;
            }
        }

        private void NormalizeAutoRecordingSettings()
        {
            AutoRecordingWindowTitle = string.IsNullOrWhiteSpace(AutoRecordingWindowTitle)
                ? "VRChat"
                : AutoRecordingWindowTitle.Trim();
            AutoRecordingCommand = string.IsNullOrWhiteSpace(AutoRecordingCommand)
                ? string.Empty
                : AutoRecordingCommand.Trim();
            AutoRecordingFrameRate = NormalizeRecordingFrameRate(AutoRecordingFrameRate);
            AutoRecordingResolution = AutoRecordingService.NormalizeResolutionOption(AutoRecordingResolution);
            AutoRecordingArguments = string.IsNullOrWhiteSpace(AutoRecordingArguments)
                ? string.Empty
                : AutoRecordingArguments.Trim();
            AutoRecordingOutputDirectory = string.IsNullOrWhiteSpace(AutoRecordingOutputDirectory)
                ? "recordings"
                : AutoRecordingOutputDirectory.Trim();
            AutoRecordingOutputExtension = NormalizeRecordingExtension(AutoRecordingOutputExtension);
            AutoRecordingVideoCodec = AutoRecordingService.NormalizeCodec(AutoRecordingOutputExtension, AutoRecordingVideoCodec);
            AutoRecordingVideoBitrate = AutoRecordingService.NormalizeVideoBitrate(AutoRecordingVideoBitrate);
            AutoRecordingAudioBitrate = AutoRecordingService.NormalizeAudioBitrate(AutoRecordingAudioBitrate);
            AutoRecordingHardwareEncoder = AutoRecordingService.NormalizeHardwareOption(AutoRecordingHardwareEncoder);
            AutoRecordingRoundTypes = NormalizeStringList(AutoRecordingRoundTypes);
            AutoRecordingTerrors = NormalizeStringList(AutoRecordingTerrors);
        }

        private static string NormalizeRecordingExtension(string? extension)
        {
            var trimmed = string.IsNullOrWhiteSpace(extension)
                ? AutoRecordingService.SupportedExtensions[0]
                : extension!.Trim().TrimStart('.');

            foreach (var candidate in AutoRecordingService.SupportedExtensions)
            {
                if (trimmed.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return AutoRecordingService.SupportedExtensions[0];
        }

        private static int NormalizeRecordingFrameRate(int frameRate)
        {
            if (frameRate < 5)
            {
                return 5;
            }

            if (frameRate > 240)
            {
                return 240;
            }

            return frameRate;
        }

        private static List<string> NormalizeStringList(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Select(v => (v ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void NormalizeItemMusicSpeeds(ItemMusicEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (double.IsNaN(entry.MinSpeed) || double.IsInfinity(entry.MinSpeed) || entry.MinSpeed < 0)
            {
                entry.MinSpeed = 0;
            }

            if (double.IsNaN(entry.MaxSpeed) || double.IsInfinity(entry.MaxSpeed) || entry.MaxSpeed < entry.MinSpeed)
            {
                entry.MaxSpeed = entry.MinSpeed;
            }
        }

        private static string NormalizeThemeKey(string? key)
        {
            return Theme.EnsureTheme(string.IsNullOrWhiteSpace(key) ? Theme.DefaultThemeKey : key!).Key;
        }

        private static string NormalizeLegacyTheme(JToken? token)
        {
            if (token == null)
            {
                return Theme.DefaultThemeKey;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() == 1 ? "dark" : Theme.DefaultThemeKey;
            }

            var value = token.Value<string>() ?? string.Empty;

            if (value.Equals("dark", StringComparison.OrdinalIgnoreCase))
            {
                return "dark";
            }

            if (value.Equals("light", StringComparison.OrdinalIgnoreCase))
            {
                return Theme.DefaultThemeKey;
            }

            return NormalizeThemeKey(value);
        }

        private List<string> Validate()
        {
            var errors = new List<string>();
            if (OSCPort <= 0 || OSCPort > 65535)
            {
                errors.Add("Invalid OSCPort value.");
            }
            if (NetworkAnalyzerProxyPort < 1025 || NetworkAnalyzerProxyPort > 65535)
            {
                errors.Add("Invalid NetworkAnalyzerProxyPort value.");
            }
            return errors;
        }

        public async Task SaveAsync()
        {
            _logger.LogEvent("AppSettings", $"Saving settings to {Path.GetFullPath(settingsFile)}.");
            _bus.Publish(new SettingsSaving(this));
            NormalizeAutoLaunchEntries();
            NormalizeItemMusicEntries();
            NormalizeRoundBgmEntries();
            NormalizeRoundBgmPreferences();
            NormalizeAutoRecordingSettings();
            OverlayOpacity = NormalizeOverlayOpacity(OverlayOpacity);
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
                OverlayShowVelocity = OverlayShowVelocity,
                OverlayShowTerror = OverlayShowTerror,
                OverlayShowDamage = OverlayShowDamage,
                OverlayShowNextRound = OverlayShowNextRound,
                OverlayShowRoundStatus = OverlayShowRoundStatus,
                OverlayShowRoundHistory = OverlayShowRoundHistory,
                OverlayShowRoundStats = OverlayShowRoundStats,
                OverlayShowTerrorInfo = OverlayShowTerrorInfo,
                OverlayShowShortcuts = OverlayShowShortcuts,
                OverlayShowAngle = OverlayShowAngle,
                OverlayShowClock = OverlayShowClock,
                OverlayShowInstanceTimer = OverlayShowInstanceTimer,
                OverlayOpacity = OverlayOpacity,
                OverlayRoundHistoryLength = OverlayRoundHistoryLength,
                OverlayPositions = OverlayPositions,
                OverlayScaleFactors = OverlayScaleFactors,
                OverlaySizes = OverlaySizes,
                RoundTypeStats = RoundTypeStats,
                AutoSuicideEnabled = AutoSuicideEnabled,
                AutoSuicideRoundTypes = AutoSuicideRoundTypes,
                AutoSuicidePresets = AutoSuicidePresets,
                AutoSuicideDetailCustom = AutoSuicideDetailCustom,
                AutoSuicideFuzzyMatch = AutoSuicideFuzzyMatch,
                AutoSuicideUseDetail = AutoSuicideUseDetail,
                apikey = apikey,
                ThemeKey = NormalizeThemeKey(ThemeKey),
                Language = LanguageManager.NormalizeCulture(Language),
                LogFilePath = LogFilePath,
                WebSocketIp = WebSocketIp,
                AutoLaunchEnabled = AutoLaunchEnabled,
                AutoLaunchEntries = AutoLaunchEntries,
                ItemMusicEnabled = ItemMusicEnabled,
                ItemMusicEntries = ItemMusicEntries,
                RoundBgmEnabled = RoundBgmEnabled,
                RoundBgmEntries = RoundBgmEntries,
                RoundBgmItemConflictBehavior = RoundBgmItemConflictBehavior,
                AutoRecordingEnabled = AutoRecordingEnabled,
                AutoRecordingWindowTitle = AutoRecordingWindowTitle,
                AutoRecordingCommand = AutoRecordingCommand,
                AutoRecordingFrameRate = AutoRecordingFrameRate,
                AutoRecordingResolution = AutoRecordingResolution,
                AutoRecordingArguments = AutoRecordingArguments,
                AutoRecordingOutputDirectory = AutoRecordingOutputDirectory,
                AutoRecordingOutputExtension = AutoRecordingOutputExtension,
                AutoRecordingVideoCodec = AutoRecordingVideoCodec,
                AutoRecordingVideoBitrate = AutoRecordingVideoBitrate,
                AutoRecordingAudioBitrate = AutoRecordingAudioBitrate,
                AutoRecordingHardwareEncoder = AutoRecordingHardwareEncoder,
                AutoRecordingIncludeOverlay = AutoRecordingIncludeOverlay,
                AutoRecordingRoundTypes = AutoRecordingRoundTypes,
                AutoRecordingTerrors = AutoRecordingTerrors,
                DiscordWebhookUrl = DiscordWebhookUrl,
                LastSaveCode = LastSaveCode,
                AfkSoundCancelEnabled = AfkSoundCancelEnabled,
                CoordinatedAutoSuicideBrainEnabled = CoordinatedAutoSuicideBrainEnabled,
                NetworkAnalyzerConsentGranted = NetworkAnalyzerConsentGranted,
                NetworkAnalyzerConsentTimestamp = NetworkAnalyzerConsentTimestamp,
                NetworkAnalyzerConsentMarkerId = NetworkAnalyzerConsentMarkerId,
                NetworkAnalyzerProxyPort = NetworkAnalyzerProxyPort
            };

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            bool success = false;
            try
            {
                _logger.LogEvent("AppSettings", "Writing serialized settings to disk.");
                using (var writer = new StreamWriter(settingsFile, false))
                {
                    await writer.WriteAsync(json).ConfigureAwait(false);
                }
                PersistSettingsSnapshot(json);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("Error", "Failed to save app settings: " + ex.Message, Serilog.Events.LogEventLevel.Error);
                throw;
            }
            finally
            {
                if (success)
                {
                    _logger.LogEvent("AppSettings", "Settings saved successfully.");
                    _bus.Publish(new SettingsSaved(this));
                }
            }
        }
    }

    public class AppSettingsData
    {
        public int OSCPort { get; set; }
        public bool OSCPortChanged { get; set; }
        public string BackgroundColor_InfoPanel { get; set; } = string.Empty;
        public string BackgroundColor_Stats { get; set; } = string.Empty;
        public string BackgroundColor_Log { get; set; } = string.Empty;
        public string FixedTerrorColor { get; set; } = string.Empty;
        public bool ShowStats { get; set; }
        public bool ShowDebug { get; set; }
        public bool ShowRoundLog { get; set; }
        public bool Filter_RoundType { get; set; }
        public bool Filter_Terror { get; set; }
        public bool Filter_Appearance { get; set; }
        public bool Filter_Survival { get; set; }
        public bool Filter_Death { get; set; }
        public bool Filter_SurvivalRate { get; set; }
        public bool OverlayShowVelocity { get; set; }
        public bool OverlayShowTerror { get; set; }
        public bool OverlayShowDamage { get; set; }
        public bool OverlayShowNextRound { get; set; }
        public bool OverlayShowRoundStatus { get; set; }
        public bool OverlayShowRoundHistory { get; set; }
        public bool OverlayShowRoundStats { get; set; }
        public bool OverlayShowTerrorInfo { get; set; }
        public bool OverlayShowShortcuts { get; set; }
        public bool OverlayShowAngle { get; set; }
        public bool OverlayShowClock { get; set; }
        public bool OverlayShowInstanceTimer { get; set; }
        public double OverlayOpacity { get; set; }
        public int OverlayRoundHistoryLength { get; set; }
        public Dictionary<string, Point> OverlayPositions { get; set; } = new Dictionary<string, Point>();
        public Dictionary<string, float> OverlayScaleFactors { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, Size> OverlaySizes { get; set; } = new Dictionary<string, Size>();
        public List<string> RoundTypeStats { get; set; } = new List<string>();
        public bool AutoSuicideEnabled { get; set; }
        public List<string> AutoSuicideRoundTypes { get; set; } = new List<string>();
        public Dictionary<string, AutoSuicidePreset> AutoSuicidePresets { get; set; } = new Dictionary<string, AutoSuicidePreset>();
        public List<string> AutoSuicideDetailCustom { get; set; } = new List<string>();
        public bool AutoSuicideFuzzyMatch { get; set; }
        public bool AutoSuicideUseDetail { get; set; }
        public string apikey { get; set; } = string.Empty;
        public string ThemeKey { get; set; } = Theme.DefaultThemeKey;
        public string Language { get; set; } = LanguageManager.DefaultCulture;
        public string LogFilePath { get; set; } = string.Empty;
        public string WebSocketIp { get; set; } = string.Empty;
        public string CloudWebSocketUrl { get; set; } = string.Empty;
        public bool CloudSyncEnabled { get; set; }
        public string CloudPlayerName { get; set; } = string.Empty;
        public bool AutoLaunchEnabled { get; set; }
        public List<AutoLaunchEntry> AutoLaunchEntries { get; set; } = new List<AutoLaunchEntry>();
        public bool ItemMusicEnabled { get; set; }
        public List<ItemMusicEntry> ItemMusicEntries { get; set; } = new List<ItemMusicEntry>();
        public bool RoundBgmEnabled { get; set; }
        public List<RoundBgmEntry> RoundBgmEntries { get; set; } = new List<RoundBgmEntry>();
        public RoundBgmItemConflictBehavior RoundBgmItemConflictBehavior { get; set; } = RoundBgmItemConflictBehavior.PlayBoth;
        public bool AutoRecordingEnabled { get; set; }
        public string AutoRecordingWindowTitle { get; set; } = "VRChat";
        public string AutoRecordingCommand { get; set; } = string.Empty;
        public int AutoRecordingFrameRate { get; set; } = 30;
        public string AutoRecordingResolution { get; set; } = AutoRecordingService.DefaultResolutionOptionId;
        public string AutoRecordingArguments { get; set; } = string.Empty;
        public string AutoRecordingOutputDirectory { get; set; } = "recordings";
        public string AutoRecordingOutputExtension { get; set; } = "avi";
        public string AutoRecordingVideoCodec { get; set; } = AutoRecordingService.DefaultCodec;
        public int AutoRecordingVideoBitrate { get; set; }
        public int AutoRecordingAudioBitrate { get; set; }
        public string AutoRecordingHardwareEncoder { get; set; } = AutoRecordingService.DefaultHardwareEncoderOptionId;
        public bool AutoRecordingIncludeOverlay { get; set; }
        public List<string> AutoRecordingRoundTypes { get; set; } = new List<string>();
        public List<string> AutoRecordingTerrors { get; set; } = new List<string>();
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string LastSaveCode { get; set; } = string.Empty;
        public bool AfkSoundCancelEnabled { get; set; } = true;
        public bool CoordinatedAutoSuicideBrainEnabled { get; set; } = true;
        public bool NetworkAnalyzerConsentGranted { get; set; }
        public DateTimeOffset? NetworkAnalyzerConsentTimestamp { get; set; }
        public string? NetworkAnalyzerConsentMarkerId { get; set; }
        public int NetworkAnalyzerProxyPort { get; set; }
    }
}
