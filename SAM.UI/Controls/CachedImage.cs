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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SAM.Core.Threading;
using SAM.UI.Imaging;

namespace SAM.UI.Controls
{
    /// <summary>
    /// An <see cref="Image"/> that resolves its content through an
    /// <see cref="ImageSourceCache"/> instead of being handed a source directly.
    /// </summary>
    /// <remarks>
    /// This is what makes virtualised card lists work: when a container is recycled onto a
    /// different item, the identity changes, a cached image is applied immediately, and only a
    /// genuine miss goes near the disk or the network. A generation counter discards results
    /// that arrive after the control has already moved on to another item.
    /// </remarks>
    public class CachedImage : Image
    {
        public static readonly DependencyProperty CacheProperty = DependencyProperty.Register(
            nameof(Cache),
            typeof(ImageSourceCache),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourceInputChanged));

        public static readonly DependencyProperty CacheIdentityProperty = DependencyProperty.Register(
            nameof(CacheIdentity),
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourceInputChanged));

        public static readonly DependencyProperty SourceUriProperty = DependencyProperty.Register(
            nameof(SourceUri),
            typeof(Uri),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourceInputChanged));

        public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(
            nameof(DecodeWidth),
            typeof(int),
            typeof(CachedImage),
            new PropertyMetadata(0, OnSourceInputChanged));

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder),
            typeof(ImageSource),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourceInputChanged));

        private static readonly DependencyPropertyKey _IsLoadedFromCachePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsResolved),
                typeof(bool),
                typeof(CachedImage),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsResolvedProperty = _IsLoadedFromCachePropertyKey.DependencyProperty;

        private int _Generation;

        public ImageSourceCache Cache
        {
            get => (ImageSourceCache)this.GetValue(CacheProperty);
            set => this.SetValue(CacheProperty, value);
        }

        /// <summary>Cache key for the asset; changing it starts a new resolve.</summary>
        public string CacheIdentity
        {
            get => (string)this.GetValue(CacheIdentityProperty);
            set => this.SetValue(CacheIdentityProperty, value);
        }

        /// <summary>Where to fetch the asset on a cache miss.</summary>
        public Uri SourceUri
        {
            get => (Uri)this.GetValue(SourceUriProperty);
            set => this.SetValue(SourceUriProperty, value);
        }

        /// <summary>Pixel width to decode at; 0 decodes at native size.</summary>
        public int DecodeWidth
        {
            get => (int)this.GetValue(DecodeWidthProperty);
            set => this.SetValue(DecodeWidthProperty, value);
        }

        /// <summary>Shown while the asset is resolving, or when it cannot be resolved.</summary>
        public ImageSource Placeholder
        {
            get => (ImageSource)this.GetValue(PlaceholderProperty);
            set => this.SetValue(PlaceholderProperty, value);
        }

        /// <summary>Whether real artwork is currently shown rather than the placeholder.</summary>
        public bool IsResolved
        {
            get => (bool)this.GetValue(IsResolvedProperty);
            private set => this.SetValue(_IsLoadedFromCachePropertyKey, value);
        }

        private static void OnSourceInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CachedImage)d).Refresh();
        }

        private void Refresh()
        {
            // Anything already in flight belongs to a previous item.
            var generation = ++this._Generation;

            var cache = this.Cache;
            var identity = this.CacheIdentity;

            if (cache == null || string.IsNullOrEmpty(identity) == true)
            {
                this.Apply(null);
                return;
            }

            if (cache.TryGetCached(identity, out var cached) == true)
            {
                this.Apply(cached);
                return;
            }

            this.Apply(null);
            this.ResolveAsync(cache, identity, this.SourceUri, this.DecodeWidth, generation).Forget();
        }

        private async Task ResolveAsync(ImageSourceCache cache, string identity, Uri uri, int decodeWidth, int generation)
        {
            ImageSource resolved;
            try
            {
                resolved = await cache.GetAsync(identity, uri, decodeWidth).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Artwork is decorative; a failure here must never reach the dispatcher.
                return;
            }

            if (generation != this._Generation || resolved == null)
            {
                return;
            }

            this.Apply(resolved);
        }

        private void Apply(ImageSource image)
        {
            this.IsResolved = image != null;
            this.Source = image ?? this.Placeholder;
        }
    }
}
