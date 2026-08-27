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

namespace SAM.Core.Steam
{
    /// <summary>
    /// Everything the game library needs from the running Steam client.
    /// </summary>
    /// <remarks>
    /// Implementations talk to a live Steam pipe, which belongs to the thread that opened it.
    /// Every member here is expected to be called from that thread; the view models honour
    /// that by never touching a service from a background continuation.
    /// </remarks>
    public interface ISteamLibraryService
    {
        /// <summary>The language Steam is currently running games in, e.g. "english".</summary>
        string CurrentLanguage { get; }

        bool OwnsApp(uint appId);

        string GetAppName(uint appId);

        /// <summary>
        /// Resolves the store capsule for an app, or <see langword="null"/> when Steam has no
        /// artwork cached for it yet.
        /// </summary>
        string GetCapsuleUrl(uint appId);

        /// <summary>Pumps pending Steam callbacks. Raises <see cref="AppDataChanged"/>.</summary>
        void RunCallbacks();

        /// <summary>
        /// Raised with the app id whose metadata Steam has just filled in. Artwork and names
        /// often arrive this way well after the library has been listed.
        /// </summary>
        event Action<uint> AppDataChanged;
    }
}
