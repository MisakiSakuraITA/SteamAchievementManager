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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SAM.Core.Caching;
using SAM.Core.Threading;
using SAM.Core.WinForms;
using static SAM.Core.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Picker
{
    internal partial class GamePicker : Form
    {
        private const int _MaximumConcurrentLogoLoads = 6;
        private const int _OwnershipSliceMilliseconds = 8;

        private static readonly TimeSpan _CacheRetention = TimeSpan.FromDays(90);

        private readonly API.Client _SteamClient;

        private readonly Dictionary<uint, GameInfo> _Games;
        private readonly List<GameInfo> _FilteredGames;

        private readonly Queue<GameInfo> _LogoQueue;
        private readonly HashSet<string> _QueuedLogos;
        private readonly ImageCache _LogoCache;
        private readonly ImageListCache _LogoImages;

        private readonly CancellationTokenSource _Shutdown;
        private readonly CancellationToken _ShutdownToken;

        private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;

        private bool _IsPumpingLogos;
        private bool _IsLoadingGameList;

        public GamePicker(API.Client client)
        {
            this._Games = new();
            this._FilteredGames = new();
            this._LogoQueue = new();
            this._QueuedLogos = new(StringComparer.Ordinal);
            this._Shutdown = new();
            this._ShutdownToken = this._Shutdown.Token;

            this.InitializeComponent();

            this._LogoImages = new(this._LogoImageList);
            this._LogoCache = new("logos", this._LogoImageList.ImageSize, _MaximumConcurrentLogoLoads);

            // Index 0 is the placeholder every game starts on.
            this._LogoImages.Add("Blank", CreateBlankLogo(this._LogoImageList.ImageSize));

            this._SteamClient = client;

            this._AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;
        }

        private static Bitmap CreateBlankLogo(Size size)
        {
            Bitmap blank = new(size.Width, size.Height);
            try
            {
                using (var graphics = Graphics.FromImage(blank))
                {
                    graphics.Clear(Color.DimGray);
                }
                return blank;
            }
            catch (Exception)
            {
                blank.Dispose();
                throw;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            this._LogoCache.SchedulePrune(_CacheRetention);

            // The list load owns its own error handling, so discarding the task is safe.
            this.ReloadGamesAsync().Forget();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            // Cancel, but do not dispose: loads that are still unwinding keep observing the
            // token, and tearing it down here would only trade cancellation for a fault.
            this._Shutdown.Cancel();
            this._LogoCache.Dispose();
        }

        private void OnAppDataChanged(APITypes.AppDataChanged param)
        {
            if (param.Result == false)
            {
                return;
            }

            if (this._Games.TryGetValue(param.Id, out var game) == false)
            {
                return;
            }

            game.Name = this._SteamClient.SteamApps001.GetAppData(game.Id, "name");

            // The app data behind the capsule URL may have changed too, so drop the resolved
            // URL and let EnqueueLogo ask Steam again.
            game.ImageUrl = null;
            game.ImageUri = null;

            this.EnqueueLogo(game);
            this.StartLogoPump();
        }

        #region Game list

        private async Task ReloadGamesAsync()
        {
            if (this._IsLoadingGameList == true)
            {
                return;
            }

            this._IsLoadingGameList = true;
            this._RefreshGamesButton.Enabled = false;
            try
            {
                this._Games.Clear();
                this.ClearLogoQueue();

                this._PickerStatusLabel.Text = "Downloading game list...";

                List<GameListEntry> entries = null;
                string failure = null;
                try
                {
                    entries = await GameListLoader.LoadAsync(this._ShutdownToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }

                if (this.IsDisposed == true || this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                if (entries == null)
                {
                    this.AddDefaultGames();
                    this.RefreshGames();
                    MessageBox.Show(
                        this,
                        "Failed to retrieve the game list.\n\n(" + failure + ")",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                await this.ScanOwnershipAsync(entries).ConfigureAwait(true);
                if (this.IsDisposed == true || this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                this.RefreshGames();
                this.StartLogoPump();
            }
            catch (Exception ex)
            {
                // Both entry points discard this task, so anything unexpected has to be
                // reported here or it would vanish and leave an empty list behind.
                if (this.IsDisposed == false)
                {
                    MessageBox.Show(
                        this,
                        "Error while building the game list:\n" + ex,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                this._IsLoadingGameList = false;
                if (this.IsDisposed == false)
                {
                    this._RefreshGamesButton.Enabled = true;
                }
            }
        }

        /// <summary>
        /// Walks the candidate list asking Steam what the user owns.
        /// </summary>
        /// <remarks>
        /// These are native calls into the Steam client, and the callback timer drives the
        /// same pipe from the UI thread, so they are deliberately kept on the UI thread
        /// rather than moved to a worker. Responsiveness comes from yielding the message
        /// pump on a time slice instead.
        /// </remarks>
        private async Task ScanOwnershipAsync(List<GameListEntry> entries)
        {
            var slice = Stopwatch.StartNew();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                this.AddGame(entry.Id, entry.Type);

                if (slice.ElapsedMilliseconds < _OwnershipSliceMilliseconds)
                {
                    continue;
                }

                this._PickerStatusLabel.Text = _($"Checking game ownership... ({i + 1} of {entries.Count})");

                await Task.Yield();

                if (this.IsDisposed == true || this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                slice.Restart();
            }
        }

        private bool OwnsGame(uint id)
        {
            return this._SteamClient.SteamApps008.IsSubscribedApp(id);
        }

        private void AddGame(uint id, string type)
        {
            if (this._Games.ContainsKey(id) == true)
            {
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                return;
            }

            GameInfo info = new(id, type);
            info.Name = this._SteamClient.SteamApps001.GetAppData(info.Id, "name");
            this._Games.Add(id, info);
        }

        private void AddDefaultGames()
        {
            this.AddGame(480, "normal"); // Spacewar
        }

        private void RefreshGames()
        {
            var nameSearch = this._SearchGameTextBox.Text.Length > 0
                ? this._SearchGameTextBox.Text
                : null;

            var wantNormals = this._FilterGamesMenuItem.Checked == true;
            var wantDemos = this._FilterDemosMenuItem.Checked == true;
            var wantMods = this._FilterModsMenuItem.Checked == true;
            var wantJunk = this._FilterJunkMenuItem.Checked == true;

            this._FilteredGames.Clear();
            foreach (var info in this._Games.Values.OrderBy(gi => gi.Name))
            {
                info.IsFiltered = false;

                if (nameSearch != null &&
                    info.Name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool wanted = info.Type switch
                {
                    "normal" => wantNormals,
                    "demo" => wantDemos,
                    "mod" => wantMods,
                    "junk" => wantJunk,
                    _ => true,
                };
                if (wanted == false)
                {
                    continue;
                }

                info.IsFiltered = true;
                this._FilteredGames.Add(info);
            }

            this._GameListView.VirtualListSize = this._FilteredGames.Count;
            this._PickerStatusLabel.Text =
                $"Displaying {this._GameListView.Items.Count} games. Total {this._Games.Count} games.";

            if (this._GameListView.Items.Count > 0)
            {
                this._GameListView.Items[0].Selected = true;
                this._GameListView.Select();
            }
        }

        #endregion

        #region Logos

        private string GetGameImageUrl(uint id)
        {
            string candidate;

            var currentLanguage = this._SteamClient.SteamApps008.GetCurrentGameLanguage();

            candidate = this._SteamClient.SteamApps001.GetAppData(id, _($"small_capsule/{currentLanguage}"));
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
            }

            if (currentLanguage != "english")
            {
                candidate = this._SteamClient.SteamApps001.GetAppData(id, "small_capsule/english");
                if (string.IsNullOrEmpty(candidate) == false)
                {
                    return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
                }
            }

            candidate = this._SteamClient.SteamApps001.GetAppData(id, "logo");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{candidate}.jpg");
            }

            return null;
        }

        private void EnqueueLogo(GameInfo info)
        {
            if (info.ImageIndex > 0)
            {
                return;
            }

            // This runs from the paint handler for every still-blank row, and resolving a URL
            // costs one to three calls into the Steam client, so only do it once per game.
            if (info.ImageUri == null)
            {
                var resolved = this.GetGameImageUrl(info.Id);
                if (string.IsNullOrEmpty(resolved) == true ||
                    Uri.TryCreate(resolved, UriKind.Absolute, out var resolvedUri) == false)
                {
                    return;
                }

                info.ImageUrl = resolved;
                info.ImageUri = resolvedUri;
            }

            var imageUrl = info.ImageUrl;

            if (this._LogoImages.TryGetIndex(imageUrl, out int imageIndex) == true)
            {
                info.ImageIndex = imageIndex;
                return;
            }

            if (this._QueuedLogos.Add(imageUrl) == false)
            {
                return;
            }

            this._LogoQueue.Enqueue(info);
        }

        private void ClearLogoQueue()
        {
            this._LogoQueue.Clear();
            this._QueuedLogos.Clear();
        }

        private bool TryDequeueLogo(out GameInfo info)
        {
            while (this._LogoQueue.Count > 0)
            {
                var candidate = this._LogoQueue.Dequeue();

                if (candidate.Item != null &&
                    candidate.IsFiltered == true &&
                    candidate.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == true)
                {
                    info = candidate;
                    return true;
                }

                // Filtered out or scrolled off-screen: forget it, so painting it again later
                // puts it back in the queue.
                this._QueuedLogos.Remove(candidate.ImageUrl);
            }

            info = null;
            return false;
        }

        private void StartLogoPump()
        {
            if (this._IsPumpingLogos == true)
            {
                return;
            }

            // PumpLogosAsync swallows its own failures, so the task can be discarded.
            this.PumpLogosAsync().Forget();
        }

        /// <summary>
        /// Keeps up to <see cref="_MaximumConcurrentLogoLoads"/> icon loads in flight until
        /// the queue drains. Only one pump runs at a time, and everything outside the awaits
        /// stays on the UI thread.
        /// </summary>
        private async Task PumpLogosAsync()
        {
            if (this._IsPumpingLogos == true)
            {
                return;
            }

            this._IsPumpingLogos = true;
            List<Task> running = new();
            try
            {
                while (true)
                {
                    while (running.Count < _MaximumConcurrentLogoLoads &&
                           this.TryDequeueLogo(out var info) == true)
                    {
                        running.Add(this.LoadLogoAsync(info));
                    }

                    if (running.Count == 0)
                    {
                        break;
                    }

                    this._DownloadStatusLabel.Text =
                        _($"Downloading {running.Count + this._LogoQueue.Count} game icons...");
                    this._DownloadStatusLabel.Visible = true;

                    var completed = await Task.WhenAny(running).ConfigureAwait(true);
                    running.Remove(completed);

                    if (this.IsDisposed == true || this._ShutdownToken.IsCancellationRequested == true)
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Icons are decorative. Most failures here are the form going away while a
                // continuation was queued, and none of them justify taking down the app.
            }
            finally
            {
                this._IsPumpingLogos = false;
                if (this.IsDisposed == false)
                {
                    this._DownloadStatusLabel.Visible = false;
                }
            }
        }

        /// <summary>
        /// Resolves one logo and publishes it to the image list. Never throws: the pump
        /// observes completion only, so a fault here would go unobserved.
        /// </summary>
        private async Task LoadLogoAsync(GameInfo info)
        {
            var imageUrl = info.ImageUrl;
            Bitmap bitmap = null;
            try
            {
                bitmap = await this._LogoCache
                    .GetAsync(CacheKey.ForGameLogo(info.Id, imageUrl), info.ImageUri)
                    .ConfigureAwait(true);

                if (bitmap == null || this.IsDisposed == true)
                {
                    return;
                }

                // The image list takes ownership of the bitmap from here.
                info.ImageIndex = this._LogoImages.Add(imageUrl, bitmap);
                bitmap = null;

                this._GameListView.Invalidate();
            }
            catch (Exception)
            {
                // A single missing icon must never take down the pump.
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        #endregion

        #region List view

        private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var info = this._FilteredGames[e.ItemIndex];
            e.Item = info.Item = new()
            {
                Text = info.Name,
                ImageIndex = info.ImageIndex,
            };
        }

        private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
        {
            if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false)
            {
                return;
            }

            var count = this._FilteredGames.Count;
            if (count < 2)
            {
                return;
            }

            var text = e.Text;
            int startIndex = e.StartIndex;

            Predicate<GameInfo> predicate =
                gi => gi.Name != null && gi.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);

            int index;
            if (e.StartIndex >= count)
            {
                // starting from the last item in the list
                index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
            }
            else if (startIndex <= 0)
            {
                // starting from the first item in the list
                index = this._FilteredGames.FindIndex(0, count, predicate);
            }
            else
            {
                index = this._FilteredGames.FindIndex(startIndex, count - startIndex, predicate);
                if (index < 0)
                {
                    index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
                }
            }

            e.Index = index < 0 ? -1 : index;
        }

        private void OnGameListViewDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;

            if (e.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
            {
                return;
            }

            var info = this._FilteredGames[e.ItemIndex];
            if (info.ImageIndex <= 0)
            {
                this.EnqueueLogo(info);
                this.StartLogoPump();
            }
        }

        #endregion

        #region Commands

        private void OnTimer(object sender, EventArgs e)
        {
            this._CallbackTimer.Enabled = false;
            this._SteamClient.RunCallbacks(false);
            this._CallbackTimer.Enabled = true;
        }

        private void OnActivateGame(object sender, EventArgs e)
        {
            var focusedItem = (sender as MyListView)?.FocusedItem;
            var index = focusedItem != null ? focusedItem.Index : -1;
            if (index < 0 || index >= this._FilteredGames.Count)
            {
                return;
            }

            var info = this._FilteredGames[index];
            if (info == null)
            {
                return;
            }

            try
            {
                Process.Start("SAM.Game.exe", info.Id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    this,
                    "Failed to start SAM.Game.exe.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void OnRefresh(object sender, EventArgs e)
        {
            this._AddGameTextBox.Text = "";
            await this.ReloadGamesAsync().ConfigureAwait(true);
        }

        private void OnAddGame(object sender, EventArgs e)
        {
            if (uint.TryParse(this._AddGameTextBox.Text, out uint id) == false)
            {
                MessageBox.Show(
                    this,
                    "Please enter a valid game ID.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                MessageBox.Show(this, "You don't own that game.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Only one app will be shown, so anything still queued is now irrelevant.
            this.ClearLogoQueue();

            this._AddGameTextBox.Text = "";
            this._Games.Clear();
            this.AddGame(id, "normal");
            this._FilterGamesMenuItem.Checked = true;
            this.RefreshGames();
            this.StartLogoPump();
        }

        private void OnFilterUpdate(object sender, EventArgs e)
        {
            this.RefreshGames();

            // Compatibility with _GameListView SearchForVirtualItemEventHandler (otherwise _SearchGameTextBox loose focus on KeyUp)
            this._SearchGameTextBox.Focus();
        }

        #endregion
    }
}
