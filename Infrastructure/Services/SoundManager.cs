using System;
using System.IO;
using System.Windows.Media;
using Serilog.Events;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// Manages sound playback for notifications, item music, and round BGM.
    /// </summary>
    public class SoundManager : ISoundManager
    {
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private bool _disposed;

        // Fixed sound players
        private readonly MediaPlayer _notifyPlayer;
        private readonly MediaPlayer _afkPlayer;
        private readonly MediaPlayer _punishPlayer;
        private readonly MediaPlayer _testerRoundStartAlternatePlayer;
        private readonly MediaPlayer _testerIDICIDEDKILLALLPlayer;
        private readonly MediaPlayer _testerBATOU01Player;
        private readonly MediaPlayer _testerBATOU02Player;
        private readonly MediaPlayer _testerBATOU03Player;

        // Item music state
        private MediaPlayer? _itemMusicPlayer;
        private bool _itemMusicLoopRequested;
        private bool _itemMusicActive;
        private string _lastLoadedItemMusicPath = string.Empty;
        private ItemMusicEntry? _activeItemMusicEntry;

        // Round BGM state
        private MediaPlayer? _roundBgmPlayer;
        private bool _roundBgmLoopRequested;
        private bool _roundBgmActive;
        private string _lastLoadedRoundBgmPath = string.Empty;
        private RoundBgmEntry? _activeRoundBgmEntry;

        private enum SoundConflictSource
        {
            ItemMusic,
            RoundBgm
        }

        public SoundManager(IAppSettings settings, IEventLogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _notifyPlayer = CreatePlayerWithErrorHandling("./audio/notify.mp3", "Notification");
            _afkPlayer = CreatePlayerWithErrorHandling("./audio/afk70.mp3", "AFK Warning");
            _punishPlayer = CreatePlayerWithErrorHandling("./audio/punish_8page.mp3", "Punish");
            _testerRoundStartAlternatePlayer = CreatePlayerWithErrorHandling("./audio/testerOnly/RoundStart/alternate.mp3", "Tester Alternate");
            _testerIDICIDEDKILLALLPlayer = CreatePlayerWithErrorHandling("./audio/testerOnly/RoundStart/IDICIDEDKILLALL.mp3", "Tester KILLALL");
            _testerBATOU01Player = CreatePlayerWithErrorHandling("./audio/testerOnly/Batou/Batou-01.mp3", "Batou 01");
            _testerBATOU02Player = CreatePlayerWithErrorHandling("./audio/testerOnly/Batou/Batou-02.mp3", "Batou 02");
            _testerBATOU03Player = CreatePlayerWithErrorHandling("./audio/testerOnly/Batou/Batou-03.mp3", "Batou 03");
        }

        public void Initialize()
        {
            _logger.LogEvent("SoundManager", "Initializing media players for notifications.", LogEventLevel.Debug);
            _notifyPlayer.Stop();
            _afkPlayer.Stop();
            _punishPlayer.Stop();
            _testerRoundStartAlternatePlayer.Stop();
            _testerIDICIDEDKILLALLPlayer.Stop();
            _testerBATOU01Player.Stop();
            _testerBATOU02Player.Stop();
            _testerBATOU03Player.Stop();
            StopItemMusic();
            StopRoundBgm();
        }

        public void PlayNotification()
        {
            ThrowIfDisposed();
            PlayFromStart(_notifyPlayer);
            _logger.LogEvent("SoundManager", "Notification sound played.", LogEventLevel.Debug);
        }

        public void PlayAfkWarning()
        {
            ThrowIfDisposed();
            PlayFromStart(_afkPlayer);
            _logger.LogEvent("SoundManager", "AFK warning sound played.", LogEventLevel.Debug);
        }

        public void PlayPunishSound()
        {
            ThrowIfDisposed();
            PlayFromStart(_punishPlayer);
            _logger.LogEvent("SoundManager", "Punish detection sound played.", LogEventLevel.Debug);
        }

        public void StartItemMusic(ItemMusicEntry? entry)
        {
            ThrowIfDisposed();
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

            if (_itemMusicPlayer != null)
            {
                _itemMusicLoopRequested = true;
                _itemMusicActive = true;
                PlayFromStart(_itemMusicPlayer);
                var displayName = string.IsNullOrWhiteSpace(entry.ItemName) ? "Unknown Item" : entry.ItemName;
                _logger.LogEvent("SoundManager", $"Item music started for '{displayName}'.");
            }
        }

        public void StopItemMusic()
        {
            ThrowIfDisposed();
            _itemMusicLoopRequested = false;
            _itemMusicActive = false;
            _itemMusicPlayer?.Stop();
            _logger.LogEvent("SoundManager", "Item music playback stopped.", LogEventLevel.Debug);
        }

        public void ResetItemMusicTracking()
        {
            ThrowIfDisposed();
            _logger.LogEvent("SoundManager", "Resetting item music tracking state.", LogEventLevel.Debug);
            _itemMusicLoopRequested = false;
            if (_itemMusicActive)
            {
                StopItemMusic();
            }
        }

        public void StartRoundBgm(RoundBgmEntry? entry)
        {
            ThrowIfDisposed();
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

            if (_roundBgmPlayer != null)
            {
                _roundBgmLoopRequested = true;
                _roundBgmActive = true;
                PlayFromStart(_roundBgmPlayer);
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

        public void StopRoundBgm()
        {
            ThrowIfDisposed();
            _roundBgmLoopRequested = false;
            _roundBgmActive = false;
            _roundBgmPlayer?.Stop();
            _logger.LogEvent("SoundManager", "Round BGM playback stopped.", LogEventLevel.Debug);
        }

        public void ResetRoundBgmTracking()
        {
            _logger.LogEvent("SoundManager", "Resetting round BGM tracking state.", LogEventLevel.Debug);
            _roundBgmLoopRequested = false;
            if (_roundBgmActive)
            {
                StopRoundBgm();
            }
        }

        public void UpdateItemMusicPlayer(ItemMusicEntry? entry = null)
        {
            try
            {
                DisposeItemMusicPlayer();

                if (!_settings.ItemMusicEnabled)
                {
                    _logger.LogEvent("SoundManager", "Item music disabled. Skipping player update.", LogEventLevel.Debug);
                    return;
                }

                _activeItemMusicEntry = entry;

                if (entry == null || !entry.Enabled)
                {
                    _logger.LogEvent("SoundManager", "No active item music entry. Player not updated.", LogEventLevel.Debug);
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    _logger.LogEvent("SoundManager", "Item music entry has no configured path.", LogEventLevel.Warning);
                    return;
                }

                string fullPath = Path.GetFullPath(configuredPath);
                if (!File.Exists(fullPath))
                {
                    _logger.LogEvent("ItemMusic", $"Sound file not found: {fullPath}", LogEventLevel.Warning);
                    return;
                }

                _itemMusicPlayer = new MediaPlayer();
                _itemMusicPlayer.Open(new Uri(fullPath));
                _itemMusicPlayer.MediaEnded += ItemMusicPlayer_MediaEnded;
                _itemMusicPlayer.MediaFailed += ItemMusicPlayer_MediaFailed;
                _lastLoadedItemMusicPath = fullPath;
                _logger.LogEvent("ItemMusic", $"Loaded sound file: {fullPath}");
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
                DisposeRoundBgmPlayer();

                if (!_settings.RoundBgmEnabled)
                {
                    _logger.LogEvent("SoundManager", "Round BGM disabled. Skipping player update.", LogEventLevel.Debug);
                    return;
                }

                _activeRoundBgmEntry = entry;

                if (entry == null || !entry.Enabled)
                {
                    _logger.LogEvent("SoundManager", "No active round BGM entry. Player not updated.", LogEventLevel.Debug);
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    _logger.LogEvent("SoundManager", "Round BGM entry has no configured path.", LogEventLevel.Warning);
                    return;
                }

                string fullPath = Path.GetFullPath(configuredPath);
                if (!File.Exists(fullPath))
                {
                    _logger.LogEvent("RoundBgm", $"Sound file not found: {fullPath}", LogEventLevel.Warning);
                    return;
                }

                _roundBgmPlayer = new MediaPlayer();
                _roundBgmPlayer.Open(new Uri(fullPath));
                _roundBgmPlayer.MediaEnded += RoundBgmPlayer_MediaEnded;
                _roundBgmPlayer.MediaFailed += RoundBgmPlayer_MediaFailed;
                _lastLoadedRoundBgmPath = fullPath;
                _logger.LogEvent("RoundBgm", $"Loaded sound file: {fullPath}");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("RoundBgm", ex.ToString(), LogEventLevel.Error);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DisposeItemMusicPlayer();
            DisposeRoundBgmPlayer();

            _notifyPlayer?.Close();
            _afkPlayer?.Close();
            _punishPlayer?.Close();
            _testerRoundStartAlternatePlayer?.Close();
            _testerIDICIDEDKILLALLPlayer?.Close();
            _testerBATOU01Player?.Close();
            _testerBATOU02Player?.Close();
            _testerBATOU03Player?.Close();

            _disposed = true;
        }

        private static MediaPlayer CreatePlayer(string path)
        {
            var player = new MediaPlayer();
            player.Open(new Uri(path, UriKind.Relative));
            return player;
        }

        private MediaPlayer CreatePlayerWithErrorHandling(string path, string soundName)
        {
            var player = new MediaPlayer();
            player.Open(new Uri(path, UriKind.Relative));
            player.MediaFailed += (sender, e) =>
            {
                _logger.LogEvent("SoundManager", $"Failed to play {soundName} sound from {path}: {e.ErrorException?.Message}", LogEventLevel.Error);
            };
            return player;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundManager));
            }
        }

        private static void PlayFromStart(MediaPlayer player)
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        }

        private void EnsureItemMusicPlayer(ItemMusicEntry? entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                _logger.LogEvent("SoundManager", "EnsureItemMusicPlayer skipped due to configuration.", LogEventLevel.Debug);
                return;
            }

            bool needsReload = _itemMusicPlayer == null ||
                               !ReferenceEquals(_activeItemMusicEntry, entry) ||
                               string.IsNullOrEmpty(_lastLoadedItemMusicPath) ||
                               !File.Exists(_lastLoadedItemMusicPath);

            if (needsReload)
            {
                _logger.LogEvent("SoundManager", "Reloading item music player due to configuration change.", LogEventLevel.Debug);
                UpdateItemMusicPlayer(entry);
            }
        }

        private void EnsureRoundBgmPlayer(RoundBgmEntry? entry)
        {
            if (!_settings.RoundBgmEnabled || entry == null || !entry.Enabled)
            {
                _logger.LogEvent("SoundManager", "EnsureRoundBgmPlayer skipped due to configuration.", LogEventLevel.Debug);
                return;
            }

            bool needsReload = _roundBgmPlayer == null ||
                               !ReferenceEquals(_activeRoundBgmEntry, entry) ||
                               string.IsNullOrEmpty(_lastLoadedRoundBgmPath) ||
                               !File.Exists(_lastLoadedRoundBgmPath);

            if (needsReload)
            {
                _logger.LogEvent("SoundManager", "Reloading round BGM player due to configuration change.", LogEventLevel.Debug);
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
                    if (_roundBgmActive)
                    {
                        _logger.LogEvent("SoundManager", "Stopping round BGM due to item music priority preference.", LogEventLevel.Debug);
                        StopRoundBgm();
                    }

                    return true;
                }

                if (_itemMusicActive)
                {
                    _logger.LogEvent("SoundManager", "Round BGM start skipped because item music is prioritized.", LogEventLevel.Debug);
                    return false;
                }

                return true;
            }

            if (behavior == RoundBgmItemConflictBehavior.RoundBgmPriority)
            {
                if (source == SoundConflictSource.RoundBgm)
                {
                    if (_itemMusicActive)
                    {
                        _logger.LogEvent("SoundManager", "Stopping item music due to round BGM priority preference.", LogEventLevel.Debug);
                        StopItemMusic();
                    }

                    return true;
                }

                if (_roundBgmActive)
                {
                    _logger.LogEvent("SoundManager", "Item music start skipped because round BGM is prioritized.", LogEventLevel.Debug);
                    return false;
                }
            }

            return true;
        }

        private void DisposeItemMusicPlayer()
        {
            if (_itemMusicPlayer != null)
            {
                try
                {
                    _itemMusicPlayer.MediaEnded -= ItemMusicPlayer_MediaEnded;
                    _itemMusicPlayer.MediaFailed -= ItemMusicPlayer_MediaFailed;
                    _itemMusicPlayer.Stop();
                    _itemMusicPlayer.Close();
                }
                catch
                {
                    // Ignore disposal errors
                }
                finally
                {
                    _itemMusicPlayer = null;
                    _logger.LogEvent("SoundManager", "Item music player disposed.", LogEventLevel.Debug);
                }
            }

            _lastLoadedItemMusicPath = string.Empty;
            _itemMusicLoopRequested = false;
            _itemMusicActive = false;
            _activeItemMusicEntry = null;
        }

        private void DisposeRoundBgmPlayer()
        {
            if (_roundBgmPlayer != null)
            {
                try
                {
                    _roundBgmPlayer.MediaEnded -= RoundBgmPlayer_MediaEnded;
                    _roundBgmPlayer.MediaFailed -= RoundBgmPlayer_MediaFailed;
                    _roundBgmPlayer.Stop();
                    _roundBgmPlayer.Close();
                }
                catch
                {
                    // Ignore disposal errors
                }
                finally
                {
                    _roundBgmPlayer = null;
                    _logger.LogEvent("SoundManager", "Round BGM player disposed.", LogEventLevel.Debug);
                }
            }

            _lastLoadedRoundBgmPath = string.Empty;
            _roundBgmLoopRequested = false;
            _roundBgmActive = false;
            _activeRoundBgmEntry = null;
        }

        private void ItemMusicPlayer_MediaEnded(object? sender, EventArgs e)
        {
            if (_itemMusicLoopRequested && _itemMusicPlayer != null)
            {
                _logger.LogEvent("SoundManager", "Item music track ended. Loop restart requested.", LogEventLevel.Debug);
                PlayFromStart(_itemMusicPlayer);
            }
        }

        private void ItemMusicPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            _logger.LogEvent("ItemMusic", $"Failed to play sound: {e.ErrorException?.Message}", LogEventLevel.Error);
            StopItemMusic();
        }

        private void RoundBgmPlayer_MediaEnded(object? sender, EventArgs e)
        {
            if (_roundBgmLoopRequested && _roundBgmPlayer != null)
            {
                _logger.LogEvent("SoundManager", "Round BGM track ended. Loop restart requested.", LogEventLevel.Debug);
                PlayFromStart(_roundBgmPlayer);
            }
        }

        private void RoundBgmPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            _logger.LogEvent("RoundBgm", $"Failed to play sound: {e.ErrorException?.Message}", LogEventLevel.Error);
            StopRoundBgm();
        }
    }
}
