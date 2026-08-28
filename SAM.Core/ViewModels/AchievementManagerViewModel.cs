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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private string _Status = "Retrieving stat information...";
        private bool _IsBusy = true;
        private bool _AllowStatEditing;
        private bool _IsSteamConnected = true;

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

        public bool AllowStatEditing
        {
            get => this._AllowStatEditing;
            set => this.Set(ref this._AllowStatEditing, value);
        }

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
                this.Load(schema);
            }
            catch (Exception e)
            {
                this.IsBusy = false;
                this.Status = "Error when handling stats retrieval.";
                this.ErrorRaised?.Invoke("Error when handling stats retrieval:\n" + e.Message);
                return;
            }

            this.IsBusy = false;
            this.Status = _($"Retrieved {this._AllAchievements.Count} achievements and {this._AllStatistics.Count} statistics.");
        }

        /// <summary>
        /// Replaces the achievement and statistic lists from a schema, reading each current
        /// value back from Steam. Separated from the load so a schema obtained any other way
        /// can drive the same screen.
        /// </summary>
        /// <remarks>
        /// Steam can redeliver <c>UserStatsReceived</c> on its own schedule, not only in
        /// response to a request this view model made, so a reload can arrive while the user
        /// still has edits pending. Rebuilding from scratch would otherwise discard them
        /// without asking -- the pending state is captured by id before the rebuild and
        /// reapplied to whichever new view models still exist afterwards.
        /// </remarks>
        public void Load(UserGameStatsSchema schema)
        {
            var pendingUnlocked = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var achievement in this._AllAchievements)
            {
                if (achievement.IsModified == true)
                {
                    pendingUnlocked[achievement.Id] = achievement.IsUnlocked;
                }

                achievement.Changed -= this.OnAchievementChanged;
                achievement.ProtectedChangeRejected -= this.OnProtectedChangeRejected;
            }

            var pendingStatText = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var statistic in this._AllStatistics)
            {
                if (statistic.IsModified == true)
                {
                    pendingStatText[statistic.Id] = statistic.ValueText;
                }

                statistic.Changed -= this.OnStatisticChanged;
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

                AchievementViewModel achievement = new(this._Steam.AppId, definition, isAchieved, unlockTime);
                achievement.Changed += this.OnAchievementChanged;
                achievement.ProtectedChangeRejected += this.OnProtectedChangeRejected;
                this._AllAchievements.Add(achievement);

                // TrySetUnlocked silently declines when the achievement is protected, exactly
                // as it does for the bulk commands -- a reload must not force through an edit
                // that would have been refused had the user made it just now.
                if (pendingUnlocked.TryGetValue(achievement.Id, out var wantsUnlocked) == true)
                {
                    achievement.TrySetUnlocked(wantsUnlocked);
                }
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
                        statistic = new IntegerStatViewModel(integerDefinition, value);
                    }
                }
                else if (definition is FloatStatDefinition floatDefinition)
                {
                    if (this._Steam.TryGetFloatStat(floatDefinition.Id, out var value) == true)
                    {
                        statistic = new FloatStatViewModel(floatDefinition, value);
                    }
                }

                if (statistic == null)
                {
                    continue;
                }

                statistic.Changed += this.OnStatisticChanged;
                this._AllStatistics.Add(statistic);

                if (pendingStatText.TryGetValue(statistic.Id, out var wantsText) == true)
                {
                    statistic.ValueText = wantsText;
                }
            }

            this.Statistics.ReplaceAll(this._AllStatistics);

            this.ApplyFilter();
            this.RaiseTotals();

            // Having data is what ends the wait, whichever route the schema arrived by.
            this.IsBusy = false;
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
                    _ => true,
                };
                return wanted == true && achievement.Matches(search) == true;
            });
            this.Achievements.ReplaceAll(achievements);

            var statistics = this._AllStatistics.Where(statistic => statistic.Matches(search) == true);
            this.Statistics.ReplaceAll(statistics);
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
                nameof(this.IsModified),
                nameof(this.HasValidationErrors));
            this.StoreCommand.RaiseCanExecuteChanged();
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
                nameof(this.IsModified),
                nameof(this.HasValidationErrors));
            this.StoreCommand.RaiseCanExecuteChanged();
        }

        private void RaiseCommandStates()
        {
            this.ReloadCommand.RaiseCanExecuteChanged();
            this.StoreCommand.RaiseCanExecuteChanged();
            this.UnlockAllCommand.RaiseCanExecuteChanged();
            this.LockAllCommand.RaiseCanExecuteChanged();
            this.InvertAllCommand.RaiseCanExecuteChanged();
            this.ResetAllCommand.RaiseCanExecuteChanged();
        }

        private static string TranslateError(int id) => id switch
        {
            2 => "generic error -- this usually means you don't own the game",
            _ => _($"{id}"),
        };
    }
}
