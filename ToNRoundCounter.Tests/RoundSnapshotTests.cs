using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class RoundSnapshotTests
    {
        [Fact]
        public void Constructor_InitializesDefaults()
        {
            var round = new Round();

            Assert.NotNull(round);
            Assert.Null(round.RoundType);
            Assert.Null(round.TerrorKey);
            Assert.Null(round.MapName);
            Assert.False(round.IsDeath);
            Assert.Equal(0, round.Damage);
        }

        [Fact]
        public void Properties_RoundTrip()
        {
            var round = new Round
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum",
                IsDeath = true,
                Damage = 42
            };

            Assert.Equal("Classic", round.RoundType);
            Assert.Equal("Tornado", round.TerrorKey);
            Assert.Equal("Asylum", round.MapName);
            Assert.True(round.IsDeath);
            Assert.Equal(42, round.Damage);
        }
    }
}
