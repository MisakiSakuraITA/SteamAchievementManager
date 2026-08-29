using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Steam;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class GameLibraryViewModelTests
    {
        private static GameLibraryViewModel Build(
            FakeLibrary steam,
            IEnumerable<GameListEntry> entries)
        {
            var list = entries.ToList();
            return new GameLibraryViewModel(steam, _ => Task.FromResult(list));
        }

        [Fact]
        public async Task LoadTracksOnlyOwnedGamesAndDefaultsToNormalFilter()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Zeta Game");
            steam.Add(20, "alpha game");
            steam.Add(30, "Some Demo");
            steam.Add(40, "A Mod");
            steam.Add(50, "Junk App");

            var entries = new List<GameListEntry>
            {
                new(10, "normal"),
                new(20, "normal"),
                new(30, "demo"),
                new(40, "mod"),
                new(50, "junk"),
                new(60, "normal"), // not owned
            };

            var library = Build(steam, entries);
            await library.LoadAsync();

            Assert.Equal(5, library.TotalCount);
            Assert.True(steam.OwnershipQueryCount >= 6);
            Assert.Equal(2, library.DisplayedCount);
            Assert.Equal(new[] { "alpha game", "Zeta Game" }, library.Games.Select(g => g.Name));

            library.Shutdown();
        }

        [Fact]
        public async Task SortOrderReordersTheDisplayedGames()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Zeta Game");
            steam.Add(20, "alpha game");
            var library = Build(steam, new[] { new GameListEntry(10, "normal"), new GameListEntry(20, "normal") });
            await library.LoadAsync();

            library.SortOrder = GameSortOrder.NameDescending;
            Assert.Equal(new[] { "Zeta Game", "alpha game" }, library.Games.Select(g => g.Name));

            library.SortOrder = GameSortOrder.AppId;
            Assert.Equal(new uint[] { 10, 20 }, library.Games.Select(g => g.Id));

            library.Shutdown();
        }

        [Fact]
        public async Task CategoryTogglesWidenAndNarrowTheDisplayedSet()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Normal");
            steam.Add(30, "Demo");
            steam.Add(40, "Mod");
            steam.Add(50, "Junk");
            var entries = new[]
            {
                new GameListEntry(10, "normal"),
                new GameListEntry(30, "demo"),
                new GameListEntry(40, "mod"),
                new GameListEntry(50, "junk"),
            };
            var library = Build(steam, entries);
            await library.LoadAsync();

            Assert.Equal(1, library.DisplayedCount);

            library.ShowDemos = true;
            Assert.Equal(2, library.DisplayedCount);

            library.ShowMods = true;
            library.ShowJunk = true;
            Assert.Equal(4, library.DisplayedCount);

            library.Shutdown();
        }

        [Fact]
        public async Task SearchNarrowsTheListAndFollowsOrClearsSelection()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Zeta Game");
            steam.Add(20, "alpha game");
            var library = Build(steam, new[] { new GameListEntry(10, "normal"), new GameListEntry(20, "normal") });
            await library.LoadAsync();

            library.SearchText = "zeta";
            Assert.Equal(1, library.DisplayedCount);
            Assert.NotNull(library.SelectedGame);
            Assert.Equal(10u, library.SelectedGame.Id);

            library.SearchText = "nothing matches this";
            Assert.Equal(0, library.DisplayedCount);
            Assert.Null(library.SelectedGame);
            Assert.Equal(2, library.TotalCount);

            library.SearchText = "";
            Assert.Equal(2, library.DisplayedCount);

            library.Shutdown();
        }

        [Fact]
        public async Task AppDataChangedUpdatesNameAndCapsuleOfATrackedGame()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Zeta Game");
            var library = Build(steam, new[] { new GameListEntry(10, "normal") });
            await library.LoadAsync();

            steam.Rename(10, "Zeta Game: Remastered", "https://example.invalid/zeta.jpg");

            var renamed = library.Games.First(g => g.Id == 10);
            Assert.Equal("Zeta Game: Remastered", renamed.Name);
            Assert.True(renamed.HasCapsule);

            library.Shutdown();
        }

        [Fact]
        public async Task LaunchCommandRaisesLaunchRequestedForTheSelection()
        {
            FakeLibrary steam = new();
            steam.Add(10, "Zeta Game");
            var library = Build(steam, new[] { new GameListEntry(10, "normal") });
            await library.LoadAsync();

            var launched = 0;
            library.LaunchRequested += _ => launched++;
            library.SelectedGame = library.Games.First();

            library.LaunchCommand.Execute(null);

            Assert.Equal(1, launched);
            library.Shutdown();
        }

        [Fact]
        public async Task CatalogueFailureIsReportedAndFallsBackToSpacewar()
        {
            FakeLibrary steam = new();
            steam.Add(480, "Spacewar");

            var errors = new List<string>();
            GameLibraryViewModel library = new(
                steam,
                _ => Task.FromException<List<GameListEntry>>(new InvalidOperationException("no network")));
            library.ErrorRaised += errors.Add;

            await library.LoadAsync();

            Assert.Single(errors);
            Assert.Contains("no network", errors[0]);
            Assert.Equal(1, library.TotalCount);
            Assert.Equal(480u, library.Games[0].Id);
            Assert.False(library.IsLoading);
            Assert.True(library.RefreshCommand.CanExecute(null));

            library.Shutdown();
        }

        [Fact]
        public async Task DisconnectGatesCommandsAndPreservesTheMessageThroughFiltering()
        {
            FakeLibrary steam = new();
            for (uint i = 0; i < 10; i++)
            {
                steam.Add(940000 + i, "Game " + i);
            }
            var entries = Enumerable.Range(0, 10).Select(i => new GameListEntry((uint)(940000 + i), "normal"));

            var library = Build(steam, entries);
            await library.LoadAsync();
            library.SelectedGame = library.Games.FirstOrDefault();

            Assert.True(library.IsSteamConnected);
            Assert.True(library.RefreshCommand.CanExecute(null));
            Assert.True(library.LaunchCommand.CanExecute(null));
            Assert.True(library.AddGameCommand.CanExecute(null));

            var notified = new List<string>();
            library.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

            steam.SimulateDisconnect();

            Assert.False(library.IsSteamConnected);
            Assert.True(library.IsSteamDisconnected);
            Assert.Contains(nameof(library.IsSteamConnected), notified);
            Assert.Contains(nameof(library.IsSteamDisconnected), notified);
            Assert.Equal(library.DisconnectedMessage, library.Status);

            Assert.False(library.RefreshCommand.CanExecute(null));
            Assert.False(library.LaunchCommand.CanExecute(null));
            Assert.False(library.AddGameCommand.CanExecute(null));

            // A filter change must not overwrite the disconnect message with a game count.
            library.SearchText = "Game";
            Assert.Equal(library.DisconnectedMessage, library.Status);

            library.Shutdown();
        }

        [Fact]
        public async Task ApplyFilterRaisesExactlyOneCollectionChangedPerFilterChange()
        {
            // Regression coverage for the batching fix: rebuilding the filtered view used to
            // Clear() then Add() every survivor, firing one event per item. It must now fire
            // exactly one Reset, regardless of how many games are in the library.
            const int total = 5000;
            FakeLibrary steam = new();
            var entries = new List<GameListEntry>(total);
            for (uint i = 0; i < total; i++)
            {
                steam.Add(i, $"Game {i:D4}");
                entries.Add(new GameListEntry(i, "normal"));
            }

            var library = Build(steam, entries);
            await library.LoadAsync();
            Assert.Equal(total, library.Games.Count);

            var events = new List<NotifyCollectionChangedAction>();
            library.Games.CollectionChanged += (_, e) => events.Add(e.Action);

            library.SearchText = "Game 123";
            Assert.Single(events);
            Assert.Equal(NotifyCollectionChangedAction.Reset, events[0]);

            var expected = Enumerable.Range(0, total).Count(i =>
            {
                var name = $"Game {i:D4}";
                var idText = i.ToString(CultureInfo.InvariantCulture);
                return name.IndexOf("Game 123", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       idText.StartsWith("Game 123", StringComparison.Ordinal);
            });
            Assert.Equal(expected, library.Games.Count);

            events.Clear();
            foreach (var partial in new[] { "G", "Ga", "Gam", "Game", "Game ", "Game 1" })
            {
                library.SearchText = partial;
            }
            Assert.Equal(6, events.Count);
            Assert.All(events, a => Assert.Equal(NotifyCollectionChangedAction.Reset, a));

            library.Shutdown();
        }

        // ============================ active account ============================

        [Fact]
        public void ActiveAccountPropertiesAreReadThroughFromTheSteamService()
        {
            FakeLibrary steam = new()
            {
                ActiveSteamId = 76561197960287930UL,
                ActivePersonaName = "Alice",
                ActiveAvatarFilePath = @"C:\Steam\config\avatars\abc123hash_full.jpg",
            };
            var library = Build(steam, Enumerable.Empty<GameListEntry>());

            Assert.Equal(76561197960287930UL, library.ActiveSteamId);
            Assert.Equal("76561197960287930", library.ActiveSteamIdText);
            Assert.Equal("Alice", library.ActivePersonaName);
            Assert.Equal("Alice", library.ActiveAccountDisplayName);
            Assert.Equal(@"C:\Steam\config\avatars\abc123hash_full.jpg", library.ActiveAvatarFilePath);
        }

        [Fact]
        public void ActiveAvatarFilePathIsNullWhenNoAvatarWasFound()
        {
            FakeLibrary steam = new() { ActiveAvatarFilePath = null };
            var library = Build(steam, Enumerable.Empty<GameListEntry>());

            Assert.Null(library.ActiveAvatarFilePath);
        }

        [Fact]
        public void ActiveAccountDisplayNameFallsBackToTheSteamIdWhenThePersonaNameIsUnknown()
        {
            FakeLibrary steam = new() { ActiveSteamId = 76561197960287930UL, ActivePersonaName = null };
            var library = Build(steam, Enumerable.Empty<GameListEntry>());

            Assert.Null(library.ActivePersonaName);
            Assert.Equal("76561197960287930", library.ActiveAccountDisplayName);
        }

    }
}
