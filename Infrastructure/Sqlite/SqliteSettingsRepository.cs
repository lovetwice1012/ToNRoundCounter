using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Serilog;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteSettingsRepository : ISettingsRepository
    {
        private readonly string _connectionString;

        public SqliteSettingsRepository(string databasePath)
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

                using var command = connection.CreateCommand();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS SettingsSnapshots (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Content TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize settings database.");
            }
        }

        public string? LoadLatest()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"SELECT Content FROM SettingsSnapshots ORDER BY Id DESC LIMIT 1;";
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : Convert.ToString(result);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings snapshot from SQLite.");
                return null;
            }
        }

        public void SaveSnapshot(string json, DateTime recordedAt)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO SettingsSnapshots (Content, CreatedAt)
                    VALUES ($content, $createdAt);";

                command.Parameters.AddWithValue("$content", json);
                command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist settings snapshot to SQLite.");
            }
        }
    }
}

