using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Events;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// Coordinates auto-suicide logic including rule management, scheduling, and execution.
    /// </summary>
    public class AutoSuicideCoordinator : IAutoSuicideCoordinator
    {
        private readonly AutoSuicideService _autoSuicideService;
        private readonly IInputSender _inputSender;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly IOverlayManager _overlayManager;
        private readonly ModuleHost _moduleHost;
        private Action? _updateOverlayStateCallback;

        private List<AutoSuicideRule> _rules = new List<AutoSuicideRule>();
        private bool _isAllRoundsModeEnabled;
        private bool _isAllRoundsModeForced;
        private bool _manualCancelRequested;
        private DateTime? _manualDelayUntil;

        private static readonly string[] AllRoundTypes = new string[]
        {
            "クラシック",
            "霧",
            "パニッシュ",
            "サボタージュ",
            "クラックド",
            "ラン",
            "ブラッドバス",
            "8ページ"
        };

        public bool IsAllRoundsModeEnabled => _isAllRoundsModeEnabled;
        public bool IsAllRoundsModeForced => _isAllRoundsModeForced;

        public AutoSuicideCoordinator(
            AutoSuicideService autoSuicideService,
            IInputSender inputSender,
            IAppSettings settings,
            IEventLogger logger,
            IOverlayManager overlayManager,
            ModuleHost moduleHost)
        {
            _autoSuicideService = autoSuicideService ?? throw new ArgumentNullException(nameof(autoSuicideService));
            _inputSender = inputSender ?? throw new ArgumentNullException(nameof(inputSender));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
            _moduleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
        }

        public void SetUpdateOverlayStateCallback(Action callback)
        {
            _updateOverlayStateCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Initialize()
        {
            LoadRules();
        }

        public void LoadRules()
        {
            _rules = new List<AutoSuicideRule>();
            var lines = new List<string>();

            if (!_settings.AutoSuicideUseDetail)
            {
                foreach (var round in AllRoundTypes)
                {
                    bool enabled = _settings.AutoSuicideRoundTypes.Contains(round);
                    lines.Add($"{round}::{(enabled ? 1 : 0)}");
                }
            }

            if (_settings.AutoSuicideDetailCustom != null)
            {
                lines.AddRange(_settings.AutoSuicideDetailCustom);
            }

            var temp = new List<AutoSuicideRule>();
            foreach (var line in lines)
            {
                if (AutoSuicideRule.TryParse(line, out var r) && r != null)
                {
                    temp.Add(r);
                }
            }

            var cleaned = new List<AutoSuicideRule>();
            for (int i = temp.Count - 1; i >= 0; i--)
            {
                var r = temp[i];
                bool redundant = cleaned.Any(c => c.Covers(r));
                if (!redundant)
                {
                    cleaned.Add(r);
                }
            }

            cleaned.Reverse();
            _rules = cleaned;

            if (_settings.CoordinatedAutoSuicideBrainEnabled)
            {
                _moduleHost.NotifyAutoSuicideRulesPrepared(new ModuleAutoSuicideRuleContext(_rules, _settings, _moduleHost.CurrentServiceProvider));
            }

            _updateOverlayStateCallback?.Invoke();
        }

        public void Schedule(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode = false, bool isManualAction = false)
        {
            if (!isManualAction && _manualCancelRequested)
            {
                _manualCancelRequested = false;
                _manualDelayUntil = null;
                _updateOverlayStateCallback?.Invoke();
                return;
            }

            if (resetStartTime)
            {
                _manualCancelRequested = false;
                _manualDelayUntil = null;
            }

            if (!isManualAction)
            {
                if (_manualDelayUntil.HasValue)
                {
                    DateTime now = DateTime.UtcNow;
                    if (_manualDelayUntil.Value > now)
                    {
                        TimeSpan manualRemaining = _manualDelayUntil.Value - now;
                        if (manualRemaining > delay)
                        {
                            delay = manualRemaining;
                        }
                    }
                    else
                    {
                        _manualDelayUntil = null;
                    }
                }
            }
            else
            {
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                _manualCancelRequested = false;
                _manualDelayUntil = DateTime.UtcNow + delay;
            }

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            _autoSuicideService.Schedule(delay, resetStartTime, Execute);
            _isAllRoundsModeForced = fromAllRoundsMode;
            _updateOverlayStateCallback?.Invoke();
        }

        public void Cancel(bool manualOverride = false)
        {
            if (_autoSuicideService.HasScheduled)
            {
                _autoSuicideService.Cancel();
            }

            if (manualOverride)
            {
                _manualCancelRequested = true;
            }

            _manualDelayUntil = null;
            _isAllRoundsModeForced = false;
            _updateOverlayStateCallback?.Invoke();
        }

        public TimeSpan? Delay(bool manualOverride = false)
        {
            if (!_autoSuicideService.HasScheduled)
            {
                _updateOverlayStateCallback?.Invoke();
                return null;
            }

            TimeSpan elapsed = DateTime.UtcNow - _autoSuicideService.RoundStartTime;
            TimeSpan remaining = TimeSpan.FromSeconds(40) - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.FromSeconds(40);
            }

            if (manualOverride)
            {
                _manualCancelRequested = false;
            }

            Schedule(remaining, false, _isAllRoundsModeForced, manualOverride);
            return remaining;
        }

        public void Execute()
        {
            _logger.LogEvent("Suicide", "Performing Suicide");
            _inputSender.PressKeys();
            _logger.LogEvent("Suicide", "finish");
            _isAllRoundsModeForced = false;
            _updateOverlayStateCallback?.Invoke();
        }

        public bool ShouldScheduleForRound(Round round)
        {
            if (round == null)
            {
                return false;
            }

            int decision = EvaluateAutoSuicideDecision(round.RoundType, round.TerrorKey, out bool hasPendingDelayed);
            return decision > 0;
        }

        public void ToggleAllRoundsMode()
        {
            _isAllRoundsModeEnabled = !_isAllRoundsModeEnabled;

            if (_isAllRoundsModeEnabled)
            {
                EnsureAllRoundsModeAutoSuicide();
            }
            else if (_isAllRoundsModeForced)
            {
                Cancel();
            }

            _updateOverlayStateCallback?.Invoke();
        }

        public void EnsureAllRoundsModeAutoSuicide()
        {
            if (!_autoSuicideService.HasScheduled)
            {
                Schedule(TimeSpan.FromSeconds(13), true, fromAllRoundsMode: true);
            }
        }

        public void ScheduleWithDesireCheck(TimeSpan delay, bool resetStartTime, bool fromAllRoundsMode, int desirePlayerCount, Action<bool> onConfirm)
        {
            if (desirePlayerCount > 0)
            {
                // Add 10 seconds delay
                var extendedDelay = delay.Add(TimeSpan.FromSeconds(10));
                Schedule(extendedDelay, resetStartTime, fromAllRoundsMode, false);

                // Invoke confirmation callback (MainForm will show dialog)
                onConfirm?.Invoke(true);
            }
            else
            {
                Schedule(delay, resetStartTime, fromAllRoundsMode, false);
            }
        }

        private int EvaluateAutoSuicideDecision(string roundType, string? terrorName, out bool hasPendingDelayed)
        {
            hasPendingDelayed = false;
            if (!_settings.AutoSuicideEnabled)
            {
                return 0;
            }

            Func<string, string, bool> comparer;
            if (_settings.AutoSuicideFuzzyMatch)
            {
                comparer = (a, b) => MatchWithTypoTolerance(a, b).result;
            }
            else
            {
                comparer = (a, b) => a == b;
            }

            if (string.IsNullOrWhiteSpace(terrorName))
            {
                terrorName = null;
                hasPendingDelayed = _rules.Any(r =>
                    r.Value == 2 &&
                    r.MatchesRound(roundType, comparer) &&
                    !r.Matches(roundType, null, comparer));
            }

            int decision = 0;
            for (int i = _rules.Count - 1; i >= 0; i--)
            {
                var r = _rules[i];
                if (r.Matches(roundType, terrorName, comparer))
                {
                    decision = r.Value;
                    break;
                }
            }

            var decisionContext = new ModuleAutoSuicideDecisionContext(roundType, terrorName, decision, hasPendingDelayed, _moduleHost.CurrentServiceProvider);
            if (_settings.CoordinatedAutoSuicideBrainEnabled)
            {
                _moduleHost.NotifyAutoSuicideDecisionEvaluated(decisionContext);
            }

            hasPendingDelayed = decisionContext.HasPendingDelayed;
            return decisionContext.Decision;
        }

        private static (bool result, int distance) MatchWithTypoTolerance(string pattern, string input)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            {
                return (false, int.MaxValue);
            }

            if (pattern == input)
            {
                return (true, 0);
            }

            int distance = LevenshteinDistance(pattern, input);
            int threshold = Math.Max(1, pattern.Length / 4);
            return (distance <= threshold, distance);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
