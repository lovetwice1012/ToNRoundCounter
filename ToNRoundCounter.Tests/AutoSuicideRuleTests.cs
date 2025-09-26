using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class AutoSuicideRuleTests
    {
        [Fact]
        public void TryParse_ParsesValidRule()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("A:B:1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.Equal("A", parsedRule.Round);
            Assert.Equal("B", parsedRule.Terror);
            Assert.Equal(1, parsedRule.Value);
        }

        [Fact]
        public void TryParse_ValueOnlyLine_Parses()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.Null(parsedRule.RoundExpression);
            Assert.Null(parsedRule.TerrorExpression);
            Assert.Equal(1, parsedRule.Value);
        }

        [Fact]
        public void TryParse_InvalidSegmentCount_ReturnsFalse()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("A:B:C:D", out rule);
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
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("!(A||B)::1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.True(parsedRule.Matches("C", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("A", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("B", null, (a, b) => a == b));
        }

        [Fact]
        public void Matches_NegatedGroupWithFollowingExpression_Works()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("!(A||B)&&C::1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.True(parsedRule.Matches("C", null, (a, b) => a == b));
            Assert.False(parsedRule.Matches("A", null, (a, b) => a == b));
        }

        [Fact]
        public void GetRoundTerms_ParsesNegatedGroup()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("!(A||B)::1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            var rounds = parsedRule.GetRoundTerms();
            Assert.NotNull(rounds);
            Assert.Equal(new[] { "A", "B" }, rounds);
        }

        [Fact]
        public void GetTerrorTerms_ParsesNegatedGroup()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse(":!(X||Y):2", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            var terrors = parsedRule.GetTerrorTerms();
            Assert.NotNull(terrors);
            Assert.Equal(new[] { "X", "Y" }, terrors);
        }

        [Fact]
        public void TryParse_AllowsNegatedComplexGroup()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("!((A&&B)||(!C&&D))::1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.True(parsedRule.RoundNegate);
            Assert.Equal("((A&&B)||(!C&&D))", parsedRule.RoundExpression);
            Assert.False(parsedRule.Matches("D", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsNestedNegations()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse("!(!(A||B)&&C)::1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.False(parsedRule.Matches("C", null, (a, b) => a == b));
        }

        [Fact]
        public void TryParse_AllowsEscapedColon()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParse(@"A\:B:C\:D:1", out rule);
            Assert.True(ok);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.Equal("A:B", parsedRule.Round);
            Assert.Equal("C:D", parsedRule.Terror);
            Assert.Equal(1, parsedRule.Value);
        }

        [Fact]
        public void ToString_PreservesEscapes()
        {
            AutoSuicideRule? rule;
            AutoSuicideRule.TryParse(@"A\:B:C\\D:1", out rule);
            var parsedRule = Assert.IsType<AutoSuicideRule>(rule);
            Assert.Equal(@"A\:B:C\\D:1", parsedRule.ToString());
        }

        [Fact]
        public void TryParseDetailed_ReturnsSegmentError()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParseDetailed("A:1", out rule, out var err);
            Assert.False(ok);
            Assert.Equal("セグメント数不正", err);
        }

        [Fact]
        public void TryParseDetailed_ReturnsValueError()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParseDetailed("A:B:5", out rule, out var err);
            Assert.False(ok);
            Assert.Equal("値が 0/1/2 以外", err);
        }

        [Fact]
        public void TryParseDetailed_ReturnsExpressionError()
        {
            AutoSuicideRule? rule;
            var ok = AutoSuicideRule.TryParseDetailed("(A:B:1", out rule, out var err);
            Assert.False(ok);
            Assert.Equal("括弧の不整合や演算子の誤用", err);
        }

        [Fact]
        public void Covers_DetectsBroaderRules()
        {
            AutoSuicideRule.TryParse("A::1", out var broad);
            AutoSuicideRule.TryParse("A:B:0", out var specific);

            var broadRule = Assert.IsType<AutoSuicideRule>(broad);
            var specificRule = Assert.IsType<AutoSuicideRule>(specific);

            Assert.True(broadRule.Covers(specificRule));
            Assert.False(specificRule.Covers(broadRule));
        }
    }
}
