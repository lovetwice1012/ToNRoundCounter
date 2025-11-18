using ToNRoundCounter.Application;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class StateServiceTests
    {
        [Fact]
        public void RecordRoundResult_UpdatesAggregates()
        {
            var service = new StateService();
            service.RecordRoundResult("RoundA", "Terror1", true);
            service.RecordRoundResult("RoundA", "Terror1", false);

            var roundAgg = service.GetRoundAggregates()["RoundA"];
            Assert.Equal(2, roundAgg.Total);
            Assert.Equal(1, roundAgg.Survival);
            Assert.Equal(1, roundAgg.Death);

            Assert.True(service.TryGetTerrorAggregates("RoundA", out var terrorDict));
            Assert.NotNull(terrorDict);
            Assert.True(terrorDict!.ContainsKey("Terror1"));
            var terrorAgg = terrorDict!["Terror1"];
            Assert.Equal(2, terrorAgg.Total);
            Assert.Equal(1, terrorAgg.Survival);
            Assert.Equal(1, terrorAgg.Death);
        }

        [Fact]
        public void UpdateStat_StoresValue()
        {
            var service = new StateService();
            service.UpdateStat("Survivals", 5);
            var stats = service.GetStats();
            Assert.True(stats.ContainsKey("Survivals"));
            Assert.Equal(5, stats["Survivals"]);
            service.Reset();
            Assert.Empty(service.GetStats());
        }

        [Fact]
        public void UpdateCurrentRound_StoresPreviousRoundSnapshot()
        {
            var service = new StateService();
            var round = new Round
            {
                RoundType = "Alpha",
                MapName = "Sanctuary",
                TerrorKey = "Phantom",
                Damage = 27,
                PageCount = 3,
                IsDeath = true,
                RoundColor = 0x112233
            };
            round.ItemNames.Add("Lantern");

            service.UpdateCurrentRound(round);
            service.UpdateCurrentRound(null);

            // Mutate the original to ensure a deep copy was stored.
            round.MapName = "Changed";
            round.ItemNames.Add("Sword");
            round.Damage = 99;

            var previous = service.PreviousRound;

            Assert.NotNull(previous);
            Assert.NotSame(round, previous);
            Assert.Equal("Alpha", previous!.RoundType);
            Assert.Equal("Sanctuary", previous.MapName);
            Assert.Equal("Phantom", previous.TerrorKey);
            Assert.Equal(27, previous.Damage);
            Assert.Equal(3, previous.PageCount);
            Assert.True(previous.IsDeath);
            Assert.Equal(0x112233, previous.RoundColor);
            Assert.Single(previous.ItemNames);
            Assert.Equal("Lantern", previous.ItemNames[0]);

            var snapshotBeforeNewRound = service.PreviousRound;
            service.UpdateCurrentRound(new Round { RoundType = "Beta" });

            Assert.Same(snapshotBeforeNewRound, service.PreviousRound);
            Assert.Equal("Alpha", service.PreviousRound!.RoundType);
        }

        [Fact]
        public void RoundCycle_IncrementAndSet()
        {
            var service = new StateService();
            Assert.Equal(0, service.RoundCycle);

            service.IncrementRoundCycle();
            Assert.Equal(1, service.RoundCycle);

            service.IncrementRoundCycle();
            service.IncrementRoundCycle();
            Assert.Equal(3, service.RoundCycle);

            service.SetRoundCycle(10);
            Assert.Equal(10, service.RoundCycle);

            service.SetRoundCycle(0);
            Assert.Equal(0, service.RoundCycle);
        }

        [Fact]
        public void RecordRoundResult_WithNullTerror_TracksCorrectly()
        {
            var service = new StateService();
            service.RecordRoundResult("ClassicRound", null, true);
            service.RecordRoundResult("ClassicRound", null, false);

            var roundAgg = service.GetRoundAggregates()["ClassicRound"];
            Assert.Equal(2, roundAgg.Total);
            Assert.Equal(1, roundAgg.Survival);
            Assert.Equal(1, roundAgg.Death);
        }

        [Fact]
        public void MultipleRoundTypes_TrackedSeparately()
        {
            var service = new StateService();
            service.RecordRoundResult("Run", "Ghost", true);
            service.RecordRoundResult("Run", "Ghost", true);
            service.RecordRoundResult("Alternate", "Jester", false);
            service.RecordRoundResult("Sabotage", null, true);

            var aggregates = service.GetRoundAggregates();
            Assert.Equal(3, aggregates.Count);

            Assert.Equal(2, aggregates["Run"].Total);
            Assert.Equal(2, aggregates["Run"].Survival);

            Assert.Equal(1, aggregates["Alternate"].Total);
            Assert.Equal(1, aggregates["Alternate"].Death);

            Assert.Equal(1, aggregates["Sabotage"].Total);
            Assert.Equal(1, aggregates["Sabotage"].Survival);
        }

        [Fact]
        public void AddRoundLog_StoresEntryCorrectly()
        {
            var service = new StateService();
            var round = new Round
            {
                RoundType = "Test",
                TerrorKey = "TestTerror"
            };

            bool logAdded = false;
            service.RoundLogAdded += (r, log) =>
            {
                logAdded = true;
                Assert.Equal("Test", r.RoundType);
                Assert.Equal("Test log entry", log);
            };

            service.AddRoundLog(round, "Test log entry");
            Assert.True(logAdded);
        }

        [Fact]
        public void StateChanged_EventFiredOnChanges()
        {
            var service = new StateService();
            int eventCount = 0;

            service.StateChanged += () =>
            {
                eventCount++;
            };

            service.UpdateStat("TestStat", 42);
            Assert.Equal(1, eventCount);

            service.IncrementRoundCycle();
            Assert.Equal(2, eventCount);
        }

        [Fact]
        public void Reset_ClearsAllData()
        {
            var service = new StateService();
            service.RecordRoundResult("TestRound", "TestTerror", true);
            service.UpdateStat("Stat1", 10);
            service.IncrementRoundCycle();

            service.Reset();

            Assert.Empty(service.GetRoundAggregates());
            Assert.Empty(service.GetStats());
            Assert.Equal(0, service.RoundCycle);
        }
    }
}
