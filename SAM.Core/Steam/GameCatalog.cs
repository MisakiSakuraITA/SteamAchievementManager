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
using System.Xml;
using System.Xml.XPath;
using SAM.Core.Caching;
using SAM.Core.Net;

namespace SAM.Core.Steam
{
    public readonly struct GameListEntry
    {
        public readonly uint Id;
        public readonly string Type;

        public GameListEntry(uint id, string type)
        {
            this.Id = id;
            this.Type = type;
        }
    }

    /// <summary>
    /// Fetches and parses the master game list. A recent local copy is used without touching
    /// the network; a stale copy is still preferable to no list at all when the download
    /// fails, so the app stays usable offline.
    /// </summary>
    public static class GameCatalog
    {
        private const string _CacheCategory = "lists";
        private const string _CacheIdentity = "https://gib.me/sam/games.xml";

        private static readonly Uri _ListUri = new(_CacheIdentity);
        private static readonly TimeSpan _FreshFor = TimeSpan.FromHours(12);

        public static async Task<List<GameListEntry>> LoadAsync(CancellationToken cancellationToken)
        {
            DiskAssetCache cache = new(_CacheCategory);
            var key = CacheKey.FromIdentity(_CacheIdentity);

            if (cache.TryGetAge(key, out var age) == true && age < _FreshFor)
            {
                var fresh = await cache.TryReadAsync(key, cancellationToken).ConfigureAwait(false);
                var cached = TryParse(fresh);
                if (cached != null)
                {
                    return cached;
                }
            }

            var downloadResult = await HttpDownloader.TryGetBytesAsync(_ListUri, cancellationToken).ConfigureAwait(false);
            var downloaded = downloadResult.Data;
            var parsed = TryParse(downloaded);
            if (parsed != null)
            {
                await cache.WriteAsync(key, downloaded, cancellationToken).ConfigureAwait(false);
                return parsed;
            }

            var stale = await cache.TryReadAsync(key, cancellationToken).ConfigureAwait(false);
            var fallback = TryParse(stale);
            if (fallback != null)
            {
                return fallback;
            }

            throw new InvalidOperationException("could not download the game list, and no cached copy is available");
        }

        private static List<GameListEntry> TryParse(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            try
            {
                List<GameListEntry> entries = new();

                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    CloseInput = false,
                };

                using (MemoryStream stream = new(data, false))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    XPathDocument document = new(reader);
                    var navigator = document.CreateNavigator();
                    var nodes = navigator.Select("/games/game");
                    while (nodes.MoveNext() == true)
                    {
                        var type = nodes.Current.GetAttribute("type", "");
                        if (string.IsNullOrEmpty(type) == true)
                        {
                            type = "normal";
                        }
                        entries.Add(new((uint)nodes.Current.ValueAsLong, type));
                    }
                }

                return entries.Count > 0 ? entries : null;
            }
            catch (XmlException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }
}
