using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class RoundAggregateTests
    {
        [Fact]
        public void SurvivalRate_IsZeroWhenTotalZero()
        {
            var agg = new RoundAggregate();
            Assert.Equal(0, agg.SurvivalRate);
        }

        [Fact]
        public void SurvivalRate_ComputesPercentage()
        {
            var agg = new RoundAggregate { Total = 4, Survival = 1, Death = 3 };
            Assert.Equal(25, agg.SurvivalRate);
        }
    }
}
