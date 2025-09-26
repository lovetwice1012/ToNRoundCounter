using System;
using System.IO;
using System.Windows.Media;
using Serilog.Events;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private static MediaPlayer CreatePlayer(string path)
        {
            var player = new MediaPlayer();
            player.Open(new Uri(path, UriKind.Relative));
            return player;
        }

        private static void PlayFromStart(MediaPlayer player)
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        }

        private readonly MediaPlayer notifyPlayer = CreatePlayer("./audio/notify.mp3");
        private readonly MediaPlayer afkPlayer = CreatePlayer("./audio/afk70.mp3");
        private readonly MediaPlayer punishPlayer = CreatePlayer("./audio/punish_8page.mp3");
        private readonly MediaPlayer tester_roundStartAlternatePlayer = CreatePlayer("./audio/testerOnly/RoundStart/alternate.mp3");
        private readonly MediaPlayer tester_IDICIDEDKILLALLPlayer = CreatePlayer("./audio/testerOnly/RoundStart/IDICIDEDKILLALL.mp3");
        private readonly MediaPlayer tester_BATOU_01Player = CreatePlayer("./audio/testerOnly/Batou/Batou-01.mp3");
        private readonly MediaPlayer tester_BATOU_02Player = CreatePlayer("./audio/testerOnly/Batou/Batou-02.mp3");
        private readonly MediaPlayer tester_BATOU_03Player = CreatePlayer("./audio/testerOnly/Batou/Batou-03.mp3");

        private void InitializeSoundPlayers()
        {
            LogUi("Initializing media players for notifications.", LogEventLevel.Debug);
            notifyPlayer.Stop();
            afkPlayer.Stop();
            punishPlayer.Stop();
            tester_roundStartAlternatePlayer.Stop();
            tester_IDICIDEDKILLALLPlayer.Stop();
            tester_BATOU_01Player.Stop();
            tester_BATOU_02Player.Stop();
            tester_BATOU_03Player.Stop();
            StopItemMusic();
        }

        private void StartItemMusic(ItemMusicEntry? entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                LogUi("Item music playback skipped due to settings or entry state.", LogEventLevel.Debug);
                return;
            }

            EnsureItemMusicPlayer(entry);

            if (itemMusicPlayer != null)
            {
                itemMusicLoopRequested = true;
                itemMusicActive = true;
                PlayFromStart(itemMusicPlayer);
                var displayName = string.IsNullOrWhiteSpace(entry.ItemName) ? "Unknown Item" : entry.ItemName;
                LogUi($"Item music started for '{displayName}'.");
            }
        }

        private void StopItemMusic()
        {
            itemMusicLoopRequested = false;
            itemMusicActive = false;
            itemMusicPlayer?.Stop();
            LogUi("Item music playback stopped.", LogEventLevel.Debug);
        }

        private void ResetItemMusicTracking()
        {
            LogUi("Resetting item music tracking state.", LogEventLevel.Debug);
            itemMusicMatchStart = DateTime.MinValue;
            itemMusicLoopRequested = false;
            if (itemMusicActive)
            {
                StopItemMusic();
            }
        }

        private void EnsureItemMusicPlayer(ItemMusicEntry? entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                LogUi("EnsureItemMusicPlayer skipped due to configuration.", LogEventLevel.Debug);
                return;
            }

            bool needsReload = itemMusicPlayer == null ||
                               !ReferenceEquals(activeItemMusicEntry, entry) ||
                               string.IsNullOrEmpty(lastLoadedItemMusicPath) ||
                               !File.Exists(lastLoadedItemMusicPath);

            if (needsReload)
            {
                LogUi("Reloading item music player due to configuration change.", LogEventLevel.Debug);
                UpdateItemMusicPlayer(entry);
            }
        }

        private void UpdateItemMusicPlayer(ItemMusicEntry? entry = null)
        {
            try
            {
                DisposeItemMusicPlayer();

                if (!_settings.ItemMusicEnabled)
                {
                    LogUi("Item music disabled. Skipping player update.", LogEventLevel.Debug);
                    return;
                }

                activeItemMusicEntry = entry;

                if (entry == null || !entry.Enabled)
                {
                    LogUi("No active item music entry. Player not updated.", LogEventLevel.Debug);
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    LogUi("Item music entry has no configured path.", LogEventLevel.Warning);
                    return;
                }

                string fullPath = Path.GetFullPath(configuredPath);
                if (!File.Exists(fullPath))
                {
                    _logger.LogEvent("ItemMusic", $"Sound file not found: {fullPath}", LogEventLevel.Warning);
                    LogUi($"Configured item music file not found: {fullPath}.", LogEventLevel.Warning);
                    return;
                }

                itemMusicPlayer = new MediaPlayer();
                itemMusicPlayer.Open(new Uri(fullPath));
                itemMusicPlayer.MediaEnded += ItemMusicPlayer_MediaEnded;
                itemMusicPlayer.MediaFailed += ItemMusicPlayer_MediaFailed;
                lastLoadedItemMusicPath = fullPath;
                _logger.LogEvent("ItemMusic", $"Loaded sound file: {fullPath}");
                LogUi($"Item music player loaded file '{fullPath}'.", LogEventLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("ItemMusic", ex.ToString(), LogEventLevel.Error);
                LogUi($"Failed to update item music player: {ex.Message}", LogEventLevel.Error);
            }
        }

        private void DisposeItemMusicPlayer()
        {
            if (itemMusicPlayer != null)
            {
                try
                {
                    itemMusicPlayer.MediaEnded -= ItemMusicPlayer_MediaEnded;
                    itemMusicPlayer.MediaFailed -= ItemMusicPlayer_MediaFailed;
                    itemMusicPlayer.Stop();
                    itemMusicPlayer.Close();
                }
                catch
                {
                }
                finally
                {
                    itemMusicPlayer = null;
                    LogUi("Item music player disposed.", LogEventLevel.Debug);
                }
            }

            lastLoadedItemMusicPath = string.Empty;
            itemMusicLoopRequested = false;
            itemMusicActive = false;
            activeItemMusicEntry = null;
        }

        private void ItemMusicPlayer_MediaEnded(object sender, EventArgs e)
        {
            if (itemMusicLoopRequested && itemMusicPlayer != null)
            {
                LogUi("Item music track ended. Loop restart requested.", LogEventLevel.Debug);
                PlayFromStart(itemMusicPlayer);
            }
        }

        private void ItemMusicPlayer_MediaFailed(object sender, ExceptionEventArgs e)
        {
            _logger.LogEvent("ItemMusic", $"Failed to play sound: {e.ErrorException?.Message}", LogEventLevel.Error);
            LogUi($"Item music playback failure: {e.ErrorException?.Message}", LogEventLevel.Error);
            StopItemMusic();
        }
    }
}
