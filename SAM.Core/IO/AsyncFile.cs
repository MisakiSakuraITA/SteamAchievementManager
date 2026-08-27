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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.IO
{
    /// <summary>
    /// Non-blocking file helpers. Every entry point is best-effort: callers get
    /// <see langword="null"/> or <see langword="false"/> instead of an exception, because
    /// all current callers treat file trouble as a cache miss.
    /// </summary>
    public static class AsyncFile
    {
        private const int _BufferSize = 16 * 1024;

        public static async Task<byte[]> TryReadAllBytesAsync(
            string path,
            long maximumLength,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path) == true || File.Exists(path) == false)
            {
                return null;
            }

            try
            {
                using (FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    _BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var length = stream.Length;
                    if (length <= 0 || length > maximumLength)
                    {
                        return null;
                    }

                    var buffer = new byte[(int)length];
                    int offset = 0;
                    while (offset < buffer.Length)
                    {
                        int read = await stream
                            .ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            return null;
                        }
                        offset += read;
                    }
                    return buffer;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes to a unique temporary file and renames it into place, so a process that
        /// dies mid-write can never leave a truncated entry behind that later reads as valid.
        /// </summary>
        public static async Task<bool> TryWriteAllBytesAtomicAsync(
            string path,
            byte[] data,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path) == true || data == null || data.Length == 0)
            {
                return false;
            }

            var temporaryPath = _($"{path}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    _BufferSize,
                    FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    File.Move(temporaryPath, path);
                }
                catch (IOException) when (File.Exists(path) == true)
                {
                    // Another SAM process published the same entry first; theirs is as good
                    // as ours, so drop the temporary copy and report success.
                    TryDelete(temporaryPath);
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                TryDelete(temporaryPath);
                throw;
            }
            catch (Exception)
            {
                TryDelete(temporaryPath);
                return false;
            }
        }

        public static bool TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path) == true)
            {
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
