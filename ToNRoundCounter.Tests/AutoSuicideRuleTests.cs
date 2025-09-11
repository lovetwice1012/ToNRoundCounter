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
        public void TryParse_ValueOnlyLine_Parses()
        {
            var ok = AutoSuicideRule.TryParse("1", out var rule);
            Assert.True(ok);
            Assert.Null(rule.RoundExpression);
            Assert.Null(rule.TerrorExpression);
            Assert.Equal(1, rule.Value);
        }

        [Fact]
        public void TryParse_InvalidSegmentCount_ReturnsFalse()
        {
            var ok = AutoSuicideRule.TryParse("A:B:C:D", out var _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_InvalidValue_ReturnsFalse()
        {
            Assert.False(AutoSuicideRule.TryParse("A:B:3", out _));
            Assert.False(AutoSuicideRule.TryParse("3", out _));
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

        [Fact]
        public void ToString_PreservesEscapes()
        {
            AutoSuicideRule.TryParse(@"A\:B:C\\D:1", out var rule);
            Assert.Equal(@"A\:B:C\\D:1", rule.ToString());
        }

        [Fact]
        public void TryParseDetailed_ReturnsSegmentError()
        {
            var ok = AutoSuicideRule.TryParseDetailed("A:1", out var _, out var err);
            Assert.False(ok);
            Assert.Equal("セグメント数不正", err);
        }

        [Fact]
        public void TryParseDetailed_ReturnsValueError()
        {
            var ok = AutoSuicideRule.TryParseDetailed("A:B:5", out var _, out var err);
            Assert.False(ok);
            Assert.Equal("値が 0/1/2 以外", err);
        }

        [Fact]
        public void TryParseDetailed_ReturnsExpressionError()
        {
            var ok = AutoSuicideRule.TryParseDetailed("(A:B:1", out var _, out var err);
            Assert.False(ok);
            Assert.Equal("括弧の不整合や演算子の誤用", err);
        }

        [Fact]
        public void Covers_DetectsBroaderRules()
        {
            AutoSuicideRule.TryParse("A::1", out var broad);
            AutoSuicideRule.TryParse("A:B:0", out var specific);

            Assert.True(broad.Covers(specific));
            Assert.False(specific.Covers(broad));
        }
    }
}
