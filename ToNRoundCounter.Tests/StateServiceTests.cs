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
    }
}
