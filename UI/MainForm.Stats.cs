using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Serilog.Events;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private readonly object aggregateStatsUpdateSync = new();
        private CancellationTokenSource? aggregateStatsUpdateCts;

        private readonly struct FormattedTextLine
        {
            public FormattedTextLine(string text, Color color, bool indent)
            {
                Text = text;
                Color = color;
                Indent = indent;
            }

            public string Text { get; }
            public Color Color { get; }
            public bool Indent { get; }
        }

        private sealed class AggregateStatsUpdate
        {
            public AggregateStatsUpdate(List<FormattedTextLine> lines, IReadOnlyList<OverlayRoundStatsForm.RoundStatEntry> entries, int totalRounds)
            {
                Lines = lines ?? throw new ArgumentNullException(nameof(lines));
                Entries = entries ?? throw new ArgumentNullException(nameof(entries));
                TotalRounds = totalRounds;
            }

            public List<FormattedTextLine> Lines { get; }
            public IReadOnlyList<OverlayRoundStatsForm.RoundStatEntry> Entries { get; }
            public int TotalRounds { get; }
        }

        private readonly struct StatsDisplayConfig
        {
            public StatsDisplayConfig(bool filterAppearance, bool filterSurvival, bool filterDeath, bool filterSurvivalRate, bool filterTerror, bool showDebug, Color fixedTerrorColor, HashSet<string>? roundTypeFilter)
            {
                FilterAppearance = filterAppearance;
                FilterSurvival = filterSurvival;
                FilterDeath = filterDeath;
                FilterSurvivalRate = filterSurvivalRate;
                FilterTerror = filterTerror;
                ShowDebug = showDebug;
                FixedTerrorColor = fixedTerrorColor;
                RoundTypeFilter = roundTypeFilter;
            }

            public bool FilterAppearance { get; }
            public bool FilterSurvival { get; }
            public bool FilterDeath { get; }
            public bool FilterSurvivalRate { get; }
            public bool FilterTerror { get; }
            public bool ShowDebug { get; }
            public Color FixedTerrorColor { get; }
            public HashSet<string>? RoundTypeFilter { get; }
        }

        private void UpdateAggregateStatsDisplay()
        {
            HashSet<string>? roundTypeFilter = null;
            if (_settings.RoundTypeStats != null && _settings.RoundTypeStats.Count > 0)
            {
                roundTypeFilter = GetRoundTypeFilterSnapshot(_settings.RoundTypeStats);
            }

            var config = new StatsDisplayConfig(
                _settings.Filter_Appearance,
                _settings.Filter_Survival,
                _settings.Filter_Death,
                _settings.Filter_SurvivalRate,
                _settings.Filter_Terror,
                _settings.ShowDebug,
                _settings.FixedTerrorColor,
                roundTypeFilter);

            var cts = RenewTokenSource(ref aggregateStatsUpdateCts, aggregateStatsUpdateSync);
            var token = cts.Token;

            Task.Run(() =>
            {
                try
                {
                    var update = BuildAggregateStatsUpdate(token, config);
                    if (update == null || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        ApplyAggregateStatsUpdate(update);
                    });
                }
                finally
                {
                    cts.Dispose();
                }
            }, _cancellation.Token);
        }

        // Reusable StringBuilder to avoid per-row allocations when the aggregate stats panel is
        // rebuilt (can be thousands of rows when many terrors are tracked).
        [ThreadStatic] private static StringBuilder? _statsLineBuilder;

        // Cached HashSet for RoundTypeStats. Settings rarely change but the stats panel rebuilds
        // frequently, so we keep a reference-equality check on the source list.
        private List<string>? _cachedRoundTypeStatsSource;
        private int _cachedRoundTypeStatsCount;
        private HashSet<string>? _cachedRoundTypeStats;

        private HashSet<string> GetRoundTypeFilterSnapshot(List<string> source)
        {
            if (ReferenceEquals(_cachedRoundTypeStatsSource, source)
                && _cachedRoundTypeStatsCount == source.Count
                && _cachedRoundTypeStats != null)
            {
                return _cachedRoundTypeStats;
            }

            var snapshot = new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
            _cachedRoundTypeStatsSource = source;
            _cachedRoundTypeStatsCount = source.Count;
            _cachedRoundTypeStats = snapshot;
            return snapshot;
        }

        private AggregateStatsUpdate? BuildAggregateStatsUpdate(CancellationToken token, StatsDisplayConfig config)
        {
            // Translate labels once per rebuild rather than M*N times inside loops.
            string trAppearance = LanguageManager.Translate("出現回数") ?? "出現回数";
            string trSurvival = LanguageManager.Translate("生存回数") ?? "生存回数";
            string trDeath = LanguageManager.Translate("死亡回数") ?? "死亡回数";
            string trSurvivalRate = LanguageManager.Translate("生存率") ?? "生存率";
            string trOccurrenceRate = LanguageManager.Translate("出現率") ?? "出現率";

            var sb = _statsLineBuilder ??= new StringBuilder(256);

            var lines = new List<FormattedTextLine>();
            var roundAggregates = stateService.GetRoundAggregates();
            int overallTotal = 0;
            foreach (var ra in roundAggregates.Values)
            {
                overallTotal += ra.Total;
            }
            foreach (var kvp in roundAggregates)
            {
                if (token.IsCancellationRequested)
                {
                    return null;
                }

                string roundType = kvp.Key;
                if (config.RoundTypeFilter != null && config.RoundTypeFilter.Count > 0 && !config.RoundTypeFilter.Contains(roundType))
                {
                    continue;
                }

                RoundAggregate agg = kvp.Value;
                sb.Clear();
                sb.Append(roundType);
                if (config.FilterAppearance)
                {
                    sb.Append(' ').Append(trAppearance).Append('=').Append(agg.Total);
                }
                if (config.FilterSurvival)
                {
                    sb.Append(' ').Append(trSurvival).Append('=').Append(agg.Survival);
                }
                if (config.FilterDeath)
                {
                    sb.Append(' ').Append(trDeath).Append('=').Append(agg.Death);
                }
                if (config.FilterSurvivalRate)
                {
                    sb.Append(' ').Append(trSurvivalRate).Append('=');
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1}%", agg.SurvivalRate);
                }
                if (overallTotal > 0 && config.FilterAppearance)
                {
                    double occurrenceRate = agg.Total * 100.0 / overallTotal;
                    sb.Append(' ').Append(trOccurrenceRate).Append('=');
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1}%", occurrenceRate);
                }
                lines.Add(new FormattedTextLine(sb.ToString(), Theme.Current.Foreground, indent: false));

                if (config.FilterTerror && stateService.TryGetTerrorAggregates(roundType, out var terrorDict) && terrorDict != null)
                {
                    foreach (var terrorKvp in terrorDict)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return null;
                        }

                        string terrorKey = terrorKvp.Key;
                        TerrorAggregate tAgg = terrorKvp.Value;
                        sb.Clear();
                        sb.Append(terrorKey);
                        if (config.FilterAppearance)
                        {
                            sb.Append(' ').Append(trAppearance).Append('=').Append(tAgg.Total);
                        }
                        if (config.FilterSurvival)
                        {
                            sb.Append(' ').Append(trSurvival).Append('=').Append(tAgg.Survival);
                        }
                        if (config.FilterDeath)
                        {
                            sb.Append(' ').Append(trDeath).Append('=').Append(tAgg.Death);
                        }
                        if (config.FilterSurvivalRate)
                        {
                            sb.Append(' ').Append(trSurvivalRate).Append('=');
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1}%", tAgg.SurvivalRate);
                        }
                        Color rawColor = terrorColors.TryGetValue(terrorKey, out var foundColor) ? foundColor : Color.Black;
                        Color terrorColor = (config.FixedTerrorColor != Color.Empty && config.FixedTerrorColor != Color.White)
                            ? config.FixedTerrorColor
                            : AdjustColorForVisibility(rawColor);
                        lines.Add(new FormattedTextLine(sb.ToString(), terrorColor, indent: true));
                    }
                }

                lines.Add(new FormattedTextLine(string.Empty, Theme.Current.Foreground, indent: false));
            }

            if (config.ShowDebug)
            {
                lines.Add(new FormattedTextLine($"VelocityMagnitude: {currentVelocity:F2}", Color.Blue, indent: false));
                if (idleStartTime != DateTime.MinValue)
                {
                    double idleSeconds = (DateTime.Now - idleStartTime).TotalSeconds;
                    lines.Add(new FormattedTextLine($"Idle Time: {idleSeconds:F2} sec", Color.Blue, indent: false));
                }
            }

            var (entries, totalRounds) = BuildRoundStatsEntries();
            return new AggregateStatsUpdate(lines, entries, totalRounds);
        }

        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        private static void SuspendDrawing(Control control)
        {
            SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }

        private static void ResumeDrawing(Control control)
        {
            SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            control.Invalidate(true);
        }

        private void ApplyAggregateStatsUpdate(AggregateStatsUpdate update)
        {
            try
            {
                SuspendDrawing(rtbStatsDisplay);
                try
                {
                    rtbStatsDisplay.Clear();
                    foreach (var line in update.Lines)
                    {
                        if (line.Indent)
                        {
                            AppendIndentedLine(rtbStatsDisplay, line.Text, line.Color);
                        }
                        else
                        {
                            AppendLine(rtbStatsDisplay, line.Text, line.Color);
                        }
                    }
                }
                finally
                {
                    ResumeDrawing(rtbStatsDisplay);
                }
            }
            catch (Exception ex)
            {
                LogUi($"Failed to apply aggregate stats text update: {ex.Message}", LogEventLevel.Warning);
            }

            try
            {
                _overlayManager.RefreshRoundStats();
            }
            catch (Exception ex)
            {
                LogUi($"Failed to refresh round stats overlay: {ex.Message}", LogEventLevel.Warning);
            }
        }

        private CancellationTokenSource RenewTokenSource(ref CancellationTokenSource? target, object syncRoot)
        {
            CancellationTokenSource? previous;
            var newSource = new CancellationTokenSource();
            lock (syncRoot)
            {
                previous = target;
                target = newSource;
            }

            if (previous != null)
            {
                try
                {
                    previous.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // If the token source has already been disposed by a previous operation,
                    // we can safely ignore the exception and continue with the new source.
                }

                previous.Dispose();
            }
            return newSource;
        }

        private void AppendLine(RichTextBox rtb, string text, Color color)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.AppendText(text + Environment.NewLine);
            rtb.SelectionColor = rtb.ForeColor;
        }

        private void AppendIndentedLine(RichTextBox rtb, string text, Color color)
        {
            string prefix = "    ";
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionColor = Color.Black;
            rtb.AppendText(prefix);
            rtb.SelectionColor = color;
            rtb.AppendText(text);
            rtb.SelectionColor = Color.Black;
            rtb.AppendText(Environment.NewLine);
        }

        private void UpdateDisplayVisibility()
        {
            LogUi($"Updating display visibility. Stats: {_settings.ShowStats}, RoundLog: {_settings.ShowRoundLog}.", LogEventLevel.Debug);
            lblStatsTitle.Visible = _settings.ShowStats;
            rtbStatsDisplay.Visible = _settings.ShowStats;
            lblRoundLogTitle.Visible = _settings.ShowRoundLog;
            logPanel.RoundLogTextBox.Visible = _settings.ShowRoundLog;
        }

        public void UpdateRoundLog(IEnumerable<string> logEntries)
        {
            var entries = logEntries ?? Array.Empty<string>();
            var builder = new StringBuilder();
            foreach (var entry in entries)
            {
                builder.AppendLine(entry ?? string.Empty);
            }

            var text = builder.ToString();
            _dispatcher.Invoke(() =>
            {
                // logPanel may not be initialized yet if this is called during early construction
                if (logPanel?.RoundLogTextBox == null)
                {
                    return;
                }

                var rtb = logPanel.RoundLogTextBox;
                SuspendDrawing(rtb);
                try
                {
                    rtb.Text = text;
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.ScrollToCaret();
                }
                finally
                {
                    ResumeDrawing(rtb);
                }
            });
        }

        public void AppendRoundLogEntry(string logEntry)
        {
            var entry = logEntry ?? string.Empty;
            _dispatcher.Invoke(() =>
            {
                // logPanel may not be initialized yet if this is called during early construction
                if (logPanel?.RoundLogTextBox == null)
                {
                    return;
                }

                var rtb = logPanel.RoundLogTextBox;
                SuspendDrawing(rtb);
                try
                {
                    rtb.AppendText(entry + Environment.NewLine);
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.ScrollToCaret();
                }
                finally
                {
                    ResumeDrawing(rtb);
                }
            });
        }
    }
}
