using System.Reflection;
using System.Windows.Media.Imaging;
using SAM.UI.Imaging;
using Xunit;

namespace SAM.Tests
{
    [Collection(WpfCollection.Name)]
    public class ImageSourceCacheTests
    {
        private readonly WpfTestFixture _Fixture;

        public ImageSourceCacheTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static readonly MethodInfo _StoreMethod =
            typeof(ImageSourceCache).GetMethod("Store", BindingFlags.NonPublic | BindingFlags.Instance);

        private static BitmapSource MakeBitmap(int width, int height)
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var bitmap = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }

        [Fact]
        public void RejectsANonPositiveByteBudget()
        {
            this._Fixture.Invoke(() =>
            {
                Assert.Throws<System.ArgumentOutOfRangeException>(() => new ImageSourceCache("t", 1, 0));
            });
        }

        [Fact]
        public void EntriesUnderBudgetAreAllKeptAndSizeIsTracked()
        {
            this._Fixture.Invoke(() =>
            {
                using ImageSourceCache cache = new("t-under-budget", 1, 1_000_000);
                var image = MakeBitmap(200, 200); // 160,000 bytes each
                for (var i = 0; i < 6; i++)
                {
                    _StoreMethod.Invoke(cache, new object[] { $"id-{i}", image });
                }

                Assert.Equal(6, cache.Count);
                Assert.Equal(200L * 200 * 4 * 6, cache.SizeInBytes);
            });
        }

        [Fact]
        public void ExceedingTheBudgetEvictsTheLeastRecentlyUsedEntryFirst()
        {
            this._Fixture.Invoke(() =>
            {
                using ImageSourceCache cache = new("t-lru", 1, 1_000_000);
                var image = MakeBitmap(200, 200); // 160,000 bytes each; 7 exceeds the budget
                for (var i = 0; i < 6; i++)
                {
                    _StoreMethod.Invoke(cache, new object[] { $"id-{i}", image });
                }

                // Touch id-0 so it becomes most-recently-used before the 7th entry forces
                // an eviction; id-1 is now the least-recently-used one instead.
                cache.TryGetCached("id-0", out _);
                _StoreMethod.Invoke(cache, new object[] { "id-6", image });

                Assert.Equal(6, cache.Count);
                Assert.True(cache.SizeInBytes <= 1_000_000);
                Assert.True(cache.TryGetCached("id-0", out _));
                Assert.False(cache.TryGetCached("id-1", out _));
                Assert.True(cache.TryGetCached("id-6", out _));
            });
        }

        [Fact]
        public void AnEntryLargerThanTheWholeBudgetIsStillCachedRatherThanDropped()
        {
            this._Fixture.Invoke(() =>
            {
                using ImageSourceCache cache = new("t-oversize", 1, 1_000_000);
                var huge = MakeBitmap(2000, 2000); // 16,000,000 bytes, over the budget alone

                _StoreMethod.Invoke(cache, new object[] { "huge", huge });

                Assert.Equal(1, cache.Count);
                Assert.True(cache.TryGetCached("huge", out _));
            });
        }

        [Fact]
        public void TryGetCachedMissesAnUnknownIdentity()
        {
            this._Fixture.Invoke(() =>
            {
                using ImageSourceCache cache = new("t-miss", 1, 1_000_000);
                Assert.False(cache.TryGetCached("nope", out var image));
                Assert.Null(image);
            });
        }

        [Fact]
        public void DisposeEmptiesTheCacheAndZeroesTheTrackedSize()
        {
            this._Fixture.Invoke(() =>
            {
                ImageSourceCache cache = new("t-dispose", 1, 1_000_000);
                _StoreMethod.Invoke(cache, new object[] { "id", MakeBitmap(64, 64) });
                Assert.Equal(1, cache.Count);

                cache.Dispose();

                Assert.Equal(0, cache.Count);
                Assert.Equal(0, cache.SizeInBytes);
            });
        }
    }
}
