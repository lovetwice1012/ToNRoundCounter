using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Serilog.Events;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure.Interop;
using ToNRoundCounter.UI;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// Manages overlay windows that display game information on top of VRChat.
    /// </summary>
    public class OverlayManager : IOverlayManager
    {
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly IStateService _stateService;
        private readonly IUiDispatcher _dispatcher;
        private readonly CultureInfo _japaneseCulture = new CultureInfo("ja-JP");

        private readonly Dictionary<OverlaySection, OverlaySectionForm> _overlayForms = new();
        private readonly List<(string Label, string Status)> _overlayRoundHistory = new();
        private System.Windows.Forms.Timer? _overlayVisibilityTimer;
        private OverlayShortcutForm? _shortcutOverlayForm;
        private bool _overlayTemporarilyHidden;
        private int _activeOverlayInteractions;
        private DateTime _lastVrChatForegroundTime = DateTime.MinValue;

        private const string NextRoundPredictionUnavailableMessage = "データ不足";

        private enum OverlaySection
        {
            Velocity,
            Angle,
            Terror,
            Damage,
            NextRound,
            RoundStatus,
            RoundHistory,
            RoundStats,
            TerrorInfo,
            Shortcuts,
            Clock,
            InstanceTimer,
            InstanceMembers
        }

        public OverlayManager(
            IAppSettings settings,
            IEventLogger logger,
            IStateService stateService,
            IUiDispatcher dispatcher)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public event EventHandler<ShortcutButtonClickedEventArgs>? ShortcutButtonClicked;

        public void Initialize()
        {
            _dispatcher.Invoke(() =>
            {
                _overlayForms.Clear();
                if (_shortcutOverlayForm != null)
                {
                    _shortcutOverlayForm.ShortcutClicked -= OnShortcutClicked;
                    _shortcutOverlayForm = null;
                }

                Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ??
                                        Screen.AllScreens.FirstOrDefault()?.WorkingArea ??
                                        new Rectangle(0, 0, 1280, 720);
                int offsetX = 40;
                int offsetY = 80;
                int spacing = 16;

                _settings.OverlayPositions ??= new Dictionary<string, Point>();
                _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
                _settings.OverlaySizes ??= new Dictionary<string, Size>();

                var sections = new (OverlaySection Section, string Title, string InitialValue)[]
                {
                    (OverlaySection.Velocity, "速度", "00.00"),
                    (OverlaySection.Terror, "テラー", string.Empty),
                    (OverlaySection.Damage, "ダメージ", "Damage: 0"),
                    (OverlaySection.NextRound, "次ラウンド予測", NextRoundPredictionUnavailableMessage),
                    (OverlaySection.RoundStatus, "ラウンド状況", string.Empty),
                    (OverlaySection.RoundHistory, "ラウンドタイプ推移", string.Empty),
                    (OverlaySection.RoundStats, "ラウンド統計", string.Empty),
                    (OverlaySection.TerrorInfo, "テラー詳細", string.Empty),
                    (OverlaySection.Shortcuts, "ショートカット", string.Empty),
                    (OverlaySection.Clock, "時計", FormatClockOverlayText(DateTimeOffset.Now)),
                    (OverlaySection.InstanceTimer, "滞在時間", FormatElapsedText(TimeSpan.Zero)),
                    (OverlaySection.InstanceMembers, "メンバー", string.Empty)
                };

                int x = Math.Max(workingArea.Left, workingArea.Right - 260 - offsetX);
                int nextDefaultY = Math.Max(workingArea.Top, workingArea.Top + offsetY);

                foreach (var (section, title, initialValue) in sections)
                {
                    OverlaySectionForm form = section switch
                    {
                        OverlaySection.Velocity => new OverlayVelocityForm(title) { StartPosition = FormStartPosition.Manual },
                        OverlaySection.Clock => new OverlayClockForm(title) { StartPosition = FormStartPosition.Manual },
                        OverlaySection.RoundStats => new OverlayRoundStatsForm(title) { StartPosition = FormStartPosition.Manual },
                        OverlaySection.RoundHistory => new OverlayRoundHistoryForm(title) { StartPosition = FormStartPosition.Manual },
                        OverlaySection.Shortcuts => new OverlayShortcutForm(title) { StartPosition = FormStartPosition.Manual },
                        OverlaySection.InstanceMembers => new OverlayInstanceMembersForm(title) { StartPosition = FormStartPosition.Manual },
                        _ => new OverlaySectionForm(title) { StartPosition = FormStartPosition.Manual }
                    };

                    form.SetBackgroundOpacity(GetEffectiveOverlayOpacity());

                    string key = GetOverlaySectionKey(section);

                    if (section == OverlaySection.RoundHistory && form is OverlayRoundHistoryForm historyForm)
                    {
                        historyForm.SetHistory(_overlayRoundHistory);
                    }
                    else if (section == OverlaySection.Shortcuts && form is OverlayShortcutForm shortcuts)
                    {
                        _shortcutOverlayForm = shortcuts;
                        _shortcutOverlayForm.ShortcutClicked += OnShortcutClicked;
                    }
                    else if (section == OverlaySection.Clock && form is OverlayClockForm clockForm)
                    {
                        clockForm.UpdateTime(DateTimeOffset.Now, _japaneseCulture);
                    }
                    else if (section == OverlaySection.Velocity && form is OverlayVelocityForm velocityForm)
                    {
                        velocityForm.UpdateReadings(0, 0);
                    }
                    else
                    {
                        form.SetValue(initialValue);
                    }

                    if (_settings.OverlayScaleFactors.TryGetValue(key, out var savedScale) && savedScale > 0f)
                    {
                        form.ScaleFactor = savedScale;
                    }

                    _settings.OverlayScaleFactors[key] = form.ScaleFactor;

                    if (_settings.OverlaySizes.TryGetValue(key, out var savedSize) && savedSize.Width > 0 && savedSize.Height > 0)
                    {
                        form.ApplySavedSize(savedSize);
                    }

                    _settings.OverlaySizes[key] = form.Size;

                    Point location;
                    if (_settings.OverlayPositions.TryGetValue(key, out var savedLocation))
                    {
                        location = ClampOverlayLocation(savedLocation, form.Size, workingArea);
                    }
                    else
                    {
                        location = new Point(x, nextDefaultY);
                        nextDefaultY += form.Height + spacing;
                    }

                    form.Location = location;
                    _settings.OverlayPositions[key] = form.Location;

                    form.Hide();
                    _overlayForms[section] = form;
                    form.Move += (_, _) => HandleOverlayMoved(section, form);
                    form.SizeChanged += (_, _) => HandleOverlayResized(section, form);
                    form.DragInteractionStarted += HandleOverlayInteractionStarted;
                    form.DragInteractionEnded += HandleOverlayInteractionEnded;
                }

                ApplyRoundHistorySettings();
                RefreshRoundStats();

                _overlayVisibilityTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _overlayVisibilityTimer.Tick += OverlayVisibilityTimer_Tick;
                _overlayVisibilityTimer.Start();

                _logger.LogEvent("OverlayManager", "Overlay manager initialized successfully.", LogEventLevel.Information);
            });
        }

        public void UpdateVelocity(double velocity, double afkSeconds)
        {
            string fallback = $"{velocity.ToString("00.00", CultureInfo.InvariantCulture)}\nAFK: {afkSeconds:F1}秒";

            UpdateOverlay(OverlaySection.Velocity, form =>
            {
                if (form is OverlayVelocityForm velocityForm)
                {
                    velocityForm.UpdateReadings(velocity, afkSeconds);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        public void UpdateTerror(string terrorText, string? terrorInfoText = null)
        {
            UpdateOverlay(OverlaySection.Terror, form => form.SetValue(terrorText));

            if (!string.IsNullOrEmpty(terrorInfoText))
            {
                UpdateOverlay(OverlaySection.TerrorInfo, form => form.SetValue(terrorInfoText));
            }
        }

        public void UpdateDamage(string damageText)
        {
            UpdateOverlay(OverlaySection.Damage, form => form.SetValue($"Damage: {damageText}"));
        }

        public void UpdateNextRound(string nextRoundText, bool hasPrediction)
        {
            string value = hasPrediction ? $"次のラウンドは\n{nextRoundText}" : NextRoundPredictionUnavailableMessage;
            UpdateOverlay(OverlaySection.NextRound, form => form.SetValue(value));
        }

        public void UpdateRoundStatus(string statusText)
        {
            UpdateOverlay(OverlaySection.RoundStatus, form => form.SetValue(statusText));
        }

        public void RecordRoundHistory(string roundType, string status)
        {
            if (string.IsNullOrWhiteSpace(roundType))
            {
                return;
            }

            if (_overlayRoundHistory.Count > 0)
            {
                var last = _overlayRoundHistory[_overlayRoundHistory.Count - 1];
                if (last.Label == roundType && last.Status == status)
                {
                    return;
                }
            }

            int maxEntries = _settings.OverlayRoundHistoryLength;
            if (maxEntries <= 0)
            {
                maxEntries = 3;
            }
            maxEntries = Math.Max(1, maxEntries);

            _overlayRoundHistory.Add((roundType, status));
            while (_overlayRoundHistory.Count > maxEntries)
            {
                _overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        public void RefreshRoundStats()
        {
            var aggregates = _stateService.GetRoundAggregates();
            int totalRounds = aggregates.Values.Sum(r => r.Total);

            bool hasRoundFilter = _settings.RoundTypeStats != null && _settings.RoundTypeStats.Count > 0;
            HashSet<string>? filterSet = null;
            if (hasRoundFilter)
            {
                filterSet = new HashSet<string>(_settings.RoundTypeStats, StringComparer.OrdinalIgnoreCase);
            }

            var entries = aggregates
                .Where(kvp => !hasRoundFilter || (filterSet != null && filterSet.Contains(kvp.Key)))
                .Select(kvp => new OverlayRoundStatsForm.RoundStatEntry(kvp.Key, kvp.Value.Total, kvp.Value.Survival, kvp.Value.Death))
                .OrderByDescending(entry => entry.Total)
                .ToList();

            UpdateOverlay(OverlaySection.RoundStats, form =>
            {
                if (form is OverlayRoundStatsForm statsForm)
                {
                    statsForm.SetStats(entries, totalRounds);
                }
                else
                {
                    var builder = new StringBuilder();
                    foreach (var entry in entries)
                    {
                        builder.AppendLine($"{entry.RoundName}: {entry.Total} (生存 {entry.Survival}, 死亡 {entry.Death}, {entry.SurvivalRate:F1}% )");
                    }
                    form.SetValue(builder.ToString().Trim());
                }
            });
        }

        public void UpdateClock()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string fallback = FormatClockOverlayText(now);

            UpdateOverlay(OverlaySection.Clock, form =>
            {
                if (form is OverlayClockForm clockForm)
                {
                    clockForm.UpdateTime(now, _japaneseCulture);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        public void UpdateInstanceTimer(string instanceId, DateTimeOffset enteredAt)
        {
            string displayText = string.IsNullOrEmpty(instanceId)
                ? FormatElapsedText(TimeSpan.Zero)
                : FormatElapsedText(DateTimeOffset.Now - enteredAt);

            UpdateOverlay(OverlaySection.InstanceTimer, form => form.SetValue(displayText));
        }

        public void UpdateInstanceMembers(IReadOnlyList<string> members)
        {
            UpdateOverlay(OverlaySection.InstanceMembers, form =>
            {
                if (form is OverlayInstanceMembersForm membersForm)
                {
                    membersForm.SetMembers(members);
                }
                else
                {
                    form.SetValue(string.Join("\n", members));
                }
            });
        }

        public void CapturePositions()
        {
            _dispatcher.Invoke(() =>
            {
                if (_overlayForms.Count == 0)
                {
                    return;
                }

                _settings.OverlayPositions ??= new Dictionary<string, Point>();
                _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
                _settings.OverlaySizes ??= new Dictionary<string, Size>();

                foreach (var kvp in _overlayForms)
                {
                    var section = kvp.Key;
                    var form = kvp.Value;
                    if (form.IsDisposed)
                    {
                        continue;
                    }

                    string key = GetOverlaySectionKey(section);
                    _settings.OverlayPositions[key] = form.Location;
                    _settings.OverlayScaleFactors[key] = form.ScaleFactor;
                    _settings.OverlaySizes[key] = form.Size;
                }
            });
        }

        public void ApplyBackgroundOpacity()
        {
            double opacity = GetEffectiveOverlayOpacity();

            _dispatcher.Invoke(() =>
            {
                foreach (var form in _overlayForms.Values)
                {
                    if (form.IsDisposed)
                    {
                        continue;
                    }

                    form.SetBackgroundOpacity(opacity);
                }
            });
        }

        public void ApplyRoundHistorySettings()
        {
            int maxEntries = _settings.OverlayRoundHistoryLength;
            if (maxEntries <= 0)
            {
                maxEntries = 3;
            }
            maxEntries = Math.Max(1, maxEntries);

            while (_overlayRoundHistory.Count > maxEntries)
            {
                _overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        public void SetTemporarilyHidden(bool hidden)
        {
            _dispatcher.Invoke(() =>
            {
                _overlayTemporarilyHidden = hidden;
                UpdateOverlayVisibility();
            });
        }

        public void UpdateShortcutOverlayState(
            bool autoSuicideEnabled,
            bool allRoundsModeEnabled,
            bool coordinatedBrainEnabled,
            bool afkDetectionEnabled,
            bool overlayTemporarilyHidden,
            bool autoSuicideScheduled)
        {
            if (_shortcutOverlayForm == null)
            {
                return;
            }

            _dispatcher.Invoke(() =>
            {
                _shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AutoSuicideToggle, autoSuicideEnabled);
                _shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle, allRoundsModeEnabled);
                _shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle, coordinatedBrainEnabled);
                _shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AfkDetectionToggle, afkDetectionEnabled);
                _shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd, overlayTemporarilyHidden);
                _shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel, autoSuicideScheduled);
                _shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay, autoSuicideScheduled);
            });
        }

        public void ResetRoundScopedShortcutButtons()
        {
            _shortcutOverlayForm?.ResetButtons(
                OverlayShortcutForm.ShortcutButton.AutoSuicideDelay,
                OverlayShortcutForm.ShortcutButton.ManualSuicide,
                OverlayShortcutForm.ShortcutButton.AutoSuicideCancel);
        }

        public void Dispose()
        {
            _overlayVisibilityTimer?.Stop();
            _overlayVisibilityTimer?.Dispose();

            foreach (var form in _overlayForms.Values)
            {
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }

            _overlayForms.Clear();
        }

        private void OverlayVisibilityTimer_Tick(object? sender, EventArgs e)
        {
            UpdateClock();
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            _dispatcher.Invoke(() =>
            {
                if (_overlayForms.Count == 0)
                {
                    return;
                }

                bool isVrChatForeground = WindowUtilities.IsProcessInForeground("VRChat");
                if (isVrChatForeground)
                {
                    _lastVrChatForegroundTime = DateTime.UtcNow;
                }
                else if (_lastVrChatForegroundTime != DateTime.MinValue &&
                         DateTime.UtcNow - _lastVrChatForegroundTime < TimeSpan.FromSeconds(2))
                {
                    isVrChatForeground = true;
                }

                foreach (var kvp in _overlayForms.ToList())
                {
                    var section = kvp.Key;
                    var form = kvp.Value;
                    if (form.IsDisposed)
                    {
                        _overlayForms.Remove(section);
                        continue;
                    }

                    bool enabled = IsOverlaySectionEnabled(section);
                    bool overlayHasFocus = form.ContainsFocus;
                    bool shouldShow = enabled && !_overlayTemporarilyHidden &&
                                      (isVrChatForeground || overlayHasFocus || _activeOverlayInteractions > 0);

                    if (shouldShow)
                    {
                        if (!form.Visible)
                        {
                            form.Show();
                        }
                        form.SetTopMostState(true);
                    }
                    else
                    {
                        form.SetTopMostState(false);

                        if (form.Visible)
                        {
                            form.Hide();
                        }
                    }
                }
            });
        }

        private void HandleOverlayInteractionStarted(object? sender, EventArgs e)
        {
            _activeOverlayInteractions++;
            if (_activeOverlayInteractions == 1)
            {
                UpdateOverlayVisibility();
            }
        }

        private void HandleOverlayInteractionEnded(object? sender, EventArgs e)
        {
            if (_activeOverlayInteractions > 0)
            {
                _activeOverlayInteractions--;
            }

            if (_activeOverlayInteractions == 0)
            {
                UpdateOverlayVisibility();
            }
        }

        private void HandleOverlayMoved(OverlaySection section, OverlaySectionForm form)
        {
            if (form.IsDisposed)
            {
                return;
            }

            string key = GetOverlaySectionKey(section);
            Rectangle workingArea = Screen.FromPoint(form.Location)?.WorkingArea
                ?? Screen.PrimaryScreen?.WorkingArea
                ?? new Rectangle(0, 0, 1920, 1080);
            Point clampedLocation = ClampOverlayLocation(form.Location, form.Size, workingArea);
            if (form.Location != clampedLocation)
            {
                form.Location = clampedLocation;
            }

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayPositions[key] = form.Location;
        }

        private void HandleOverlayResized(OverlaySection section, OverlaySectionForm form)
        {
            if (form.IsDisposed)
            {
                return;
            }

            HandleOverlayMoved(section, form);

            string key = GetOverlaySectionKey(section);
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlayScaleFactors[key] = form.ScaleFactor;
            _settings.OverlaySizes ??= new Dictionary<string, Size>();
            _settings.OverlaySizes[key] = form.Size;
        }

        private void OnShortcutClicked(object? sender, OverlayShortcutForm.ShortcutButtonEventArgs e)
        {
            ShortcutButton button = e.Button switch
            {
                OverlayShortcutForm.ShortcutButton.AutoSuicideToggle => ShortcutButton.AutoSuicideToggle,
                OverlayShortcutForm.ShortcutButton.AutoSuicideCancel => ShortcutButton.AutoSuicideCancel,
                OverlayShortcutForm.ShortcutButton.AutoSuicideDelay => ShortcutButton.AutoSuicideDelay,
                OverlayShortcutForm.ShortcutButton.ManualSuicide => ShortcutButton.ManualSuicide,
                OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle => ShortcutButton.AllRoundsModeToggle,
                OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle => ShortcutButton.CoordinatedBrainToggle,
                OverlayShortcutForm.ShortcutButton.AfkDetectionToggle => ShortcutButton.AfkDetectionToggle,
                OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd => ShortcutButton.HideUntilRoundEnd,
                _ => throw new ArgumentException($"Unknown shortcut button: {e.Button}")
            };

            ShortcutButtonClicked?.Invoke(this, new ShortcutButtonClickedEventArgs(button));
        }

        private void RefreshRoundHistoryOverlay()
        {
            UpdateOverlay(OverlaySection.RoundHistory, form =>
            {
                if (form is OverlayRoundHistoryForm historyForm)
                {
                    historyForm.SetHistory(_overlayRoundHistory);
                }
                else
                {
                    form.SetValue(string.Join(" > ", _overlayRoundHistory.Select(h => h.Label)));
                }
            });
        }

        private void UpdateOverlay(OverlaySection section, Action<OverlaySectionForm> updater)
        {
            if (_overlayForms.TryGetValue(section, out var form) && !form.IsDisposed)
            {
                _dispatcher.Invoke(() =>
                {
                    try
                    {
                        updater(form);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("OverlayManager", $"Failed to update overlay section {section}: {ex.Message}", LogEventLevel.Error);
                    }
                });
            }
        }

        private bool IsOverlaySectionEnabled(OverlaySection section)
        {
            return section switch
            {
                OverlaySection.Velocity => _settings.OverlayShowVelocity,
                OverlaySection.Angle => false,
                OverlaySection.Terror => _settings.OverlayShowTerror,
                OverlaySection.Damage => _settings.OverlayShowDamage,
                OverlaySection.NextRound => _settings.OverlayShowNextRound,
                OverlaySection.RoundStatus => _settings.OverlayShowRoundStatus,
                OverlaySection.RoundHistory => _settings.OverlayShowRoundHistory,
                OverlaySection.RoundStats => _settings.OverlayShowRoundStats,
                OverlaySection.TerrorInfo => _settings.OverlayShowTerrorInfo,
                OverlaySection.Shortcuts => _settings.OverlayShowShortcuts,
                OverlaySection.Clock => _settings.OverlayShowClock,
                OverlaySection.InstanceTimer => _settings.OverlayShowInstanceTimer,
                _ => false
            };
        }

        private double GetEffectiveOverlayOpacity()
        {
            double opacity = _settings.OverlayOpacity;
            if (opacity <= 0d)
            {
                opacity = 0.95d;
            }

            if (opacity < 0.2d)
            {
                opacity = 0.2d;
            }

            if (opacity > 1d)
            {
                opacity = 1d;
            }

            return opacity;
        }

        private static Point ClampOverlayLocation(Point location, Size size, Rectangle workingArea)
        {
            int maxX = Math.Max(workingArea.Left, workingArea.Right - size.Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - size.Height);

            int x = Math.Min(Math.Max(location.X, workingArea.Left), maxX);
            int y = Math.Min(Math.Max(location.Y, workingArea.Top), maxY);

            return new Point(x, y);
        }

        private static string GetOverlaySectionKey(OverlaySection section) => section.ToString();

        private string FormatClockOverlayText(DateTimeOffset now)
        {
            string dayName = _japaneseCulture.DateTimeFormat.GetDayName(now.DayOfWeek);
            if (dayName.EndsWith("曜日", StringComparison.Ordinal))
            {
                int trimmedLength = Math.Max(0, dayName.Length - 2);
                dayName = trimmedLength > 0 ? dayName.Substring(0, trimmedLength) : dayName;
            }

            if (string.IsNullOrEmpty(dayName))
            {
                dayName = _japaneseCulture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek);
            }

            return $"{now:yyyy:MM:dd} ({dayName})\n{now:HH:mm:ss}";
        }

        private static string FormatElapsedText(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            int totalHours = (int)Math.Floor(elapsed.TotalHours);
            return $"経過時間:\n{totalHours:D2}時間{elapsed.Minutes:D2}分{elapsed.Seconds:D2}秒";
        }
    }
}
