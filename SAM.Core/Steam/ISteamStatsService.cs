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
    /// Everything the achievement manager needs from the running Steam client.
    /// </summary>
    /// <remarks>
    /// As with <see cref="ISteamLibraryService"/>, every member belongs to the thread that
    /// opened the Steam pipe.
    /// </remarks>
    public interface ISteamStatsService : IDisposable
    {
        uint AppId { get; }

        /// <summary>
        /// Whether Steam was still answering as of the last callback pump. Once this goes
        /// false it stays false: the pipe cannot be re-established without restarting.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>Raised once when the Steam pipe stops answering.</summary>
        event Action Disconnected;

        string AppName { get; }

        string CurrentLanguage { get; }

        /// <summary>Where Steam is installed; the stats schema lives under it.</summary>
        string InstallPath { get; }

        /// <summary>
        /// Asks Steam for the signed-in user's stats. <see cref="UserStatsReceived"/> follows.
        /// </summary>
        bool RequestUserStats();

        /// <summary>Raised with the Steam result code; 1 means success.</summary>
        event Action<int> UserStatsReceived;

        void RunCallbacks();

        bool TryGetAchievement(string id, out bool isAchieved, out DateTime? unlockTime);

        bool SetAchievement(string id, bool isAchieved);

        bool TryGetIntegerStat(string id, out int value);

        bool TryGetFloatStat(string id, out float value);

        bool SetIntegerStat(string id, int value);

        bool SetFloatStat(string id, float value);

        /// <summary>Commits everything set since the last store.</summary>
        bool StoreStats();

        bool ResetAllStats(bool includeAchievements);
    }
}
