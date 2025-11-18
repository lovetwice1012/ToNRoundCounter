using System;
using System.IO;
using System.Linq;
using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using ToNRoundCounter.Infrastructure.Sqlite;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for SqliteRoundDataRepository functionality.
    /// </summary>
    public class SqliteRoundDataRepositoryTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly SqliteRoundDataRepository _repository;
        private readonly MockEventLogger _logger;

        public SqliteRoundDataRepositoryTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_rounds_{Guid.NewGuid()}.db");
            _logger = new MockEventLogger();
            _repository = new SqliteRoundDataRepository(_testDbPath, _logger);
        }

        public void Dispose()
        {
            _repository?.Dispose();
            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        [Fact]
        public void Initialize_CreatesDatabase()
        {
            Assert.True(File.Exists(_testDbPath));
        }

        [Fact]
        public void AddRoundLog_InsertsRound()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum",
                IsDeath = false,
                Damage = 0
            };

            _repository.AddRoundLog(snapshot, "Test log entry", DateTime.UtcNow);

            var rounds = _repository.GetAllRounds();
            Assert.Single(rounds);
            Assert.Equal("Classic", rounds[0].RoundType);
            Assert.Equal("Tornado", rounds[0].TerrorKey);
        }

        [Fact]
        public void AddRoundLog_MultipleRounds_AllAdded()
        {
            var snapshot1 = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            var snapshot2 = new RoundSnapshot
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School"
            };

            _repository.AddRoundLog(snapshot1, "Log 1", DateTime.UtcNow);
            _repository.AddRoundLog(snapshot2, "Log 2", DateTime.UtcNow);

            var rounds = _repository.GetAllRounds();
            Assert.Equal(2, rounds.Count);
        }

        [Fact]
        public void GetAllRounds_EmptyDatabase_ReturnsEmptyList()
        {
            var rounds = _repository.GetAllRounds();

            Assert.NotNull(rounds);
            Assert.Empty(rounds);
        }

        [Fact]
        public void GetAllRounds_OrderedByTimestamp()
        {
            var now = DateTime.UtcNow;

            var snapshot1 = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            var snapshot2 = new RoundSnapshot
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School"
            };

            var snapshot3 = new RoundSnapshot
            {
                RoundType = "Cracked",
                TerrorKey = "Sabotage",
                MapName = "Hospital"
            };

            _repository.AddRoundLog(snapshot1, "Log 1", now.AddMinutes(-10));
            _repository.AddRoundLog(snapshot2, "Log 2", now.AddMinutes(-5));
            _repository.AddRoundLog(snapshot3, "Log 3", now);

            var rounds = _repository.GetAllRounds();

            Assert.Equal(3, rounds.Count);
            Assert.Equal("Classic", rounds[0].RoundType);
            Assert.Equal("Run", rounds[1].RoundType);
            Assert.Equal("Cracked", rounds[2].RoundType);
        }

        [Fact]
        public void AddRoundLog_PreservesAllFields()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School",
                IsDeath = true,
                Damage = 42,
                TerrorDetail = "Test detail",
                TerrorAppearance = "Scary"
            };

            _repository.AddRoundLog(snapshot, "Complete log", DateTime.UtcNow);

            var rounds = _repository.GetAllRounds();
            var round = rounds[0];

            Assert.Equal("Run", round.RoundType);
            Assert.Equal("Ghost", round.TerrorKey);
            Assert.Equal("School", round.MapName);
            Assert.True(round.IsDeath);
            Assert.Equal(42, round.Damage);
            Assert.Equal("Test detail", round.TerrorDetail);
            Assert.Equal("Scary", round.TerrorAppearance);
        }

        [Fact]
        public void ClearAllRounds_RemovesAllData()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            _repository.AddRoundLog(snapshot, "Log 1", DateTime.UtcNow);
            _repository.AddRoundLog(snapshot, "Log 2", DateTime.UtcNow);

            Assert.Equal(2, _repository.GetAllRounds().Count);

            _repository.ClearAllRounds();

            Assert.Empty(_repository.GetAllRounds());
        }

        [Fact]
        public void GetRoundsSince_FiltersCorrectly()
        {
            var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var snapshot = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            _repository.AddRoundLog(snapshot, "Old log", baseTime.AddHours(-2));
            _repository.AddRoundLog(snapshot, "Recent log 1", baseTime.AddMinutes(-30));
            _repository.AddRoundLog(snapshot, "Recent log 2", baseTime);

            var cutoff = baseTime.AddHours(-1);
            var recentRounds = _repository.GetRoundsSince(cutoff);

            Assert.Equal(2, recentRounds.Count);
        }

        [Fact]
        public void GetRoundCountByType_CountsCorrectly()
        {
            var classic = new RoundSnapshot { RoundType = "Classic", TerrorKey = "Tornado", MapName = "Asylum" };
            var run = new RoundSnapshot { RoundType = "Run", TerrorKey = "Ghost", MapName = "School" };

            _repository.AddRoundLog(classic, "Log 1", DateTime.UtcNow);
            _repository.AddRoundLog(classic, "Log 2", DateTime.UtcNow);
            _repository.AddRoundLog(run, "Log 3", DateTime.UtcNow);
            _repository.AddRoundLog(classic, "Log 4", DateTime.UtcNow);

            var counts = _repository.GetRoundCountByType();

            Assert.Equal(2, counts.Count);
            Assert.Equal(3, counts["Classic"]);
            Assert.Equal(1, counts["Run"]);
        }

        [Fact]
        public void GetRoundCountByType_EmptyDatabase_ReturnsEmptyDictionary()
        {
            var counts = _repository.GetRoundCountByType();

            Assert.NotNull(counts);
            Assert.Empty(counts);
        }

        [Fact]
        public void AddRoundLog_WithNullValues_HandlesGracefully()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = null,
                MapName = null,
                TerrorDetail = null,
                TerrorAppearance = null
            };

            _repository.AddRoundLog(snapshot, "Null test", DateTime.UtcNow);

            var rounds = _repository.GetAllRounds();
            Assert.Single(rounds);
            Assert.Equal("Classic", rounds[0].RoundType);
        }

        [Fact]
        public void AddRoundLog_WithSpecialCharacters_StoresCorrectly()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Special's \"Round\"",
                TerrorKey = "Terror with <tags>",
                MapName = "Map & More",
                TerrorDetail = "Detail with 'quotes'"
            };

            _repository.AddRoundLog(snapshot, "Special chars test", DateTime.UtcNow);

            var rounds = _repository.GetAllRounds();
            var round = rounds[0];

            Assert.Equal("Special's \"Round\"", round.RoundType);
            Assert.Equal("Terror with <tags>", round.TerrorKey);
            Assert.Equal("Map & More", round.MapName);
        }

        [Fact]
        public void GetRoundsWithFilter_FiltersCorrectly()
        {
            var classic = new RoundSnapshot { RoundType = "Classic", TerrorKey = "Tornado", MapName = "Asylum" };
            var run = new RoundSnapshot { RoundType = "Run", TerrorKey = "Ghost", MapName = "School" };

            _repository.AddRoundLog(classic, "Log 1", DateTime.UtcNow);
            _repository.AddRoundLog(run, "Log 2", DateTime.UtcNow);
            _repository.AddRoundLog(classic, "Log 3", DateTime.UtcNow);

            var classicRounds = _repository.GetRoundsWithFilter("Classic");

            Assert.Equal(2, classicRounds.Count);
            Assert.All(classicRounds, r => Assert.Equal("Classic", r.RoundType));
        }

        [Fact]
        public void ExportRounds_ReturnsAllRounds()
        {
            var snapshot1 = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            var snapshot2 = new RoundSnapshot
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School"
            };

            _repository.AddRoundLog(snapshot1, "Log 1", DateTime.UtcNow);
            _repository.AddRoundLog(snapshot2, "Log 2", DateTime.UtcNow);

            var exported = _repository.ExportRounds();

            Assert.NotNull(exported);
            Assert.Equal(2, exported.Count);
        }

        // Mock implementation
        private class MockEventLogger : IEventLogger
        {
            public void LogEvent(string category, string message, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
            }

            public void LogEvent(string category, Func<string> messageFactory, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
            {
            }
        }
    }
}
