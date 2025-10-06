using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Serilog;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteRoundDataRepository : IRoundDataRepository
    {
        private readonly string _connectionString;

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

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var command = connection.CreateCommand())
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
                            RoundJson TEXT
                        );";
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS RoundResults (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            RoundType TEXT NOT NULL,
                            TerrorKey TEXT,
                            Survived INTEGER NOT NULL,
                            CreatedAt TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS Stats (
                            Name TEXT PRIMARY KEY,
                            Value TEXT,
                            ValueType TEXT,
                            UpdatedAt TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize round data database.");
            }
        }

        public void AddRoundLog(Round round, string logEntry, DateTime recordedAt)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO RoundLogs (
                        RoundId,
                        RoundType,
                        TerrorKey,
                        MapName,
                        IsDeath,
                        Status,
                        CreatedAt,
                        RoundJson)
                    VALUES (
                        $roundId,
                        $roundType,
                        $terrorKey,
                        $mapName,
                        $isDeath,
                        $status,
                        $createdAt,
                        $roundJson);";

                command.Parameters.AddWithValue("$roundId", round.Id.Value.ToString());
                command.Parameters.AddWithValue("$roundType", (object?)round.RoundType ?? DBNull.Value);
                command.Parameters.AddWithValue("$terrorKey", (object?)round.TerrorKey ?? DBNull.Value);
                command.Parameters.AddWithValue("$mapName", (object?)round.MapName ?? DBNull.Value);
                command.Parameters.AddWithValue("$isDeath", round.IsDeath ? 1 : 0);
                command.Parameters.AddWithValue("$status", logEntry);
                command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));
                command.Parameters.AddWithValue("$roundJson", JsonConvert.SerializeObject(round));

                command.ExecuteNonQuery();
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
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
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
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
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
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist statistic '{StatName}' to SQLite.", name);
            }
        }
    }
}

