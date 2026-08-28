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
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SAM.Core.Caching;

namespace SAM.UI.Imaging
{
    /// <summary>
    /// Turns cached asset bytes into frozen <see cref="ImageSource"/> instances.
    /// </summary>
    /// <remarks>
    /// A frozen image source is shareable across threads and can be handed to any number of
    /// <c>Image</c> elements without being copied, so one entry here serves every card showing
    /// that artwork. Entries are capped by a least-recently-used bound on total decoded bytes,
    /// rather than a raw item count: a large library would otherwise accumulate decoded artwork
    /// for games the user scrolled past once, and how much of it fits in a fixed memory budget
    /// depends on how big each decoded image actually is, not how many of them there are. A
    /// re-decode from the disk cache costs a couple of milliseconds.
    /// </remarks>
    public sealed class ImageSourceCache : IDisposable
    {
        private readonly AssetCache _Assets;
        private readonly long _MaximumBytes;
        private readonly object _Lock;
        private readonly Dictionary<string, LinkedListNode<Entry>> _Index;
        private readonly LinkedList<Entry> _Recency;

        private long _SizeInBytes;
        private bool _IsDisposed;

        /// <param name="maximumBytes">
        /// Approximate ceiling, in bytes, on the total decoded size of everything cached. Least-
        /// recently-used entries are evicted as needed to stay under it.
        /// </param>
        public ImageSourceCache(string category, int maximumConcurrency, long maximumBytes)
        {
            if (maximumBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            this._Assets = new(category, maximumConcurrency);
            this._MaximumBytes = maximumBytes;
            this._Lock = new();
            this._Index = new(StringComparer.Ordinal);
            this._Recency = new();
        }

        public bool IsDiskCacheEnabled => this._Assets.IsDiskCacheEnabled;

        public string DiskCachePath => this._Assets.DiskCachePath;

        public int Count
        {
            get
            {
                lock (this._Lock)
                {
                    return this._Index.Count;
                }
            }
        }

        /// <summary>Estimated total decoded size of everything currently cached.</summary>
        public long SizeInBytes
        {
            get
            {
                lock (this._Lock)
                {
                    return this._SizeInBytes;
                }
            }
        }

        public void SchedulePrune(TimeSpan maximumAge) => this._Assets.SchedulePrune(maximumAge);

        /// <summary>
        /// Returns a decoded, frozen image for an asset, or <see langword="null"/> when it is
        /// unavailable. A hit is returned synchronously through an already-completed task.
        /// </summary>
        /// <param name="decodeWidth">
        /// Width to decode at, in pixels. Decoding to the size actually shown keeps large
        /// libraries from holding full-size artwork.
        /// </param>
        public Task<ImageSource> GetAsync(string identity, Uri uri, int decodeWidth)
        {
            if (string.IsNullOrEmpty(identity) == true)
            {
                return Task.FromResult<ImageSource>(null);
            }

            if (this.TryGetCached(identity, out var cached) == true)
            {
                return Task.FromResult(cached);
            }

            return this.LoadAsync(identity, uri, decodeWidth);
        }

        /// <summary>Synchronous lookup, for the paint path of a recycled container.</summary>
        public bool TryGetCached(string identity, out ImageSource image)
        {
            image = null;
            if (string.IsNullOrEmpty(identity) == true)
            {
                return false;
            }

            lock (this._Lock)
            {
                if (this._Index.TryGetValue(identity, out var node) == false)
                {
                    return false;
                }

                // Touch: move to the most-recent end.
                this._Recency.Remove(node);
                this._Recency.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        private async Task<ImageSource> LoadAsync(string identity, Uri uri, int decodeWidth)
        {
            var data = await this._Assets.GetAsync(identity, uri).ConfigureAwait(true);
            if (data == null)
            {
                return null;
            }

            // A second request for the same asset may have finished decoding while this one
            // was waiting on the network.
            if (this.TryGetCached(identity, out var existing) == true)
            {
                return existing;
            }

            var decoded = Decode(data, decodeWidth);
            if (decoded == null)
            {
                // Not a usable image: drop it so a later run can fetch a fresh copy.
                this._Assets.Invalidate(identity);
                return null;
            }

            this.Store(identity, decoded);
            return decoded;
        }

        private static ImageSource Decode(byte[] data, int decodeWidth)
        {
            try
            {
                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                if (decodeWidth > 0)
                {
                    image.DecodePixelWidth = decodeWidth;
                }
                image.StreamSource = new MemoryStream(data, false);
                image.EndInit();

                // OnLoad has already copied the pixels, so the stream is no longer needed and
                // freezing makes the result shareable and cheap to render.
                image.Freeze();
                return image;
            }
            catch (NotSupportedException)
            {
                // WPF reports an unrecognised or corrupt codec payload this way.
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// A rough estimate of an image's decoded footprint, used only to keep the cache's
        /// total under its byte budget. Four bytes per pixel covers every pixel format
        /// <see cref="Decode"/> actually produces, so this errs toward evicting sooner rather
        /// than letting real usage drift past the configured budget.
        /// </summary>
        private static long EstimateBytes(ImageSource image)
        {
            if (image is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                return (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
            }

            return 0;
        }

        private void Store(string identity, ImageSource image)
        {
            lock (this._Lock)
            {
                if (this._IsDisposed == true || this._Index.ContainsKey(identity) == true)
                {
                    return;
                }

                var size = EstimateBytes(image);
                var node = this._Recency.AddFirst(new Entry(identity, image, size));
                this._Index.Add(identity, node);
                this._SizeInBytes += size;

                // Never evict the entry just inserted: a single image estimated larger than
                // the whole budget would otherwise be unable to stay cached at all, forcing a
                // re-decode on every request.
                while (this._SizeInBytes > this._MaximumBytes &&
                       this._Recency.Last != null &&
                       this._Recency.Last != node)
                {
                    var oldest = this._Recency.Last;
                    this._Recency.RemoveLast();
                    this._Index.Remove(oldest.Value.Identity);
                    this._SizeInBytes -= oldest.Value.SizeInBytes;
                }
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
                this._Index.Clear();
                this._Recency.Clear();
                this._SizeInBytes = 0;
            }

            this._Assets.Dispose();
        }

        private readonly struct Entry
        {
            public readonly string Identity;
            public readonly ImageSource Image;
            public readonly long SizeInBytes;

            public Entry(string identity, ImageSource image, long sizeInBytes)
            {
                this.Identity = identity;
                this.Image = image;
                this.SizeInBytes = sizeInBytes;
            }
        }
    }
}
