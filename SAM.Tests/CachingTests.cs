using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Caching;
using SAM.Core.IO;
using SAM.Core.Threading;
using Xunit;

namespace SAM.Tests
{
    public class CacheKeyTests
    {
        [Fact]
        public void FromIdentityIsDeterministicAndDistinctPerIdentity()
        {
            var a = CacheKey.FromIdentity("logo:480:https://example/x.jpg");
            var b = CacheKey.FromIdentity("logo:480:https://example/x.jpg");
            var c = CacheKey.FromIdentity("logo:481:https://example/x.jpg");

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void FromIdentityIsThirtyTwoLowercaseHexCharacters()
        {
            var key = CacheKey.FromIdentity("logo:480:https://example/x.jpg");

            Assert.Equal(32, key.Length);
            Assert.All(key, ch => Assert.Contains(ch, "0123456789abcdef"));
        }

        [Fact]
        public void ForGameLogoAndForAchievementIconShapeTheIdentity()
        {
            Assert.Equal("logo:480:u.jpg", CacheKey.ForGameLogo(480, "u.jpg"));
            Assert.Equal("achievement:480:i.jpg", CacheKey.ForAchievementIcon(480, "i.jpg"));
        }
    }

    public class DiskAssetCacheTests : IDisposable
    {
        private readonly string _Category = "selftest-" + Guid.NewGuid().ToString("N");

        [Fact]
        public async Task WriteThenReadRoundTripsTheExactBytes()
        {
            DiskAssetCache cache = new(this._Category);
            Assert.True(cache.IsEnabled);

            var key = CacheKey.FromIdentity("blob-1");
            var payload = new byte[4096];
            new Random(1234).NextBytes(payload);

            Assert.Null(await cache.TryReadAsync(key, CancellationToken.None));
            Assert.False(cache.TryGetAge(key, out _));

            Assert.True(await cache.WriteAsync(key, payload, CancellationToken.None));

            var read = await cache.TryReadAsync(key, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(payload, read);
            Assert.True(cache.TryGetAge(key, out var age));
            Assert.True(age < TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task EntriesAreShardedByTheFirstTwoHashCharacters()
        {
            DiskAssetCache cache = new(this._Category);
            var key = CacheKey.FromIdentity("blob-2");
            await cache.WriteAsync(key, new byte[] { 1, 2, 3 }, CancellationToken.None);

            var path = cache.GetEntryPath(key);
            var shard = Path.GetFileName(Path.GetDirectoryName(path));

            Assert.Equal(key.Substring(0, 2), shard);
            Assert.Equal(key + ".bin", Path.GetFileName(path));
        }

        [Fact]
        public async Task RemoveDeletesTheEntry()
        {
            DiskAssetCache cache = new(this._Category);
            var key = CacheKey.FromIdentity("blob-3");
            await cache.WriteAsync(key, new byte[] { 1, 2, 3 }, CancellationToken.None);

            Assert.True(cache.TryRemove(key));
            Assert.Null(await cache.TryReadAsync(key, CancellationToken.None));
        }

        [Fact]
        public async Task PruneRemovesStaleEntriesAndOrphanedTemporaryFiles()
        {
            DiskAssetCache cache = new(this._Category);
            var freshKey = CacheKey.FromIdentity("prune-fresh");
            var staleKey = CacheKey.FromIdentity("prune-stale");
            await cache.WriteAsync(freshKey, new byte[] { 1, 2, 3 }, CancellationToken.None);
            await cache.WriteAsync(staleKey, new byte[] { 4, 5, 6 }, CancellationToken.None);
            File.SetLastWriteTimeUtc(cache.GetEntryPath(staleKey), DateTime.UtcNow.AddDays(-200));

            var orphan = cache.GetEntryPath(freshKey) + ".abandoned.tmp";
            File.WriteAllBytes(orphan, new byte[] { 0 });

            var removed = await cache.PruneAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal(2, removed);
            Assert.Null(await cache.TryReadAsync(staleKey, CancellationToken.None));
            Assert.NotNull(await cache.TryReadAsync(freshKey, CancellationToken.None));
            Assert.False(File.Exists(orphan));
        }

        public void Dispose()
        {
            var path = CacheLocation.GetCategoryPath(this._Category);
            if (path != null && Directory.Exists(path))
            {
                try { Directory.Delete(path, true); } catch { }
            }
        }
    }

    public class AsyncFileTests : IDisposable
    {
        private readonly string _Directory = Path.Combine(Path.GetTempPath(), "SAM.Tests-" + Guid.NewGuid().ToString("N"));

        public AsyncFileTests()
        {
            Directory.CreateDirectory(this._Directory);
        }

        [Fact]
        public async Task ConcurrentWritersToTheSameEntryLeaveExactlyOneIntactPublishAndNoTemporaryFiles()
        {
            var path = Path.Combine(this._Directory, "concurrent.bin");
            var payload = new byte[1024];
            new Random(7).NextBytes(payload);

            var writers = Enumerable.Range(0, 10)
                .Select(_ => AsyncFile.TryWriteAllBytesAtomicAsync(path, payload, CancellationToken.None))
                .ToArray();
            var results = await Task.WhenAll(writers);

            Assert.All(results, Assert.True);

            var final = await AsyncFile.TryReadAllBytesAsync(path, 1 << 20, CancellationToken.None);
            Assert.Equal(payload, final);

            Assert.Empty(Directory.GetFiles(this._Directory, "*.tmp"));
        }

        [Fact]
        public async Task ReadRejectsAnOversizeOrMissingFile()
        {
            var path = Path.Combine(this._Directory, "sized.bin");
            await AsyncFile.TryWriteAllBytesAtomicAsync(path, new byte[64], CancellationToken.None);

            Assert.Null(await AsyncFile.TryReadAllBytesAsync(path, 16, CancellationToken.None));
            Assert.Null(await AsyncFile.TryReadAllBytesAsync(path + ".nope", 1 << 20, CancellationToken.None));
        }

        public void Dispose()
        {
            try { Directory.Delete(this._Directory, true); } catch { }
        }
    }

    public class TaskExtensionsTests
    {
        [Fact]
        public void ForgetDoesNotRethrowAtTheCallSite()
        {
            var faulted = Task.Run(() => throw new InvalidOperationException("boom"));

            var exception = Record.Exception(() => faulted.Forget());

            Assert.Null(exception);
        }

        [Fact]
        public async Task ForgetObservesTheFaultRatherThanLeavingItUnobserved()
        {
            // Matched by message rather than just counting notifications: this handler is
            // process-wide, and other tests running concurrently in a different collection can
            // have their own unrelated faults pass through it too.
            const string marker = "SAM.Tests.Forget.boom2";
            var sawOurs = false;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(x => x.Message == marker))
                {
                    sawOurs = true;
                }
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                Task.Run(() => throw new InvalidOperationException(marker)).Forget();
                await Task.Delay(50);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50);

                Assert.False(sawOurs);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        [Fact]
        public void ForgetRejectsNull()
        {
            Task task = null;
            Assert.Throws<ArgumentNullException>(() => task.Forget());
        }
    }
}
