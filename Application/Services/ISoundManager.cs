using System;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application.Services
{
    /// <summary>
    /// Manages sound playback for notifications, item music, and round BGM.
    /// </summary>
    public interface ISoundManager : IDisposable
    {
        /// <summary>
        /// Initializes all media players and stops any currently playing sounds.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Plays the notification sound.
        /// </summary>
        void PlayNotification();

        /// <summary>
        /// Plays the AFK warning sound.
        /// </summary>
        void PlayAfkWarning();

        /// <summary>
        /// Plays the punish detection sound.
        /// </summary>
        void PlayPunishSound();

        /// <summary>
        /// Starts playing item music for the specified entry.
        /// </summary>
        void StartItemMusic(ItemMusicEntry? entry);

        /// <summary>
        /// Stops item music playback.
        /// </summary>
        void StopItemMusic();

        /// <summary>
        /// Resets item music tracking state.
        /// </summary>
        void ResetItemMusicTracking();

        /// <summary>
        /// Starts playing round BGM for the specified entry.
        /// </summary>
        void StartRoundBgm(RoundBgmEntry? entry);

        /// <summary>
        /// Stops round BGM playback.
        /// </summary>
        void StopRoundBgm();

        /// <summary>
        /// Resets round BGM tracking state.
        /// </summary>
        void ResetRoundBgmTracking();

        /// <summary>
        /// Updates the item music player configuration.
        /// </summary>
        void UpdateItemMusicPlayer(ItemMusicEntry? entry = null);

        /// <summary>
        /// Updates the round BGM player configuration.
        /// </summary>
        void UpdateRoundBgmPlayer(RoundBgmEntry? entry = null);

        /// <summary>
        /// Re-applies global notification sound volumes from settings.
        /// </summary>
        void ApplyNotificationVolumes();

        /// <summary>
        /// Re-applies the equalizer settings to all currently active playbacks.
        /// </summary>
        void ApplyEqualizer();

        /// <summary>
        /// Plays a one-shot test sound from the given file path using the supplied category-equivalent volume settings.
        /// </summary>
        /// <param name="path">Absolute or relative file path of the audio to test.</param>
        /// <param name="categoryVolume">Category-level volume (0-1).</param>
        /// <param name="entryVolume">Per-entry volume (0-1). Use 1.0 if not applicable.</param>
        /// <param name="loop">Whether to loop the playback (false for one-shot tests).</param>
        /// <returns>An IDisposable handle. Dispose to stop playback.</returns>
        IDisposable PlayTestSound(string path, double categoryVolume, double entryVolume, bool loop = false);

        /// <summary>
        /// Plays a custom sound or playlist on behalf of an external caller (e.g. a module).
        /// Supports local file paths and YouTube URLs (resolved via the configured cache).
        /// Volume is multiplied by the global master volume and respects the master mute toggle.
        /// </summary>
        /// <param name="pathsOrUrls">One or more local file paths and/or YouTube URLs to play sequentially.</param>
        /// <param name="volume">Caller volume in the range [0.0, 1.0].</param>
        /// <param name="loop">When true, cycles through the list indefinitely.</param>
        /// <returns>An IDisposable handle. Dispose to stop playback.</returns>
        IDisposable PlayCustomSound(System.Collections.Generic.IReadOnlyList<string> pathsOrUrls, double volume = 1.0, bool loop = false);
    }
}
