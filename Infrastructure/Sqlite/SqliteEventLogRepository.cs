using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteEventLogRepository : IEventLogRepository
    {
        private readonly string _connectionString;

        public SqliteEventLogRepository(string databasePath)
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 5
            };

            _connectionString = connectionStringBuilder.ToString();

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using (var journalCommand = connection.CreateCommand())
                {
                    journalCommand.CommandText = "PRAGMA journal_mode=WAL;";
                    journalCommand.ExecuteNonQuery();
                }

                using (var busyTimeoutCommand = connection.CreateCommand())
                {
                    busyTimeoutCommand.CommandText = "PRAGMA busy_timeout=5000;";
                    busyTimeoutCommand.ExecuteNonQuery();
                }

                using var command = connection.CreateCommand();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS EventLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventType TEXT NOT NULL,
                        Message TEXT NOT NULL,
                        Level TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize event log database.");
            }
        }

        public void WriteLog(string eventType, string message, LogEventLevel level, DateTime recordedAt)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var busyTimeoutCommand = connection.CreateCommand())
                {
                    busyTimeoutCommand.CommandText = "PRAGMA busy_timeout=5000;";
                    busyTimeoutCommand.ExecuteNonQuery();
                }

                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO EventLogs (EventType, Message, Level, CreatedAt)
                    VALUES ($eventType, $message, $level, $createdAt);";

                command.Parameters.AddWithValue("$eventType", eventType);
                command.Parameters.AddWithValue("$message", message);
                command.Parameters.AddWithValue("$level", level.ToString());
                command.Parameters.AddWithValue("$createdAt", recordedAt.ToString("o"));

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist event log '{EventType}' to SQLite.", eventType);
            }
        }
    }
}

