/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Net;
using SAM.Core.Threading;

namespace SAM.Core.Caching
{
    /// <summary>
    /// Three-tier asset loader: in-flight de-duplication, then the on-disk cache, then the
    /// Steam CDN. A cache hit never touches the network, and concurrent requests for the
    /// same asset share a single download.
    /// </summary>
    /// <remarks>
    /// This deals only in encoded bytes. Decoding belongs to the presentation layer, which
    /// knows the display size and the image type it wants; keeping decoded images here as
    /// well would duplicate what the presentation layer already has to hold.
    /// </remarks>
    public sealed class AssetCache : IDisposable
    {
        private static readonly Task<byte[]> _NoData = Task.FromResult<byte[]>(null);

        private readonly DiskAssetCache _Disk;
        private readonly SemaphoreSlim _NetworkSlots;
        private readonly CancellationTokenSource _Shutdown;
        private readonly CancellationToken _ShutdownToken;
        private readonly object _Lock;
        private readonly Dictionary<string, Task<byte[]>> _Pending;
        private readonly HashSet<string> _Unavailable;

        private bool _IsDisposed;

        public AssetCache(string category, int maximumConcurrency)
        {
            if (maximumConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            }

            this._Disk = new(category);
            this._NetworkSlots = new(maximumConcurrency, maximumConcurrency);
            this._Shutdown = new();
            this._ShutdownToken = this._Shutdown.Token;
            this._Lock = new();
            this._Pending = new(StringComparer.Ordinal);
            this._Unavailable = new(StringComparer.Ordinal);
        }

        public bool IsDiskCacheEnabled => this._Disk.IsEnabled;

        public string DiskCachePath => this._Disk.RootPath;

        /// <summary>
        /// Resolves an asset to its encoded bytes, or <see langword="null"/> when it is
        /// unavailable or shutdown intervened. The returned array is shared between callers
        /// and must be treated as read-only.
        /// </summary>
        public Task<byte[]> GetAsync(string identity, Uri uri)
        {
            if (string.IsNullOrEmpty(identity) == true)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            lock (this._Lock)
            {
                if (this._IsDisposed == true || this._Unavailable.Contains(identity) == true)
                {
                    return _NoData;
                }

                if (this._Pending.TryGetValue(identity, out var pending) == true)
                {
                    return pending;
                }

                // Hand off to the thread pool immediately. The first stretch of the load
                // opens a file handle, which would otherwise run on whichever thread asked
                // for the asset -- in practice, the UI thread.
                var task = Task.Run(() => this.LoadAsync(identity, uri), this._ShutdownToken);
                this._Pending[identity] = task;
                return task;
            }
        }

        /// <summary>
        /// Reports that a payload could not be used. The disk entry is dropped so a later run
        /// can fetch a fresh copy, and the asset is not requested again this session.
        /// </summary>
        public void Invalidate(string identity)
        {
            if (string.IsNullOrEmpty(identity) == true)
            {
                return;
            }

            this._Disk.TryRemove(CacheKey.FromIdentity(identity));

            lock (this._Lock)
            {
                this._Unavailable.Add(identity);
            }
        }

        /// <summary>
        /// Starts a background sweep of stale entries. Fire and forget; never throws.
        /// </summary>
        public void SchedulePrune(TimeSpan maximumAge)
        {
            if (this._Disk.IsEnabled == false)
            {
                return;
            }

            this._Disk.PruneAsync(maximumAge, this._ShutdownToken).Forget();
        }

        private async Task<byte[]> LoadAsync(string identity, Uri uri)
        {
            try
            {
                var key = CacheKey.FromIdentity(identity);

                var cached = await this._Disk.TryReadAsync(key, this._ShutdownToken).ConfigureAwait(false);
                if (cached != null)
                {
                    return cached;
                }

                if (uri == null)
                {
                    this.MarkUnavailable(identity);
                    return null;
                }

                byte[] downloaded;
                await this._NetworkSlots.WaitAsync(this._ShutdownToken).ConfigureAwait(false);
                try
                {
                    downloaded = await HttpDownloader
                        .TryGetBytesAsync(uri, this._ShutdownToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    this._NetworkSlots.Release();
                }

                if (downloaded == null || downloaded.Length == 0)
                {
                    this.MarkUnavailable(identity);
                    return null;
                }

                await this._Disk.WriteAsync(key, downloaded, this._ShutdownToken).ConfigureAwait(false);
                return downloaded;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            finally
            {
                lock (this._Lock)
                {
                    this._Pending.Remove(identity);
                }
            }
        }

        private void MarkUnavailable(string identity)
        {
            lock (this._Lock)
            {
                this._Unavailable.Add(identity);
            }
        }

        public void Dispose()
        {
            lock (this._Lock)
            {
                if (this._IsDisposed == true)
                {
                    return;
                }
                this._IsDisposed = true;
            }

            // The token source and semaphore are deliberately left undisposed: loads that are
            // still unwinding continue to observe both, and disposing them here would turn an
            // orderly cancellation into an ObjectDisposedException on a pool thread.
            this._Shutdown.Cancel();
        }
    }
}
