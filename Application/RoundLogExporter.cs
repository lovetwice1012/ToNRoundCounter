using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Serilog;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    public sealed class RoundLogExporter
    {
        private static readonly string[] TerrorSeparators = new[] { "&", "＆" };
        private static readonly HashSet<string> AlternateRounds = new(StringComparer.OrdinalIgnoreCase)
        {
            "オルタネイト",
            "Alternate"
        };

        private static readonly HashSet<string> EightPagesRounds = new(StringComparer.OrdinalIgnoreCase)
        {
            "8ページ",
            "8 Page",
            "Eight Pages",
            "Eight_Pages"
        };

        private static readonly HashSet<string> UnboundRounds = new(StringComparer.OrdinalIgnoreCase)
        {
            "アンバウンド",
            "Unbound"
        };

        private static readonly HashSet<string> MoonRounds = new(StringComparer.OrdinalIgnoreCase)
        {
            "ミスティックムーン",
            "Mystic Moon",
            "ブラッドムーン",
            "Blood Moon",
            "トワイライト",
            "Twilight",
            "ソルスティス",
            "Solstice"
        };

        private static readonly HashSet<string> EventRounds = new(StringComparer.OrdinalIgnoreCase)
        {
            "寒い夜",
            "Cold Night"
        };

        private static readonly HashSet<string> EncounterlessTerrors = new(StringComparer.OrdinalIgnoreCase)
        {
            "Wild Yet Bloodthirsty Creature",
            "atrached",
            "Hungry Home Invader"
        };

        private static readonly HashSet<string> SpecialGroupOverrideTerrors = new(StringComparer.OrdinalIgnoreCase)
        {
            "GIGABITE",
            "Neo Pilot"
        };

        private readonly ILogger? _logger;

        public RoundLogExporter(ILogger? logger)
        {
            _logger = logger;
        }

        public async Task<int> ExportAsync(RoundLogExportOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var records = await LoadRoundLogRecordsAsync(options.DataDirectory, cancellationToken).ConfigureAwait(false);

            var uniqueRecords = Deduplicate(records);
            var orderedRecords = uniqueRecords.OrderBy(r => r.Timestamp).ToList();
            var exportEntries = new List<RoundLogExportEntry>();

            foreach (var record in orderedRecords)
            {
                if (record.Round?.IsDeath == true)
                {
                    _logger?.Debug(
                        "Skipping round log entry {RoundId} from '{Source}' because the round ended in death.",
                        record.RoundId ?? record.RowId.ToString(CultureInfo.InvariantCulture),
                        record.SourcePath);
                    continue;
                }

                var entry = ConvertToExportEntry(record);
                exportEntries.Add(entry);
            }

            string? directory = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var json = JsonConvert.SerializeObject(exportEntries, Formatting.Indented);

            using (var stream = new FileStream(
                       options.OutputPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       useAsync: true))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            _logger?.Information("Exported {Count} round log entries to '{OutputPath}'.", exportEntries.Count, options.OutputPath);
            return exportEntries.Count;
        }

        private async Task<List<RoundLogRecord>> LoadRoundLogRecordsAsync(string dataDirectory, CancellationToken cancellationToken)
        {
            var records = new List<RoundLogRecord>();
            if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                _logger?.Warning("Round data directory '{Directory}' does not exist. No entries will be exported.", dataDirectory);
                return records;
            }

            string roundsDirectory = Path.Combine(dataDirectory, "rounds");
            if (!Directory.Exists(roundsDirectory))
            {
                _logger?.Warning("Round SQLite directory '{Directory}' does not exist. No entries will be exported.", roundsDirectory);
                return records;
            }

            foreach (var file in EnumerateRoundDatabases(roundsDirectory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fileRecords = await LoadRecordsFromDatabaseAsync(file, cancellationToken).ConfigureAwait(false);
                    records.AddRange(fileRecords);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, "Failed to read round log entries from '{DatabasePath}'.", file);
                }
            }

            return records;
        }

        private IEnumerable<string> EnumerateRoundDatabases(string rootDirectory, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(rootDirectory);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();

                IEnumerable<string>? files = null;
                try
                {
                    files = Directory.EnumerateFiles(current, "*.sqlite", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, "Failed to enumerate round log files in '{Directory}'.", current);
                }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        yield return file;
                    }
                }

                IEnumerable<string>? subdirectories = null;
                try
                {
                    subdirectories = Directory.EnumerateDirectories(current);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, "Failed to enumerate subdirectories in '{Directory}'.", current);
                }

                if (subdirectories != null)
                {
                    foreach (var directory in subdirectories)
                    {
                        pending.Push(directory);
                    }
                }
            }
        }

        private async Task<List<RoundLogRecord>> LoadRecordsFromDatabaseAsync(string databasePath, CancellationToken cancellationToken)
        {
            var results = new List<RoundLogRecord>();
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            };

            using (var connection = new SqliteConnection(builder.ToString()))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, RoundId, RoundJson, RoundType, TerrorKey, MapName, IsDeath, CreatedAt, RoundInt, MapId, TerrorIds FROM RoundLogs";

                    var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    using (reader)
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            long rowId = reader.GetInt64(0);
                            string? roundId = reader.IsDBNull(1) ? null : reader.GetString(1);
                            string? roundJson = reader.IsDBNull(2) ? null : reader.GetString(2);
                            string? roundType = reader.IsDBNull(3) ? null : reader.GetString(3);
                            string? terrorKey = reader.IsDBNull(4) ? null : reader.GetString(4);
                            string? mapName = reader.IsDBNull(5) ? null : reader.GetString(5);
                            bool isDeath = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;
                            string? createdAtRaw = reader.IsDBNull(7) ? null : reader.GetString(7);
                            int? roundInt = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
                            int? mapId = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9);
                            string? terrorIdsJson = reader.IsDBNull(10) ? null : reader.GetString(10);

                            var round = DeserializeRound(roundJson) ?? new Round();
                            if (!string.IsNullOrWhiteSpace(roundType))
                            {
                                round.RoundType = roundType;
                            }
                            if (!string.IsNullOrWhiteSpace(terrorKey))
                            {
                                round.TerrorKey = terrorKey;
                            }
                            if (!string.IsNullOrWhiteSpace(mapName))
                            {
                                round.MapName = mapName;
                            }
                            round.IsDeath = round.IsDeath || isDeath;

                            if (roundInt.HasValue)
                            {
                                round.RoundNumber = roundInt;
                            }

                            if (mapId.HasValue)
                            {
                                round.MapId = mapId;
                            }

                            if (!string.IsNullOrWhiteSpace(terrorIdsJson))
                            {
                                try
                                {
                                    var ids = JsonConvert.DeserializeObject<int[]>(terrorIdsJson!);
                                    if (ids != null)
                                    {
                                        round.TerrorIds = ids;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.Warning(ex, "Failed to parse terror IDs from '{DatabasePath}'.", databasePath);
                                }
                            }

                            DateTime timestamp = ParseTimestamp(createdAtRaw);

                            results.Add(new RoundLogRecord
                            {
                                RowId = rowId,
                                RoundId = roundId,
                                Round = round,
                                Timestamp = timestamp,
                                SourcePath = databasePath
                            });
                        }
                    }
                }
            }

            return results;
        }

        private static Round? DeserializeRound(string? roundJson)
        {
            if (string.IsNullOrWhiteSpace(roundJson))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<Round>(roundJson!);
            }
            catch
            {
                return null;
            }
        }

        private static DateTime ParseTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private static List<RoundLogRecord> Deduplicate(List<RoundLogRecord> records)
        {
            var lookup = new Dictionary<string, RoundLogRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                string key = !string.IsNullOrWhiteSpace(record.RoundId)
                    ? record.RoundId!
                    : $"{record.Round.RoundType}|{record.Timestamp:O}|{record.SourcePath}|{record.RowId}";

                if (!lookup.TryGetValue(key, out var existing) || record.Timestamp > existing.Timestamp)
                {
                    lookup[key] = record;
                }
            }

            return new List<RoundLogRecord>(lookup.Values);
        }

        private RoundLogExportEntry ConvertToExportEntry(RoundLogRecord record)
        {
            var round = record.Round;
            string timestamp = FormatTimestamp(record.Timestamp);
            int roundTypeId = round.RoundNumber ?? 0;
            int mapId = round.MapId ?? 0;
            int level = DetermineLevel(round.RoundType);

            var terrorNames = SplitTerrorNames(round.TerrorKey);
            var terrorEntries = new List<RoundLogExportEntry.TerrorEntry>(capacity: 3);
            var terrorIds = round.TerrorIds ?? Array.Empty<int>();

            for (int index = 0; index < 3; index++)
            {
                string? terrorName = index < terrorNames.Count ? terrorNames[index] : null;
                int terrorId = index < terrorIds.Length ? terrorIds[index] : 0;
                int group = ResolveGroup(round.RoundType, terrorName);
                int? encounter = ResolveEncounter(terrorName);

                var terrorData = new RoundLogExportEntry.TerrorEntry
                {
                    Index = terrorId,
                    RoundType = roundTypeId,
                    Group = group,
                    Encounter = encounter,
                    Level = level
                };

                terrorEntries.Add(terrorData);
            }

            int result = 1;

            return new RoundLogExportEntry
            {
                Note = string.Empty,
                Timestamp = timestamp,
                Content = string.Empty,
                PlayerCount = 1,
                Players = string.Empty,
                RoundTypeId = roundTypeId,
                TerrorData = terrorEntries,
                MapId = mapId,
                RoundResult = result,
                RoundTerrors = null,
                RoundType = null
            };
        }

        private static List<string> SplitTerrorNames(string? terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey))
            {
                return new List<string>();
            }

            string key = terrorKey.Trim();

            if (key.Length == 0)
            {
                return new List<string>();
            }

            return key
                .Split(TerrorSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }

        private static string FormatTimestamp(DateTime timestamp)
        {
            if (timestamp == DateTime.MinValue)
            {
                return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }

            var normalized = timestamp.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc) : timestamp.ToUniversalTime();
            return normalized.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static int DetermineLevel(string? roundType)
        {
            if (string.IsNullOrWhiteSpace(roundType))
            {
                return 1;
            }

            if (roundType.IndexOf("ダブルトラブル", StringComparison.OrdinalIgnoreCase) >= 0 ||
                roundType.IndexOf("Double Trouble", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            if (roundType.IndexOf("EX", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }

            return 1;
        }

        private int ResolveGroup(string? roundType, string? terrorName)
        {
            int group = 0;

            if (!string.IsNullOrWhiteSpace(roundType))
            {
                if (AlternateRounds.Contains(roundType!))
                {
                    group = 1;
                }
                else if (EightPagesRounds.Contains(roundType!))
                {
                    group = 2;
                }
                else if (UnboundRounds.Contains(roundType!))
                {
                    group = 3;
                }
                else if (MoonRounds.Contains(roundType!))
                {
                    group = 4;
                }
                else if (EventRounds.Contains(roundType!))
                {
                    group = 6;
                }
            }

            if (!string.IsNullOrWhiteSpace(terrorName) && SpecialGroupOverrideTerrors.Contains(terrorName!))
            {
                group = 6;
            }

            return group;
        }

        private int? ResolveEncounter(string? terrorName)
        {
            if (!string.IsNullOrWhiteSpace(terrorName) && EncounterlessTerrors.Contains(terrorName!))
            {
                return null;
            }

            return -1;
        }

        private sealed class RoundLogRecord
        {
            public long RowId { get; init; }
            public string? RoundId { get; init; }
            public Round Round { get; init; } = new Round();
            public DateTime Timestamp { get; init; }
            public string SourcePath { get; init; } = string.Empty;
        }

        private sealed class RoundLogExportEntry
        {
            [JsonProperty("Note")]
            public string Note { get; set; } = string.Empty;

            [JsonProperty("Timestamp")]
            public string Timestamp { get; set; } = string.Empty;

            [JsonProperty("Content")]
            public string Content { get; set; } = string.Empty;

            [JsonProperty("pc")]
            public int PlayerCount { get; set; }

            [JsonProperty("Players")]
            public string Players { get; set; } = string.Empty;

            [JsonProperty("RT")]
            public int RoundTypeId { get; set; }

            [JsonProperty("TD")]
            public List<TerrorEntry> TerrorData { get; set; } = new List<TerrorEntry>();

            [JsonProperty("MapID")]
            public int MapId { get; set; }

            [JsonProperty("RResult")]
            public int RoundResult { get; set; }

            [JsonProperty("RTerrors")]
            public object? RoundTerrors { get; set; }

            [JsonProperty("RType")]
            public object? RoundType { get; set; }

            public sealed class TerrorEntry
            {
                [JsonProperty("i")]
                public int Index { get; set; }

                [JsonProperty("r")]
                public int RoundType { get; set; }

                [JsonProperty("g")]
                public int Group { get; set; }

                [JsonProperty("e", NullValueHandling = NullValueHandling.Ignore)]
                public int? Encounter { get; set; }

                [JsonProperty("l")]
                public int Level { get; set; }

                [JsonProperty("p", DefaultValueHandling = DefaultValueHandling.Ignore)]
                [DefaultValue(0)]
                public int Phase { get; set; }
            }
        }
    }
}

