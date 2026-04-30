using ToNRoundCounter.UI;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class CoordinatedAutoSuicideMatchingTests
    {
        [Theory]
        [InlineData("", "ブラッドバス", true)]
        [InlineData("*", "ブラッドバス", true)]
        [InlineData("all", "ブラッドバス", true)]
        [InlineData("オルタネイト", "オルタネイト", true)]
        [InlineData("オルタネイト", "クラシック", false)]
        public void EntryMatchesRound_AllowsWildcardRounds(string entryRound, string actualRound, bool expected)
        {
            Assert.Equal(expected, MainForm.EntryMatchesRound(entryRound, actualRound));
        }

        [Theory]
        [InlineData("", "", false)]
        [InlineData("*", "all", false)]
        [InlineData("", "ブラッドバス", true)]
        [InlineData("Ao Oni", "", true)]
        public void EntryHasCoordinatedTarget_RequiresAtLeastOneScopedAxis(string entryTerror, string entryRound, bool expected)
        {
            Assert.Equal(expected, MainForm.EntryHasCoordinatedTarget(entryRound, entryTerror));
        }

        [Theory]
        [InlineData("", null, true)]
        [InlineData("*", "Ao Oni", true)]
        [InlineData("Ao Oni", "Ao Oni", true)]
        [InlineData("Ao Oni", "Something & Ao Oni", true)]
        [InlineData("Ao Oni", null, false)]
        [InlineData("Ao Oni", "TBH", false)]
        public void EntryMatchesTerror_AllowsAllTerrorWildcard(string entryTerror, string? actualTerror, bool expected)
        {
            Assert.Equal(expected, MainForm.EntryMatchesTerror(entryTerror, actualTerror));
        }
    }
}
