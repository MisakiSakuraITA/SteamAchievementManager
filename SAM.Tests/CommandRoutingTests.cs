using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SAM.Core.Steam;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Confirms the picker's double-click and Enter-key handlers route through
    /// <see cref="GameLibraryViewModel.LaunchCommand"/> -- specifically its <c>CanExecute</c> --
    /// rather than launching unconditionally.
    /// </summary>
    [Collection(WpfCollection.Name)]
    public class CommandRoutingTests
    {
        private readonly WpfTestFixture _Fixture;

        public CommandRoutingTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        [Fact]
        public void DoubleClickAndEnterOnlyLaunchWhenLaunchCommandCanExecute()
        {
            this._Fixture.Invoke(() =>
            {
                FakeLibrary library = new();
                library.Add(10, "Alpha");
                library.Add(20, "Beta");

                Task<List<GameListEntry>> Loader(CancellationToken ct) => Task.FromResult(new List<GameListEntry>
                {
                    new(10, "normal"),
                    new(20, "normal"),
                });

                var vm = new GameLibraryViewModel(library, Loader);
                var launched = new List<GameViewModel>();
                vm.LaunchRequested += g => launched.Add(g);

                var window = new SAM.Picker.MainWindow(vm);
                window.Show();

                // The window's own OnLaunchRequested calls Process.Start(SAM.Game.exe); detach
                // it so exercising the input handlers here does not spawn a real process.
                var windowLaunchHandler = (Action<GameViewModel>)Delegate.CreateDelegate(
                    typeof(Action<GameViewModel>),
                    window,
                    typeof(SAM.Picker.MainWindow).GetMethod("OnLaunchRequested", BindingFlags.NonPublic | BindingFlags.Instance));
                vm.LaunchRequested -= windowLaunchHandler;

                var gameList = (ListBox)typeof(SAM.Picker.MainWindow)
                    .GetField("_GameList", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(window);
                var activatedMethod = typeof(SAM.Picker.MainWindow)
                    .GetMethod("OnGameActivated", BindingFlags.NonPublic | BindingFlags.Instance);
                var keyDownMethod = typeof(SAM.Picker.MainWindow)
                    .GetMethod("OnGameListKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.Equal(2, vm.Games.Count);

                // No selection: neither handler launches anything.
                gameList.SelectedItem = null;
                var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left);
                activatedMethod.Invoke(window, new object[] { window, mouseArgs });
                Assert.Empty(launched);

                // Selected and connected: both double-click and Enter launch via the command.
                var alpha = vm.Games[0].Id == 10 ? vm.Games[0] : vm.Games[1];
                gameList.SelectedItem = alpha;
                activatedMethod.Invoke(window, new object[] { window, mouseArgs });
                Assert.Equal(new[] { alpha }, launched);

                var beta = alpha.Id == 10 ? vm.Games[1] : vm.Games[0];
                gameList.SelectedItem = beta;
                var keySource = PresentationSource.FromVisual(window);
                var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
                keyDownMethod.Invoke(window, new object[] { window, keyArgs });
                Assert.Equal(new[] { alpha, beta }, launched);
                Assert.True(keyArgs.Handled);

                // Disconnect Steam: CanExecute must now gate both handlers, even with a
                // selection in place -- the exact scenario the fix targets.
                library.SimulateDisconnect();
                Assert.False(vm.LaunchCommand.CanExecute(alpha));

                gameList.SelectedItem = alpha;
                activatedMethod.Invoke(window, new object[] { window, mouseArgs });
                Assert.Equal(2, launched.Count);

                var keyArgs2 = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
                keyDownMethod.Invoke(window, new object[] { window, keyArgs2 });
                Assert.Equal(2, launched.Count);
                Assert.True(keyArgs2.Handled);

                window.Close();
            });
        }
    }
}
