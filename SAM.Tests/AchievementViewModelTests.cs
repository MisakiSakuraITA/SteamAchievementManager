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
            int permission = 0) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            IconNormal = iconNormal,
            IconLocked = iconLocked,
            Permission = permission,
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
    }
}
