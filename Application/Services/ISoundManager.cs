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
    }
}
