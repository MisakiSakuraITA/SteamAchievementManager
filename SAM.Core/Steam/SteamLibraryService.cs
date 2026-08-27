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
using static SAM.Core.InvariantShorthand;
using APICallbacks = SAM.API.Callbacks;
using APITypes = SAM.API.Types;

namespace SAM.Core.Steam
{
    /// <summary>
    /// <see cref="ISteamLibraryService"/> backed by a live Steam client pipe.
    /// </summary>
    public sealed class SteamLibraryService : ISteamLibraryService
    {
        private const string _StoreAssetHost = "https://shared.cloudflare.steamstatic.com";
        private const string _CommunityAssetHost = "https://cdn.steamstatic.com";

        private readonly Client _Client;
        private readonly APICallbacks.AppDataChanged _AppDataChangedCallback;

        public SteamLibraryService(Client client)
        {
            this._Client = client ?? throw new ArgumentNullException(nameof(client));

            this._AppDataChangedCallback = client.CreateAndRegisterCallback<APICallbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;
        }

        public event Action<uint> AppDataChanged;

        public string CurrentLanguage => this._Client.SteamApps008.GetCurrentGameLanguage();

        public bool OwnsApp(uint appId) => this._Client.SteamApps008.IsSubscribedApp(appId);

        public string GetAppName(uint appId) => this._Client.SteamApps001.GetAppData(appId, "name");

        public string GetCapsuleUrl(uint appId)
        {
            var language = this.CurrentLanguage;

            var candidate = this._Client.SteamApps001.GetAppData(appId, _($"small_capsule/{language}"));
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"{_StoreAssetHost}/store_item_assets/steam/apps/{appId}/{candidate}");
            }

            if (language != "english")
            {
                candidate = this._Client.SteamApps001.GetAppData(appId, "small_capsule/english");
                if (string.IsNullOrEmpty(candidate) == false)
                {
                    return _($"{_StoreAssetHost}/store_item_assets/steam/apps/{appId}/{candidate}");
                }
            }

            candidate = this._Client.SteamApps001.GetAppData(appId, "logo");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"{_CommunityAssetHost}/steamcommunity/public/images/apps/{appId}/{candidate}.jpg");
            }

            return null;
        }

        public void RunCallbacks() => this._Client.RunCallbacks(false);

        private void OnAppDataChanged(APITypes.AppDataChanged param)
        {
            if (param.Result == false)
            {
                return;
            }

            this.AppDataChanged?.Invoke(param.Id);
        }
    }
}
