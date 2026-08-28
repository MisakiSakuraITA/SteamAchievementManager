using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises the shells' window-wide keyboard shortcuts. HandleShortcut takes the
    /// modifier set as a parameter specifically so these can supply Control explicitly --
    /// Keyboard.Modifiers itself reflects the real OS keyboard state, which nothing outside
    /// an actual keypress can fake.
    /// </summary>
    [Collection(WpfCollection.Name)]
    public class KeyboardShortcutTests
    {
        private readonly WpfTestFixture _Fixture;

        public KeyboardShortcutTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static bool InvokeShortcut(object window, Key key, ModifierKeys modifiers)
        {
            var method = window.GetType().GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (bool)method.Invoke(window, new object[] { key, modifiers });
        }

        private static T FindByName<T>(FrameworkElement root, string name) where T : FrameworkElement
        {
            return root.FindName(name) as T;
        }

        [Fact]
        public void GameShellCtrlFFocusesAndSelectsTheSearchBox()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, manager) = BuildGameWindow();
                manager.Load(BuildAchievementSchema());
                var searchBox = FindByName<TextBox>(window, "_SearchBox");
                Assert.NotNull(searchBox);

                var handled = InvokeShortcut(window, Key.F, ModifierKeys.Control);
                WpfTestFixture.Pump();

                Assert.True(handled);
                Assert.True(searchBox.IsKeyboardFocused);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void GameShellEscapeClearsAnActiveSearchButDeclinesWhenAlreadyEmpty()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, manager) = BuildGameWindow();
                manager.Load(BuildAchievementSchema());

                manager.SearchText = "alph";
                Assert.True(InvokeShortcut(window, Key.Escape, ModifierKeys.None));
                Assert.Equal("", manager.SearchText);

                // Nothing left to clear: Escape should not claim the key, so it stays free
                // for whatever else (like closing a popup) might want it.
                Assert.False(InvokeShortcut(window, Key.Escape, ModifierKeys.None));

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void GameShellF5ReloadsWhenTheCommandCanExecute()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, manager) = BuildGameWindow();
                manager.Load(BuildAchievementSchema());
                Assert.False(manager.IsBusy);

                Assert.True(InvokeShortcut(window, Key.F5, ModifierKeys.None));
                WpfTestFixture.Pump();

                // BeginLoad -- what ReloadCommand actually runs -- sets IsBusy true
                // synchronously and then waits on a callback that never arrives in this
                // test; that it moved at all is what proves F5 actually reached the command
                // rather than doing nothing.
                Assert.True(manager.IsBusy);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void GameShellCtrlSStoresWhenThereIsSomethingPendingAndIsANoOpOtherwise()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, manager) = BuildGameWindow();
                manager.Load(BuildAchievementSchema());

                // Nothing pending yet: StoreCommand cannot execute, so this claims the key
                // (it is still "the store shortcut") without anything actually happening.
                Assert.True(InvokeShortcut(window, Key.S, ModifierKeys.Control));
                Assert.False(manager.IsModified);

                manager.Achievements[0].IsUnlocked = true;
                Assert.True(manager.IsModified);

                Assert.True(InvokeShortcut(window, Key.S, ModifierKeys.Control));
                WpfTestFixture.Pump();

                Assert.False(manager.IsModified);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void PickerShellCtrlFFocusesAndSelectsTheSearchBox()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, _) = BuildPickerWindow();
                var searchBox = FindByName<TextBox>(window, "_SearchBox");
                Assert.NotNull(searchBox);

                var handled = InvokeShortcut(window, Key.F, ModifierKeys.Control);
                WpfTestFixture.Pump();

                Assert.True(handled);
                Assert.True(searchBox.IsKeyboardFocused);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void PickerShellEscapeClearsAnActiveSearch()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, library) = BuildPickerWindow();

                library.SearchText = "something";
                Assert.True(InvokeShortcut(window, Key.Escape, ModifierKeys.None));
                Assert.Equal("", library.SearchText);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void PickerShellUnmodifiedKeysAreNotClaimedAsShortcuts()
        {
            this._Fixture.Invoke(() =>
            {
                var (window, _) = BuildPickerWindow();

                // Plain 'F' (no Control) and plain Enter must fall through untouched, or
                // ordinary typing and the existing per-list Enter-to-launch handling would
                // both lose keystrokes to this handler.
                Assert.False(InvokeShortcut(window, Key.F, ModifierKeys.None));
                Assert.False(InvokeShortcut(window, Key.Enter, ModifierKeys.None));

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        private static (SAM.Game.MainWindow Window, AchievementManagerViewModel Manager) BuildGameWindow()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            SAM.Game.MainWindow window = new(manager)
            {
                ShowInTaskbar = false,
                Left = -32000,
                Top = -32000,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.Show();
            WpfTestFixture.Pump();
            return (window, manager);
        }

        private static (SAM.Picker.MainWindow Window, GameLibraryViewModel Library) BuildPickerWindow()
        {
            FakeLibrary steam = new();
            Task<List<GameListEntry>> Loader(CancellationToken ct) => Task.FromResult(new List<GameListEntry>());
            var library = new GameLibraryViewModel(steam, Loader);
            SAM.Picker.MainWindow window = new(library)
            {
                ShowInTaskbar = false,
                Left = -32000,
                Top = -32000,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.Show();
            WpfTestFixture.Pump();
            return (window, library);
        }

        private static UserGameStatsSchema BuildAchievementSchema()
        {
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 0 },
            };
            return new UserGameStatsSchema(definitions, Array.Empty<StatDefinition>());
        }
    }
}
