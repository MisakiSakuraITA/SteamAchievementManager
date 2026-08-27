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
using System.Security.Cryptography;
using System.Text;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.Caching
{
    /// <summary>
    /// Turns a logical asset identity (an app id, an achievement id, an asset URL) into a
    /// short, filesystem-safe, collision-resistant file name.
    /// </summary>
    public static class CacheKey
    {
        private const int _KeyByteLength = 16;

        private static readonly char[] _HexDigits = "0123456789abcdef".ToCharArray();

        public static string FromIdentity(string identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            byte[] hash;
            using (SHA256 algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(identity));
            }

            var characters = new char[_KeyByteLength * 2];
            for (int i = 0; i < _KeyByteLength; i++)
            {
                var value = hash[i];
                characters[i * 2] = _HexDigits[value >> 4];
                characters[(i * 2) + 1] = _HexDigits[value & 0xF];
            }
            return new string(characters);
        }

        /// <summary>
        /// Identity for a game capsule/logo. The asset name is part of the identity so that
        /// a capsule replaced on the store naturally lands on a different cache entry.
        /// </summary>
        public static string ForGameLogo(uint appId, string assetUrl) => _($"logo:{appId}:{assetUrl}");

        public static string ForAchievementIcon(long appId, string iconName) => _($"achievement:{appId}:{iconName}");
    }
}
