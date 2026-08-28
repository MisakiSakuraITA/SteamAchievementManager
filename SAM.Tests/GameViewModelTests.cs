using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class GameViewModelTests
    {
        [Fact]
        public void NullNameFallsBackToAppId()
        {
            GameViewModel game = new(480, "normal", null);
            Assert.Equal("App 480", game.Name);
        }

        [Fact]
        public void CapsuleStartsUnset()
        {
            GameViewModel game = new(730, "normal", "Counter-Strike 2");
            Assert.False(game.HasCapsule);
            Assert.Null(game.CapsuleIdentity);
        }

        [Fact]
        public void UpdateCapsuleReportsAChangeAndBuildsIdentity()
        {
            GameViewModel game = new(730, "normal", "Counter-Strike 2");

            Assert.True(game.UpdateCapsule("https://example.invalid/a.jpg"));
            Assert.Equal("logo:730:https://example.invalid/a.jpg", game.CapsuleIdentity);
            Assert.NotNull(game.CapsuleUri);
            Assert.True(game.HasCapsule);
        }

        [Fact]
        public void RepeatCapsuleUpdateIsANoOp()
        {
            GameViewModel game = new(730, "normal", "Counter-Strike 2");
            game.UpdateCapsule("https://example.invalid/a.jpg");

            Assert.False(game.UpdateCapsule("https://example.invalid/a.jpg"));
            Assert.True(game.UpdateCapsule("https://example.invalid/b.jpg"));
        }

        [Fact]
        public void UnparseableCapsuleClearsTheIdentity()
        {
            GameViewModel game = new(730, "normal", "Counter-Strike 2");
            game.UpdateCapsule("https://example.invalid/a.jpg");

            game.UpdateCapsule("not a url");

            Assert.Null(game.CapsuleIdentity);
            Assert.False(game.HasCapsule);
        }

        [Theory]
        [InlineData("counter", true)]
        [InlineData("COUNTER", true)]
        [InlineData("73", true)]
        [InlineData("skyrim", false)]
        [InlineData("", true)]
        public void MatchesSearchesNameAndAppIdPrefix(string search, bool expected)
        {
            GameViewModel game = new(730, "normal", "Counter-Strike 2");
            Assert.Equal(expected, game.Matches(search));
        }
    }
}
