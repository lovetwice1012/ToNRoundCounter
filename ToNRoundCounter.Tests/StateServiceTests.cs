using ToNRoundCounter.Application;
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
    }
}
