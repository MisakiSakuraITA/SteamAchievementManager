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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SAM.Core.Imaging
{
    /// <summary>
    /// Decodes downloaded image bytes into bitmaps that are safe to hand around.
    /// </summary>
    public static class BitmapDecoder
    {
        /// <summary>
        /// Decodes <paramref name="data"/> and rescales it to <paramref name="targetSize"/>,
        /// returning <see langword="null"/> if the payload is not a usable image.
        /// </summary>
        /// <remarks>
        /// The result is a standalone bitmap with no tie back to the source stream. GDI+
        /// keeps a live reference to the stream a <see cref="Bitmap"/> was constructed from,
        /// so returning one directly would leave the caller holding an image backed by a
        /// disposed stream.
        /// </remarks>
        public static Bitmap TryDecode(byte[] data, Size targetSize)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new(data, false))
                using (var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false))
                {
                    return Rescale(source, targetSize);
                }
            }
            catch (ArgumentException)
            {
                // GDI+ reports malformed image data as ArgumentException...
                return null;
            }
            catch (OutOfMemoryException)
            {
                // ...and reports an unsupported pixel format as OutOfMemoryException.
                return null;
            }
            catch (ExternalException)
            {
                return null;
            }
        }

        private static Bitmap Rescale(Image source, Size targetSize)
        {
            var size = targetSize.Width > 0 && targetSize.Height > 0
                ? targetSize
                : source.Size;

            Bitmap target = new(size.Width, size.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(target))
                using (ImageAttributes attributes = new())
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;

                    // Clamping the sampler stops the bicubic filter from bleeding transparent
                    // pixels in along the edges of the scaled image.
                    attributes.SetWrapMode(WrapMode.TileFlipXY);

                    graphics.DrawImage(
                        source,
                        new Rectangle(Point.Empty, size),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
                return target;
            }
            catch (Exception)
            {
                target.Dispose();
                throw;
            }
        }
    }
}
