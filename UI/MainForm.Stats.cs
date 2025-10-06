using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
                roundTypeFilter = new HashSet<string>(_settings.RoundTypeStats, StringComparer.OrdinalIgnoreCase);
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
            });
        }

        private AggregateStatsUpdate? BuildAggregateStatsUpdate(CancellationToken token, StatsDisplayConfig config)
        {
            static string TranslateSafe(string key)
            {
                return LanguageManager.Translate(key) ?? key;
            }

            var lines = new List<FormattedTextLine>();
            var roundAggregates = stateService.GetRoundAggregates();
            int overallTotal = roundAggregates.Values.Sum(r => r.Total);
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
                var parts = new List<string> { roundType };
                if (config.FilterAppearance)
                    parts.Add(TranslateSafe("出現回数") + "=" + agg.Total);
                if (config.FilterSurvival)
                    parts.Add(TranslateSafe("生存回数") + "=" + agg.Survival);
                if (config.FilterDeath)
                    parts.Add(TranslateSafe("死亡回数") + "=" + agg.Death);
                if (config.FilterSurvivalRate)
                    parts.Add(string.Format(TranslateSafe("生存率") + "={0:F1}%", agg.SurvivalRate));
                if (overallTotal > 0 && config.FilterAppearance)
                {
                    double occurrenceRate = agg.Total * 100.0 / overallTotal;
                    parts.Add(string.Format(TranslateSafe("出現率") + "={0:F1}%", occurrenceRate));
                }
                string roundLine = string.Join(" ", parts);
                lines.Add(new FormattedTextLine(roundLine, Theme.Current.Foreground, indent: false));

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
                        var terrorParts = new List<string> { terrorKey };
                        if (config.FilterAppearance)
                            terrorParts.Add(TranslateSafe("出現回数") + "=" + tAgg.Total);
                        if (config.FilterSurvival)
                            terrorParts.Add(TranslateSafe("生存回数") + "=" + tAgg.Survival);
                        if (config.FilterDeath)
                            terrorParts.Add(TranslateSafe("死亡回数") + "=" + tAgg.Death);
                        if (config.FilterSurvivalRate)
                            terrorParts.Add(string.Format(TranslateSafe("生存率") + "={0:F1}%", tAgg.SurvivalRate));
                        string terrorLine = string.Join(" ", terrorParts);
                        Color rawColor = terrorColors.ContainsKey(terrorKey) ? terrorColors[terrorKey] : Color.Black;
                        Color terrorColor = (config.FixedTerrorColor != Color.Empty && config.FixedTerrorColor != Color.White)
                            ? config.FixedTerrorColor
                            : AdjustColorForVisibility(rawColor);
                        lines.Add(new FormattedTextLine(terrorLine, terrorColor, indent: true));
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

        private void ApplyAggregateStatsUpdate(AggregateStatsUpdate update)
        {
            rtbStatsDisplay.SuspendLayout();
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
                rtbStatsDisplay.ResumeLayout();
            }

            RefreshRoundStatsOverlay(update.Entries, update.TotalRounds);
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
                logPanel.RoundLogTextBox.SuspendLayout();
                try
                {
                    logPanel.RoundLogTextBox.Text = text;
                    logPanel.RoundLogTextBox.SelectionStart = logPanel.RoundLogTextBox.TextLength;
                    logPanel.RoundLogTextBox.ScrollToCaret();
                }
                finally
                {
                    logPanel.RoundLogTextBox.ResumeLayout();
                }
            });
        }

        public void AppendRoundLogEntry(string logEntry)
        {
            var entry = logEntry ?? string.Empty;
            _dispatcher.Invoke(() =>
            {
                logPanel.RoundLogTextBox.SuspendLayout();
                try
                {
                    logPanel.RoundLogTextBox.AppendText(entry + Environment.NewLine);
                    logPanel.RoundLogTextBox.SelectionStart = logPanel.RoundLogTextBox.TextLength;
                    logPanel.RoundLogTextBox.ScrollToCaret();
                }
                finally
                {
                    logPanel.RoundLogTextBox.ResumeLayout();
                }
            });
        }
    }
}
