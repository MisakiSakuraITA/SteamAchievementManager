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
using System.IO;

namespace SAM.Core.Steam
{
    /// <summary>
    /// Reads the locally-cached persona name for a SteamID64 out of Steam's own
    /// <c>config/loginusers.vdf</c>.
    /// </summary>
    /// <remarks>
    /// A live persona name is a property of <c>ISteamFriends::GetPersonaName</c>, an
    /// interface this project has never bound. Binding it would mean guessing both a
    /// Steamworks interface version string and the full, precisely-ordered vtable layout
    /// behind it -- a much larger and less verifiable guess than wrapping an already-declared
    /// slot on an interface already bound elsewhere in this project (compare
    /// <c>SteamUserStats013.RequestGlobalAchievementPercentages</c>), and one where a wrong
    /// guess is invoked as a raw function pointer against a live Steam client rather than
    /// simply failing a unit test. Reading Steam's own login-users file instead -- the same
    /// file every established third-party Steam tool already relies on for this -- gets the
    /// name with no interop risk at all, at the cost of being only as fresh as Steam's last
    /// write to it.
    /// </remarks>
    public static class LocalSteamProfile
    {
        /// <summary>
        /// Looks up the persona name Steam last recorded for <paramref name="steamId64"/>.
        /// Best-effort: returns <see langword="null"/> for anything from a missing install
        /// path to a SteamID64 the file has no entry for, never throws.
        /// </summary>
        public static string GetPersonaName(string installPath, ulong steamId64)
        {
            if (string.IsNullOrEmpty(installPath) == true || steamId64 == 0)
            {
                return null;
            }

            try
            {
                var path = Path.Combine(installPath, "config", "loginusers.vdf");
                var kv = KeyValue.LoadAsText(path);
                if (kv == null)
                {
                    return null;
                }

                var idText = steamId64.ToString(CultureInfo.InvariantCulture);
                var name = kv["users"][idText]["PersonaName"].AsString(null);
                return string.IsNullOrEmpty(name) == true ? null : name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves the locally-cached avatar image for <paramref name="steamId64"/>: the
        /// hash Steam recorded for it in <c>loginusers.vdf</c>, then whichever of the file
        /// names Steam's own avatar cache has used over the years actually exists on disk, and
        /// failing that, a per-account file under <c>userdata</c> that a Steam install may
        /// have instead. Best-effort, exactly like <see cref="GetPersonaName"/>: returns
        /// <see langword="null"/> rather than throwing, and rather than pointing at a file
        /// that turns out not to be there.
        /// </summary>
        /// <remarks>
        /// Every candidate below is tried purely as "does this file exist" -- a candidate this
        /// install's Steam version never wrote is simply skipped, at the cost of nothing but
        /// one more <see cref="File.Exists"/> check, so the list can be generous without risk.
        /// One thing this deliberately does not do: guess a registry location for the avatar
        /// hash itself. <see cref="Steam.GetInstallPath"/> reads a real, documented registry
        /// value, but nothing about an avatar hash is known to live in the registry at all --
        /// inventing a key on the chance one might exist would only add a lookup that can never
        /// succeed.
        /// </remarks>
        public static string GetAvatarFilePath(string installPath, ulong steamId64)
        {
            if (string.IsNullOrEmpty(installPath) == true || steamId64 == 0)
            {
                return null;
            }

            try
            {
                // The hash-named cache under config/avatars is tried first, since it is keyed
                // to the exact avatar Steam last recorded rather than just the account -- but
                // a missing or unreadable loginusers.vdf must not skip the fallback below,
                // which needs nothing from it.
                var path = Path.Combine(installPath, "config", "loginusers.vdf");
                var kv = KeyValue.LoadAsText(path);
                if (kv != null)
                {
                    var idText = steamId64.ToString(CultureInfo.InvariantCulture);
                    var hash = kv["users"][idText]["avatar"].AsString(null);
                    if (string.IsNullOrEmpty(hash) == false)
                    {
                        var avatarsDirectory = Path.Combine(installPath, "config", "avatars");
                        foreach (var fileName in new[] { hash + "_full.jpg", hash + "_medium.jpg", hash + ".jpg", hash + ".png" })
                        {
                            var candidate = Path.Combine(avatarsDirectory, fileName);
                            if (File.Exists(candidate) == true)
                            {
                                return candidate;
                            }
                        }
                    }
                }

                // The account's 32-bit id -- the low 32 bits of its SteamID64 -- names its
                // folder under userdata, independently of whatever avatar hash (or lack of
                // one) loginusers.vdf recorded.
                var accountId32 = (uint)(steamId64 & 0xFFFFFFFFUL);
                var userDataCandidate = Path.Combine(
                    installPath, "userdata", accountId32.ToString(CultureInfo.InvariantCulture), "config", "avatar.jpg");
                if (File.Exists(userDataCandidate) == true)
                {
                    return userDataCandidate;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
