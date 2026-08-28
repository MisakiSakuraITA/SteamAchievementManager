using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using SAM.Core.Caching;
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

        private static byte[] EncodePng(BitmapSource bitmap)
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
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

        [Fact]
        public void StoreReturnsTheAlreadyCachedInstanceWhenTwoDecodesRaceForTheSameIdentity()
        {
            this._Fixture.Invoke(() =>
            {
                using ImageSourceCache cache = new("t-race", 1, 1_000_000);
                var first = MakeBitmap(64, 64);
                var second = MakeBitmap(64, 64); // a distinct instance, same identity as `first`

                var firstResult = (System.Windows.Media.ImageSource)_StoreMethod.Invoke(cache, new object[] { "id", first });
                var secondResult = (System.Windows.Media.ImageSource)_StoreMethod.Invoke(cache, new object[] { "id", second });

                // Both callers must end up sharing the winner's instance -- the first one
                // stored -- rather than the second race silently overwriting it, or handing
                // its own caller back an uncounted copy.
                Assert.Same(first, firstResult);
                Assert.Same(first, secondResult);
                Assert.Equal(1, cache.Count);
            });
        }

        [Fact]
        public async Task GetAsyncDecodesFromDiskOffTheUiThreadAndReturnsAFrozenImage()
        {
            var category = "t-diskhit-" + Guid.NewGuid().ToString("N");
            try
            {
                const string identity = "some-icon";
                var key = CacheKey.FromIdentity(identity);

                BitmapSource seed = null;
                this._Fixture.Invoke(() => seed = MakeBitmap(48, 48));
                var encoded = EncodePng(seed);

                DiskAssetCache disk = new(category);
                Assert.True(await disk.WriteAsync(key, encoded, CancellationToken.None));

                // Run entirely off the UI thread with no captured SynchronizationContext, so
                // this only passes if decoding and Freeze() genuinely do not need a
                // dispatcher to complete -- a disk hit never touches the network either way,
                // so this exercises exactly the decode path LoadAsync now runs with
                // ConfigureAwait(false).
                var resolved = await Task.Run(async () =>
                {
                    using ImageSourceCache cache = new(category, 1, 1_000_000);
                    return await cache.GetAsync(identity, null, 0);
                });

                Assert.NotNull(resolved);
                Assert.True(((Freezable)resolved).IsFrozen);
            }
            finally
            {
                var path = CacheLocation.GetCategoryPath(category);
                if (path != null && Directory.Exists(path))
                {
                    try { Directory.Delete(path, true); } catch { }
                }
            }
        }
    }
}
