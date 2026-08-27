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
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Core.WinForms
{
    /// <summary>
    /// Keyed slot allocator for an <see cref="ImageList"/>: it hands out stable image
    /// indices, collapses duplicate keys, and takes ownership of the bitmaps it is given.
    /// </summary>
    /// <remarks>
    /// This is the only part of SAM.Core that knows about Windows Forms; a different
    /// presentation layer replaces this type and nothing else.
    /// </remarks>
    public sealed class ImageListCache
    {
        private readonly ImageList _ImageList;
        private readonly Dictionary<string, int> _Indices;

        public ImageListCache(ImageList imageList)
        {
            this._ImageList = imageList ?? throw new ArgumentNullException(nameof(imageList));
            this._Indices = new(StringComparer.Ordinal);

            // Realise the native image list up front. Once the handle exists, every Add
            // copies the pixels into it, which is what makes releasing the source bitmap in
            // Add() safe -- before the handle exists the ImageList holds the caller's
            // instance instead, and disposing it corrupts the entry.
            _ = imageList.Handle;
        }

        public Size ImageSize => this._ImageList.ImageSize;

        public int Count => this._Indices.Count;

        public bool TryGetIndex(string key, out int index) => this._Indices.TryGetValue(key, out index);

        /// <summary>
        /// Adds an image under <paramref name="key"/> and returns its index, or returns the
        /// existing index if the key is already present. Takes ownership of
        /// <paramref name="bitmap"/> and disposes it either way.
        /// </summary>
        public int Add(string key, Bitmap bitmap)
        {
            if (string.IsNullOrEmpty(key) == true)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            try
            {
                if (this._Indices.TryGetValue(key, out int existing) == true)
                {
                    return existing;
                }

                var index = this._ImageList.Images.Count;
                this._ImageList.Images.Add(key, bitmap);
                this._Indices.Add(key, index);
                return index;
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }
}
