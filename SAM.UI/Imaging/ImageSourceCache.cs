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
    /// that artwork. Entries are capped by a least-recently-used bound: a large library would
    /// otherwise accumulate decoded artwork for games the user scrolled past once, and a
    /// re-decode from the disk cache costs a couple of milliseconds.
    /// </remarks>
    public sealed class ImageSourceCache : IDisposable
    {
        private readonly AssetCache _Assets;
        private readonly int _Capacity;
        private readonly object _Lock;
        private readonly Dictionary<string, LinkedListNode<Entry>> _Index;
        private readonly LinkedList<Entry> _Recency;

        private bool _IsDisposed;

        public ImageSourceCache(string category, int maximumConcurrency, int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this._Assets = new(category, maximumConcurrency);
            this._Capacity = capacity;
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

        private void Store(string identity, ImageSource image)
        {
            lock (this._Lock)
            {
                if (this._IsDisposed == true || this._Index.ContainsKey(identity) == true)
                {
                    return;
                }

                var node = this._Recency.AddFirst(new Entry(identity, image));
                this._Index.Add(identity, node);

                while (this._Index.Count > this._Capacity)
                {
                    var oldest = this._Recency.Last;
                    if (oldest == null)
                    {
                        break;
                    }

                    this._Recency.RemoveLast();
                    this._Index.Remove(oldest.Value.Identity);
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
            }

            this._Assets.Dispose();
        }

        private readonly struct Entry
        {
            public readonly string Identity;
            public readonly ImageSource Image;

            public Entry(string identity, ImageSource image)
            {
                this.Identity = identity;
                this.Image = image;
            }
        }
    }
}
