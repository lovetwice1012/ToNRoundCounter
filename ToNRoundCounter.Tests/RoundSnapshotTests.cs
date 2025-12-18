using System;
using ToNRoundCounter.Domain;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for RoundSnapshot functionality.
    /// </summary>
    public class RoundSnapshotTests
    {
        [Fact]
        public void Constructor_InitializesDefaults()
        {
            var snapshot = new RoundSnapshot();

            Assert.NotNull(snapshot);
            Assert.Null(snapshot.RoundType);
            Assert.Null(snapshot.TerrorKey);
            Assert.Null(snapshot.MapName);
            Assert.False(snapshot.IsDeath);
            Assert.Equal(0, snapshot.Damage);
        }

        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var original = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum",
                IsDeath = true,
                Damage = 42,
                TerrorDetail = "Detail",
                TerrorAppearance = "Scary"
            };

            var clone = original.Clone();

            Assert.NotSame(original, clone);
            Assert.Equal(original.RoundType, clone.RoundType);
            Assert.Equal(original.TerrorKey, clone.TerrorKey);
            Assert.Equal(original.MapName, clone.MapName);
            Assert.Equal(original.IsDeath, clone.IsDeath);
            Assert.Equal(original.Damage, clone.Damage);
            Assert.Equal(original.TerrorDetail, clone.TerrorDetail);
            Assert.Equal(original.TerrorAppearance, clone.TerrorAppearance);
        }

        [Fact]
        public void Clone_ModifyingCloneDoesNotAffectOriginal()
        {
            var original = new RoundSnapshot
            {
                RoundType = "Classic",
                TerrorKey = "Tornado",
                MapName = "Asylum"
            };

            var clone = original.Clone();
            clone.RoundType = "Modified";
            clone.TerrorKey = "Changed";
            clone.IsDeath = true;

            Assert.Equal("Classic", original.RoundType);
            Assert.Equal("Tornado", original.TerrorKey);
            Assert.False(original.IsDeath);
        }

        [Fact]
        public void ToRound_ConvertsCorrectly()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Run",
                TerrorKey = "Ghost",
                MapName = "School",
                IsDeath = true,
                Damage = 25,
                TerrorDetail = "Detail info",
                TerrorAppearance = "Appearance info"
            };

            var round = snapshot.ToRound();

            Assert.Equal(snapshot.RoundType, round.RoundType);
            Assert.Equal(snapshot.TerrorKey, round.TerrorKey);
            Assert.Equal(snapshot.MapName, round.MapName);
            Assert.Equal(snapshot.IsDeath, round.IsDeath);
            Assert.Equal(snapshot.Damage, round.Damage);
            Assert.Equal(snapshot.TerrorDetail, round.TerrorDetail);
            Assert.Equal(snapshot.TerrorAppearance, round.TerrorAppearance);
        }

        [Fact]
        public void RoundType_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                RoundType = "Classic"
            };

            Assert.Equal("Classic", snapshot.RoundType);

            snapshot.RoundType = "Run";
            Assert.Equal("Run", snapshot.RoundType);
        }

        [Fact]
        public void TerrorKey_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                TerrorKey = "Tornado"
            };

            Assert.Equal("Tornado", snapshot.TerrorKey);

            snapshot.TerrorKey = "Ghost";
            Assert.Equal("Ghost", snapshot.TerrorKey);
        }

        [Fact]
        public void MapName_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                MapName = "Asylum"
            };

            Assert.Equal("Asylum", snapshot.MapName);

            snapshot.MapName = "School";
            Assert.Equal("School", snapshot.MapName);
        }

        [Fact]
        public void IsDeath_CanBeToggled()
        {
            var snapshot = new RoundSnapshot
            {
                IsDeath = false
            };

            Assert.False(snapshot.IsDeath);

            snapshot.IsDeath = true;
            Assert.True(snapshot.IsDeath);
        }

        [Fact]
        public void Damage_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                Damage = 10
            };

            Assert.Equal(10, snapshot.Damage);

            snapshot.Damage = 50;
            Assert.Equal(50, snapshot.Damage);
        }

        [Fact]
        public void TerrorDetail_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                TerrorDetail = "Initial detail"
            };

            Assert.Equal("Initial detail", snapshot.TerrorDetail);

            snapshot.TerrorDetail = "Updated detail";
            Assert.Equal("Updated detail", snapshot.TerrorDetail);
        }

        [Fact]
        public void TerrorAppearance_CanBeSet()
        {
            var snapshot = new RoundSnapshot
            {
                TerrorAppearance = "Initial appearance"
            };

            Assert.Equal("Initial appearance", snapshot.TerrorAppearance);

            snapshot.TerrorAppearance = "Updated appearance";
            Assert.Equal("Updated appearance", snapshot.TerrorAppearance);
        }

        [Fact]
        public void Clone_WithNullValues_HandlesCorrectly()
        {
            var original = new RoundSnapshot
            {
                RoundType = null,
                TerrorKey = null,
                MapName = null,
                TerrorDetail = null,
                TerrorAppearance = null
            };

            var clone = original.Clone();

            Assert.Null(clone.RoundType);
            Assert.Null(clone.TerrorKey);
            Assert.Null(clone.MapName);
            Assert.Null(clone.TerrorDetail);
            Assert.Null(clone.TerrorAppearance);
        }
    }
}
