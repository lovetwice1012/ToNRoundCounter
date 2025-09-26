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
            var parsedRule = Assert.NotNull(rule);
            Assert.Equal("A", parsedRule.Round);
            Assert.Equal("B", parsedRule.Terror);
            Assert.Equal(1, parsedRule.Value);
        }

        [Fact]
        public void TryParse_ValueOnlyLine_Parses()
        {
            var ok = AutoSuicideRule.TryParse("1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            Assert.Null(parsedRule.RoundExpression);
            Assert.Null(parsedRule.TerrorExpression);
            Assert.Equal(1, parsedRule.Value);
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
            var parsedRule = Assert.NotNull(rule);
            Assert.True(parsedRule.Matches("C", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("A", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("B", null, (a, b) => a == b));
        }

        [Fact]
        public void Matches_NegatedGroupWithFollowingExpression_Works()
        {
            var ok = AutoSuicideRule.TryParse("!(A||B)&&C::1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            Assert.True(parsedRule.Matches("C", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("A", null, (a, b) => a == b));
        }

        [Fact]
        public void GetRoundTerms_ParsesNegatedGroup()
        {
            var ok = AutoSuicideRule.TryParse("!(A||B)::1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            var rounds = parsedRule.GetRoundTerms();
            Assert.NotNull(rounds);
            Assert.Equal(new[] { "A", "B" }, rounds);
        }

        [Fact]
        public void GetTerrorTerms_ParsesNegatedGroup()
        {
            var ok = AutoSuicideRule.TryParse(":!(X||Y):2", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            var terrors = parsedRule.GetTerrorTerms();
            Assert.NotNull(terrors);
            Assert.Equal(new[] { "X", "Y" }, terrors);
        }

        [Fact]
        public void TryParse_AllowsNegatedComplexGroup()
        {
            var ok = AutoSuicideRule.TryParse("!((A&&B)||(!C&&D))::1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            Assert.True(parsedRule.RoundNegate);
            Assert.Equal("((A&&B)||(!C&&D))", parsedRule.RoundExpression);
            Assert.False(parsedRule.Matches("D", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsNestedNegations()
        {
            var ok = AutoSuicideRule.TryParse("!(!(A||B)&&C)::1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            Assert.False(parsedRule.Matches("C", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsEscapedColon()
        {
            var ok = AutoSuicideRule.TryParse(@"A\:B:C\:D:1", out var rule);
            Assert.True(ok);
            var parsedRule = Assert.NotNull(rule);
            Assert.Equal("A:B", parsedRule.Round);
            Assert.Equal("C:D", parsedRule.Terror);
            Assert.Equal(1, parsedRule.Value);
        }

        [Fact]
        public void ToString_PreservesEscapes()
        {
            AutoSuicideRule.TryParse(@"A\:B:C\\D:1", out var rule);
            var parsedRule = Assert.NotNull(rule);
            Assert.Equal(@"A\:B:C\\D:1", parsedRule.ToString());
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

            var broadRule = Assert.NotNull(broad);
            var specificRule = Assert.NotNull(specific);

            Assert.True(broadRule.Covers(specificRule));
            Assert.False(specificRule.Covers(broadRule));
        }
    }
}
