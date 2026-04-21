using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Serilog;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteSettingsRepository : ISettingsRepository, IDisposable
    {
        private readonly string _connectionString;
        // Persistent connection to avoid repeated open/close overhead. SaveSnapshot can be
        // called ~10-15 times/min during coalesced saves; each new SqliteConnection incurs
        // pool lookup, PRAGMA replay, and journal init.
        private readonly SqliteConnection _connection;
        private readonly object _connectionLock = new object();
        private bool _disposed;

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

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                lock (_connectionLock)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = @"CREATE TABLE IF NOT EXISTS SettingsSnapshots (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Content TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );";
                    command.ExecuteNonQuery();
                }
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
                lock (_connectionLock)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = @"SELECT Content FROM SettingsSnapshots ORDER BY Id DESC LIMIT 1;";
                    var result = command.ExecuteScalar();
                    return result == null || result == DBNull.Value ? null : Convert.ToString(result);
                }
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
                lock (_connectionLock)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = @"INSERT INTO SettingsSnapshots (Content, CreatedAt)
                    VALUES ($content, $createdAt);";

                    command.Parameters.AddWithValue("$content", json);
                    command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist settings snapshot to SQLite.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                lock (_connectionLock)
                {
                    _connection.Dispose();
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}

