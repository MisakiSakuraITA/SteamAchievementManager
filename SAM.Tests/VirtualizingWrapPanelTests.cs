using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using SAM.UI.Controls;
using Xunit;

namespace SAM.Tests
{
    [Collection(WpfCollection.Name)]
    public class VirtualizingWrapPanelTests
    {
        private readonly WpfTestFixture _Fixture;

        public VirtualizingWrapPanelTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static DataTemplate BuildSimpleTemplate()
        {
            FrameworkElementFactory border = new(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.DimGray);
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
            return new DataTemplate { VisualTree = border };
        }

        private static ItemsPanelTemplate BuildPanelTemplate(double itemWidth, double itemHeight)
        {
            FrameworkElementFactory panel = new(typeof(VirtualizingWrapPanel));
            panel.SetValue(VirtualizingWrapPanel.ItemWidthProperty, itemWidth);
            panel.SetValue(VirtualizingWrapPanel.ItemHeightProperty, itemHeight);
            return new ItemsPanelTemplate { VisualTree = panel };
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

        /// <summary>Builds a bare virtualised list -- no theme, no shell -- of the given size.</summary>
        private static (Window Host, ListBox List, VirtualizingWrapPanel Panel) BuildList(int count, double itemWidth = 248, double itemHeight = 156)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new GameViewModel((uint)i, "normal", "Game " + i))
                .ToList();

            ListBox list = new()
            {
                ItemsSource = items,
                Width = 1000,
                Height = 600,
                ItemTemplate = BuildSimpleTemplate(),
            };
            list.SetValue(ScrollViewer.CanContentScrollProperty, true);
            list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Standard);
            list.ItemsPanel = BuildPanelTemplate(itemWidth, itemHeight);

            Window host = new()
            {
                Width = 1020,
                Height = 640,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                Content = list,
            };
            host.Show();
            WpfTestFixture.Pump();

            var panel = FindChild<VirtualizingWrapPanel>(list);
            return (host, list, panel);
        }

        [Fact]
        public void DoesNotRealizeEveryItemOfAVeryLargeList()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, _, panel) = BuildList(5000);
                Assert.NotNull(panel);

                var realized = panel.Children.Count;
                Assert.True(realized < 100, $"realized {realized} containers");
                Assert.True(realized >= 12, $"realized only {realized} containers");

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void RealizesOneRowOfOverscanAboveAndBelowTheVisibleViewportWhenScrolledToTheMiddle()
        {
            this._Fixture.Invoke(() =>
            {
                const double itemHeight = 156.0;
                var (host, list, panel) = BuildList(5000);
                var columns = Math.Max(1, (int)Math.Floor(panel.ViewportWidth / 248.0));

                // Scrolled well past the top and well short of the end, so neither overscan
                // row is clamped away by the edges of the list.
                panel.SetVerticalOffset(20 * itemHeight);
                WpfTestFixture.Pump();

                var realizedRows = panel.Children
                    .OfType<FrameworkElement>()
                    .Select(e => e.DataContext)
                    .OfType<GameViewModel>()
                    .Select(g => (int)g.Id / columns)
                    .ToList();
                Assert.NotEmpty(realizedRows);

                var visibleFirstRow = (int)Math.Floor(panel.VerticalOffset / itemHeight);
                var visibleLastRow = (int)Math.Ceiling((panel.VerticalOffset + panel.ViewportHeight) / itemHeight) - 1;

                Assert.Equal(visibleFirstRow - 1, realizedRows.Min());
                Assert.Equal(visibleLastRow + 1, realizedRows.Max());

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void OverscanAboveTheViewportIsClampedAtTheTopOfTheList()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, list, panel) = BuildList(5000);

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();

                var minRealizedIndex = panel.Children
                    .OfType<FrameworkElement>()
                    .Select(e => e.DataContext)
                    .OfType<GameViewModel>()
                    .Min(g => (int)g.Id);

                // Nothing precedes row 0, so there is no row above it left to overscan into.
                Assert.Equal(0, minRealizedIndex);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void ExtentCoversEveryRowForTheActualColumnCount()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, _, panel) = BuildList(5000);

                var columns = Math.Max(1, (int)Math.Floor(panel.ViewportWidth / 248.0));
                var expectedExtent = Math.Ceiling(5000.0 / columns) * 156.0;
                Assert.True(Math.Abs(panel.ExtentHeight - expectedExtent) < 1.0, $"{panel.ExtentHeight} vs {expectedExtent}");
                Assert.True(panel.ViewportHeight > 0);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void OffsetClampsAtBothEndsOfTheExtent()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, _, panel) = BuildList(5000);

                panel.SetVerticalOffset(20000);
                WpfTestFixture.Pump();
                Assert.Equal(20000.0, panel.VerticalOffset);
                Assert.True(panel.Children.Count < 100);

                panel.SetVerticalOffset(double.MaxValue);
                WpfTestFixture.Pump();
                Assert.True(Math.Abs(panel.VerticalOffset - (panel.ExtentHeight - panel.ViewportHeight)) < 1.0);

                panel.SetVerticalOffset(-500);
                WpfTestFixture.Pump();
                Assert.Equal(0.0, panel.VerticalOffset);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void SourceReplacementResetsToTheTopAndRealizesOnlyWhatTheNewListHas()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, list, panel) = BuildList(5000);
                var items = ((IEnumerable<GameViewModel>)list.ItemsSource).ToList();

                panel.SetVerticalOffset(5000);
                WpfTestFixture.Pump();

                list.ItemsSource = items.Take(4).ToList();
                WpfTestFixture.Pump();

                Assert.Equal(0.0, panel.VerticalOffset);
                Assert.Equal(4, panel.Children.Count);

                list.ItemsSource = new List<GameViewModel>();
                WpfTestFixture.Pump();
                Assert.Empty(panel.Children);
                Assert.Equal(0.0, panel.ExtentHeight);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void ScrollViewerObservesAPanelDrivenOffsetChangeImmediately()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, list, panel) = BuildList(500);
                var viewer = FindChild<ScrollViewer>(list);
                Assert.NotNull(viewer);

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();
                var before = viewer.VerticalOffset;

                panel.MouseWheelDown();
                WpfTestFixture.Pump();

                Assert.True(Math.Abs(viewer.VerticalOffset - panel.VerticalOffset) < 0.5);
                Assert.True(viewer.VerticalOffset > before);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void ScrollViewerApiDrivesThePanelAndPagingMovesAViewport()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, list, panel) = BuildList(500);
                var viewer = FindChild<ScrollViewer>(list);

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();
                viewer.ScrollToVerticalOffset(1500);
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();
                Assert.True(Math.Abs(panel.VerticalOffset - 1500) < 2.0, panel.VerticalOffset.ToString());

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();
                panel.LineDown();
                WpfTestFixture.Pump();
                Assert.True(panel.VerticalOffset is >= 16 and <= 120, panel.VerticalOffset.ToString());

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();
                panel.PageDown();
                WpfTestFixture.Pump();
                Assert.True(Math.Abs(panel.VerticalOffset - panel.ViewportHeight) < 2.0);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void ScrollbarThumbValueTracksTheContent()
        {
            this._Fixture.Invoke(() =>
            {
                var (host, list, panel) = BuildList(500);
                var viewer = FindChild<ScrollViewer>(list);
                var bar = FindVisibleVerticalScrollBar(viewer);
                Assert.NotNull(bar);

                panel.SetVerticalOffset(0);
                WpfTestFixture.Pump();
                var before = bar.Value;

                panel.SetVerticalOffset(2000);
                WpfTestFixture.Pump();

                Assert.True(Math.Abs(bar.Value - 2000) < 2.0);
                Assert.True(bar.Value > before);

                host.Close();
                WpfTestFixture.Pump();
            });
        }

        private static ScrollBar FindVisibleVerticalScrollBar(DependencyObject root)
        {
            ScrollBar found = null;
            Walk(root);
            return found;

            void Walk(DependencyObject node)
            {
                if (found != null || node == null)
                {
                    return;
                }
                if (node is ScrollBar sb && sb.Orientation == Orientation.Vertical && sb.Visibility == Visibility.Visible)
                {
                    found = sb;
                    return;
                }
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i));
                    if (found != null)
                    {
                        return;
                    }
                }
            }
        }

        [Fact]
        public void BackgroundReloadKeepsTheScrollPositionAndAShrunkListClampsTheOffset()
        {
            this._Fixture.Invoke(() =>
            {
                FakeStats steam = new() { AppId = 480, AppName = "Test", InstallPath = null };
                var definitions = new List<AchievementDefinition>();
                for (int i = 0; i < 200; i++)
                {
                    var id = $"ACH_{i:D3}";
                    steam.SeedAchievement(id, false);
                    definitions.Add(new() { Id = id, Name = "A" + i, Description = "D" + i, Permission = 0 });
                }

                AchievementManagerViewModel manager = new(steam, new FakeDialogService());
                var schema = new UserGameStatsSchema(definitions, Array.Empty<StatDefinition>());
                manager.Load(schema);

                var window = new SAM.Game.MainWindow(manager)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                    Width = 1120,
                    Height = 760,
                };
                window.Show();
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();

                var panel = FindChild<VirtualizingWrapPanel>(window);
                Assert.NotNull(panel);

                panel.SetVerticalOffset(1200);
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();
                var beforeReload = panel.VerticalOffset;

                // Steam can deliver UserStatsReceived more than once; each delivery reloads the
                // schema, which clears and refills the bound collection.
                manager.Load(schema);
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();

                Assert.True(Math.Abs(panel.VerticalOffset - beforeReload) < 2.0, $"{beforeReload} -> {panel.VerticalOffset}");

                var few = new UserGameStatsSchema(definitions.Take(4).ToList(), Array.Empty<StatDefinition>());
                manager.Load(few);
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();

                Assert.True(
                    panel.VerticalOffset <= Math.Max(0, panel.ExtentHeight - panel.ViewportHeight) + 0.5,
                    $"offset={panel.VerticalOffset} extent={panel.ExtentHeight} viewport={panel.ViewportHeight}");

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void AchievementsWrapPanelDeclaresStandardVirtualizationOnItself()
        {
            // VirtualizingPanel.VirtualizationMode is not an inherited property: a style
            // setter on the hosting ListBox is what VirtualizingStackPanel itself resolves
            // internally, but it never reaches a value read directly off the panel instance --
            // only a value actually set on the panel does. So this checks the one thing the
            // fix is actually responsible for: the wrap panel carries its own local value,
            // matching what it actually does, rather than reporting the framework default and
            // leaving that to be inferred from ListBox.Plain.
            this._Fixture.Invoke(() =>
            {
                FakeStats steam = new() { AppId = 480, AppName = "Test", InstallPath = null };
                var definitions = new List<AchievementDefinition>
                {
                    new() { Id = "ACH_A", Name = "A", Description = "", Permission = 0 },
                };
                AchievementManagerViewModel manager = new(steam, new FakeDialogService());
                manager.Load(new UserGameStatsSchema(definitions, Array.Empty<StatDefinition>()));

                var window = new SAM.Game.MainWindow(manager)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                };
                window.Show();
                WpfTestFixture.Pump();

                var achievementsPanel = FindChild<VirtualizingWrapPanel>(window);
                Assert.NotNull(achievementsPanel);
                Assert.Equal(VirtualizationMode.Standard, VirtualizingPanel.GetVirtualizationMode(achievementsPanel));

                // ListBox.Plain's own Recycling request is untouched by this fix -- it is
                // still declared on the ListBox that hosts the (separate) Statistics list,
                // which is what VirtualizingStackPanel actually resolves for itself. Selecting
                // the tab tears down the achievements content, so the only ListBox left in the
                // tree afterwards is Statistics'.
                var tabs = FindChild<TabControl>(window);
                Assert.NotNull(tabs);
                tabs.SelectedIndex = 1;
                WpfTestFixture.Pump();
                WpfTestFixture.Pump();

                var statisticsList = FindChild<ListBox>(window);
                Assert.NotNull(statisticsList);
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(statisticsList));

                window.Close();
                WpfTestFixture.Pump();
            });
        }

        [Fact]
        public void PickerGameListPanelDeclaresStandardVirtualization()
        {
            this._Fixture.Invoke(() =>
            {
                FakeLibrary library = new();
                library.Add(10, "Alpha");
                Task<List<GameListEntry>> Loader(CancellationToken ct) =>
                    Task.FromResult(new List<GameListEntry> { new(10, "normal") });
                var vm = new GameLibraryViewModel(library, Loader);

                var window = new SAM.Picker.MainWindow(vm)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                };
                window.Show();
                WpfTestFixture.Pump();

                var panel = FindChild<VirtualizingWrapPanel>(window);
                Assert.NotNull(panel);
                Assert.Equal(VirtualizationMode.Standard, VirtualizingPanel.GetVirtualizationMode(panel));

                window.Close();
                WpfTestFixture.Pump();
            });
        }
    }
}
