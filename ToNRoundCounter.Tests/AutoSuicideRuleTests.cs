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

        [Fact]
        public void Matches_NegatedGroup_Works()
        {
            var ok = AutoSuicideRule.TryParse("!(A||B)::1", out var rule);
            Assert.True(ok);
            Assert.True(rule.Matches("C", null, (a, b) => a == b));
            Assert.False(rule.Matches("A", null, (a, b) => a == b));
            Assert.False(rule.Matches("B", null, (a, b) => a == b));
        }

        [Fact]
        public void Matches_NegatedGroupWithFollowingExpression_Works()
        {
            var ok = AutoSuicideRule.TryParse("!(A||B)&&C::1", out var rule);
            Assert.True(ok);
            Assert.True(rule.Matches("C", null, (a, b) => a == b));
            Assert.False(rule.Matches("A", null, (a, b) => a == b));
        }

        [Fact]
        public void GetRoundTerms_ParsesNegatedGroup()
        {
            var ok = AutoSuicideRule.TryParse("!(A||B)::1", out var rule);
            Assert.True(ok);
            var rounds = rule.GetRoundTerms();
            Assert.NotNull(rounds);
            Assert.Equal(new[] { "A", "B" }, rounds);
        }

        [Fact]
        public void GetTerrorTerms_ParsesNegatedGroup()
        {
            var ok = AutoSuicideRule.TryParse(":!(X||Y):2", out var rule);
            Assert.True(ok);
            var terrors = rule.GetTerrorTerms();
            Assert.NotNull(terrors);
            Assert.Equal(new[] { "X", "Y" }, terrors);
        }

        [Fact]
        public void TryParse_AllowsNegatedComplexGroup()
        {
            var ok = AutoSuicideRule.TryParse("!((A&&B)||(!C&&D))::1", out var rule);
            Assert.True(ok);
            Assert.True(rule.RoundNegate);
            Assert.Equal("((A&&B)||(!C&&D))", rule.RoundExpression);
            Assert.False(rule.Matches("D", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsNestedNegations()
        {
            var ok = AutoSuicideRule.TryParse("!(!(A||B)&&C)::1", out var rule);
            Assert.True(ok);
            Assert.False(rule.Matches("C", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsEscapedColon()
        {
            var ok = AutoSuicideRule.TryParse(@"A\:B:C\:D:1", out var rule);
            Assert.True(ok);
            Assert.Equal("A:B", rule.Round);
            Assert.Equal("C:D", rule.Terror);
            Assert.Equal(1, rule.Value);
        }
    }
}
