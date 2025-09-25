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

        private void StartItemMusic(ItemMusicEntry entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                return;
            }

            EnsureItemMusicPlayer(entry);

            if (itemMusicPlayer != null)
            {
                itemMusicLoopRequested = true;
                itemMusicActive = true;
                PlayFromStart(itemMusicPlayer);
            }
        }

        private void StopItemMusic()
        {
            itemMusicLoopRequested = false;
            itemMusicActive = false;
            itemMusicPlayer?.Stop();
        }

        private void ResetItemMusicTracking()
        {
            itemMusicMatchStart = DateTime.MinValue;
            itemMusicLoopRequested = false;
            if (itemMusicActive)
            {
                StopItemMusic();
            }
        }

        private void EnsureItemMusicPlayer(ItemMusicEntry entry)
        {
            if (!_settings.ItemMusicEnabled || entry == null || !entry.Enabled)
            {
                return;
            }

            bool needsReload = itemMusicPlayer == null ||
                               !ReferenceEquals(activeItemMusicEntry, entry) ||
                               string.IsNullOrEmpty(lastLoadedItemMusicPath) ||
                               !File.Exists(lastLoadedItemMusicPath);

            if (needsReload)
            {
                UpdateItemMusicPlayer(entry);
            }
        }

        private void UpdateItemMusicPlayer(ItemMusicEntry entry = null)
        {
            try
            {
                DisposeItemMusicPlayer();

                if (!_settings.ItemMusicEnabled)
                {
                    return;
                }

                activeItemMusicEntry = entry;

                if (entry == null || !entry.Enabled)
                {
                    return;
                }

                string configuredPath = entry.SoundPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    return;
                }

                string fullPath = Path.GetFullPath(configuredPath);
                if (!File.Exists(fullPath))
                {
                    _logger.LogEvent("ItemMusic", $"Sound file not found: {fullPath}", LogEventLevel.Warning);
                    return;
                }

                itemMusicPlayer = new MediaPlayer();
                itemMusicPlayer.Open(new Uri(fullPath));
                itemMusicPlayer.MediaEnded += ItemMusicPlayer_MediaEnded;
                itemMusicPlayer.MediaFailed += ItemMusicPlayer_MediaFailed;
                lastLoadedItemMusicPath = fullPath;
                _logger.LogEvent("ItemMusic", $"Loaded sound file: {fullPath}");
            }
            catch (Exception ex)
            {
                _logger.LogEvent("ItemMusic", ex.ToString(), LogEventLevel.Error);
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
                PlayFromStart(itemMusicPlayer);
            }
        }

        private void ItemMusicPlayer_MediaFailed(object sender, ExceptionEventArgs e)
        {
            _logger.LogEvent("ItemMusic", $"Failed to play sound: {e.ErrorException?.Message}", LogEventLevel.Error);
            StopItemMusic();
        }
    }
}
