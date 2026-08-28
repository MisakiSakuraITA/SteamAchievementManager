using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using SAM.UI.Controls;
using Xunit;

namespace SAM.Tests
{
    [Collection(WpfCollection.Name)]
    public class NotificationBannerTests
    {
        private readonly WpfTestFixture _Fixture;

        public NotificationBannerTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }
            if (root is T found)
            {
                return found;
            }
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var result = FindChild<T>(VisualTreeHelper.GetChild(root, i));
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static (Window Host, NotificationBanner Banner) BuildHostedBanner()
        {
            NotificationBanner banner = new();
            Window host = new()
            {
                Content = banner,
                Width = 500,
                Height = 120,
                ShowInTaskbar = false,
                Left = -32000,
                Top = -32000,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            host.Show();
            WpfTestFixture.Pump();
            return (host, banner);
        }

        [Fact]
        public void IsCollapsedUntilOpenedAndVisibleWhileOpen()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, banner) = BuildHostedBanner();

                Assert.Equal(Visibility.Collapsed, banner.Visibility);

                banner.Message = "Something failed.";
                banner.IsOpen = true;
                WpfTestFixture.Pump();
                Assert.Equal(Visibility.Visible, banner.Visibility);

                banner.IsOpen = false;
                WpfTestFixture.Pump();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void DismissButtonClosesTheBannerWithoutInvokingRetry()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, banner) = BuildHostedBanner();

                var invoked = false;
                RelayCommand retry = new(() => invoked = true);
                banner.Message = "Something failed.";
                banner.RetryCommand = retry;
                banner.IsOpen = true;
                WpfTestFixture.Pump();

                var buttons = FindAllButtons(banner);
                var dismissButton = buttons.Single(b => (string)b.GetValue(AutomationProperties.NameProperty) == "Dismiss");
                InvokeClick(dismissButton);
                WpfTestFixture.Pump();

                Assert.False(banner.IsOpen);
                Assert.False(invoked);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void RetryButtonClosesTheBannerAndInvokesRetry()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, banner) = BuildHostedBanner();

                var invoked = false;
                RelayCommand retry = new(() => invoked = true);
                banner.Message = "Something failed.";
                banner.RetryCommand = retry;
                banner.IsOpen = true;
                WpfTestFixture.Pump();

                var buttons = FindAllButtons(banner);
                var retryButton = buttons.Single(b => (string)b.GetValue(AutomationProperties.NameProperty) == "Retry");
                InvokeClick(retryButton);
                WpfTestFixture.Pump();

                Assert.False(banner.IsOpen);
                Assert.True(invoked);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void RetryButtonIsHiddenWhenNoRetryCommandIsSet()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, banner) = BuildHostedBanner();

                banner.Message = "Something failed, nothing to retry.";
                banner.RetryCommand = null;
                banner.IsOpen = true;
                WpfTestFixture.Pump();

                var buttons = FindAllButtons(banner);
                var retryButton = buttons.SingleOrDefault(b => (string)b.GetValue(AutomationProperties.NameProperty) == "Retry");
                Assert.NotNull(retryButton);
                Assert.NotEqual(Visibility.Visible, retryButton.Visibility);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        private static List<System.Windows.Controls.Button> FindAllButtons(DependencyObject root)
        {
            var found = new List<System.Windows.Controls.Button>();
            Walk(root);
            return found;

            void Walk(DependencyObject node)
            {
                if (node == null)
                {
                    return;
                }
                if (node is System.Windows.Controls.Button button)
                {
                    found.Add(button);
                }
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i));
                }
            }
        }

        private static void InvokeClick(System.Windows.Controls.Button button)
        {
            var onClick = typeof(System.Windows.Controls.Primitives.ButtonBase)
                .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(onClick);
            onClick.Invoke(button, null);
        }

        [Fact]
        public void GameShellRoutesErrorsToANotificationBannerWithRetryWiredToReload()
        {
            this._Fixture.Invoke(() =>
            {
                FakeStats steam = new() { InstallPath = null };
                AchievementManagerViewModel manager = new(steam, new FakeDialogService());
                manager.Load(new UserGameStatsSchema(
                    System.Array.Empty<AchievementDefinition>(),
                    System.Array.Empty<StatDefinition>()));

                var window = new SAM.Game.MainWindow(manager)
                {
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                window.Show();
                WpfTestFixture.Pump();

                var raise = typeof(SAM.Game.MainWindow).GetMethod("OnErrorRaised", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(raise);
                raise.Invoke(window, new object[] { "Something went wrong." });
                WpfTestFixture.Pump();

                var banner = FindChild<NotificationBanner>(window);
                Assert.NotNull(banner);
                Assert.True(banner.IsOpen);
                Assert.Equal("Something went wrong.", banner.Message);
                Assert.Equal(NotificationSeverity.Error, banner.Severity);
                Assert.Same(manager.ReloadCommand, banner.RetryCommand);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void GameShellRoutesStoreConfirmationsToASuccessBannerWithNoRetry()
        {
            this._Fixture.Invoke(() =>
            {
                FakeStats steam = new() { InstallPath = null };
                AchievementManagerViewModel manager = new(steam, new FakeDialogService());
                manager.Load(new UserGameStatsSchema(
                    System.Array.Empty<AchievementDefinition>(),
                    System.Array.Empty<StatDefinition>()));

                var window = new SAM.Game.MainWindow(manager)
                {
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                window.Show();
                WpfTestFixture.Pump();

                var raise = typeof(SAM.Game.MainWindow).GetMethod("OnInfoRaised", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(raise);
                raise.Invoke(window, new object[] { "Stored 2 achievements and 1 statistic." });
                WpfTestFixture.Pump();

                var banner = FindChild<NotificationBanner>(window);
                Assert.NotNull(banner);
                Assert.True(banner.IsOpen);
                Assert.Equal(NotificationSeverity.Success, banner.Severity);
                Assert.Null(banner.RetryCommand);

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void PickerShellRoutesErrorsToANotificationBannerWithRetryWiredToRefresh()
        {
            this._Fixture.Invoke(() =>
            {
                FakeLibrary library = new();
                var vm = new GameLibraryViewModel(library, _ => System.Threading.Tasks.Task.FromResult(new List<GameListEntry>()));

                var window = new SAM.Picker.MainWindow(vm)
                {
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                window.Show();
                WpfTestFixture.Pump();

                var raise = typeof(SAM.Picker.MainWindow).GetMethod("OnErrorRaised", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(raise);
                raise.Invoke(window, new object[] { "Failed to retrieve the game list." });
                WpfTestFixture.Pump();

                var banner = FindChild<NotificationBanner>(window);
                Assert.NotNull(banner);
                Assert.True(banner.IsOpen);
                Assert.Equal(NotificationSeverity.Error, banner.Severity);
                Assert.Same(vm.RefreshCommand, banner.RetryCommand);

                window.Close();
                WpfTestFixture.Pump();
            });
        }
    }
}
