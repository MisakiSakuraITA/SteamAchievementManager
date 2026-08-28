using System.Globalization;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class StatViewModelTests
    {
        [Fact]
        public void IntegerStatStartsUnmodifiedWithFormattedText()
        {
            IntegerStatDefinition definition = new()
            {
                Id = "kills",
                DisplayName = "Kills",
                MinValue = 0,
                MaxValue = 1000,
            };
            IntegerStatViewModel kills = new(definition, 42);

            Assert.False(kills.IsModified);
            Assert.False(kills.HasError);
            Assert.Equal("42", kills.ValueText);
            Assert.Equal("Integer", kills.TypeName);
        }

        [Fact]
        public void IntegerStatValidatesRangeAndNumericFormat()
        {
            IntegerStatDefinition definition = new()
            {
                Id = "kills",
                DisplayName = "Kills",
                MinValue = 0,
                MaxValue = 1000,
            };
            IntegerStatViewModel kills = new(definition, 42);

            kills.ValueText = "99";
            Assert.True(kills.IsModified);
            Assert.False(kills.HasError);
            Assert.Equal(99, kills.Value);

            kills.ValueText = "not a number";
            Assert.True(kills.HasError);

            kills.ValueText = "5000";
            Assert.True(kills.HasError);

            kills.ValueText = "42";
            Assert.False(kills.IsModified);
            Assert.False(kills.HasError);
        }

        [Fact]
        public void IncrementOnlyIntegerStatRejectsADecrease()
        {
            IntegerStatDefinition definition = new()
            {
                Id = "distance",
                DisplayName = "Distance",
                MinValue = 0,
                MaxValue = int.MaxValue,
                IncrementOnly = true,
            };
            IntegerStatViewModel distance = new(definition, 100);

            distance.ValueText = "50";
            Assert.True(distance.HasError);

            distance.ValueText = "150";
            Assert.False(distance.HasError);
            Assert.True(distance.IsModified);
            Assert.Equal("increment only", distance.Extra);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void IsProtectedChecksOnlyTheSecondPermissionBit(int permission, bool expected)
        {
            IntegerStatDefinition definition = new()
            {
                Id = "score",
                DisplayName = "Score",
                MinValue = 0,
                MaxValue = 100,
                Permission = permission,
            };
            IntegerStatViewModel score = new(definition, 10);
            Assert.Equal(expected, score.IsProtected);
        }

        [Fact]
        public void ProtectedIntegerStatRefusesToBeModified()
        {
            IntegerStatDefinition definition = new()
            {
                Id = "score",
                DisplayName = "Score",
                MinValue = 0,
                MaxValue = 100,
                Permission = 2,
            };
            IntegerStatViewModel score = new(definition, 10);

            score.ValueText = "50";

            Assert.False(score.IsModified);
            Assert.True(score.HasError);
            Assert.Equal("protected", score.Extra);
        }

        [Fact]
        public void FloatStatValidatesRangeAndType()
        {
            FloatStatDefinition definition = new()
            {
                Id = "accuracy",
                DisplayName = "Accuracy",
                MinValue = 0f,
                MaxValue = 1f,
            };
            FloatStatViewModel accuracy = new(definition, 0.25f);

            Assert.Equal("Float", accuracy.TypeName);

            accuracy.ValueText = (0.75f).ToString(CultureInfo.CurrentCulture);
            Assert.True(accuracy.IsModified);
            Assert.False(accuracy.HasError);
            Assert.Equal(0.75f, accuracy.Value, 4);

            accuracy.ValueText = (2.5f).ToString(CultureInfo.CurrentCulture);
            Assert.True(accuracy.HasError);
        }

        [Fact]
        public void MatchesSearchesDisplayNameAndId()
        {
            IntegerStatDefinition definition = new() { Id = "kills", DisplayName = "Kills", MinValue = 0, MaxValue = 1000 };
            IntegerStatViewModel kills = new(definition, 42);

            Assert.True(kills.Matches("kill"));
            Assert.True(kills.Matches("kills"));
            Assert.False(kills.Matches("elephant"));
        }

        [Fact]
        public void StoreWritesThePendingValueThroughTheService()
        {
            IntegerStatDefinition definition = new() { Id = "kills", DisplayName = "Kills", MinValue = 0, MaxValue = 1000 };
            IntegerStatViewModel kills = new(definition, 42) { ValueText = "99" };

            FakeStats stats = new();
            Assert.True(kills.Store(stats));
            Assert.Contains("kills", stats.StoredStats);

            kills.AcceptPending();
            Assert.False(kills.IsModified);
        }
    }
}
