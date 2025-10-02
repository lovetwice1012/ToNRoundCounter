using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog.Events;
using ToNRoundCounter.Infrastructure.Interop;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private readonly Dictionary<OverlaySection, OverlaySectionForm> overlayForms = new();
        private readonly List<(string Label, string Status)> overlayRoundHistory = new();
        private string lastRoundTypeForHistory = string.Empty;
        private System.Windows.Forms.Timer? overlayVisibilityTimer;
        private OverlayShortcutForm? shortcutOverlayForm;
        private bool overlayTemporarilyHidden;
        private int activeOverlayInteractions;
        private DateTime lastVrChatForegroundTime = DateTime.MinValue;

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
            InstanceTimer
        }

        private void InitializeOverlay()
        {
            overlayForms.Clear();
            if (shortcutOverlayForm != null)
            {
                shortcutOverlayForm.ShortcutClicked -= ShortcutOverlay_ShortcutClicked;
                shortcutOverlayForm = null;
            }

            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens.FirstOrDefault()?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            int offsetX = 40;
            int offsetY = 80;
            int spacing = 16;

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlaySizes ??= new Dictionary<string, Size>();

            var sections = new (OverlaySection Section, string Title, string InitialValue)[]
            {
                (OverlaySection.Velocity, "速度", currentVelocity.ToString("00.00", CultureInfo.InvariantCulture)),
                (OverlaySection.Terror, "テラー", GetOverlayTerrorDisplayText()),
                (OverlaySection.Damage, "ダメージ", GetDamageOverlayText()),
                (OverlaySection.NextRound, "次ラウンド予測", GetNextRoundOverlayValue()),
                (OverlaySection.RoundStatus, "ラウンド状況", InfoPanel?.RoundTypeValue?.Text ?? string.Empty),
                (OverlaySection.RoundHistory, "ラウンドタイプ推移", string.Empty),
                (OverlaySection.RoundStats, "ラウンド統計", string.Empty),
                (OverlaySection.TerrorInfo, "テラー詳細", BuildTerrorInfoOverlayText()),
                (OverlaySection.Shortcuts, "ショートカット", string.Empty),
                (OverlaySection.Clock, "時計", GetClockOverlayText()),
                (OverlaySection.InstanceTimer, "滞在時間", GetInstanceTimerDisplayText())
            };

            int x = Math.Max(workingArea.Left, workingArea.Right - 260 - offsetX);
            int nextDefaultY = Math.Max(workingArea.Top, workingArea.Top + offsetY);

            foreach (var (section, title, initialValue) in sections)
            {
                OverlaySectionForm form = section switch
                {
                    OverlaySection.Velocity => new OverlayVelocityForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.Clock => new OverlayClockForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.RoundStats => new OverlayRoundStatsForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.RoundHistory => new OverlayRoundHistoryForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    OverlaySection.Shortcuts => new OverlayShortcutForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    },
                    _ => new OverlaySectionForm(title)
                    {
                        StartPosition = FormStartPosition.Manual,
                    }
                };

                form.SetBackgroundOpacity(GetEffectiveOverlayOpacity());

                string key = GetOverlaySectionKey(section);

                if (section == OverlaySection.RoundHistory && form is OverlayRoundHistoryForm historyForm)
                {
                    historyForm.SetHistory(overlayRoundHistory);
                }
                else if (section == OverlaySection.Shortcuts && form is OverlayShortcutForm shortcuts)
                {
                    shortcutOverlayForm = shortcuts;
                    SetupShortcutOverlay(shortcuts);
                }
                else if (section == OverlaySection.Clock && form is OverlayClockForm clockForm)
                {
                    UpdateClockForm(clockForm);
                }
                else if (section == OverlaySection.Velocity && form is OverlayVelocityForm velocityForm)
                {
                    velocityForm.UpdateReadings(currentVelocity, lastIdleSeconds);
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
                overlayForms[section] = form;
                form.Move += (_, _) => HandleOverlayMoved(section, form);
                form.SizeChanged += (_, _) => HandleOverlayResized(section, form);
                form.DragInteractionStarted += HandleOverlayInteractionStarted;
                form.DragInteractionEnded += HandleOverlayInteractionEnded;
            }

            ApplyOverlayRoundHistorySettings();
            RefreshRoundStatsOverlay();

            overlayVisibilityTimer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };
            overlayVisibilityTimer.Tick += OverlayVisibilityTimer_Tick;
            overlayVisibilityTimer.Start();
            UpdateShortcutOverlayState();
        }

        private void OverlayVisibilityTimer_Tick(object? sender, EventArgs e)
        {
            UpdateClockOverlay();
            UpdateVelocityOverlay();
            UpdateInstanceTimerOverlay();
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateOverlayVisibility));
                return;
            }

            if (overlayForms.Count == 0)
            {
                return;
            }

            bool isVrChatForeground = WindowUtilities.IsProcessInForeground("VRChat");
            if (isVrChatForeground)
            {
                lastVrChatForegroundTime = DateTime.UtcNow;
            }
            else if (lastVrChatForegroundTime != DateTime.MinValue &&
                     DateTime.UtcNow - lastVrChatForegroundTime < TimeSpan.FromSeconds(2))
            {
                isVrChatForeground = true;
            }

            foreach (var kvp in overlayForms.ToList())
            {
                var section = kvp.Key;
                var form = kvp.Value;
                if (form.IsDisposed)
                {
                    overlayForms.Remove(section);
                    continue;
                }

                bool enabled = IsOverlaySectionEnabled(section);
                bool overlayHasFocus = form.ContainsFocus;
                bool shouldShow = enabled && !overlayTemporarilyHidden &&
                                  (isVrChatForeground || overlayHasFocus || activeOverlayInteractions > 0);

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
        }

        private void HandleOverlayInteractionStarted(object? sender, EventArgs e)
        {
            activeOverlayInteractions++;

            if (activeOverlayInteractions == 1)
            {
                UpdateOverlayVisibility();
            }
        }

        private void HandleOverlayInteractionEnded(object? sender, EventArgs e)
        {
            if (activeOverlayInteractions > 0)
            {
                activeOverlayInteractions--;
            }

            if (activeOverlayInteractions == 0)
            {
                UpdateOverlayVisibility();
            }
        }

        private void ApplyOverlayBackgroundOpacityToForms()
        {
            double opacity = GetEffectiveOverlayOpacity();

            foreach (var form in overlayForms.Values)
            {
                if (form.IsDisposed)
                {
                    continue;
                }

                form.SetBackgroundOpacity(opacity);
            }
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

        private void SetupShortcutOverlay(OverlayShortcutForm form)
        {
            form.ShortcutClicked += ShortcutOverlay_ShortcutClicked;
            form.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel, autoSuicideService.HasScheduled);
            form.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay, autoSuicideService.HasScheduled);
        }

        private void UpdateShortcutOverlayState()
        {
            if (shortcutOverlayForm == null)
            {
                return;
            }

            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AutoSuicideToggle, _settings.AutoSuicideEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle, issetAllSelfKillMode);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle, _settings.CoordinatedAutoSuicideBrainEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.AfkDetectionToggle, _settings.AfkSoundCancelEnabled);
            shortcutOverlayForm.SetToggleState(OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd, overlayTemporarilyHidden);
            shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel, autoSuicideService.HasScheduled);
            shortcutOverlayForm.SetButtonEnabled(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay, autoSuicideService.HasScheduled);
        }

        private async void ShortcutOverlay_ShortcutClicked(object? sender, OverlayShortcutForm.ShortcutButtonEventArgs e)
        {
            switch (e.Button)
            {
                case OverlayShortcutForm.ShortcutButton.AutoSuicideToggle:
                    _settings.AutoSuicideEnabled = !_settings.AutoSuicideEnabled;
                    LoadAutoSuicideRules();
                    if (!_settings.AutoSuicideEnabled && !issetAllSelfKillMode)
                    {
                        CancelAutoSuicide();
                    }
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.AutoSuicideCancel:
                    bool hadScheduled = autoSuicideService.HasScheduled;
                    if (hadScheduled)
                    {
                        CancelAutoSuicide(manualOverride: true);
                        shortcutOverlayForm?.PulseButton(OverlayShortcutForm.ShortcutButton.AutoSuicideCancel);
                    }
                    else
                    {
                        UpdateShortcutOverlayState();
                    }
                    break;
                case OverlayShortcutForm.ShortcutButton.AutoSuicideDelay:
                    bool canDelay = autoSuicideService.HasScheduled;
                    if (canDelay)
                    {
                        DelayAutoSuicide(manualOverride: true);
                        shortcutOverlayForm?.PulseButton(OverlayShortcutForm.ShortcutButton.AutoSuicideDelay);
                    }
                    else
                    {
                        UpdateShortcutOverlayState();
                    }
                    break;
                case OverlayShortcutForm.ShortcutButton.ManualSuicide:
                    _ = Task.Run(PerformAutoSuicide);
                    shortcutOverlayForm?.SetToggleState(OverlayShortcutForm.ShortcutButton.ManualSuicide, true);
                    break;
                case OverlayShortcutForm.ShortcutButton.AllRoundsModeToggle:
                    issetAllSelfKillMode = !issetAllSelfKillMode;
                    if (issetAllSelfKillMode)
                    {
                        EnsureAllRoundsModeAutoSuicide();
                    }
                    else if (allRoundsForcedSchedule)
                    {
                        CancelAutoSuicide();
                    }
                    UpdateShortcutOverlayState();
                    break;
                case OverlayShortcutForm.ShortcutButton.CoordinatedBrainToggle:
                    _settings.CoordinatedAutoSuicideBrainEnabled = !_settings.CoordinatedAutoSuicideBrainEnabled;
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.AfkDetectionToggle:
                    _settings.AfkSoundCancelEnabled = !_settings.AfkSoundCancelEnabled;
                    UpdateShortcutOverlayState();
                    await _settings.SaveAsync();
                    break;
                case OverlayShortcutForm.ShortcutButton.HideUntilRoundEnd:
                    SetOverlayTemporarilyHidden(!overlayTemporarilyHidden);
                    break;
            }

            WindowUtilities.TryFocusProcessWindowIfAltNotPressed("VRChat");
        }

        private void SetOverlayTemporarilyHidden(bool hidden)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetOverlayTemporarilyHidden(hidden)));
                return;
            }

            overlayTemporarilyHidden = hidden;
            UpdateOverlayVisibility();
            UpdateShortcutOverlayState();
        }

        private void EnsureAllRoundsModeAutoSuicide()
        {
            if (!issetAllSelfKillMode)
            {
                return;
            }

            if (stateService.CurrentRound == null)
            {
                return;
            }

            if (autoSuicideService.HasScheduled)
            {
                CancelAutoSuicide();
            }

            ScheduleAutoSuicide(TimeSpan.FromSeconds(13), true, true);
        }

        private void ResetRoundScopedShortcutButtons()
        {
            shortcutOverlayForm?.ResetButtons(
                OverlayShortcutForm.ShortcutButton.AutoSuicideDelay,
                OverlayShortcutForm.ShortcutButton.ManualSuicide,
                OverlayShortcutForm.ShortcutButton.AutoSuicideCancel);
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

        private static Point ClampOverlayLocation(Point location, Size size, Rectangle workingArea)
        {
            int maxX = Math.Max(workingArea.Left, workingArea.Right - size.Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - size.Height);

            int x = Math.Min(Math.Max(location.X, workingArea.Left), maxX);
            int y = Math.Min(Math.Max(location.Y, workingArea.Top), maxY);

            return new Point(x, y);
        }

        private static string GetOverlaySectionKey(OverlaySection section) => section.ToString();

        private string GetDamageOverlayText()
        {
            string? damageValue = InfoPanel?.DamageValue?.Text;
            if (string.IsNullOrWhiteSpace(damageValue))
            {
                damageValue = "0";
            }

            return $"Damage: {damageValue}";
        }

        private string GetNextRoundOverlayValue()
        {
            if (!hasObservedSpecialRound)
            {
                return NextRoundPredictionUnavailableMessage;
            }

            string nextRoundText = InfoPanel?.NextRoundType?.Text ?? string.Empty;
            return $"次のラウンドは\n{nextRoundText}";
        }

        private void UpdateClockOverlay()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string fallback = FormatClockOverlayText(now);

            UpdateOverlay(OverlaySection.Clock, form =>
            {
                if (form is OverlayClockForm clockForm)
                {
                    clockForm.UpdateTime(now, JapaneseCulture);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        private void UpdateVelocityOverlay()
        {
            string fallback = $"{currentVelocity.ToString("00.00", CultureInfo.InvariantCulture)}\nAFK: {lastIdleSeconds:F1}秒";

            UpdateOverlay(OverlaySection.Velocity, form =>
            {
                if (form is OverlayVelocityForm velocityForm)
                {
                    velocityForm.UpdateReadings(currentVelocity, lastIdleSeconds);
                }
                else
                {
                    form.SetValue(fallback);
                }
            });
        }

        private void UpdateInstanceTimerOverlay()
        {
            UpdateOverlay(OverlaySection.InstanceTimer, form => form.SetValue(GetInstanceTimerDisplayText()));
        }

        private string GetClockOverlayText()
        {
            return FormatClockOverlayText(DateTimeOffset.Now);
        }

        private string FormatClockOverlayText(DateTimeOffset now)
        {
            string dayName = JapaneseCulture.DateTimeFormat.GetDayName(now.DayOfWeek);
            if (dayName.EndsWith("曜日", StringComparison.Ordinal))
            {
                int trimmedLength = Math.Max(0, dayName.Length - 2);
                dayName = trimmedLength > 0 ? dayName.Substring(0, trimmedLength) : dayName;
            }

            if (string.IsNullOrEmpty(dayName))
            {
                dayName = JapaneseCulture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek);
            }

            return $"{now:yyyy:MM:dd} ({dayName})\n{now:HH:mm:ss}";
        }

        private void UpdateClockForm(OverlayClockForm form)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            form.UpdateTime(now, JapaneseCulture);
        }

        private string GetInstanceTimerDisplayText()
        {
            string instanceId;
            DateTimeOffset enteredAt;

            lock (instanceTimerSync)
            {
                instanceId = currentInstanceId;
                enteredAt = currentInstanceEnteredAt;
            }

            string FormatElapsedText(TimeSpan elapsed)
            {
                int totalHours = (int)Math.Floor(elapsed.TotalHours);
                return $"経過時間:\n{totalHours:D2}時間{elapsed.Minutes:D2}分{elapsed.Seconds:D2}秒";
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                return FormatElapsedText(TimeSpan.Zero);
            }

            TimeSpan elapsed = DateTimeOffset.Now - enteredAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return FormatElapsedText(elapsed);
        }

        private void CaptureOverlayPositions()
        {
            if (overlayForms.Count == 0)
            {
                return;
            }

            _settings.OverlayPositions ??= new Dictionary<string, Point>();
            _settings.OverlayScaleFactors ??= new Dictionary<string, float>();
            _settings.OverlaySizes ??= new Dictionary<string, Size>();

            foreach (var kvp in overlayForms)
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

        private void UpdateOverlay(OverlaySection section, Action<OverlaySectionForm> updater)
        {
            if (overlayForms.TryGetValue(section, out var form) && !form.IsDisposed)
            {
                _dispatcher.Invoke(() =>
                {
                    try
                    {
                        updater(form);
                    }
                    catch (Exception ex)
                    {
                        LogUi($"Failed to update overlay section {section}: {ex.Message}", LogEventLevel.Error);
                    }
                });
            }
        }

        private void RecordRoundHistory(string? statusOverride, int? roundCycleForHistory = null)
        {
            string label = !string.IsNullOrWhiteSpace(lastRoundTypeForHistory)
                ? lastRoundTypeForHistory
                : InfoPanel?.NextRoundType?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(statusOverride))
            {
                return;
            }

            string status = statusOverride ?? GetDefaultRoundHistoryStatus(roundCycleForHistory ?? stateService.RoundCycle);

            if (overlayRoundHistory.Count > 0)
            {
                var last = overlayRoundHistory[overlayRoundHistory.Count - 1];
                if (last.Label == label && last.Status == status)
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

            overlayRoundHistory.Add((label, status));
            while (overlayRoundHistory.Count > maxEntries)
            {
                overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        private string GetDefaultRoundHistoryStatus(int roundCycle)
        {
            if (roundCycle <= 0)
            {
                return "クラシック確定";
            }

            if (roundCycle == 1)
            {
                return "50/50";
            }

            return "特殊確定";
        }

        private void ApplyOverlayRoundHistorySettings()
        {
            int maxEntries = _settings.OverlayRoundHistoryLength;
            if (maxEntries <= 0)
            {
                maxEntries = 3;
            }
            maxEntries = Math.Max(1, maxEntries);

            while (overlayRoundHistory.Count > maxEntries)
            {
                overlayRoundHistory.RemoveAt(0);
            }

            RefreshRoundHistoryOverlay();
        }

        private void RefreshRoundHistoryOverlay()
        {
            UpdateOverlay(OverlaySection.RoundHistory, form =>
            {
                if (form is OverlayRoundHistoryForm historyForm)
                {
                    historyForm.SetHistory(overlayRoundHistory);
                }
                else
                {
                    form.SetValue(string.Join(" > ", overlayRoundHistory.Select(h => h.Label)));
                }
            });
        }

        private (List<OverlayRoundStatsForm.RoundStatEntry> Entries, int TotalRounds) BuildRoundStatsEntries()
        {
            var aggregates = stateService.GetRoundAggregates();
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

            return (entries, totalRounds);
        }

        private void RefreshRoundStatsOverlay()
        {
            var (entries, totalRounds) = BuildRoundStatsEntries();
            RefreshRoundStatsOverlay(entries, totalRounds);
        }

        private void RefreshRoundStatsOverlay(IReadOnlyList<OverlayRoundStatsForm.RoundStatEntry> entries, int totalRounds)
        {
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
    }
}
