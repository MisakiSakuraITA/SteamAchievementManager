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
using System.Globalization;

namespace SAM.Core.Protocol
{
    /// <summary>
    /// Parses this application's own <c>sam://</c> launch links.
    /// </summary>
    public static class ProtocolUri
    {
        /// <summary>The URI scheme this application registers and accepts.</summary>
        public const string Scheme = "sam";

        /// <summary>
        /// Parses <c>sam://game/{appid}</c> or the shorthand <c>sam://{appid}</c> into an app
        /// id. The scheme and the literal "game" segment are matched case-insensitively, since
        /// neither carries meaning from its casing and a link typed or generated elsewhere
        /// should not silently fail over it. Returns false for anything else, including a
        /// wrong scheme, a missing or non-numeric id, or an id of 0 (never a real Steam app).
        /// </summary>
        public static bool TryParseAppId(string uriText, out uint appId)
        {
            appId = 0;

            if (string.IsNullOrWhiteSpace(uriText) == true)
            {
                return false;
            }

            if (Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out var uri) == false)
            {
                return false;
            }

            if (string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }

            var host = uri.Host ?? "";
            var path = uri.AbsolutePath.Trim('/');

            string candidate;
            if (string.Equals(host, "game", StringComparison.OrdinalIgnoreCase) == true)
            {
                // sam://game/440 -> Host "game", path "440".
                if (string.IsNullOrEmpty(path) == true)
                {
                    return false;
                }

                candidate = path.Split('/')[0];
            }
            else if (string.IsNullOrEmpty(host) == false && string.IsNullOrEmpty(path) == true)
            {
                // sam://440 -> Host "440", no path.
                candidate = host;
            }
            else
            {
                return false;
            }

            return uint.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId) == true
                && appId > 0;
        }
    }
}
