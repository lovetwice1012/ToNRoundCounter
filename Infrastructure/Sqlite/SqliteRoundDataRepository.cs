using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Serilog;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteRoundDataRepository : IRoundDataRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;
        private readonly object _connectionLock = new();
        private bool _disposed;

        public SqliteRoundDataRepository(string databasePath)
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ToString();

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            try
            {
                using var pragmaCommand = _connection.CreateCommand();
                pragmaCommand.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaCommand.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enable WAL journal mode for round data database.");
            }

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                ExecuteInTransaction(command =>
                {
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS RoundLogs (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            RoundId TEXT,
                            RoundType TEXT,
                            TerrorKey TEXT,
                            MapName TEXT,
                            IsDeath INTEGER,
                            Status TEXT NOT NULL,
                            CreatedAt TEXT NOT NULL,
                            RoundJson TEXT,
                            RoundInt INTEGER,
                            MapId INTEGER,
                            TerrorIds TEXT
                        );";
                    command.ExecuteNonQuery();

                    command.CommandText = @"CREATE TABLE IF NOT EXISTS RoundResults (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            RoundType TEXT NOT NULL,
                            TerrorKey TEXT,
                            Survived INTEGER NOT NULL,
                            CreatedAt TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();

                    command.CommandText = @"CREATE TABLE IF NOT EXISTS Stats (
                            Name TEXT PRIMARY KEY,
                            Value TEXT,
                            ValueType TEXT,
                            UpdatedAt TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                });

                EnsureColumnExists("RoundLogs", "RoundInt", "INTEGER");
                EnsureColumnExists("RoundLogs", "MapId", "INTEGER");
                EnsureColumnExists("RoundLogs", "TerrorIds", "TEXT");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize round data database.");
            }
        }

        private void EnsureColumnExists(string tableName, string columnName, string columnDefinition)
        {
            try
            {
                lock (_connectionLock)
                {
                    bool exists = false;
                    using (var pragmaCommand = _connection.CreateCommand())
                    {
                        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";
                        using var reader = pragmaCommand.ExecuteReader();
                        while (reader.Read())
                        {
                            var existingName = reader.GetString(1);
                            if (string.Equals(existingName, columnName, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                    }

                    if (exists)
                    {
                        return;
                    }

                    using var alterCommand = _connection.CreateCommand();
                    alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
                    alterCommand.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to add column '{Column}' to table '{Table}'.", columnName, tableName);
            }
        }

        public void AddRoundLog(Round round, string logEntry, DateTime recordedAt)
        {
            try
            {
                ExecuteInTransaction(command =>
                {
                    command.CommandText = @"INSERT INTO RoundLogs (
                            RoundId,
                            RoundType,
                            TerrorKey,
                            MapName,
                            IsDeath,
                            Status,
                            CreatedAt,
                            RoundJson,
                            RoundInt,
                            MapId,
                            TerrorIds)
                        VALUES (
                            $roundId,
                            $roundType,
                            $terrorKey,
                            $mapName,
                            $isDeath,
                            $status,
                            $createdAt,
                            $roundJson,
                            $roundInt,
                            $mapId,
                            $terrorIds);";

                    command.Parameters.AddWithValue("$roundId", round.Id.Value.ToString());
                    command.Parameters.AddWithValue("$roundType", (object?)round.RoundType ?? DBNull.Value);
                    command.Parameters.AddWithValue("$terrorKey", (object?)round.TerrorKey ?? DBNull.Value);
                    command.Parameters.AddWithValue("$mapName", (object?)round.MapName ?? DBNull.Value);
                    command.Parameters.AddWithValue("$isDeath", round.IsDeath ? 1 : 0);
                    command.Parameters.AddWithValue("$status", logEntry);
                    command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));
                    command.Parameters.AddWithValue("$roundJson", JsonConvert.SerializeObject(round));
                    command.Parameters.AddWithValue("$roundInt", round.RoundNumber.HasValue ? (object)round.RoundNumber.Value : DBNull.Value);
                    command.Parameters.AddWithValue("$mapId", round.MapId.HasValue ? (object)round.MapId.Value : DBNull.Value);

                    string? terrorIdsJson = null;
                    if (round.TerrorIds != null && round.TerrorIds.Length > 0)
                    {
                        terrorIdsJson = JsonConvert.SerializeObject(round.TerrorIds);
                    }

                    command.Parameters.AddWithValue("$terrorIds", (object?)terrorIdsJson ?? DBNull.Value);

                    command.ExecuteNonQuery();
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist round log entry to SQLite.");
            }
        }

        public void RecordRoundResult(string roundType, string? terrorType, bool survived, DateTime recordedAt)
        {
            try
            {
                ExecuteInTransaction(command =>
                {
                    command.CommandText = @"INSERT INTO RoundResults (
                            RoundType,
                            TerrorKey,
                            Survived,
                            CreatedAt)
                        VALUES (
                            $roundType,
                            $terrorKey,
                            $survived,
                            $createdAt);";

                    command.Parameters.AddWithValue("$roundType", roundType);
                    command.Parameters.AddWithValue("$terrorKey", (object?)terrorType ?? DBNull.Value);
                    command.Parameters.AddWithValue("$survived", survived ? 1 : 0);
                    command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));

                    command.ExecuteNonQuery();
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist round result to SQLite.");
            }
        }

        public void UpsertStat(string name, object? value, DateTime recordedAt)
        {
            try
            {
                ExecuteInTransaction(command =>
                {
                    command.CommandText = @"INSERT INTO Stats (Name, Value, ValueType, UpdatedAt)
                        VALUES ($name, $value, $valueType, $updatedAt)
                        ON CONFLICT(Name) DO UPDATE SET
                            Value = excluded.Value,
                            ValueType = excluded.ValueType,
                            UpdatedAt = excluded.UpdatedAt;";

                    var serializedValue = value == null ? null : JsonConvert.SerializeObject(value);
                    var valueType = value?.GetType().FullName ?? string.Empty;

                    command.Parameters.AddWithValue("$name", name);
                    command.Parameters.AddWithValue("$value", (object?)serializedValue ?? DBNull.Value);
                    command.Parameters.AddWithValue("$valueType", valueType);
                    command.Parameters.AddWithValue("$updatedAt", recordedAt.ToString("o"));

                    command.ExecuteNonQuery();
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist statistic '{StatName}' to SQLite.", name);
            }
        }

        private void ExecuteInTransaction(Action<SqliteCommand> execute)
        {
            ThrowIfDisposed();

            lock (_connectionLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                execute(command);
                transaction.Commit();
                command.Parameters.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteRoundDataRepository));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_connectionLock)
            {
                if (_disposed)
                {
                    return;
                }

                if (_connection.State != ConnectionState.Closed)
                {
                    _connection.Close();
                }

                _connection.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}

