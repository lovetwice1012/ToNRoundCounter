using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class AutoSuicideRuleTests
    {
        [Fact]
        public void TryParse_ParsesValidRule()
        {
            var ok = AutoSuicideRule.TryParse("A:B:1", out var rule);
            Assert.True(ok);
            Assert.Equal("A", rule.Round);
            Assert.Equal("B", rule.Terror);
            Assert.Equal(1, rule.Value);
        }
    }
}
