using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Guards against the callback pump timer quietly reverting to
    /// <see cref="DispatcherPriority.Background"/>, where sustained scrolling can starve it --
    /// and, with it, the disconnect check that runs at the end of every pumped tick.
    /// </summary>
    [Collection(WpfCollection.Name)]
    public class CallbackTimerPriorityTests
    {
        private readonly WpfTestFixture _Fixture;

        public CallbackTimerPriorityTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static DispatcherTimer GetCallbackTimer(object window)
        {
            var field = window.GetType().GetField("_CallbackTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            return (DispatcherTimer)field.GetValue(window);
        }

        // DispatcherTimer does not expose the priority it was constructed with through any
        // public member; it only ever uses it internally to schedule the tick.
        private static DispatcherPriority GetPriority(DispatcherTimer timer)
        {
            var field = typeof(DispatcherTimer).GetField("_priority", BindingFlags.NonPublic | BindingFlags.Instance);
            return (DispatcherPriority)field.GetValue(timer);
        }

        [Fact]
        public void PickerCallbackTimerRunsAtInputPriority()
        {
            this._Fixture.Invoke(() =>
            {
                FakeLibrary library = new();
                Task<List<GameListEntry>> Loader(CancellationToken ct) => Task.FromResult(new List<GameListEntry>());
                var vm = new GameLibraryViewModel(library, Loader);

                var window = new SAM.Picker.MainWindow(vm);
                var timer = GetCallbackTimer(window);

                Assert.Equal(DispatcherPriority.Input, GetPriority(timer));
            });
        }

        [Fact]
        public void GameCallbackTimerRunsAtInputPriority()
        {
            this._Fixture.Invoke(() =>
            {
                FakeStats steam = new() { InstallPath = null };
                AchievementManagerViewModel manager = new(steam, new FakeDialogService());
                manager.Load(new UserGameStatsSchema(
                    Enumerable.Empty<AchievementDefinition>(),
                    Enumerable.Empty<StatDefinition>()));

                var window = new SAM.Game.MainWindow(manager);
                var timer = GetCallbackTimer(window);

                Assert.Equal(DispatcherPriority.Input, GetPriority(timer));
            });
        }
    }
}
