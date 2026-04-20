// Auto-generated legacy shim file. Provides compile-time shims for fields and methods
// that were extracted into AutoSuicideCoordinator / SoundManager / OverlayManager
// services but are still referenced by older call sites in MainForm partial files.
// These shims primarily delegate to the new services to keep behavior consistent.
using System;
using System.Threading.Tasks;
using System.Windows.Media;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        // Auto-suicide all-rounds mode flag (delegates to coordinator).
        private bool issetAllSelfKillMode
        {
            get => _autoSuicideCoordinator.IsAllRoundsModeEnabled;
            set
            {
                if (value != _autoSuicideCoordinator.IsAllRoundsModeEnabled)
                {
                    _autoSuicideCoordinator.ToggleAllRoundsMode();
                }
            }
        }

        // Whether the all-rounds-mode forced schedule is active.
        private bool allRoundsForcedSchedule => _autoSuicideCoordinator.IsAllRoundsModeForced;

        // Loads/refreshes the auto-suicide rules.
        private void LoadAutoSuicideRules() => _autoSuicideCoordinator.LoadRules();

        // Cancels any pending auto-suicide.
        private void CancelAutoSuicide(bool manualOverride = false)
            => _autoSuicideCoordinator.Cancel(manualOverride);

        // Schedules an auto-suicide after the given delay.
        private void ScheduleAutoSuicide(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false)
            => _autoSuicideCoordinator.Schedule(delay, resetStartTime, fromAllRoundsMode);

        // Performs the auto-suicide action immediately and returns a completed task
        // so callers using Func<Task> can await without modification.
        private Task PerformAutoSuicide()
        {
            _autoSuicideCoordinator.Execute();
            return Task.CompletedTask;
        }

        // Evaluates whether an auto-suicide should occur for the given round/terror.
        // Returns the same integer code that AutoSuicideCoordinator.EvaluateDecision returns.
        private int ShouldAutoSuicide(string roundType, string? terrorName)
            => _autoSuicideCoordinator.EvaluateDecision(roundType, terrorName, out _);

        // Overload kept for legacy call sites that also need delayed-schedule state.
        private int ShouldAutoSuicide(string roundType, string? terrorName, out bool hasPendingDelayed)
            => _autoSuicideCoordinator.EvaluateDecision(roundType, terrorName, out hasPendingDelayed);

        // Delays the currently scheduled auto-suicide and returns the new remaining time.
        private TimeSpan? DelayAutoSuicide(bool manualOverride = false)
            => _autoSuicideCoordinator.Delay(manualOverride);

        // Test sound players were removed; provide null shims so call sites compile.
        private MediaPlayer? tester_roundStartAlternatePlayer => null;
        private MediaPlayer? tester_BATOU_01Player => null;
        private MediaPlayer? tester_BATOU_02Player => null;
        private MediaPlayer? tester_BATOU_03Player => null;
        private MediaPlayer? tester_IDICIDEDKILLALLPlayer => null;

        // Compatibility shim for direct PlayFromStart calls; the actual playback
        // logic lives in SoundManager. With null players this becomes a no-op.
        private static void PlayFromStart(MediaPlayer? player)
        {
            if (player == null)
            {
                return;
            }

            try
            {
                player.Stop();
                player.Position = TimeSpan.Zero;
                player.Play();
            }
            catch
            {
                // Best-effort playback for legacy code paths; ignore failures.
            }
        }

        // ------------------------------------------------------------------
        // Round BGM / Item Music selection state shims.
        // The selection/tracking logic in MainForm.cs still maintains these
        // local fields. SoundManager keeps its own copies for actual playback.
        // ------------------------------------------------------------------
        private string? roundBgmSelectionRoundType;
        private string? roundBgmSelectionTerrorType;
        private RoundBgmEntry? activeRoundBgmEntry = null;
        private ItemMusicEntry? activeItemMusicEntry = null;
        private readonly Random roundBgmRandom = new Random();

        private void EnsureItemMusicPlayer(ItemMusicEntry? entry) => _soundManager.UpdateItemMusicPlayer(entry);
        private void EnsureRoundBgmPlayer(RoundBgmEntry? entry) => _soundManager.UpdateRoundBgmPlayer(entry);
        private void ResetRoundBgmTracking() => _soundManager.ResetRoundBgmTracking();

        // Some overlay code expects an UpdateInstanceTimerOverlay method on MainForm.
        private void UpdateInstanceTimerOverlayShim()
        {
            // Implementation lives in MainForm.Overlay.cs (UpdateInstanceTimerOverlay),
            // this partial declaration intentionally has no body to avoid duplicates.
        }
    }
}
