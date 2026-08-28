using System.Collections.Generic;
using System.Reflection;
using SAM.API;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// <see cref="SteamStatsService"/> wraps a live <see cref="Client"/>, so these construct
    /// one without ever calling <see cref="Client.Initialize"/> -- everything exercised here
    /// (registering a callback, wiring an event, reflecting into a private handler) works
    /// against an uninitialised client with no real Steam pipe behind it.
    /// </summary>
    public class SteamStatsServiceTests
    {
        private static void RaiseUserStatsReceived(SteamStatsService service, ulong gameId, int result, ulong steamIdUser = 0)
        {
            var method = typeof(SteamStatsService).GetMethod("OnUserStatsReceived", BindingFlags.NonPublic | BindingFlags.Instance);
            var param = new SAM.API.Types.UserStatsReceived
            {
                GameId = gameId,
                Result = result,
                SteamIdUser = steamIdUser,
            };
            method.Invoke(service, new object[] { param });
        }

        [Fact]
        public void UserStatsReceivedForThisAppIsForwarded()
        {
            using Client client = new();
            using SteamStatsService service = new(client, 480);

            var seen = new List<int>();
            service.UserStatsReceived += seen.Add;

            RaiseUserStatsReceived(service, gameId: 480, result: 1);

            Assert.Equal(new[] { 1 }, seen);
        }

        [Fact]
        public void UserStatsReceivedForADifferentAppIsIgnored()
        {
            using Client client = new();
            using SteamStatsService service = new(client, 480);

            var seen = new List<int>();
            service.UserStatsReceived += seen.Add;

            // The pipe is shared with every other app Steam is running; a game launched
            // alongside SAM asking Steam for its own stats must not be mistaken for a reply
            // to this service's own request.
            RaiseUserStatsReceived(service, gameId: 730, result: 1);

            Assert.Empty(seen);
        }

        [Fact]
        public void UserStatsReceivedAfterDisposeIsIgnored()
        {
            using Client client = new();
            SteamStatsService service = new(client, 480);
            service.Dispose();

            var seen = new List<int>();
            service.UserStatsReceived += seen.Add;

            RaiseUserStatsReceived(service, gameId: 480, result: 1);

            Assert.Empty(seen);
        }
    }
}
