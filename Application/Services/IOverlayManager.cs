using System;
using System.Collections.Generic;
using System.Drawing;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application.Services
{
    /// <summary>
    /// Manages overlay windows that display game information on top of VRChat.
    /// </summary>
    public interface IOverlayManager : IDisposable
    {
        /// <summary>
        /// Initializes all overlay forms and starts visibility monitoring.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Updates the velocity overlay with current player speed and AFK time.
        /// </summary>
        void UpdateVelocity(double velocity, double afkSeconds);

        /// <summary>
        /// Updates the terror overlay with current terror information.
        /// </summary>
        void UpdateTerror(string terrorText, string? terrorInfoText = null);

        /// <summary>
        /// Updates the damage overlay.
        /// </summary>
        void UpdateDamage(string damageText);

        /// <summary>
        /// Updates the next round prediction overlay.
        /// </summary>
        void UpdateNextRound(string nextRoundText, bool hasPrediction);

        /// <summary>
        /// Updates the round status overlay.
        /// </summary>
        void UpdateRoundStatus(string statusText);

        /// <summary>
        /// Records a round in the history overlay.
        /// </summary>
        void RecordRoundHistory(string roundType, string status);

        /// <summary>
        /// Refreshes the round statistics overlay with current data.
        /// </summary>
        void RefreshRoundStats();

        /// <summary>
        /// Updates the clock overlay.
        /// </summary>
        void UpdateClock();

        /// <summary>
        /// Updates the instance timer overlay.
        /// </summary>
        void UpdateInstanceTimer(string instanceId, DateTimeOffset enteredAt);

        /// <summary>
        /// Updates the instance members overlay.
        /// </summary>
        void UpdateInstanceMembers(IReadOnlyList<string> members);

        /// <summary>
        /// Captures current overlay positions and sizes to settings.
        /// </summary>
        void CapturePositions();

        /// <summary>
        /// Applies the overlay background opacity from settings.
        /// </summary>
        void ApplyBackgroundOpacity();

        /// <summary>
        /// Applies round history length settings.
        /// </summary>
        void ApplyRoundHistorySettings();

        /// <summary>
        /// Temporarily hides all overlays until the round ends.
        /// </summary>
        void SetTemporarilyHidden(bool hidden);

        /// <summary>
        /// Updates the state of shortcut overlay buttons.
        /// </summary>
        void UpdateShortcutOverlayState(
            bool autoSuicideEnabled,
            bool allRoundsModeEnabled,
            bool coordinatedBrainEnabled,
            bool afkDetectionEnabled,
            bool overlayTemporarilyHidden,
            bool autoSuicideScheduled);

        /// <summary>
        /// Resets round-scoped shortcut buttons.
        /// </summary>
        void ResetRoundScopedShortcutButtons();

        /// <summary>
        /// Event raised when a shortcut button is clicked.
        /// </summary>
        event EventHandler<ShortcutButtonClickedEventArgs>? ShortcutButtonClicked;
    }

    /// <summary>
    /// Event arguments for shortcut button clicks.
    /// </summary>
    public class ShortcutButtonClickedEventArgs : EventArgs
    {
        public ShortcutButton Button { get; }

        public ShortcutButtonClickedEventArgs(ShortcutButton button)
        {
            Button = button;
        }
    }

    /// <summary>
    /// Available shortcut buttons.
    /// </summary>
    public enum ShortcutButton
    {
        AutoSuicideToggle,
        AutoSuicideCancel,
        AutoSuicideDelay,
        ManualSuicide,
        AllRoundsModeToggle,
        CoordinatedBrainToggle,
        AfkDetectionToggle,
        HideUntilRoundEnd
    }
}
