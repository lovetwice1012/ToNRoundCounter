using System;
using System.IO;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure.Sqlite;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class SqliteRoundDataRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteRoundDataRepository _repository;

        public SqliteRoundDataRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
            _repository = new SqliteRoundDataRepository(_dbPath);
        }

        public void Dispose()
        {
            _repository.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        [Fact]
        public void AddRoundLog_DoesNotThrow()
        {
            var round = new Round { RoundType = "Classic", TerrorKey = "Tornado", MapName = "Asylum" };
            var ex = Record.Exception(() => _repository.AddRoundLog(round, "test entry", DateTime.UtcNow));
            Assert.Null(ex);
        }

        [Fact]
        public void RecordRoundResult_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repository.RecordRoundResult("Classic", "Tornado", true, DateTime.UtcNow));
            Assert.Null(ex);
        }

        [Fact]
        public void UpsertStat_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repository.UpsertStat("totalRounds", 5, DateTime.UtcNow));
            Assert.Null(ex);
        }
    }
}
