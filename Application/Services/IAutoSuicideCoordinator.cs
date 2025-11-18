using System;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application.Services
{
    /// <summary>
    /// Coordinates auto-suicide logic including rule management, scheduling, and execution.
    /// </summary>
    public interface IAutoSuicideCoordinator
    {
        /// <summary>
        /// Sets the callback to update overlay state.
        /// </summary>
        void SetUpdateOverlayStateCallback(Action callback);

        /// <summary>
        /// Gets whether AllRoundsMode is enabled.
        /// </summary>
        bool IsAllRoundsModeEnabled { get; }

        /// <summary>
        /// Gets whether auto-suicide is scheduled from AllRoundsMode.
        /// </summary>
        bool IsAllRoundsModeForced { get; }

        /// <summary>
        /// Initializes the coordinator and loads auto-suicide rules.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Loads auto-suicide rules from settings.
        /// </summary>
        void LoadRules();

        /// <summary>
        /// Schedules auto-suicide with the given delay.
        /// </summary>
        /// <param name="delay">Delay before execution.</param>
        /// <param name="resetStartTime">Whether to reset the start time.</param>
        /// <param name="fromAllRoundsMode">Whether this schedule is from AllRoundsMode.</param>
        /// <param name="isManualAction">Whether this is a manual action.</param>
        void Schedule(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false, bool isManualAction = false);

        /// <summary>
        /// Cancels the scheduled auto-suicide.
        /// </summary>
        /// <param name="manualOverride">Whether this is a manual cancellation.</param>
        void Cancel(bool manualOverride = false);

        /// <summary>
        /// Delays the scheduled auto-suicide by 40 seconds.
        /// </summary>
        /// <param name="manualOverride">Whether this is a manual delay.</param>
        /// <returns>The new remaining time, or null if not scheduled.</returns>
        TimeSpan? Delay(bool manualOverride = false);

        /// <summary>
        /// Performs the auto-suicide action immediately.
        /// </summary>
        void Execute();

        /// <summary>
        /// Evaluates whether auto-suicide should be scheduled based on the current round.
        /// </summary>
        /// <param name="round">The current round.</param>
        /// <returns>True if auto-suicide should be scheduled.</returns>
        bool ShouldScheduleForRound(Round round);

        /// <summary>
        /// Evaluates auto-suicide decision for the given round type and terror.
        /// </summary>
        /// <param name="roundType">The round type.</param>
        /// <param name="terrorName">The terror name (optional).</param>
        /// <param name="hasPendingDelayed">Output: whether there is a pending delayed suicide.</param>
        /// <returns>0 = no action, 1 = immediate suicide, 2 = delayed suicide.</returns>
        int EvaluateDecision(string roundType, string? terrorName, out bool hasPendingDelayed);

        /// <summary>
        /// Toggles AllRoundsMode on or off.
        /// </summary>
        void ToggleAllRoundsMode();

        /// <summary>
        /// Ensures auto-suicide is scheduled when AllRoundsMode is enabled.
        /// </summary>
        void EnsureAllRoundsModeAutoSuicide();

        /// <summary>
        /// Schedules auto-suicide with desire player check (adds 10s delay and shows confirmation).
        /// </summary>
        /// <param name="delay">Base delay before execution.</param>
        /// <param name="resetStartTime">Whether to reset the start time.</param>
        /// <param name="fromAllRoundsMode">Whether this schedule is from AllRoundsMode.</param>
        /// <param name="desirePlayerCount">Number of desire players detected.</param>
        /// <param name="onConfirm">Callback when user confirms or denies.</param>
        void ScheduleWithDesireCheck(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode, int desirePlayerCount, Action<bool> onConfirm);
    }
}
