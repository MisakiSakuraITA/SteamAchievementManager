using SAM.Core.Protocol;
using Xunit;

namespace SAM.Tests
{
    public class ProtocolUriTests
    {
        [Theory]
        [InlineData("sam://game/440", 440u)]
        [InlineData("sam://game/1", 1u)]
        [InlineData("sam://440", 440u)]
        [InlineData("sam://1", 1u)]
        public void ParsesAValidAppId(string uriText, uint expected)
        {
            var parsed = ProtocolUri.TryParseAppId(uriText, out var appId);

            Assert.True(parsed);
            Assert.Equal(expected, appId);
        }

        [Theory]
        [InlineData("SAM://game/440")]
        [InlineData("Sam://game/440")]
        [InlineData("sam://GAME/440")]
        [InlineData("SAM://GAME/440")]
        public void SchemeAndTheGameSegmentAreCaseInsensitive(string uriText)
        {
            var parsed = ProtocolUri.TryParseAppId(uriText, out var appId);

            Assert.True(parsed);
            Assert.Equal(440u, appId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a uri at all")]
        [InlineData("http://game/440")]
        [InlineData("steam://game/440")]
        [InlineData("sam://game/")]
        [InlineData("sam://game/abc")]
        [InlineData("sam://game/440abc")]
        [InlineData("sam://abc")]
        [InlineData("sam://game/0")]
        [InlineData("sam://0")]
        [InlineData("sam://")]
        public void RejectsMalformedOrMeaninglessInput(string uriText)
        {
            var parsed = ProtocolUri.TryParseAppId(uriText, out var appId);

            Assert.False(parsed);
            Assert.Equal(0u, appId);
        }

        [Fact]
        public void OnlyTheFirstPathSegmentIsRead()
        {
            var parsed = ProtocolUri.TryParseAppId("sam://game/440/extra", out var appId);

            Assert.True(parsed);
            Assert.Equal(440u, appId);
        }
    }
}
