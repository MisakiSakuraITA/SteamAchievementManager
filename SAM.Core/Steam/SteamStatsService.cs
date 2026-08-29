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
using SAM.API;
using APICallbacks = SAM.API.Callbacks;
using APITypes = SAM.API.Types;

namespace SAM.Core.Steam
{
    /// <summary>
    /// <see cref="ISteamStatsService"/> backed by a live Steam client pipe.
    /// </summary>
    public sealed class SteamStatsService : ISteamStatsService
    {
        private readonly Client _Client;
        private readonly uint _AppId;
        private readonly APICallbacks.UserStatsReceived _UserStatsReceivedCallback;

        private bool _IsDisposed;

        public SteamStatsService(Client client, uint appId)
        {
            this._Client = client ?? throw new ArgumentNullException(nameof(client));
            this._AppId = appId;

            this._UserStatsReceivedCallback = client.CreateAndRegisterCallback<APICallbacks.UserStatsReceived>();
            this._UserStatsReceivedCallback.OnRun += this.OnUserStatsReceived;

            this._Client.Disconnected += this.OnDisconnected;

            // SteamUser is only null against a Client that never completed Initialize, which
            // production never constructs a service from -- but several tests deliberately
            // exercise this class against exactly that, so the guard is real, not decorative.
            var installPath = API.Steam.GetInstallPath();
            this.ActiveSteamId = this._Client.SteamUser?.GetSteamId() ?? 0;
            this.ActivePersonaName = LocalSteamProfile.GetPersonaName(installPath, this.ActiveSteamId);
            this.ActiveAvatarFilePath = LocalSteamProfile.GetAvatarFilePath(installPath, this.ActiveSteamId);
        }

        public event Action<int> UserStatsReceived;

        public event Action Disconnected;

        public bool IsConnected => this._IsDisposed == false && this._Client.IsConnected;

        public ulong ActiveSteamId { get; }

        public string ActivePersonaName { get; }

        public string ActiveAvatarFilePath { get; }

        public uint AppId => this._AppId;

        public string AppName => this._Client.SteamApps001.GetAppData(this._AppId, "name");

        public string CurrentLanguage => this._Client.SteamApps008.GetCurrentGameLanguage();

        public string InstallPath => API.Steam.GetInstallPath();

        public bool RequestUserStats()
        {
            var steamId = this._Client.SteamUser.GetSteamId();

            // This still triggers the UserStatsReceived callback, in addition to the
            // callresult. No need to implement callresults for the time being.
            return this._Client.SteamUserStats.RequestUserStats(steamId) != CallHandle.Invalid;
        }

        public void RunCallbacks() => this._Client.RunCallbacks(false);

        public bool TryGetAchievement(string id, out bool isAchieved, out DateTime? unlockTime)
        {
            if (this._Client.SteamUserStats.GetAchievementAndUnlockTime(id, out isAchieved, out var raw) == false)
            {
                unlockTime = null;
                return false;
            }

            unlockTime = isAchieved == true && raw > 0
                ? DateTimeOffset.FromUnixTimeSeconds(raw).LocalDateTime
                : null;
            return true;
        }

        public bool SetAchievement(string id, bool isAchieved)
            => this._Client.SteamUserStats.SetAchievement(id, isAchieved);

        public bool TryGetIntegerStat(string id, out int value)
            => this._Client.SteamUserStats.GetStatValue(id, out value);

        public bool TryGetFloatStat(string id, out float value)
            => this._Client.SteamUserStats.GetStatValue(id, out value);

        public bool SetIntegerStat(string id, int value)
            => this._Client.SteamUserStats.SetStatValue(id, value);

        public bool SetFloatStat(string id, float value)
            => this._Client.SteamUserStats.SetStatValue(id, value);

        public bool StoreStats() => this._Client.SteamUserStats.StoreStats();

        public bool ResetAllStats(bool includeAchievements)
            => this._Client.SteamUserStats.ResetAllStats(includeAchievements);

        public void RequestGlobalAchievementPercentages()
            => this._Client.SteamUserStats.RequestGlobalAchievementPercentages();

        public bool TryGetGlobalAchievementPercentage(string id, out double percentage)
        {
            if (this._Client.SteamUserStats.GetAchievementAchievedPercent(id, out var percent) == false)
            {
                percentage = 0d;
                return false;
            }

            percentage = percent;
            return true;
        }

        private void OnUserStatsReceived(APITypes.UserStatsReceived param)
        {
            if (this._IsDisposed == true)
            {
                return;
            }

            // This callback arrives on the shared global-user pipe for whichever app just
            // asked Steam for its stats, not only for this one. A game running alongside SAM
            // requesting its own stats would otherwise be indistinguishable from SAM's own
            // request and trigger a reload here too.
            if (param.GameId != this._AppId)
            {
                return;
            }

            this.UserStatsReceived?.Invoke(param.Result);
        }

        private void OnDisconnected()
        {
            if (this._IsDisposed == true)
            {
                return;
            }

            this.Disconnected?.Invoke();
        }

        /// <summary>
        /// Unhooks from the Steam pipe. After this the callback is no longer registered, so a
        /// closed window can never be reached by an incoming IPC event.
        /// </summary>
        public void Dispose()
        {
            if (this._IsDisposed == true)
            {
                return;
            }

            this._IsDisposed = true;

            this._UserStatsReceivedCallback.OnRun -= this.OnUserStatsReceived;
            this._Client.Disconnected -= this.OnDisconnected;
            this._Client.UnregisterCallback(this._UserStatsReceivedCallback);
        }
    }
}
