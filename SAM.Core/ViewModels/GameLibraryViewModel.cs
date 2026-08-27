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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Steam;
using SAM.Core.Threading;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.ViewModels
{
    public enum GameSortOrder
    {
        NameAscending,
        NameDescending,
        AppId,
    }

    /// <summary>
    /// The picker's screen: loads the catalogue, works out what the user owns, and exposes a
    /// filtered, sorted view of it.
    /// </summary>
    /// <remarks>
    /// Everything here runs on the thread that constructed it, which is also the thread that
    /// owns the Steam pipe. Long operations yield the message loop instead of moving to a
    /// worker, because the Steam calls cannot leave this thread.
    /// </remarks>
    public sealed class GameLibraryViewModel : ObservableObject
    {
        private const int _OwnershipSliceMilliseconds = 8;

        private readonly ISteamLibraryService _Steam;
        private readonly Func<CancellationToken, Task<List<GameListEntry>>> _CatalogLoader;
        private readonly Dictionary<uint, GameViewModel> _AllGames;
        private readonly CancellationTokenSource _Shutdown;
        private readonly CancellationToken _ShutdownToken;

        private string _SearchText = "";
        private GameSortOrder _SortOrder = GameSortOrder.NameAscending;
        private bool _ShowNormalGames = true;
        private bool _ShowDemos;
        private bool _ShowMods;
        private bool _ShowJunk;

        private GameViewModel _SelectedGame;
        private string _Status = "Ready.";
        private bool _IsLoading;
        private bool _IsSteamConnected = true;

        internal const string _DisconnectedMessage =
            "Steam is no longer running. Please launch Steam and restart the application.";

        public GameLibraryViewModel(ISteamLibraryService steam)
            : this(steam, GameCatalog.LoadAsync)
        {
        }

        /// <summary>
        /// Overload that accepts a catalogue source, so the library can be exercised without
        /// a network or a running Steam client.
        /// </summary>
        public GameLibraryViewModel(
            ISteamLibraryService steam,
            Func<CancellationToken, Task<List<GameListEntry>>> catalogLoader)
        {
            this._Steam = steam ?? throw new ArgumentNullException(nameof(steam));
            this._CatalogLoader = catalogLoader ?? throw new ArgumentNullException(nameof(catalogLoader));
            this._AllGames = new();
            this._Shutdown = new();
            this._ShutdownToken = this._Shutdown.Token;

            this.Games = new();

            this._IsSteamConnected = steam.IsConnected;

            this.RefreshCommand = new(this.LoadAsync, () => this._IsLoading == false && this._IsSteamConnected == true);
            this.AddGameCommand = new(this.AddGameById, _ => this._IsLoading == false && this._IsSteamConnected == true);
            this.LaunchCommand = new(
                this.OnLaunchRequested,
                _ => this._SelectedGame != null && this._IsSteamConnected == true);

            this._Steam.AppDataChanged += this.OnAppDataChanged;
            this._Steam.Disconnected += this.OnSteamDisconnected;
        }

        /// <summary>The filtered, sorted games the view binds to.</summary>
        public ObservableCollection<GameViewModel> Games { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        public RelayCommand AddGameCommand { get; }

        public RelayCommand LaunchCommand { get; }

        /// <summary>Raised when the user activates a game and the shell should open it.</summary>
        public event Action<GameViewModel> LaunchRequested;

        /// <summary>Raised with a message the shell should show as an error.</summary>
        public event Action<string> ErrorRaised;

        public int TotalCount => this._AllGames.Count;

        public int DisplayedCount => this.Games.Count;

        /// <summary>
        /// False once Steam has gone away. Every command that reaches the pipe is gated on
        /// this, so nothing is attempted against a dead connection.
        /// </summary>
        public bool IsSteamConnected
        {
            get => this._IsSteamConnected;
            private set
            {
                if (this.Set(ref this._IsSteamConnected, value) == false)
                {
                    return;
                }

                this.Raise(nameof(this.IsSteamDisconnected));
                this.RefreshCommand.RaiseCanExecuteChanged();
                this.AddGameCommand.RaiseCanExecuteChanged();
                this.LaunchCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Convenience inverse, so the banner can bind without a converter.</summary>
        public bool IsSteamDisconnected => this._IsSteamConnected == false;

        public string DisconnectedMessage => _DisconnectedMessage;

        public string SearchText
        {
            get => this._SearchText;
            set
            {
                if (this.Set(ref this._SearchText, value ?? "") == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public GameSortOrder SortOrder
        {
            get => this._SortOrder;
            set
            {
                if (this.Set(ref this._SortOrder, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public bool ShowNormalGames
        {
            get => this._ShowNormalGames;
            set
            {
                if (this.Set(ref this._ShowNormalGames, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public bool ShowDemos
        {
            get => this._ShowDemos;
            set
            {
                if (this.Set(ref this._ShowDemos, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public bool ShowMods
        {
            get => this._ShowMods;
            set
            {
                if (this.Set(ref this._ShowMods, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public bool ShowJunk
        {
            get => this._ShowJunk;
            set
            {
                if (this.Set(ref this._ShowJunk, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public GameViewModel SelectedGame
        {
            get => this._SelectedGame;
            set
            {
                if (this.Set(ref this._SelectedGame, value) == true)
                {
                    this.LaunchCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string Status
        {
            get => this._Status;
            private set => this.Set(ref this._Status, value);
        }

        public bool IsLoading
        {
            get => this._IsLoading;
            private set
            {
                if (this.Set(ref this._IsLoading, value) == true)
                {
                    this.RefreshCommand.RaiseCanExecuteChanged();
                    this.AddGameCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Downloads the catalogue and checks ownership of every entry against Steam.
        /// </summary>
        public async Task LoadAsync()
        {
            if (this._IsLoading == true)
            {
                return;
            }

            this.IsLoading = true;
            try
            {
                this._AllGames.Clear();
                this.Games.Clear();
                this.Raise(nameof(this.TotalCount), nameof(this.DisplayedCount));

                this.Status = "Downloading game list...";

                List<GameListEntry> entries;
                try
                {
                    entries = await this._CatalogLoader(this._ShutdownToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    this.AddSpacewar();
                    this.ApplyFilter();
                    this.Status = "Could not retrieve the game list.";
                    this.ErrorRaised?.Invoke("Failed to retrieve the game list.\n\n" + e.Message);
                    return;
                }

                if (this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                await this.ScanOwnershipAsync(entries).ConfigureAwait(true);
                if (this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                this.ApplyFilter();
                this.UpdateIdleStatus();
            }
            catch (Exception e)
            {
                this.Status = "Error while building the game list.";
                this.ErrorRaised?.Invoke("Error while building the game list.\n\n" + e.Message);
            }
            finally
            {
                this.IsLoading = false;
            }
        }

        /// <summary>
        /// Narrows the library to a single app id the user typed in, mirroring the old
        /// "add game" behaviour for apps missing from the catalogue.
        /// </summary>
        public void AddGameById(object parameter)
        {
            var text = parameter as string;
            if (uint.TryParse(text, out var appId) == false)
            {
                this.ErrorRaised?.Invoke("Please enter a valid game ID.");
                return;
            }

            if (this._Steam.OwnsApp(appId) == false)
            {
                this.ErrorRaised?.Invoke("You don't own that game.");
                return;
            }

            this._AllGames.Clear();
            this.Track(appId, "normal");

            this._ShowNormalGames = true;
            this._SearchText = "";
            this.Raise(nameof(this.ShowNormalGames), nameof(this.SearchText));

            this.ApplyFilter();
            this.UpdateIdleStatus();
        }

        /// <summary>
        /// Pumps Steam callbacks. The shell drives this on a timer, exactly as the Steam
        /// client expects.
        /// </summary>
        public void RunCallbacks()
        {
            this._Steam.RunCallbacks();
        }

        private void OnSteamDisconnected()
        {
            this.IsSteamConnected = false;
            this.Status = _DisconnectedMessage;
        }

        public void Shutdown()
        {
            this._Steam.AppDataChanged -= this.OnAppDataChanged;
            this._Steam.Disconnected -= this.OnSteamDisconnected;

            // Cancel, but do not dispose: loads that are still unwinding keep observing the
            // token, and tearing it down here would only trade cancellation for a fault.
            this._Shutdown.Cancel();
        }

        private async Task ScanOwnershipAsync(List<GameListEntry> entries)
        {
            var slice = Stopwatch.StartNew();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                this.Track(entry.Id, entry.Type);

                if (slice.ElapsedMilliseconds < _OwnershipSliceMilliseconds)
                {
                    continue;
                }

                this.Status = _($"Checking game ownership... ({i + 1:N0} of {entries.Count:N0})");

                // Hand the message loop back so the window keeps painting and responding.
                await Task.Yield();

                if (this._ShutdownToken.IsCancellationRequested == true)
                {
                    return;
                }

                slice.Restart();
            }
        }

        private void Track(uint appId, string type)
        {
            if (this._AllGames.ContainsKey(appId) == true)
            {
                return;
            }

            if (this._Steam.OwnsApp(appId) == false)
            {
                return;
            }

            GameViewModel game = new(appId, type, this._Steam.GetAppName(appId));
            game.UpdateCapsule(this._Steam.GetCapsuleUrl(appId));
            this._AllGames.Add(appId, game);
        }

        private void AddSpacewar()
        {
            this.Track(480, "normal");
        }

        private void OnAppDataChanged(uint appId)
        {
            if (this._AllGames.TryGetValue(appId, out var game) == false)
            {
                return;
            }

            game.UpdateName(this._Steam.GetAppName(appId));

            // The artwork behind the capsule address may have arrived with this update too.
            game.UpdateCapsule(this._Steam.GetCapsuleUrl(appId));
        }

        private bool IsWanted(GameViewModel game)
        {
            return game.Type switch
            {
                "normal" => this._ShowNormalGames,
                "demo" => this._ShowDemos,
                "mod" => this._ShowMods,
                "junk" => this._ShowJunk,
                _ => true,
            };
        }

        private void ApplyFilter()
        {
            var search = this._SearchText;

            IEnumerable<GameViewModel> matched = this._AllGames.Values
                .Where(g => this.IsWanted(g) && g.Matches(search));

            matched = this._SortOrder switch
            {
                GameSortOrder.NameDescending => matched.OrderByDescending(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
                GameSortOrder.AppId => matched.OrderBy(g => g.Id),
                _ => matched.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            };

            var previous = this._SelectedGame;

            this.Games.Clear();
            foreach (var game in matched)
            {
                this.Games.Add(game);
            }

            // Keep the selection if it survived the filter, otherwise fall to the top.
            this.SelectedGame = previous != null && this.Games.Contains(previous) == true
                ? previous
                : this.Games.FirstOrDefault();

            this.Raise(nameof(this.TotalCount), nameof(this.DisplayedCount));

            if (this._IsLoading == false)
            {
                this.UpdateIdleStatus();
            }
        }

        private void UpdateIdleStatus()
        {
            if (this._IsSteamConnected == false)
            {
                // A lost connection outranks any count; filtering must not overwrite it.
                this.Status = _DisconnectedMessage;
                return;
            }

            this.Status = this._AllGames.Count == 0
                ? "No games found."
                : _($"Showing {this.Games.Count:N0} of {this._AllGames.Count:N0} games.");
        }

        private void OnLaunchRequested(object parameter)
        {
            var game = parameter as GameViewModel ?? this._SelectedGame;
            if (game == null)
            {
                return;
            }

            this.LaunchRequested?.Invoke(game);
        }
    }
}
