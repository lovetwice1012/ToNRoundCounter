using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure.Sqlite
{
    public class SqliteEventLogRepository : IEventLogRepository, IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;
        private readonly Channel<LogEntry> _logChannel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _processingTask;
        private int _disposed;

        private const int DefaultBatchSize = 50;

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

            _connection = new SqliteConnection(_connectionString);
            try
            {
                _connection.Open();
                ApplyPragmas(_connection);
                EnsureSchema(_connection);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize event log database.");
            }

            _logChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        private static void ApplyPragmas(SqliteConnection connection)
        {
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
        }

        private static void EnsureSchema(SqliteConnection connection)
        {
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

        public void WriteLog(string eventType, string message, LogEventLevel level, DateTime recordedAt)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var entry = new LogEntry(eventType, message, level.ToString(), recordedAt.ToString("o"));
            if (!_logChannel.Writer.TryWrite(entry))
            {
                Log.Warning("Failed to enqueue event log '{EventType}'.", eventType);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            var batch = new List<LogEntry>(DefaultBatchSize);
            try
            {
                while (await _logChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    DrainToBatch(batch);
                    FlushBatch(batch);
                }

                FlushBatch(batch);
            }
            catch (OperationCanceledException)
            {
                DrainToBatch(batch);
                FlushBatch(batch);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Event log processing loop failed.");
            }
        }

        private void DrainToBatch(List<LogEntry> batch)
        {
            while (_logChannel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
                if (batch.Count >= DefaultBatchSize)
                {
                    FlushBatch(batch);
                }
            }
        }

        private void FlushBatch(List<LogEntry> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            var count = batch.Count;

            try
            {
                using var transaction = _connection.BeginTransaction();
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO EventLogs (EventType, Message, Level, CreatedAt)
                    VALUES ($eventType, $message, $level, $createdAt);";

                var eventTypeParam = command.CreateParameter();
                eventTypeParam.ParameterName = "$eventType";
                command.Parameters.Add(eventTypeParam);

                var messageParam = command.CreateParameter();
                messageParam.ParameterName = "$message";
                command.Parameters.Add(messageParam);

                var levelParam = command.CreateParameter();
                levelParam.ParameterName = "$level";
                command.Parameters.Add(levelParam);

                var createdAtParam = command.CreateParameter();
                createdAtParam.ParameterName = "$createdAt";
                command.Parameters.Add(createdAtParam);

                foreach (var entry in batch)
                {
                    eventTypeParam.Value = entry.EventType;
                    messageParam.Value = entry.Message;
                    levelParam.Value = entry.Level;
                    createdAtParam.Value = entry.CreatedAt;
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist {Count} event log(s) to SQLite.", count);
            }
            finally
            {
                batch.Clear();
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _logChannel.Writer.TryComplete();
            _cts.Cancel();

            try
            {
                await _processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Event log processing loop terminated with an error during disposal.");
            }
            finally
            {
                _cts.Dispose();
                _connection.Close();
                _connection.Dispose();
            }
        }

        private readonly record struct LogEntry(string EventType, string Message, string Level, string CreatedAt);
    }
}

