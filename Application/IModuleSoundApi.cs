using System;
using System.Collections.Generic;

namespace ToNRoundCounter.Application.Services
{
    /// <summary>
    /// Stable, module-facing facade for playing audio through the application's sound stack.
    /// Modules should depend on this interface instead of <c>ISoundManager</c> directly,
    /// so future internal refactors do not break third-party modules.
    /// </summary>
    public interface IModuleSoundApi
    {
        /// <summary>
        /// Plays a single local file or YouTube URL.
        /// Volume is multiplied by the global master volume and respects mute state.
        /// </summary>
        /// <param name="pathOrUrl">Absolute/relative file path, or a youtube.com / youtu.be URL.</param>
        /// <param name="volume">Caller volume in the range [0.0, 1.0]. Default: 1.0.</param>
        /// <param name="loop">When true, repeats indefinitely. Default: false.</param>
        /// <returns>An <see cref="IDisposable"/> that stops playback when disposed.</returns>
        IDisposable Play(string pathOrUrl, double volume = 1.0, bool loop = false);

        /// <summary>
        /// Plays a sequence of local files / YouTube URLs as a playlist.
        /// </summary>
        /// <param name="pathsOrUrls">Tracks to play in order.</param>
        /// <param name="volume">Caller volume in the range [0.0, 1.0]. Default: 1.0.</param>
        /// <param name="loop">When true, cycles through the list indefinitely.</param>
        /// <returns>An <see cref="IDisposable"/> that stops playback when disposed.</returns>
        IDisposable PlayPlaylist(IEnumerable<string> pathsOrUrls, double volume = 1.0, bool loop = false);

        /// <summary>
        /// Returns the current effective master volume (0.0 - 1.0). Returns 0 when master mute is on.
        /// </summary>
        double GetCurrentMasterVolume();

        /// <summary>True when the master mute toggle is enabled.</summary>
        bool IsMasterMuted { get; }
    }
}
