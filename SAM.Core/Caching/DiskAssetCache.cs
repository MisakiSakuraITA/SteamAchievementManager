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
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.IO;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.Caching
{
    /// <summary>
    /// A content-addressed blob cache on disk. Entries live at
    /// <c>&lt;root&gt;\&lt;category&gt;\&lt;shard&gt;\&lt;hash&gt;.bin</c>; sharding on the
    /// first two characters of the hash keeps any single directory small even for very
    /// large libraries.
    /// </summary>
    /// <remarks>
    /// Every operation is best-effort. A cache that cannot be read or written degrades to a
    /// permanent miss rather than surfacing an error to the user.
    /// </remarks>
    public sealed class DiskAssetCache
    {
        private const string _EntryExtension = ".bin";
        private const int _ShardLength = 2;
        private const long _MaximumEntryLength = 32 * 1024 * 1024;

        private readonly string _Root;
        private readonly object _Lock;
        private readonly HashSet<string> _KnownDirectories;

        public DiskAssetCache(string category)
        {
            this._Root = CacheLocation.GetCategoryPath(category);
            this._Lock = new();
            this._KnownDirectories = new(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsEnabled => this._Root != null;

        public string RootPath => this._Root;

        public string GetEntryPath(string key)
        {
            if (this._Root == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(key) == true || key.Length <= _ShardLength)
            {
                throw new ArgumentException("key is too short to shard", nameof(key));
            }

            return Path.Combine(this._Root, key.Substring(0, _ShardLength), key + _EntryExtension);
        }

        public bool TryGetAge(string key, out TimeSpan age)
        {
            age = default;

            var path = this.GetEntryPath(key);
            if (path == null)
            {
                return false;
            }

            try
            {
                if (File.Exists(path) == false)
                {
                    return false;
                }

                var written = File.GetLastWriteTimeUtc(path);
                var elapsed = DateTime.UtcNow - written;
                age = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
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

        public Task<byte[]> TryReadAsync(string key, CancellationToken cancellationToken)
        {
            var path = this.GetEntryPath(key);
            return path == null
                ? Task.FromResult<byte[]>(null)
                : AsyncFile.TryReadAllBytesAsync(path, _MaximumEntryLength, cancellationToken);
        }

        public async Task<bool> WriteAsync(string key, byte[] data, CancellationToken cancellationToken)
        {
            var path = this.GetEntryPath(key);
            if (path == null || data == null || data.Length == 0 || data.Length > _MaximumEntryLength)
            {
                return false;
            }

            if (this.EnsureDirectory(Path.GetDirectoryName(path)) == false)
            {
                return false;
            }

            return await AsyncFile.TryWriteAllBytesAtomicAsync(path, data, cancellationToken).ConfigureAwait(false);
        }

        public bool TryRemove(string key)
        {
            var path = this.GetEntryPath(key);
            return path != null && AsyncFile.TryDelete(path);
        }

        /// <summary>
        /// Drops entries that have not been touched for <paramref name="maximumAge"/>, plus
        /// any temporary files orphaned by an interrupted write. Never throws.
        /// </summary>
        public Task<int> PruneAsync(TimeSpan maximumAge, CancellationToken cancellationToken)
        {
            if (this._Root == null || maximumAge <= TimeSpan.Zero)
            {
                return Task.FromResult(0);
            }

            return Task.Run(() => this.Prune(maximumAge, cancellationToken), cancellationToken);
        }

        private int Prune(TimeSpan maximumAge, CancellationToken cancellationToken)
        {
            int removed = 0;
            try
            {
                if (Directory.Exists(this._Root) == false)
                {
                    return 0;
                }

                var cutoff = DateTime.UtcNow - maximumAge;
                foreach (var path in Directory.EnumerateFiles(this._Root, "*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested == true)
                    {
                        break;
                    }

                    try
                    {
                        var isTemporary = path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
                        if (isTemporary == false && File.GetLastWriteTimeUtc(path) >= cutoff)
                        {
                            continue;
                        }
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (AsyncFile.TryDelete(path) == true)
                    {
                        removed++;
                    }
                }
            }
            catch (Exception)
            {
                // Housekeeping only; never let it reach the user.
            }
            return removed;
        }

        private bool EnsureDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) == true)
            {
                return false;
            }

            lock (this._Lock)
            {
                if (this._KnownDirectories.Contains(directory) == true)
                {
                    return true;
                }
            }

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception)
            {
                return false;
            }

            lock (this._Lock)
            {
                this._KnownDirectories.Add(directory);
            }
            return true;
        }

        public override string ToString() => _($"DiskAssetCache({this._Root ?? "disabled"})");
    }
}
