using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class AchievementViewModelTests
    {
        private static AchievementDefinition Definition(
            string id = "ACH_WIN",
            string name = "First Win",
            string description = "Win a round",
            string iconNormal = "on.jpg",
            string iconLocked = "off.jpg",
            int permission = 0,
            bool isHidden = false) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            IconNormal = iconNormal,
            IconLocked = iconLocked,
            Permission = permission,
            IsHidden = isHidden,
        };

        [Fact]
        public void StartsLockedAndUnmodified()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);

            Assert.False(achievement.IsUnlocked);
            Assert.False(achievement.IsAchieved);
            Assert.False(achievement.IsModified);
            Assert.False(achievement.IsProtected);
        }

        [Fact]
        public void IconIdentityReflectsAchievedState()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);
            Assert.Equal("achievement:480:off.jpg", achievement.IconIdentity);
            Assert.Contains("/480/off.jpg", achievement.IconUri.AbsoluteUri);
        }

        [Fact]
        public void TogglingUnlockedMarksAndClearsModified()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);

            achievement.IsUnlocked = true;
            Assert.True(achievement.IsModified);
            Assert.False(achievement.IsAchieved); // stored state untouched until a store

            achievement.IsUnlocked = false;
            Assert.False(achievement.IsModified);
        }

        [Fact]
        public void ProtectedAchievementRefusesTheChangeAndReportsRejection()
        {
            var rejections = 0;
            AchievementViewModel achievement = new(480, Definition(permission: 1), false, null);
            achievement.ProtectedChangeRejected += _ => rejections++;

            Assert.True(achievement.IsProtected);

            achievement.IsUnlocked = true;

            Assert.False(achievement.IsUnlocked);
            Assert.Equal(1, rejections);
            Assert.False(achievement.IsModified);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void IsProtectedChecksEitherOfTheLowTwoPermissionBits(int permission, bool expected)
        {
            AchievementViewModel achievement = new(480, Definition(permission: permission), false, null);
            Assert.Equal(expected, achievement.IsProtected);
        }

        [Fact]
        public void UnlocalisedTokenNameFallsBackToTheId()
        {
            var definition = Definition(id: "ACH_RAW", name: "#ACH_RAW_NAME", description: "");
            AchievementViewModel achievement = new(480, definition, false, null);
            Assert.Equal("ACH_RAW", achievement.Name);
        }

        [Fact]
        public void MissingIconYieldsNoIdentityOrUri()
        {
            var definition = Definition(id: "ACH_NOICON", name: "No Icon", description: "", iconNormal: null, iconLocked: null);
            AchievementViewModel achievement = new(480, definition, false, null);
            Assert.Null(achievement.IconIdentity);
            Assert.Null(achievement.IconUri);
        }

        [Fact]
        public void MatchesSearchesNameAndDescription()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);
            Assert.True(achievement.Matches("first"));
            Assert.True(achievement.Matches("round"));
            Assert.False(achievement.Matches("elephant"));
        }

        [Fact]
        public void TrySetUnlockedSkipsProtectedAchievementsWithoutRaisingRejection()
        {
            var rejections = 0;
            AchievementViewModel achievement = new(480, Definition(permission: 2), false, null);
            achievement.ProtectedChangeRejected += _ => rejections++;

            var changed = achievement.TrySetUnlocked(true);

            Assert.False(changed);
            Assert.False(achievement.IsUnlocked);
            Assert.Equal(0, rejections);
        }

        [Fact]
        public void RevertPendingDiscardsAnUnstoredToggle()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);
            achievement.IsUnlocked = true;

            achievement.RevertPending();

            Assert.False(achievement.IsUnlocked);
            Assert.False(achievement.IsModified);
        }

        [Fact]
        public void HiddenLockedUnrevealedAchievementShowsAGenericPlaceholder()
        {
            var definition = Definition(name: "The Secret Ending", description: "Find the hidden door", isHidden: true);
            AchievementViewModel achievement = new(480, definition, false, null);

            Assert.NotEqual("The Secret Ending", achievement.DisplayName);
            Assert.NotEqual("Find the hidden door", achievement.DisplayDescription);
            Assert.Equal("achievement:480:off.jpg", achievement.IconIdentity);

            // The real values stay reachable through Name/Description themselves -- only the
            // display-facing properties obscure them -- and search still works on the truth.
            Assert.Equal("The Secret Ending", achievement.Name);
            Assert.True(achievement.Matches("secret ending"));
        }

        [Fact]
        public void ShowSecretDetailsRevealsAHiddenLockedAchievement()
        {
            var definition = Definition(name: "The Secret Ending", description: "Find the hidden door", isHidden: true);
            AchievementViewModel achievement = new(480, definition, false, null);

            achievement.ShowSecretDetails = true;

            Assert.Equal("The Secret Ending", achievement.DisplayName);
            Assert.Equal("Find the hidden door", achievement.DisplayDescription);
            Assert.Equal("achievement:480:on.jpg", achievement.IconIdentity);

            achievement.ShowSecretDetails = false;

            Assert.NotEqual("The Secret Ending", achievement.DisplayName);
            Assert.Equal("achievement:480:off.jpg", achievement.IconIdentity);
        }

        [Fact]
        public void EarningAHiddenAchievementRevealsItWithoutNeedingShowSecretDetails()
        {
            var definition = Definition(name: "The Secret Ending", isHidden: true);
            AchievementViewModel achievement = new(480, definition, true, null);

            Assert.Equal("The Secret Ending", achievement.DisplayName);
            Assert.Equal("achievement:480:on.jpg", achievement.IconIdentity);
        }

        [Fact]
        public void NonHiddenAchievementIsNeverObscured()
        {
            AchievementViewModel achievement = new(480, Definition(isHidden: false), false, null);

            Assert.Equal(achievement.Name, achievement.DisplayName);
            Assert.Equal(achievement.Description, achievement.DisplayDescription);
        }

        [Fact]
        public void RarityIsUnknownUntilSet()
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);

            Assert.Null(achievement.RarityPercentage);
            Assert.Null(achievement.RarityText);
            Assert.False(achievement.IsUltraRare);
        }

        [Theory]
        [InlineData(1.4, true)]
        [InlineData(4.99, true)]
        [InlineData(5.0, false)]
        [InlineData(37.2, false)]
        public void IsUltraRareChecksAgainstTheFivePercentThreshold(double percentage, bool expected)
        {
            AchievementViewModel achievement = new(480, Definition(), false, null);

            achievement.RarityPercentage = percentage;

            Assert.Equal(expected, achievement.IsUltraRare);
            Assert.NotNull(achievement.RarityText);
            Assert.Contains(percentage.ToString("0.0"), achievement.RarityText);
        }
    }
}
