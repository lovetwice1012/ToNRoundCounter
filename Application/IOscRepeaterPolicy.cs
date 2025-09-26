using System;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides a policy that can allow or prevent OSC repeater startup.
    /// </summary>
    public interface IOscRepeaterPolicy
    {
        /// <summary>
        /// Determines whether the OSC repeater should be started.
        /// </summary>
        /// <param name="settings">The current application settings.</param>
        /// <returns><c>true</c> to allow startup; otherwise <c>false</c>.</returns>
        bool ShouldStartOscRepeater(IAppSettings settings);
    }
}
