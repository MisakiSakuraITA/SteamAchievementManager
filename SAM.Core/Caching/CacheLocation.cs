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

namespace SAM.Core.Caching
{
    /// <summary>
    /// Resolves the root directory used for on-disk caches. Prefers
    /// <c>%AppData%\SAM\Cache</c> and falls back to a <c>Cache</c> directory next to the
    /// executable so the tool keeps working when run from read-only or portable locations.
    /// </summary>
    public static class CacheLocation
    {
        private const string _VendorFolderName = "SAM";
        private const string _CacheFolderName = "Cache";

        private static readonly object _Lock = new();
        private static bool _IsResolved;
        private static string _Root;

        /// <summary>
        /// The cache root, or <see langword="null"/> when no writable location exists. A
        /// null root disables disk caching without disabling the application.
        /// </summary>
        public static string Root
        {
            get
            {
                lock (_Lock)
                {
                    if (_IsResolved == false)
                    {
                        _Root = Resolve();
                        _IsResolved = true;
                    }
                    return _Root;
                }
            }
        }

        public static string GetCategoryPath(string category)
        {
            if (string.IsNullOrEmpty(category) == true)
            {
                throw new ArgumentNullException(nameof(category));
            }

            var root = Root;
            return root == null
                ? null
                : Path.Combine(root, category);
        }

        private static string Resolve()
        {
            foreach (var candidate in EnumerateCandidates())
            {
                if (string.IsNullOrEmpty(candidate) == true)
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                }
                catch (Exception)
                {
                    // Try the next candidate; caching is optional.
                    continue;
                }

                if (IsWritable(candidate) == true)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// <see cref="Directory.CreateDirectory(string)"/> succeeds on a directory that already exists
        /// even when this process cannot write to it, which is exactly the shape of a locked-
        /// down or roaming profile: the first candidate resolves as usable, every real write
        /// afterwards fails silently, and the executable-adjacent fallback never gets a
        /// chance. A genuine write-and-delete is the only way to actually tell.
        /// </summary>
        private static bool IsWritable(string directory)
        {
            var probePath = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
                File.Delete(probePath);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    File.Delete(probePath);
                }
                catch (Exception)
                {
                    // Best-effort cleanup only.
                }
                return false;
            }
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            string applicationData;
            try
            {
                applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            catch (Exception)
            {
                applicationData = null;
            }

            if (string.IsNullOrEmpty(applicationData) == false)
            {
                yield return Path.Combine(applicationData, _VendorFolderName, _CacheFolderName);
            }

            string baseDirectory;
            try
            {
                baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch (Exception)
            {
                baseDirectory = null;
            }

            if (string.IsNullOrEmpty(baseDirectory) == false)
            {
                yield return Path.Combine(baseDirectory, _CacheFolderName);
            }
        }
    }
}
