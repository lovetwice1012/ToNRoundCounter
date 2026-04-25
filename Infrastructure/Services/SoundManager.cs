using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog.Events;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// NAudio-backed sound manager with master volume and per-category mute support.
    /// </summary>
    public class SoundManager : ISoundManager
    {
        private enum SoundCategory
        {
            Notification,
            Afk,
            Punish,
            ItemMusic,
            RoundBgm
        }

        private enum SoundConflictSource
        {
            ItemMusic,
            RoundBgm
        }

        private const string NotifyPath = "./audio/notify.mp3";
        private const string AfkPath = "./audio/afk70.mp3";
        private const string PunishPath = "./audio/punish_8page.mp3";

        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly object _sync = new object();
        private readonly ConcurrentDictionary<PlaybackHandle, byte> _oneShots = new ConcurrentDictionary<PlaybackHandle, byte>();

        private PlaybackHandle? _itemMusicHandle;
        private string _lastLoadedItemMusicPath = string.Empty;
        private ItemMusicEntry? _activeItemMusicEntry;
        private bool _itemMusicActive;

        private PlaybackHandle? _roundBgmHandle;
        private string _lastLoadedRoundBgmPath = string.Empty;
        private RoundBgmEntry? _activeRoundBgmEntry;
        private bool _roundBgmActive;

        private bool _disposed;
        private readonly YoutubeAudioCache? _youtubeCache;

        public SoundManager(IAppSettings settings, IEventLogger logger)
            : this(settings, logger, null)
        {
        }

        public SoundManager(IAppSettings settings, IEventLogger logger, YoutubeAudioCache? youtubeCache)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _youtubeCache = youtubeCache;
            if (_youtubeCache != null)
            {
                _youtubeCache.CacheUpdated += OnYoutubeCacheUpdated;
            }
        }

        private void OnYoutubeCacheUpdated(object? sender, string url)
        {
            // When a previously-pending YouTube URL becomes available, re-trigger active players that referenced it.
            try
            {
                ItemMusicEntry? itemEntry;
                RoundBgmEntry? roundEntry;
                lock (_sync)
                {
                    itemEntry = _activeItemMusicEntry;
                    roundEntry = _activeRoundBgmEntry;
                }
                if (itemEntry != null && (itemEntry.SoundPath ?? string.Empty).Contains(url, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateItemMusicPlayer(itemEntry);
                }
                if (roundEntry != null && (roundEntry.SoundPath ?? string.Empty).Contains(url, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateRoundBgmPlayer(roundEntry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SoundManager", $"YT cache-updated handler failed: {ex.Message}", LogEventLevel.Warning);
            }
        }

        public void Initialize()
        {
            _logger.LogEvent("SoundManager", "Initializing NAudio playback engine.", LogEventLevel.Debug);
            StopAllOneShots();
            StopItemMusic();
            StopRoundBgm();
        }

        public void PlayNotification()
        {
            PlayOneShot(NotifyPath, SoundCategory.Notification);
            _logger.LogEvent("SoundManager", "Notification sound played.", LogEventLevel.Debug);
        }

        public void PlayAfkWarning()
        {
            PlayOneShot(AfkPath, SoundCategory.Afk);
            _logger.LogEvent("SoundManager", "AFK warning sound played.", LogEventLevel.Debug);
        }

        public void PlayPunishSound()
        {
            PlayOneShot(PunishPath, SoundCategory.Punish);
            _logger.LogEvent("SoundManager", "Punish detection sound played.", LogEventLevel.Debug);
        }

        public void StartItemMusic(ItemMusicEntry? entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                _logger.LogEvent("SoundManager", "Item music playback skipped due to settings or entry state.", LogEventLevel.Debug);
                return;
            }

            if (!HandleSoundConflictBeforePlayback(SoundConflictSource.ItemMusic))
            {
                return;
            }

            EnsureItemMusicPlayer(entry);

            lock (_sync)
            {
                if (_itemMusicHandle != null)
                {
                    _itemMusicHandle.Loop = true;
                    _itemMusicHandle.Volume = ComputeEffectiveVolume(SoundCategory.ItemMusic, entry.Volume);
                    _itemMusicActive = true;
                    _itemMusicHandle.Restart();
                    var displayName = string.IsNullOrWhiteSpace(entry.ItemName) ? "Unknown Item" : entry.ItemName;
                    _logger.LogEvent("SoundManager", $"Item music started for '{displayName}'.");
                }
            }
        }

        public void StopItemMusic()
        {
            lock (_sync)
            {
                _itemMusicActive = false;
                _itemMusicHandle?.Stop();
            }
            _logger.LogEvent("SoundManager", "Item music playback stopped.", LogEventLevel.Debug);
        }

        public void ResetItemMusicTracking()
        {
            _logger.LogEvent("SoundManager", "Resetting item music tracking state.", LogEventLevel.Debug);
            if (_itemMusicActive)
            {
                StopItemMusic();
            }
        }

        public void StartRoundBgm(RoundBgmEntry? entry)
        {
            if (!_settings.RoundBgmEnabled || entry == null || !entry.Enabled)
            {
                _logger.LogEvent("SoundManager", "Round BGM playback skipped due to settings or entry state.", LogEventLevel.Debug);
                return;
            }

            if (!HandleSoundConflictBeforePlayback(SoundConflictSource.RoundBgm))
            {
                return;
            }

            EnsureRoundBgmPlayer(entry);

            lock (_sync)
            {
                if (_roundBgmHandle != null)
                {
                    _roundBgmHandle.Loop = true;
                    _roundBgmHandle.Volume = ComputeEffectiveVolume(SoundCategory.RoundBgm, entry.Volume);
                    _roundBgmActive = true;
                    _roundBgmHandle.Restart();
                    string roundName = entry.RoundType ?? string.Empty;
                    string terrorName = entry.TerrorType ?? string.Empty;
                    string displayName;
                    if (!string.IsNullOrWhiteSpace(roundName) && !string.IsNullOrWhiteSpace(terrorName))
                    {
                        displayName = $"{roundName} / {terrorName}";
                    }
                    else if (!string.IsNullOrWhiteSpace(roundName))
                    {
                        displayName = roundName;
                    }
                    else if (!string.IsNullOrWhiteSpace(terrorName))
                    {
                        displayName = terrorName;
                    }
                    else
                    {
                        displayName = "Default";
                    }

                    _logger.LogEvent("SoundManager", $"Round BGM started for '{displayName}'.");
                }
            }
        }

        public void StopRoundBgm()
        {
            lock (_sync)
            {
                _roundBgmActive = false;
                _roundBgmHandle?.Stop();
            }
            _logger.LogEvent("SoundManager", "Round BGM playback stopped.", LogEventLevel.Debug);
        }

        public void ResetRoundBgmTracking()
        {
            _logger.LogEvent("SoundManager", "Resetting round BGM tracking state.", LogEventLevel.Debug);
            if (_roundBgmActive)
            {
                StopRoundBgm();
            }
        }

        public void UpdateItemMusicPlayer(ItemMusicEntry? entry = null)
        {
            try
            {
                if (!_settings.ItemMusicEnabled)
                {
                    DisposeItemMusicHandle();
                    _logger.LogEvent("SoundManager", "Item music disabled. Skipping player update.", LogEventLevel.Debug);
                    return;
                }

                if (entry == null || !entry.Enabled)
                {
                    DisposeItemMusicHandle();
                    _activeItemMusicEntry = entry;
                    _logger.LogEvent("SoundManager", "No active item music entry. Player not updated.", LogEventLevel.Debug);
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    DisposeItemMusicHandle();
                    _activeItemMusicEntry = entry;
                    _logger.LogEvent("SoundManager", "Item music entry has no configured path.", LogEventLevel.Warning);
                    return;
                }

                IReadOnlyList<string> playlist = ResolvePlaylist(configuredPath, "ItemMusic");
                if (playlist.Count == 0)
                {
                    DisposeItemMusicHandle();
                    return;
                }
                string fullPath = playlist[0];

                if (_itemMusicHandle != null
                    && ReferenceEquals(_activeItemMusicEntry, entry)
                    && string.Equals(_lastLoadedItemMusicPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _itemMusicHandle.Volume = ComputeEffectiveVolume(SoundCategory.ItemMusic, entry.Volume);
                    return;
                }

                DisposeItemMusicHandle();
                _activeItemMusicEntry = entry;

                _itemMusicHandle = new PlaybackHandle(playlist, true, (float)ComputeEffectiveVolume(SoundCategory.ItemMusic, entry.Volume), _logger, GetDeviceNumber());
                _itemMusicHandle.ApplyEqualizer(GetEqualizerGains(), _settings.EqualizerEnabled);
                _itemMusicHandle.Restart();
                _lastLoadedItemMusicPath = fullPath;
                _logger.LogEvent("ItemMusic", playlist.Count > 1
                    ? $"Loaded playlist ({playlist.Count} items), starting with: {fullPath}"
                    : $"Loaded sound file: {fullPath}");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("ItemMusic", ex.ToString(), LogEventLevel.Error);
            }
        }

        public void UpdateRoundBgmPlayer(RoundBgmEntry? entry = null)
        {
            try
            {
                if (!_settings.RoundBgmEnabled)
                {
                    DisposeRoundBgmHandle();
                    _logger.LogEvent("SoundManager", "Round BGM disabled. Skipping player update.", LogEventLevel.Debug);
                    return;
                }

                if (entry == null || !entry.Enabled)
                {
                    DisposeRoundBgmHandle();
                    _activeRoundBgmEntry = entry;
                    _logger.LogEvent("SoundManager", "No active round BGM entry. Player not updated.", LogEventLevel.Debug);
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    DisposeRoundBgmHandle();
                    _activeRoundBgmEntry = entry;
                    _logger.LogEvent("SoundManager", "Round BGM entry has no configured path.", LogEventLevel.Warning);
                    return;
                }

                IReadOnlyList<string> playlist = ResolvePlaylist(configuredPath, "RoundBgm");
                if (playlist.Count == 0)
                {
                    DisposeRoundBgmHandle();
                    return;
                }
                string fullPath = playlist[0];

                if (_roundBgmHandle != null
                    && ReferenceEquals(_activeRoundBgmEntry, entry)
                    && string.Equals(_lastLoadedRoundBgmPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _roundBgmHandle.Volume = ComputeEffectiveVolume(SoundCategory.RoundBgm, entry.Volume);
                    return;
                }

                DisposeRoundBgmHandle();
                _activeRoundBgmEntry = entry;

                _roundBgmHandle = new PlaybackHandle(playlist, true, (float)ComputeEffectiveVolume(SoundCategory.RoundBgm, entry.Volume), _logger, GetDeviceNumber());
                _roundBgmHandle.ApplyEqualizer(GetEqualizerGains(), _settings.EqualizerEnabled);
                _roundBgmHandle.Restart();
                _lastLoadedRoundBgmPath = fullPath;
                _logger.LogEvent("RoundBgm", playlist.Count > 1
                    ? $"Loaded playlist ({playlist.Count} items), starting with: {fullPath}"
                    : $"Loaded sound file: {fullPath}");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("RoundBgm", ex.ToString(), LogEventLevel.Error);
            }
        }

        public void ApplyNotificationVolumes()
        {
            lock (_sync)
            {
                if (_itemMusicHandle != null && _activeItemMusicEntry != null)
                {
                    _itemMusicHandle.Volume = ComputeEffectiveVolume(SoundCategory.ItemMusic, _activeItemMusicEntry.Volume);
                }
                if (_roundBgmHandle != null && _activeRoundBgmEntry != null)
                {
                    _roundBgmHandle.Volume = ComputeEffectiveVolume(SoundCategory.RoundBgm, _activeRoundBgmEntry.Volume);
                }
            }
        }

        public void ApplyEqualizer()
        {
            float[] gains = GetEqualizerGains();
            bool enabled = _settings.EqualizerEnabled;
            lock (_sync)
            {
                _itemMusicHandle?.ApplyEqualizer(gains, enabled);
                _roundBgmHandle?.ApplyEqualizer(gains, enabled);
            }
        }

        private float[] GetEqualizerGains()
        {
            float[] gains = new float[EqualizerSampleProvider.BandCount];
            double[]? src = _settings.EqualizerBandGains;
            if (src != null)
            {
                int n = Math.Min(gains.Length, src.Length);
                for (int i = 0; i < n; i++) gains[i] = (float)src[i];
            }
            return gains;
        }

        public IDisposable PlayTestSound(string path, double categoryVolume, double entryVolume, bool loop = false)
        {
            var noop = new TestPlaybackToken(null);
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    _logger.LogEvent("SoundManager", $"Test sound file not found: {path}", LogEventLevel.Warning);
                    return noop;
                }

                if (_settings.MasterMuted)
                {
                    return noop;
                }

                double effective = ClampVolume(_settings.MasterVolume) * ClampVolume(categoryVolume) * ClampVolume(entryVolume);
                if (effective <= 0)
                {
                    return noop;
                }

                var handle = new PlaybackHandle(path, loop, (float)effective, _logger, GetDeviceNumber());
                handle.ApplyEqualizer(GetEqualizerGains(), _settings.EqualizerEnabled);
                var token = new TestPlaybackToken(handle);
                handle.PlaybackEnded += (_, _) => token.Dispose();
                handle.Restart();
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SoundManager", $"Test playback failed for {path}: {ex.Message}", LogEventLevel.Error);
                return noop;
            }
        }

        public IDisposable PlayCustomSound(IReadOnlyList<string> pathsOrUrls, double volume = 1.0, bool loop = false)
        {
            var noop = new TestPlaybackToken(null);
            try
            {
                if (pathsOrUrls == null || pathsOrUrls.Count == 0)
                {
                    return noop;
                }

                if (_settings.MasterMuted)
                {
                    return noop;
                }

                double effective = ClampVolume(_settings.MasterVolume) * ClampVolume(volume);
                if (effective <= 0)
                {
                    return noop;
                }

                // Use ResolvePlaylist to handle local paths and YouTube URLs uniformly.
                string raw = string.Join("|", pathsOrUrls);
                IReadOnlyList<string> resolved = ResolvePlaylist(raw, "ModuleSoundApi");
                if (resolved.Count == 0)
                {
                    _logger.LogEvent("SoundManager", "PlayCustomSound resolved zero playable tracks (downloads may be pending).", LogEventLevel.Information);
                    return noop;
                }

                var handle = new PlaybackHandle(resolved, loop, (float)effective, _logger, GetDeviceNumber());
                handle.ApplyEqualizer(GetEqualizerGains(), _settings.EqualizerEnabled);
                var token = new TestPlaybackToken(handle);
                handle.PlaybackEnded += (_, _) => token.Dispose();
                handle.Restart();
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SoundManager", $"PlayCustomSound failed: {ex.Message}", LogEventLevel.Error);
                return noop;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAllOneShots();
            DisposeItemMusicHandle();
            DisposeRoundBgmHandle();
        }

        private void PlayOneShot(string path, SoundCategory category)
        {
            try
            {
                if (!File.Exists(path))
                {
                    _logger.LogEvent("SoundManager", $"Sound file not found: {path}", LogEventLevel.Warning);
                    return;
                }

                double volume = ComputeEffectiveVolume(category, 1.0);
                if (volume <= 0)
                {
                    return;
                }

                var handle = new PlaybackHandle(path, false, (float)volume, _logger, GetDeviceNumber());
                handle.ApplyEqualizer(GetEqualizerGains(), _settings.EqualizerEnabled);
                _oneShots[handle] = 0;
                handle.PlaybackEnded += (_, _) =>
                {
                    _oneShots.TryRemove(handle, out _);
                    handle.Dispose();
                };
                handle.Restart();
            }
            catch (Exception ex)
            {
                _logger.LogEvent("SoundManager", $"Failed to play one-shot {path}: {ex.Message}", LogEventLevel.Error);
            }
        }

        private void StopAllOneShots()
        {
            foreach (var kvp in _oneShots)
            {
                try { kvp.Key.Stop(); kvp.Key.Dispose(); }
                catch { /* ignore */ }
            }
            _oneShots.Clear();
        }

        private void DisposeItemMusicHandle()
        {
            lock (_sync)
            {
                if (_itemMusicHandle != null)
                {
                    try { _itemMusicHandle.Stop(); _itemMusicHandle.Dispose(); }
                    catch { /* ignore */ }
                    _itemMusicHandle = null;
                }
                _lastLoadedItemMusicPath = string.Empty;
                _activeItemMusicEntry = null;
                _itemMusicActive = false;
            }
        }

        private void DisposeRoundBgmHandle()
        {
            lock (_sync)
            {
                if (_roundBgmHandle != null)
                {
                    try { _roundBgmHandle.Stop(); _roundBgmHandle.Dispose(); }
                    catch { /* ignore */ }
                    _roundBgmHandle = null;
                }
                _lastLoadedRoundBgmPath = string.Empty;
                _activeRoundBgmEntry = null;
                _roundBgmActive = false;
            }
        }

        private void EnsureItemMusicPlayer(ItemMusicEntry? entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled) return;

            bool needsReload = _itemMusicHandle == null
                || !ReferenceEquals(_activeItemMusicEntry, entry)
                || string.IsNullOrEmpty(_lastLoadedItemMusicPath)
                || !File.Exists(_lastLoadedItemMusicPath);

            if (needsReload)
            {
                UpdateItemMusicPlayer(entry);
            }
        }

        private void EnsureRoundBgmPlayer(RoundBgmEntry? entry)
        {
            if (!_settings.RoundBgmEnabled || entry == null || !entry.Enabled) return;

            bool needsReload = _roundBgmHandle == null
                || !ReferenceEquals(_activeRoundBgmEntry, entry)
                || string.IsNullOrEmpty(_lastLoadedRoundBgmPath)
                || !File.Exists(_lastLoadedRoundBgmPath);

            if (needsReload)
            {
                UpdateRoundBgmPlayer(entry);
            }
        }

        private bool HandleSoundConflictBeforePlayback(SoundConflictSource source)
        {
            var behavior = _settings.RoundBgmItemConflictBehavior;

            if (behavior == RoundBgmItemConflictBehavior.PlayBoth)
            {
                return true;
            }

            if (behavior == RoundBgmItemConflictBehavior.ItemMusicPriority)
            {
                if (source == SoundConflictSource.ItemMusic)
                {
                    if (_roundBgmActive) StopRoundBgm();
                    return true;
                }
                if (_itemMusicActive) return false;
                return true;
            }

            if (behavior == RoundBgmItemConflictBehavior.RoundBgmPriority)
            {
                if (source == SoundConflictSource.RoundBgm)
                {
                    if (_itemMusicActive) StopItemMusic();
                    return true;
                }
                if (_roundBgmActive) return false;
            }

            return true;
        }

        private double ComputeEffectiveVolume(SoundCategory category, double entryVolume)
        {
            if (_settings.MasterMuted) return 0;
            if (IsCategoryMuted(category)) return 0;

            double master = ClampVolume(_settings.MasterVolume);
            double categoryVolume = GetCategoryVolume(category);
            double per = ClampVolume(entryVolume);
            return master * categoryVolume * per;
        }

        private bool IsCategoryMuted(SoundCategory category) => category switch
        {
            SoundCategory.Notification => _settings.NotificationSoundMuted,
            SoundCategory.Afk => _settings.AfkSoundMuted,
            SoundCategory.Punish => _settings.PunishSoundMuted,
            SoundCategory.ItemMusic => _settings.ItemMusicMuted,
            SoundCategory.RoundBgm => _settings.RoundBgmMuted,
            _ => false,
        };

        private double GetCategoryVolume(SoundCategory category) => category switch
        {
            SoundCategory.Notification => ClampVolume(_settings.NotificationSoundVolume),
            SoundCategory.Afk => ClampVolume(_settings.AfkSoundVolume),
            SoundCategory.Punish => ClampVolume(_settings.PunishSoundVolume),
            SoundCategory.ItemMusic => 1.0,
            SoundCategory.RoundBgm => 1.0,
            _ => 1.0,
        };

        private static double ClampVolume(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 1.0;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        private int GetDeviceNumber()
        {
            int n = _settings.AudioOutputDeviceNumber;
            // -1 = WAVE_MAPPER (system default). Validate against current device count.
            if (n < 0) return -1;
            try
            {
                if (n >= WaveOut.DeviceCount) return -1;
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Parses a SoundPath string into a list of fully-qualified, existing file paths.
        /// Supports playlist syntax via the '|' separator.
        /// Missing files are skipped with a warning. Returns an empty list when nothing is playable.
        /// </summary>
        private IReadOnlyList<string> ResolvePlaylist(string raw, string category)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            string[] parts = raw.Split('|');
            foreach (string part in parts)
            {
                string trimmed = part?.Trim() ?? string.Empty;
                if (trimmed.Length == 0) continue;

                if (YoutubeAudioCache.IsYoutubeUrl(trimmed))
                {
                    if (_youtubeCache == null)
                    {
                        _logger.LogEvent(category, $"YouTube playback requested but cache service is not available: {trimmed}", LogEventLevel.Warning);
                        continue;
                    }
                    if (_youtubeCache.TryGetCachedPath(trimmed, out string cached))
                    {
                        result.Add(cached);
                    }
                    else
                    {
                        _logger.LogEvent(category, $"YouTube URL pending download, will play when ready: {trimmed}", LogEventLevel.Information);
                    }
                    continue;
                }

                string full;
                try { full = Path.GetFullPath(trimmed); }
                catch (Exception ex)
                {
                    _logger.LogEvent(category, $"Invalid playlist path '{trimmed}': {ex.Message}", LogEventLevel.Warning);
                    continue;
                }
                if (!File.Exists(full))
                {
                    _logger.LogEvent(category, $"Playlist file not found: {full}", LogEventLevel.Warning);
                    continue;
                }
                result.Add(full);
            }
            return result;
        }

        /// <summary>
        /// Disposable wrapper handed to callers of <see cref="PlayTestSound"/>.
        /// </summary>
        private sealed class TestPlaybackToken : IDisposable
        {
            private PlaybackHandle? _handle;
            private bool _disposed;

            public TestPlaybackToken(PlaybackHandle? handle)
            {
                _handle = handle;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _handle?.Stop(); _handle?.Dispose(); } catch { /* ignore */ }
                _handle = null;
            }
        }

        /// <summary>
        /// Single NAudio playback session: AudioFileReader -> VolumeSampleProvider -> WaveOutEvent.
        /// Supports looping by rewinding the reader on PlaybackStopped.
        /// </summary>
        private sealed class PlaybackHandle : IDisposable
        {
            private readonly IReadOnlyList<string> _paths;
            private int _currentIndex;
            private readonly IEventLogger _logger;
            private readonly int _deviceNumber;
            private WaveStream? _reader;
            private ISampleProvider? _readerSampleProvider;
            private WaveOutEvent? _output;
            private VolumeSampleProvider? _volumeProvider;
            private EqualizerSampleProvider? _equalizer;
            private float[] _eqGains = new float[EqualizerSampleProvider.BandCount];
            private bool _eqEnabled;
            private float _volume;
            private bool _loop;
            private bool _disposed;
            private bool _stopRequested;

            public event EventHandler<EventArgs>? PlaybackEnded;

            public PlaybackHandle(string path, bool loop, float volume, IEventLogger logger, int deviceNumber = -1)
                : this(new[] { path }, loop, volume, logger, deviceNumber)
            {
            }

            public PlaybackHandle(IReadOnlyList<string> paths, bool loop, float volume, IEventLogger logger, int deviceNumber = -1)
            {
                _paths = paths != null && paths.Count > 0 ? paths : new[] { string.Empty };
                _loop = loop;
                _volume = volume;
                _logger = logger;
                _deviceNumber = deviceNumber;
            }

            private string CurrentPath => _paths[_currentIndex];

            public bool Loop
            {
                get => _loop;
                set => _loop = value;
            }

            public double Volume
            {
                get => _volume;
                set
                {
                    _volume = (float)Math.Max(0, Math.Min(1, value));
                    if (_volumeProvider != null)
                    {
                        _volumeProvider.Volume = _volume;
                    }
                }
            }

            public void ApplyEqualizer(float[] gainsDb, bool enabled)
            {
                _eqGains = (float[])(gainsDb ?? new float[EqualizerSampleProvider.BandCount]).Clone();
                _eqEnabled = enabled;
                _equalizer?.UpdateGains(_eqGains, _eqEnabled);
            }

            public void Restart()
            {
                if (_disposed) return;
                _stopRequested = false;
                try
                {
                    DisposePlayback();
                    _reader = CreateReaderFor(CurrentPath, out _readerSampleProvider);
                    ISampleProvider sampleProvider = _readerSampleProvider!;
                    _equalizer = new EqualizerSampleProvider(sampleProvider, _eqGains, _eqEnabled);
                    _volumeProvider = new VolumeSampleProvider(_equalizer) { Volume = _volume };
                    _output = new WaveOutEvent { DeviceNumber = _deviceNumber };
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Init(_volumeProvider);
                    _output.Play();
                    _logger.LogEvent("SoundManager", $"Playback started: path={CurrentPath} fmt={sampleProvider.WaveFormat} vol={_volume:F2} dev={_deviceNumber}", LogEventLevel.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("SoundManager", $"Failed to start playback for {CurrentPath}: {ex.Message}", LogEventLevel.Error);
                    DisposePlayback();
                }
            }

            public void Stop()
            {
                _stopRequested = true;
                try { _output?.Stop(); }
                catch { /* ignore */ }
            }

            private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
            {
                if (e.Exception != null)
                {
                    _logger.LogEvent("SoundManager", $"PlaybackStopped with error for {CurrentPath}: {e.Exception.Message}", LogEventLevel.Warning);
                }
                if (_stopRequested || _disposed)
                {
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (_loop)
                {
                    try
                    {
                        // Playlist cycle: advance to next path.
                        if (_paths.Count > 1)
                        {
                            _currentIndex = (_currentIndex + 1) % _paths.Count;
                        }
                        // Always recreate the reader/output to support all formats reliably,
                        // including MediaFoundationReader which does not always honor Position=0 + Play().
                        Restart();
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("SoundManager", $"Loop restart failed: {ex.Message}", LogEventLevel.Warning);
                    }
                }
                else if (_paths.Count > 1)
                {
                    // Non-loop playlist: advance until end, then signal completion.
                    if (_currentIndex + 1 < _paths.Count)
                    {
                        _currentIndex++;
                        Restart();
                        return;
                    }
                }

                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }

            private void DisposePlayback()
            {
                try
                {
                    if (_output != null)
                    {
                        _output.PlaybackStopped -= OnPlaybackStopped;
                        try { _output.Stop(); } catch { /* ignore */ }
                        _output.Dispose();
                        _output = null;
                    }
                }
                catch { /* ignore */ }

                try
                {
                    _reader?.Dispose();
                    _reader = null;
                    _readerSampleProvider = null;
                }
                catch { /* ignore */ }

                _volumeProvider = null;
            }

            private static WaveStream CreateReaderFor(string path, out ISampleProvider sampleProvider)
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                // AudioFileReader supports wav/aiff/mp3 natively and exposes ISampleProvider.
                if (ext == ".wav" || ext == ".aiff" || ext == ".aif" || ext == ".mp3")
                {
                    var afr = new AudioFileReader(path);
                    sampleProvider = afr.ToSampleProvider();
                    return afr;
                }
                // For m4a/aac/webm/ogg/flac/wma etc., delegate to MediaFoundation (Windows codecs).
                var mf = new MediaFoundationReader(path);
                sampleProvider = mf.ToSampleProvider();
                return mf;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _stopRequested = true;
                DisposePlayback();
            }
        }
    }
}
