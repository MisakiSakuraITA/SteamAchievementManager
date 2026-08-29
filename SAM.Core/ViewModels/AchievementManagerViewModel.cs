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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Snapshots;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using SAM.Core.Threading;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.ViewModels
{
    public enum AchievementFilter
    {
        All,
        Locked,
        Unlocked,
        Hidden,
        UltraRare,
    }

    public enum AchievementSortOrder
    {
        /// <summary>The order the schema declared them in.</summary>
        Default,
        Alphabetical,
        UnlockStatus,
        Rarity,
        HiddenStatus,
    }

    /// <summary>
    /// The manager's screen: the achievement list, the statistics list, and the pending edits
    /// waiting to be committed to Steam.
    /// </summary>
    public sealed class AchievementManagerViewModel : ObservableObject
    {
        private readonly ISteamStatsService _Steam;
        private readonly IDialogService _DialogService;
        private readonly List<AchievementViewModel> _AllAchievements;
        private readonly List<StatViewModel> _AllStatistics;
        private readonly CancellationTokenSource _Shutdown;
        private readonly CancellationToken _ShutdownToken;

        private string _SearchText = "";
        private AchievementFilter _Filter = AchievementFilter.All;
        private AchievementSortOrder _SortOrder = AchievementSortOrder.Default;
        private string _Status = "Retrieving stat information...";
        private bool _IsBusy = true;
        private bool _AllowStatEditing;
        private bool _IsSteamConnected = true;
        private bool _RevealHiddenAchievements;

        private CancellationTokenSource _QueuedStoreCancellation;
        private bool _IsQueuedStoreRunning;
        private int _QueuedStoreCompleted;
        private int _QueuedStoreTotal;

        /// <summary>
        /// Pause between individual unlocks in a queued store, purely so progress is legible
        /// and there is a real window to cancel in -- not a public setting, so it stays a UI
        /// nicety rather than becoming a dial for pacing unlocks to look like they happened
        /// during actual play. Settable internally only, and per-instance, so a test can
        /// shrink it without leaking that into any other test's view model.
        /// </summary>
        private TimeSpan _QueuedStoreDelay = TimeSpan.FromSeconds(1);

        internal TimeSpan QueuedStoreDelayForTesting
        {
            set => this._QueuedStoreDelay = value;
        }

        /// <summary>How often <see cref="PopulateRarityAsync"/> re-checks Steam's cache.
        /// Settable internally only, and per-instance, for the same reason as
        /// <see cref="QueuedStoreDelayForTesting"/>.</summary>
        private TimeSpan _RarityPollInterval = TimeSpan.FromMilliseconds(500);

        internal TimeSpan RarityPollIntervalForTesting
        {
            set => this._RarityPollInterval = value;
        }

        internal const string _DisconnectedMessage =
            "Steam is no longer running. Please launch Steam and restart the application.";

        public AchievementManagerViewModel(ISteamStatsService steam, IDialogService dialogService)
        {
            this._Steam = steam ?? throw new ArgumentNullException(nameof(steam));
            this._DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            this._AllAchievements = new();
            this._AllStatistics = new();
            this._Shutdown = new();
            this._ShutdownToken = this._Shutdown.Token;

            this.Achievements = new();
            this.Statistics = new();

            this._IsSteamConnected = steam.IsConnected;

            this.ReloadCommand = new(this.ReloadAsync, this.CanReachSteam);
            this.StoreCommand = new(this.StoreAsync, () => this.CanReachSteam() && this.IsModified == true);
            this.UnlockAllCommand = new(() => this.SetAll(true), this.CanReachSteam);
            this.LockAllCommand = new(() => this.SetAll(false), this.CanReachSteam);
            this.InvertAllCommand = new(this.InvertAll, this.CanReachSteam);
            this.ResetAllCommand = new(this.ResetAllAsync, this.CanReachSteam);
            this.QueuedStoreCommand = new(this.RunQueuedStoreAsync, () => this.CanReachSteam() && this.IsModified == true);
            this.CancelQueuedStoreCommand = new(this.CancelQueuedStore, () => this._IsQueuedStoreRunning == true);
            this.ExportSnapshotCommand = new(this.ExportSnapshotAsync, this.CanExportSnapshot);
            this.ImportSnapshotCommand = new(this.ImportSnapshotAsync, this.CanReachSteam);

            this._Steam.UserStatsReceived += this.OnUserStatsReceived;
            this._Steam.Disconnected += this.OnSteamDisconnected;
        }

        /// <summary>
        /// Every command here ends in a write to the Steam pipe, so they all share one gate:
        /// not already busy, and still connected.
        /// </summary>
        private bool CanReachSteam() => this._IsBusy == false && this._IsSteamConnected == true;

        public BulkObservableCollection<AchievementViewModel> Achievements { get; }

        public BulkObservableCollection<StatViewModel> Statistics { get; }

        public AsyncRelayCommand ReloadCommand { get; }

        public AsyncRelayCommand StoreCommand { get; }

        public RelayCommand UnlockAllCommand { get; }

        public RelayCommand LockAllCommand { get; }

        public RelayCommand InvertAllCommand { get; }

        public AsyncRelayCommand ResetAllCommand { get; }

        /// <summary>Stores pending achievements one at a time instead of all at once, so a
        /// large batch can be watched or stopped partway through.</summary>
        public AsyncRelayCommand QueuedStoreCommand { get; }

        public RelayCommand CancelQueuedStoreCommand { get; }

        /// <summary>Saves the currently-shown achievement and statistic state to a file.</summary>
        public AsyncRelayCommand ExportSnapshotCommand { get; }

        /// <summary>Loads a snapshot file and stages its values for review before a store.</summary>
        public AsyncRelayCommand ImportSnapshotCommand { get; }

        /// <summary>Raised with a message the shell should show as an error.</summary>
        public event Action<string> ErrorRaised;

        /// <summary>Raised with a message the shell should show as a confirmation.</summary>
        public event Action<string> InfoRaised;

        /// <summary>
        /// Raised when the user tried to change a protected achievement. The shell explains
        /// why nothing happened.
        /// </summary>
        public event Action<AchievementViewModel> ProtectedChangeRejected;

        public string GameName => this._Steam.AppName;

        public uint AppId => this._Steam.AppId;

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

        public AchievementFilter Filter
        {
            get => this._Filter;
            set
            {
                if (this.Set(ref this._Filter, value) == true)
                {
                    this.ApplyFilter();
                }
            }
        }

        public AchievementSortOrder SortOrder
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

        /// <summary>
        /// Reveals every hidden achievement's real name, description and icon, in place of
        /// the ordinary hover-to-peek at just one. Toggling this off returns each achievement
        /// that isn't being hovered right now to its normal obscured state.
        /// </summary>
        public bool RevealHiddenAchievements
        {
            get => this._RevealHiddenAchievements;
            set
            {
                if (this.Set(ref this._RevealHiddenAchievements, value) == false)
                {
                    return;
                }

                foreach (var achievement in this._AllAchievements)
                {
                    achievement.ShowSecretDetails = value;
                }
            }
        }

        public bool AllowStatEditing
        {
            get => this._AllowStatEditing;
            set => this.Set(ref this._AllowStatEditing, value);
        }

        /// <summary>True while a queued store is actively working through pending achievements.</summary>
        public bool IsQueuedStoreRunning
        {
            get => this._IsQueuedStoreRunning;
            private set
            {
                if (this.Set(ref this._IsQueuedStoreRunning, value) == true)
                {
                    this.QueuedStoreCommand.RaiseCanExecuteChanged();
                    this.CancelQueuedStoreCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>How many achievements the current (or most recent) queued store has
        /// finished storing.</summary>
        public int QueuedStoreCompleted
        {
            get => this._QueuedStoreCompleted;
            private set
            {
                if (this.Set(ref this._QueuedStoreCompleted, value) == true)
                {
                    this.Raise(nameof(this.QueuedStoreProgressPercentage));
                }
            }
        }

        /// <summary>How many achievements the current (or most recent) queued store started
        /// with.</summary>
        public int QueuedStoreTotal
        {
            get => this._QueuedStoreTotal;
            private set
            {
                if (this.Set(ref this._QueuedStoreTotal, value) == true)
                {
                    this.Raise(nameof(this.QueuedStoreProgressPercentage));
                }
            }
        }

        public double QueuedStoreProgressPercentage => this._QueuedStoreTotal == 0
            ? 0d
            : 100d * this._QueuedStoreCompleted / this._QueuedStoreTotal;

        public string Status
        {
            get => this._Status;
            private set => this.Set(ref this._Status, value);
        }

        public bool IsBusy
        {
            get => this._IsBusy;
            private set
            {
                if (this.Set(ref this._IsBusy, value) == true)
                {
                    this.RaiseCommandStates();
                }
            }
        }

        public int UnlockedCount => this._AllAchievements.Count(a => a.IsUnlocked);

        public int AchievementCount => this._AllAchievements.Count;

        public int StatisticCount => this._AllStatistics.Count;

        public double CompletionPercentage => this._AllAchievements.Count == 0
            ? 0d
            : 100d * this.UnlockedCount / this._AllAchievements.Count;

        public string CompletionText => this._AllAchievements.Count == 0
            ? "No achievements"
            : _($"{this.UnlockedCount} of {this._AllAchievements.Count} unlocked");

        public int ModifiedAchievementCount => this._AllAchievements.Count(a => a.IsModified);

        public int ModifiedStatisticCount => this._AllStatistics.Count(s => s.IsModified);

        /// <summary>
        /// Achievements and statistics together, for a status chip that should not read "0
        /// pending" while a statistic edit is what is actually staged.
        /// </summary>
        public int ModifiedCount => this.ModifiedAchievementCount + this.ModifiedStatisticCount;

        public bool IsModified => this.ModifiedAchievementCount > 0 || this.ModifiedStatisticCount > 0;

        public bool HasValidationErrors => this._AllStatistics.Any(s => s.HasError);

        /// <summary>
        /// False once Steam has gone away. Every command that reaches the pipe is gated on
        /// this, so nothing is written to a dead connection.
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
                this.RaiseCommandStates();
            }
        }

        /// <summary>Convenience inverse, so the banner can bind without a converter.</summary>
        public bool IsSteamDisconnected => this._IsSteamConnected == false;

        public string DisconnectedMessage => _DisconnectedMessage;

        /// <summary>The SteamID64 of the currently signed-in Steam account.</summary>
        public ulong ActiveSteamId => this._Steam.ActiveSteamId;

        /// <summary>Formatted twin of <see cref="ActiveSteamId"/>, for direct display.</summary>
        public string ActiveSteamIdText => this.ActiveSteamId.ToString(CultureInfo.InvariantCulture);

        /// <summary>The currently signed-in account's persona name, or null if unknown.</summary>
        public string ActivePersonaName => this._Steam.ActivePersonaName;

        /// <summary>The persona name when known, otherwise the SteamID64 -- the badge always has something to show.</summary>
        public string ActiveAccountDisplayName => string.IsNullOrEmpty(this.ActivePersonaName) == false
            ? this.ActivePersonaName
            : this.ActiveSteamIdText;

        /// <summary>Asks Steam for the current stats. The reply arrives on a callback.</summary>
        public void BeginLoad()
        {
            this.IsBusy = true;
            this.Status = "Retrieving stat information...";

            if (this._Steam.RequestUserStats() == false)
            {
                this.IsBusy = false;
                this.Status = "Failed to request stats.";
                this.ErrorRaised?.Invoke("Failed to request stats from Steam.");
            }
        }

        public void RunCallbacks()
        {
            this._Steam.RunCallbacks();
        }

        private void OnSteamDisconnected()
        {
            this.IsSteamConnected = false;
            this.IsBusy = false;
            this.Status = _DisconnectedMessage;
        }

        public void Shutdown()
        {
            this._Steam.UserStatsReceived -= this.OnUserStatsReceived;
            this._Steam.Disconnected -= this.OnSteamDisconnected;

            // Cancel, but do not dispose: loads that are still unwinding keep observing the
            // token, and tearing it down here would only trade cancellation for a fault.
            this._Shutdown.Cancel();
        }

        private Task ReloadAsync()
        {
            this.BeginLoad();
            return Task.CompletedTask;
        }

        private void OnUserStatsReceived(int result)
        {
            this.HandleUserStatsAsync(result).Forget();
        }

        private async Task HandleUserStatsAsync(int result)
        {
            if (result != 1)
            {
                this.IsBusy = false;
                this.Status = _($"Error while retrieving stats: {TranslateError(result)}");
                return;
            }

            this.Status = "Loading schema...";

            UserGameStatsSchema schema;
            try
            {
                schema = await UserGameStatsSchema
                    .LoadAsync(this._Steam.InstallPath, this._Steam.AppId, this._Steam.CurrentLanguage, this._ShutdownToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                this.IsBusy = false;
                this.Status = "Failed to load schema.";
                this.ErrorRaised?.Invoke("Error while reading the stats schema:\n" + e.Message);
                return;
            }

            if (this._ShutdownToken.IsCancellationRequested == true)
            {
                return;
            }

            if (schema == null)
            {
                this.IsBusy = false;
                this.Status = "Failed to load schema.";
                return;
            }

            try
            {
                // Load sets IsBusy false and reports the resulting count itself, on every
                // path that reaches it -- including a direct call from outside this method.
                this.Load(schema);
            }
            catch (Exception e)
            {
                this.IsBusy = false;
                this.Status = "Error when handling stats retrieval.";
                this.ErrorRaised?.Invoke("Error when handling stats retrieval:\n" + e.Message);
            }
        }

        /// <summary>
        /// Replaces the achievement and statistic lists from a schema, reading each current
        /// value back from Steam. Separated from the load so a schema obtained any other way
        /// can drive the same screen.
        /// </summary>
        /// <remarks>
        /// Steam can redeliver <c>UserStatsReceived</c> on its own schedule, not only in
        /// response to a request this view model made, so a reload can arrive while the user
        /// still has edits pending. Rather than rebuilding every view model from scratch and
        /// restoring pending state onto the replacements, an existing instance is looked up by
        /// id and refreshed in place when the schema still has a matching entry -- its pending
        /// edit, never having been touched, survives on its own. Only an id that is new this
        /// schema gets a freshly constructed view model; one that dropped out of the schema is
        /// simply left behind, already unsubscribed below, for the garbage collector.
        /// </remarks>
        public void Load(UserGameStatsSchema schema)
        {
            var previousAchievements = new Dictionary<string, AchievementViewModel>(StringComparer.Ordinal);
            foreach (var achievement in this._AllAchievements)
            {
                achievement.Changed -= this.OnAchievementChanged;
                achievement.ProtectedChangeRejected -= this.OnProtectedChangeRejected;
                previousAchievements[achievement.Id] = achievement;
            }

            var previousStatistics = new Dictionary<string, StatViewModel>(StringComparer.Ordinal);
            foreach (var statistic in this._AllStatistics)
            {
                statistic.Changed -= this.OnStatisticChanged;
                previousStatistics[statistic.Id] = statistic;
            }

            this._AllAchievements.Clear();
            this._AllStatistics.Clear();

            foreach (var definition in schema.Achievements)
            {
                if (string.IsNullOrEmpty(definition.Id) == true)
                {
                    continue;
                }

                if (this._Steam.TryGetAchievement(definition.Id, out var isAchieved, out var unlockTime) == false)
                {
                    continue;
                }

                AchievementViewModel achievement;
                if (previousAchievements.Remove(definition.Id, out var existing) == true)
                {
                    existing.RefreshStoredState(definition, isAchieved, unlockTime);
                    achievement = existing;
                }
                else
                {
                    achievement = new(this._Steam.AppId, definition, isAchieved, unlockTime)
                    {
                        ShowSecretDetails = this._RevealHiddenAchievements,
                    };
                }

                achievement.Changed += this.OnAchievementChanged;
                achievement.ProtectedChangeRejected += this.OnProtectedChangeRejected;
                this._AllAchievements.Add(achievement);
            }

            foreach (var definition in schema.Stats)
            {
                if (string.IsNullOrEmpty(definition.Id) == true)
                {
                    continue;
                }

                StatViewModel statistic = null;
                if (definition is IntegerStatDefinition integerDefinition)
                {
                    if (this._Steam.TryGetIntegerStat(integerDefinition.Id, out var value) == true)
                    {
                        if (previousStatistics.Remove(integerDefinition.Id, out var existing) == true &&
                            existing is IntegerStatViewModel existingInteger)
                        {
                            existingInteger.RefreshOriginalValue(integerDefinition, value);
                            statistic = existingInteger;
                        }
                        else
                        {
                            statistic = new IntegerStatViewModel(integerDefinition, value);
                        }
                    }
                }
                else if (definition is FloatStatDefinition floatDefinition)
                {
                    if (this._Steam.TryGetFloatStat(floatDefinition.Id, out var value) == true)
                    {
                        if (previousStatistics.Remove(floatDefinition.Id, out var existing) == true &&
                            existing is FloatStatViewModel existingFloat)
                        {
                            existingFloat.RefreshOriginalValue(floatDefinition, value);
                            statistic = existingFloat;
                        }
                        else
                        {
                            statistic = new FloatStatViewModel(floatDefinition, value);
                        }
                    }
                }

                if (statistic == null)
                {
                    continue;
                }

                statistic.Changed += this.OnStatisticChanged;
                this._AllStatistics.Add(statistic);
            }

            this.Statistics.ReplaceAll(this._AllStatistics);

            this.ApplyFilter();
            this.RaiseTotals();

            // Having data is what ends the wait, whichever route the schema arrived by.
            this.IsBusy = false;
            this.UpdateStatus();

            this.BeginPopulatingRarity();
        }

        /// <summary>
        /// Kicks off (or resumes) filling in every achievement's global rarity, unless every
        /// one loaded already has a value -- a redelivered schema does not need this asked
        /// for again, since a reused instance keeps whatever rarity it already resolved.
        /// </summary>
        private void BeginPopulatingRarity()
        {
            if (this._AllAchievements.Any(a => a.RarityPercentage.HasValue == false) == false)
            {
                return;
            }

            this.PopulateRarityAsync().Forget();
        }

        /// <summary>
        /// Asks Steam to compute global unlock percentages for this app, then polls for the
        /// result rather than correlating its call result -- see the remarks on
        /// <c>SteamUserStats013.RequestGlobalAchievementPercentages</c>. Steam typically
        /// answers within a second or two; this gives up quietly after a bounded number of
        /// attempts rather than polling forever, leaving <see cref="AchievementViewModel.RarityPercentage"/>
        /// null for whatever never resolved.
        /// </summary>
        private async Task PopulateRarityAsync()
        {
            const int maximumAttempts = 10;
            var interval = this._RarityPollInterval;

            this._Steam.RequestGlobalAchievementPercentages();

            try
            {
                for (var attempt = 0; attempt < maximumAttempts; attempt++)
                {
                    await Task.Delay(interval, this._ShutdownToken).ConfigureAwait(true);

                    var stillMissing = false;
                    var resolvedAny = false;
                    foreach (var achievement in this._AllAchievements)
                    {
                        if (achievement.RarityPercentage.HasValue == true)
                        {
                            continue;
                        }

                        if (this._Steam.TryGetGlobalAchievementPercentage(achievement.Id, out var percentage) == true)
                        {
                            achievement.RarityPercentage = percentage;
                            resolvedAny = true;
                        }
                        else
                        {
                            stillMissing = true;
                        }
                    }

                    // The rarity sort and the ultra-rare filter both depend on values that
                    // just changed out from under whatever view was already showing.
                    if (resolvedAny == true &&
                        (this._SortOrder == AchievementSortOrder.Rarity || this._Filter == AchievementFilter.UltraRare))
                    {
                        this.ApplyFilter();
                    }

                    if (stillMissing == false)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ApplyFilter()
        {
            var search = this._SearchText;
            var filter = this._Filter;

            var achievements = this._AllAchievements.Where(achievement =>
            {
                var wanted = filter switch
                {
                    AchievementFilter.Locked => achievement.IsUnlocked == false,
                    AchievementFilter.Unlocked => achievement.IsUnlocked == true,
                    AchievementFilter.Hidden => achievement.IsHidden == true,
                    AchievementFilter.UltraRare => achievement.IsUltraRare == true,
                    _ => true,
                };
                return wanted == true && achievement.Matches(search) == true;
            });
            this.Achievements.ReplaceAll(this.ApplySortOrder(achievements));

            var statistics = this._AllStatistics.Where(statistic => statistic.Matches(search) == true);
            this.Statistics.ReplaceAll(statistics);

            // A load still in flight has its own progress text to show; this only takes over
            // once there is a finished list for the count to describe.
            if (this._IsBusy == false)
            {
                this.UpdateStatus();
            }
        }

        /// <summary>
        /// Orders a filtered achievement sequence per <see cref="SortOrder"/>. Sorting only
        /// ever reorders this presentation-side sequence; the pending state on each
        /// achievement, and the master list it was drawn from, are untouched either way.
        /// </summary>
        private IEnumerable<AchievementViewModel> ApplySortOrder(IEnumerable<AchievementViewModel> achievements)
        {
            return this._SortOrder switch
            {
                AchievementSortOrder.Alphabetical => achievements
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),

                // Locked first, so what is left to do surfaces above what is already done.
                AchievementSortOrder.UnlockStatus => achievements
                    .OrderBy(a => a.IsUnlocked == true)
                    .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),

                // Rarest (lowest percentage) first; an achievement rarity hasn't loaded for
                // yet sorts after every achievement whose rarity is actually known.
                AchievementSortOrder.Rarity => achievements
                    .OrderBy(a => a.RarityPercentage.HasValue == false)
                    .ThenBy(a => a.RarityPercentage ?? double.MaxValue)
                    .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),

                // Hidden achievements first, surfacing the secrets there are left to
                // investigate (or reveal) above the ones with nothing to hide.
                AchievementSortOrder.HiddenStatus => achievements
                    .OrderBy(a => a.IsHidden == false)
                    .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),

                _ => achievements,
            };
        }

        /// <summary>
        /// Reports how many of the loaded achievements the current search and filter are
        /// actually showing, mirroring the picker's own idle status.
        /// </summary>
        private void UpdateStatus()
        {
            if (this._IsSteamConnected == false)
            {
                // A lost connection outranks any count; filtering must not overwrite it.
                this.Status = _DisconnectedMessage;
                return;
            }

            this.Status = this._AllAchievements.Count == 0
                ? "No achievements found."
                : _($"Showing {this.Achievements.Count:N0} of {this._AllAchievements.Count:N0} achievements.");
        }

        private void SetAll(bool unlocked)
        {
            var changed = false;
            foreach (var achievement in this.Achievements)
            {
                changed |= achievement.TrySetUnlocked(unlocked);
            }

            if (changed == true)
            {
                this.AfterAchievementChange();
            }
        }

        private void InvertAll()
        {
            var changed = false;
            foreach (var achievement in this.Achievements)
            {
                changed |= achievement.TrySetUnlocked(achievement.IsUnlocked == false);
            }

            if (changed == true)
            {
                this.AfterAchievementChange();
            }
        }

        private Task StoreAsync()
        {
            // The Steam calls behind a store all belong to the UI thread, so there is nothing
            // to await here; the command is asynchronous so the button can disable itself
            // while it runs.
            this.Store();
            return Task.CompletedTask;
        }

        private void Store()
        {
            if (this.HasValidationErrors == true)
            {
                this.ErrorRaised?.Invoke("Some statistics have invalid values. Fix them before storing.");
                return;
            }

            this.IsBusy = true;
            try
            {
                var achievements = this._AllAchievements.Where(a => a.IsModified).ToList();
                foreach (var achievement in achievements)
                {
                    if (this._Steam.SetAchievement(achievement.Id, achievement.IsUnlocked) == true)
                    {
                        continue;
                    }

                    this.ErrorRaised?.Invoke(
                        _($"An error occurred while setting the state for {achievement.Id}, aborting store."));
                    this.RevertPending();
                    return;
                }

                var statistics = this._AllStatistics.Where(s => s.IsModified).ToList();
                foreach (var statistic in statistics)
                {
                    if (statistic.Store(this._Steam) == true)
                    {
                        continue;
                    }

                    this.ErrorRaised?.Invoke(
                        _($"An error occurred while setting the value for {statistic.Id}, aborting store."));
                    this.RevertPending();
                    return;
                }

                if (this._Steam.StoreStats() == false)
                {
                    this.ErrorRaised?.Invoke("An error occurred while storing, aborting.");
                    this.RevertPending();
                    return;
                }

                var now = DateTime.Now;
                foreach (var achievement in achievements)
                {
                    achievement.AcceptPending(achievement.IsUnlocked == true ? now : null);
                }

                foreach (var statistic in statistics)
                {
                    statistic.AcceptPending();
                }

                this.RaiseTotals();
                this.InfoRaised?.Invoke(
                    _($"Stored {achievements.Count} achievements and {statistics.Count} statistics."));
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        /// <summary>
        /// Stores every pending achievement one at a time instead of in a single batch, so a
        /// large set of changes is never all-or-nothing: each achievement is fully committed
        /// (set, then stored) before the next one starts, and the whole thing can be stopped
        /// partway through without losing whatever already went through.
        /// </summary>
        /// <remarks>
        /// Statistics are not part of this: they store as an ordinary batch alongside the
        /// first achievement, since staging them one at a time has no comparable benefit.
        /// </remarks>
        private async Task RunQueuedStoreAsync()
        {
            if (this.HasValidationErrors == true)
            {
                this.ErrorRaised?.Invoke("Some statistics have invalid values. Fix them before storing.");
                return;
            }

            var achievements = this._AllAchievements.Where(a => a.IsModified == true).ToList();
            if (achievements.Count == 0)
            {
                return;
            }

            this._QueuedStoreCancellation = new CancellationTokenSource();
            var token = this._QueuedStoreCancellation.Token;

            this.IsBusy = true;
            this.IsQueuedStoreRunning = true;
            this.QueuedStoreTotal = achievements.Count;
            this.QueuedStoreCompleted = 0;

            var stored = 0;
            try
            {
                var statistics = this._AllStatistics.Where(s => s.IsModified == true).ToList();

                for (var i = 0; i < achievements.Count; i++)
                {
                    if (token.IsCancellationRequested == true)
                    {
                        break;
                    }

                    var achievement = achievements[i];
                    this.Status = _($"Storing {i + 1} of {achievements.Count}: {achievement.Name}...");

                    if (this._Steam.SetAchievement(achievement.Id, achievement.IsUnlocked) == false)
                    {
                        this.ErrorRaised?.Invoke(
                            _($"An error occurred while setting the state for {achievement.Id}, stopping the queue."));
                        break;
                    }

                    // The first item also carries any pending statistics, so they are not
                    // silently dropped by a queue that only ever iterates achievements.
                    var statisticsFailed = false;
                    if (i == 0)
                    {
                        foreach (var statistic in statistics)
                        {
                            if (statistic.Store(this._Steam) == true)
                            {
                                continue;
                            }

                            this.ErrorRaised?.Invoke(
                                _($"An error occurred while setting the value for {statistic.Id}, stopping the queue."));
                            statisticsFailed = true;
                            break;
                        }
                    }

                    if (statisticsFailed == true)
                    {
                        break;
                    }

                    if (this._Steam.StoreStats() == false)
                    {
                        this.ErrorRaised?.Invoke("An error occurred while storing, stopping the queue.");
                        break;
                    }

                    var now = DateTime.Now;
                    achievement.AcceptPending(achievement.IsUnlocked == true ? now : null);
                    if (i == 0)
                    {
                        foreach (var statistic in statistics)
                        {
                            statistic.AcceptPending();
                        }
                    }

                    stored = i + 1;
                    this.QueuedStoreCompleted = stored;
                    this.RaiseTotals();

                    if (i < achievements.Count - 1)
                    {
                        var remaining = achievements.Count - stored;
                        this.Status = _($"Stored {stored} of {achievements.Count}. {remaining} more queued...");

                        try
                        {
                            await Task.Delay(this._QueuedStoreDelay, token).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }

                this.InfoRaised?.Invoke(stored == achievements.Count
                    ? _($"Stored {stored} of {achievements.Count} achievements.")
                    : _($"Queued store stopped after {stored} of {achievements.Count} achievements."));
            }
            finally
            {
                this.IsBusy = false;
                this.IsQueuedStoreRunning = false;
                this._QueuedStoreCancellation?.Dispose();
                this._QueuedStoreCancellation = null;
                this.UpdateStatus();
            }
        }

        private void CancelQueuedStore()
        {
            this._QueuedStoreCancellation?.Cancel();
        }

        /// <summary>
        /// Captures the achievement and statistic state currently shown -- including whatever
        /// is only staged, not yet stored -- as a portable snapshot. An achievement's recorded
        /// state is its pending <see cref="AchievementViewModel.IsUnlocked"/> rather than its
        /// last-stored <see cref="AchievementViewModel.IsAchieved"/>, so a snapshot taken with
        /// edits still pending captures what the user is looking at, not only what Steam has
        /// confirmed; a statistic's recorded value is likewise its pending value.
        /// </summary>
        internal GameSnapshot BuildSnapshot()
        {
            return new GameSnapshot
            {
                AppId = this.AppId,
                Timestamp = DateTime.UtcNow,
                Achievements = this._AllAchievements
                    .Select(a => new AchievementSnapshotEntry
                    {
                        Id = a.Id,
                        IsAchieved = a.IsUnlocked,
                        UnlockTime = a.UnlockTime,
                    })
                    .ToList(),
                Statistics = this._AllStatistics
                    .Select(s => new StatisticSnapshotEntry { Id = s.Id, Value = ReadStatValue(s) })
                    .ToList(),
            };
        }

        private static double ReadStatValue(StatViewModel statistic) => statistic switch
        {
            IntegerStatViewModel integer => integer.Value,
            FloatStatViewModel floatStat => floatStat.Value,
            _ => 0d,
        };

        /// <summary>
        /// Applies an imported snapshot as staged pending edits -- exactly as if the user had
        /// set each matching achievement and statistic by hand -- so nothing reaches Steam
        /// until an ordinary store. A snapshot recorded for a different app is refused outright,
        /// since none of its ids can be assumed to mean anything against this app's schema. An
        /// id the snapshot mentions that the loaded schema does not have is simply skipped, the
        /// same way a bulk operation already skips a protected achievement.
        /// </summary>
        /// <remarks>
        /// A snapshot's recorded <see cref="AchievementSnapshotEntry.UnlockTime"/> is never
        /// applied here: this application only ever sets an unlock time at the moment of an
        /// actual store (see <see cref="Store"/>), and importing is not an exception to that.
        /// </remarks>
        internal bool TryApplySnapshot(GameSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.AppId != this.AppId)
            {
                this.ErrorRaised?.Invoke(
                    _($"This snapshot is for app {snapshot.AppId}, not {this.AppId} ({this.GameName}). Import cancelled."));
                return false;
            }

            var achievementsById = this._AllAchievements.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var statisticsById = this._AllStatistics.ToDictionary(s => s.Id, StringComparer.Ordinal);

            var matchedAchievements = 0;
            foreach (var entry in snapshot.Achievements ?? Enumerable.Empty<AchievementSnapshotEntry>())
            {
                if (string.IsNullOrEmpty(entry?.Id) == true || achievementsById.TryGetValue(entry.Id, out var achievement) == false)
                {
                    continue;
                }

                achievement.TrySetUnlocked(entry.IsAchieved);
                matchedAchievements++;
            }

            var matchedStatistics = 0;
            foreach (var entry in snapshot.Statistics ?? Enumerable.Empty<StatisticSnapshotEntry>())
            {
                if (string.IsNullOrEmpty(entry?.Id) == true || statisticsById.TryGetValue(entry.Id, out var statistic) == false)
                {
                    continue;
                }

                // The live statistic's own type decides how the value is formatted, not
                // whatever a foreign or hand-edited snapshot file might claim.
                statistic.ValueText = string.Equals(statistic.TypeName, "Integer", StringComparison.Ordinal) == true
                    ? ((long)Math.Round(entry.Value)).ToString(CultureInfo.InvariantCulture)
                    : entry.Value.ToString(CultureInfo.InvariantCulture);
                matchedStatistics++;
            }

            if (matchedAchievements > 0)
            {
                this.AfterAchievementChange();
            }

            this.InfoRaised?.Invoke(matchedAchievements + matchedStatistics == 0
                ? "The snapshot did not match any currently-loaded achievements or statistics."
                : _($"Imported snapshot: staged {matchedAchievements} achievement(s) and {matchedStatistics} statistic(s) for review."));

            return true;
        }

        private async Task ExportSnapshotAsync()
        {
            var suggestedName = _($"{SanitizeFileName(this.GameName)}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            string path;
            try
            {
                path = await this._DialogService.ShowSaveFileAsync(suggestedName).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                this.ErrorRaised?.Invoke("Failed to open the save dialog:\n" + e.Message);
                return;
            }

            if (string.IsNullOrEmpty(path) == true)
            {
                return;
            }

            try
            {
                var snapshot = this.BuildSnapshot();
                var text = GameSnapshotSerializer.DetectFormat(path) == SnapshotFileFormat.Csv
                    ? GameSnapshotSerializer.ToCsv(snapshot)
                    : GameSnapshotSerializer.ToJson(snapshot);
                File.WriteAllText(path, text);
                this.InfoRaised?.Invoke(_($"Exported snapshot to {Path.GetFileName(path)}."));
            }
            catch (Exception e)
            {
                this.ErrorRaised?.Invoke("Failed to export snapshot:\n" + e.Message);
            }
        }

        private async Task ImportSnapshotAsync()
        {
            string path;
            try
            {
                path = await this._DialogService.ShowOpenFileAsync().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                this.ErrorRaised?.Invoke("Failed to open the import dialog:\n" + e.Message);
                return;
            }

            if (string.IsNullOrEmpty(path) == true)
            {
                return;
            }

            GameSnapshot snapshot;
            try
            {
                var text = File.ReadAllText(path);
                snapshot = GameSnapshotSerializer.DetectFormat(path) == SnapshotFileFormat.Csv
                    ? GameSnapshotSerializer.FromCsv(text)
                    : GameSnapshotSerializer.FromJson(text);
            }
            catch (Exception e)
            {
                this.ErrorRaised?.Invoke("Failed to read snapshot file:\n" + e.Message);
                return;
            }

            this.TryApplySnapshot(snapshot);
        }

        /// <summary>Replaces characters a file name cannot contain, for a save dialog's suggested name.</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name) == true)
            {
                return "snapshot";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Reproduces the three-step confirmation the tool has always asked for before a reset.
        /// Resetting stats is not reversible.
        /// </summary>
        private async Task ResetAllAsync()
        {
            var confirmed = await this._DialogService.ShowConfirmationAsync(
                "Warning",
                "Are you absolutely sure you want to reset stats?",
                DialogSeverity.Warning).ConfigureAwait(true);
            if (confirmed == false)
            {
                return;
            }

            var includeAchievements = await this._DialogService.ShowConfirmationAsync(
                "Question",
                "Do you want to reset achievements too?",
                DialogSeverity.Question).ConfigureAwait(true);

            var reallySure = await this._DialogService.ShowConfirmationAsync(
                "Warning",
                "Really really sure?",
                DialogSeverity.Error).ConfigureAwait(true);
            if (reallySure == false)
            {
                return;
            }

            if (this._Steam.ResetAllStats(includeAchievements) == false)
            {
                this.ErrorRaised?.Invoke("Failed to reset stats.");
                return;
            }

            this.BeginLoad();
        }

        private void RevertPending()
        {
            foreach (var achievement in this._AllAchievements)
            {
                achievement.RevertPending();
            }

            this.RaiseTotals();
        }

        private void OnAchievementChanged(AchievementViewModel achievement)
        {
            this.AfterAchievementChange();
        }

        private void OnStatisticChanged(StatViewModel statistic)
        {
            this.Raise(
                nameof(this.ModifiedStatisticCount),
                nameof(this.ModifiedCount),
                nameof(this.IsModified),
                nameof(this.HasValidationErrors));
            this.StoreCommand.RaiseCanExecuteChanged();
            this.QueuedStoreCommand.RaiseCanExecuteChanged();
        }

        private void OnProtectedChangeRejected(AchievementViewModel achievement)
        {
            this.ProtectedChangeRejected?.Invoke(achievement);
        }

        private void AfterAchievementChange()
        {
            this.RaiseTotals();

            // A filtered view of locked or unlocked achievements has to drop entries the user
            // just toggled out of it.
            if (this._Filter != AchievementFilter.All)
            {
                this.ApplyFilter();
            }
        }

        private void RaiseTotals()
        {
            this.Raise(
                nameof(this.UnlockedCount),
                nameof(this.AchievementCount),
                nameof(this.StatisticCount),
                nameof(this.CompletionPercentage),
                nameof(this.CompletionText),
                nameof(this.ModifiedAchievementCount),
                nameof(this.ModifiedStatisticCount),
                nameof(this.ModifiedCount),
                nameof(this.IsModified),
                nameof(this.HasValidationErrors));
            this.StoreCommand.RaiseCanExecuteChanged();
            this.QueuedStoreCommand.RaiseCanExecuteChanged();
        }

        private void RaiseCommandStates()
        {
            this.ReloadCommand.RaiseCanExecuteChanged();
            this.StoreCommand.RaiseCanExecuteChanged();
            this.UnlockAllCommand.RaiseCanExecuteChanged();
            this.LockAllCommand.RaiseCanExecuteChanged();
            this.InvertAllCommand.RaiseCanExecuteChanged();
            this.ResetAllCommand.RaiseCanExecuteChanged();
            this.QueuedStoreCommand.RaiseCanExecuteChanged();
            this.ExportSnapshotCommand.RaiseCanExecuteChanged();
            this.ImportSnapshotCommand.RaiseCanExecuteChanged();
        }

        private bool CanExportSnapshot() => this._IsBusy == false && (this._AllAchievements.Count > 0 || this._AllStatistics.Count > 0);

        private static string TranslateError(int id) => id switch
        {
            2 => "generic error -- this usually means you don't own the game",
            _ => _($"{id}"),
        };
    }
}
