using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SAM.Core.Snapshots;
using SAM.Core.Steam.Schema;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class AchievementManagerViewModelTests
    {
        private static UserGameStatsSchema BuildSchema(FakeStats steam)
        {
            steam.SeedAchievement("ACH_A", false);
            steam.SeedAchievement("ACH_B", true, new DateTime(2024, 1, 1));
            steam.SeedAchievement("ACH_C", false);
            steam.SeedInt("kills", 7);

            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 0 },
                new() { Id = "ACH_B", Name = "Beta", Description = "second", Permission = 0 },
                new() { Id = "ACH_C", Name = "Gamma", Description = "third", Permission = 1 },
            };
            var stats = new List<StatDefinition>
            {
                new IntegerStatDefinition { Id = "kills", DisplayName = "Kills", MinValue = 0, MaxValue = 100 },
            };
            return new UserGameStatsSchema(definitions, stats);
        }

        /// <summary>
        /// Three unprotected, initially-locked achievements plus one stat -- unlike
        /// <see cref="BuildSchema"/>, every achievement here can actually be staged unlocked,
        /// which the queued-store tests need for more than one achievement at a time.
        /// </summary>
        private static UserGameStatsSchema BuildQueueableSchema(FakeStats steam)
        {
            steam.SeedAchievement("ACH_A", false);
            steam.SeedAchievement("ACH_B", false);
            steam.SeedAchievement("ACH_C", false);
            steam.SeedInt("kills", 7);

            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 0 },
                new() { Id = "ACH_B", Name = "Beta", Description = "second", Permission = 0 },
                new() { Id = "ACH_C", Name = "Gamma", Description = "third", Permission = 0 },
            };
            var stats = new List<StatDefinition>
            {
                new IntegerStatDefinition { Id = "kills", DisplayName = "Kills", MinValue = 0, MaxValue = 100 },
            };
            return new UserGameStatsSchema(definitions, stats);
        }

        [Fact]
        public void MissingSchemaFailsGracefullyWithoutThrowing()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());

            manager.BeginLoad();
            steam.RaiseStatsReceived(1);

            Assert.False(manager.IsBusy);
        }

        [Fact]
        public void LoadBuildsAchievementsAndStatisticsWithCorrectTotals()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            Assert.Equal(3, manager.AchievementCount);
            Assert.Equal(1, manager.StatisticCount);
            Assert.Equal(1, manager.UnlockedCount);
            Assert.Equal(100.0 / 3.0, manager.CompletionPercentage, 3);
            Assert.Equal("1 of 3 unlocked", manager.CompletionText);
            Assert.False(manager.IsModified);
            Assert.False(manager.StoreCommand.CanExecute(null));
        }

        [Fact]
        public void StatusReportsHowManyOfTheLoadedAchievementsAreCurrentlyShown()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            Assert.Equal("Showing 3 of 3 achievements.", manager.Status);

            manager.SearchText = "alph";
            Assert.Equal("Showing 1 of 3 achievements.", manager.Status);

            manager.SearchText = "";
            manager.Filter = AchievementFilter.Unlocked;
            Assert.Equal("Showing 1 of 3 achievements.", manager.Status);

            manager.SearchText = "nothing matches this";
            Assert.Equal("Showing 0 of 3 achievements.", manager.Status);
        }

        [Fact]
        public void StatusReportsNoAchievementsFoundForAnEmptySchema()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(Enumerable.Empty<AchievementDefinition>(), Enumerable.Empty<StatDefinition>()));

            Assert.Equal("No achievements found.", manager.Status);
        }

        [Fact]
        public void DisconnectingOutranksTheSearchStatusEvenAfterAFilterChange()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            steam.SimulateDisconnect();
            Assert.Equal(manager.DisconnectedMessage, manager.Status);

            // Filtering after a disconnect must not silently paper back over the banner-
            // adjacent message with a count as if nothing were wrong.
            manager.SearchText = "alph";
            Assert.Equal(manager.DisconnectedMessage, manager.Status);
        }

        [Fact]
        public void FilterAndSearchNarrowTheAchievementList()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            manager.Filter = AchievementFilter.Locked;
            Assert.Equal(2, manager.Achievements.Count);

            manager.Filter = AchievementFilter.Unlocked;
            Assert.Single(manager.Achievements);
            Assert.Equal("ACH_B", manager.Achievements[0].Id);

            manager.Filter = AchievementFilter.All;
            manager.SearchText = "alph";
            Assert.Single(manager.Achievements);
            Assert.Equal("ACH_A", manager.Achievements[0].Id);
        }

        [Fact]
        public void BulkCommandsSkipProtectedAchievements()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam)); // ACH_C is permission 1 (protected)

            manager.UnlockAllCommand.Execute(null);
            Assert.Equal(2, manager.UnlockedCount);
            Assert.Equal(1, manager.ModifiedAchievementCount);
            Assert.True(manager.StoreCommand.CanExecute(null));

            manager.LockAllCommand.Execute(null);
            Assert.Equal(0, manager.UnlockedCount);

            manager.InvertAllCommand.Execute(null);
            Assert.Equal(2, manager.UnlockedCount);
        }

        [Fact]
        public async Task StoreCommitsOnlyGenuinelyChangedEntriesAndReportsWhatItDid()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Statistics[0].ValueText = "55";

            var infos = new List<string>();
            manager.InfoRaised += infos.Add;

            await manager.StoreCommand.ExecuteAsync(null);

            Assert.Equal(1, steam.StoreCallCount);
            Assert.Equal(new[] { "ACH_A" }, steam.StoredAchievements);
            Assert.Equal(new[] { "kills" }, steam.StoredStats);
            Assert.Single(infos);
            Assert.Contains("Stored", infos[0]);
            Assert.False(manager.IsModified);
        }

        [Fact]
        public async Task FailedStoreIsReportedAndRevertsPendingState()
        {
            FakeStats failing = new() { InstallPath = null, SetAchievementSucceeds = false };
            failing.SeedAchievement("ACH_X", false);
            AchievementManagerViewModel manager = new(failing, new FakeDialogService());
            var failures = new List<string>();
            manager.ErrorRaised += failures.Add;
            manager.Load(new UserGameStatsSchema(
                new List<AchievementDefinition> { new() { Id = "ACH_X", Name = "X", Description = "", Permission = 0 } },
                new List<StatDefinition>()));

            manager.Achievements[0].IsUnlocked = true;
            await manager.StoreCommand.ExecuteAsync(null);

            Assert.Single(failures);
            Assert.Contains("ACH_X", failures[0]);
            Assert.False(manager.IsModified);
            Assert.Equal(0, failing.StoreCallCount);
        }

        [Fact]
        public async Task ResetAsksThreeQuestionsInOrderAndAppliesTheAnswers()
        {
            FakeStats steam = new() { InstallPath = null };
            FakeDialogService dialogs = new();
            dialogs.Answers.Enqueue(true);
            dialogs.Answers.Enqueue(true);
            dialogs.Answers.Enqueue(true);

            AchievementManagerViewModel manager = new(steam, dialogs);
            manager.Load(new UserGameStatsSchema(Enumerable.Empty<AchievementDefinition>(), Enumerable.Empty<StatDefinition>()));

            await manager.ResetAllCommand.ExecuteAsync(null);

            Assert.Equal(3, dialogs.Calls.Count);
            Assert.Equal(DialogSeverity.Warning, dialogs.Calls[0].Severity);
            Assert.Equal(DialogSeverity.Question, dialogs.Calls[1].Severity);
            Assert.Equal(DialogSeverity.Error, dialogs.Calls[2].Severity);
            Assert.Equal(1, steam.ResetCallCount);
            Assert.True(steam.LastResetIncludedAchievements);
        }

        [Fact]
        public async Task DecliningTheFirstResetQuestionStopsTheFlowImmediately()
        {
            FakeStats steam = new() { InstallPath = null };
            FakeDialogService dialogs = new();
            dialogs.Answers.Enqueue(false);

            AchievementManagerViewModel manager = new(steam, dialogs);
            manager.Load(new UserGameStatsSchema(Enumerable.Empty<AchievementDefinition>(), Enumerable.Empty<StatDefinition>()));

            await manager.ResetAllCommand.ExecuteAsync(null);

            Assert.Single(dialogs.Calls);
            Assert.Equal(0, steam.ResetCallCount);
        }

        [Fact]
        public async Task DecliningTheFinalResetQuestionAbortsAfterAskingAllThree()
        {
            FakeStats steam = new() { InstallPath = null };
            FakeDialogService dialogs = new();
            dialogs.Answers.Enqueue(true);
            dialogs.Answers.Enqueue(false);
            dialogs.Answers.Enqueue(false);

            AchievementManagerViewModel manager = new(steam, dialogs);
            manager.Load(new UserGameStatsSchema(Enumerable.Empty<AchievementDefinition>(), Enumerable.Empty<StatDefinition>()));

            await manager.ResetAllCommand.ExecuteAsync(null);

            Assert.Equal(3, dialogs.Calls.Count);
            Assert.Equal(0, steam.ResetCallCount);
        }

        [Fact]
        public void UnsolicitedReloadPreservesAPendingAchievementUnlock()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            var schema = BuildSchema(steam);
            manager.Load(schema);

            // The user stages an unlock, then Steam redelivers stats on its own -- another
            // app's request landing on the shared pipe, say -- reloading before the user
            // ever gets to store.
            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            Assert.True(manager.IsModified);

            manager.Load(schema);

            Assert.True(manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);
            Assert.True(manager.IsModified);
        }

        [Fact]
        public void ReloadDoesNotForceAPendingEditOntoAnAchievementNowProtected()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 0 },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(definitions, Enumerable.Empty<StatDefinition>()));

            manager.Achievements[0].IsUnlocked = true;
            Assert.True(manager.IsModified);

            // The reloaded schema now marks the same achievement protected -- the pending
            // unlock must be dropped, not forced through as it would be for a fresh edit.
            var reprotected = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 1 },
            };
            manager.Load(new UserGameStatsSchema(reprotected, Enumerable.Empty<StatDefinition>()));

            Assert.False(manager.Achievements[0].IsUnlocked);
            Assert.False(manager.IsModified);
        }

        [Fact]
        public void UnsolicitedReloadPreservesAPendingStatisticEdit()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            var schema = BuildSchema(steam);
            manager.Load(schema);

            manager.Statistics[0].ValueText = "55";
            Assert.True(manager.IsModified);

            manager.Load(schema);

            Assert.Equal("55", manager.Statistics[0].ValueText);
            Assert.True(manager.IsModified);
        }

        [Fact]
        public void ReloadWithAnUnchangedIdReusesTheSameAchievementAndStatisticInstances()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            var schema = BuildSchema(steam);
            manager.Load(schema);

            var achievementBefore = manager.Achievements.First(a => a.Id == "ACH_A");
            var statisticBefore = manager.Statistics[0];

            manager.Load(schema);

            var achievementAfter = manager.Achievements.First(a => a.Id == "ACH_A");
            var statisticAfter = manager.Statistics[0];

            // The actual point of M-05: a reload with nothing new updates the existing
            // instances in place rather than replacing every one of them.
            Assert.Same(achievementBefore, achievementAfter);
            Assert.Same(statisticBefore, statisticAfter);
        }

        [Fact]
        public void ReloadWithChangedDisplayTextUpdatesTheReusedInstanceInPlace()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            var original = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Original Name", Description = "Original description", Permission = 0 },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(original, Enumerable.Empty<StatDefinition>()));

            var before = manager.Achievements[0];
            Assert.Equal("Original Name", before.Name);

            // A redelivered schema can legitimately carry different display text -- a
            // language change, say -- and a reused instance must not go on showing what it
            // was first constructed with.
            var retranslated = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Translated Name", Description = "Translated description", Permission = 0 },
            };
            manager.Load(new UserGameStatsSchema(retranslated, Enumerable.Empty<StatDefinition>()));

            var after = manager.Achievements[0];
            Assert.Same(before, after);
            Assert.Equal("Translated Name", after.Name);
            Assert.Equal("Translated description", after.Description);
        }

        [Fact]
        public void ReloadWithAnIdNoLongerInTheSchemaDropsThatAchievement()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));
            Assert.Equal(3, manager.AchievementCount);

            var fewer = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Description = "first", Permission = 0 },
            };
            manager.Load(new UserGameStatsSchema(fewer, Enumerable.Empty<StatDefinition>()));

            Assert.Equal(1, manager.AchievementCount);
            Assert.Equal("ACH_A", manager.Achievements[0].Id);
        }

        [Fact]
        public void ModifiedCountCombinesAchievementAndStatisticEditsAndTracksEitherKindAlone()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            Assert.Equal(0, manager.ModifiedCount);

            // A statistic edit alone must move the combined count -- this is exactly the
            // path that, before M-10, only ever raised ModifiedStatisticCount, leaving a
            // chip bound to ModifiedCount alone stuck reporting pending achievements only.
            manager.Statistics[0].ValueText = "55";
            Assert.Equal(1, manager.ModifiedCount);

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            Assert.Equal(2, manager.ModifiedCount);
        }

        [Fact]
        public void StatisticEditAloneRaisesModifiedCountChanged()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            var raised = new List<string>();
            manager.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            manager.Statistics[0].ValueText = "55";

            Assert.Contains(nameof(AchievementManagerViewModel.ModifiedCount), raised);
        }

        [Fact]
        public void DisconnectGatesEveryCommandAndClearsBusyState()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));
            manager.Achievements[0].IsUnlocked = !manager.Achievements[0].IsUnlocked;

            Assert.True(manager.ReloadCommand.CanExecute(null));
            Assert.True(manager.ResetAllCommand.CanExecute(null));
            Assert.True(manager.UnlockAllCommand.CanExecute(null));
            Assert.True(manager.StoreCommand.CanExecute(null));

            steam.SimulateDisconnect();

            Assert.True(manager.IsSteamConnected == false && manager.IsSteamDisconnected);
            Assert.Equal(manager.DisconnectedMessage, manager.Status);
            Assert.False(manager.StoreCommand.CanExecute(null));
            Assert.False(manager.ReloadCommand.CanExecute(null));
            Assert.False(manager.UnlockAllCommand.CanExecute(null));
            Assert.False(manager.LockAllCommand.CanExecute(null));
            Assert.False(manager.InvertAllCommand.CanExecute(null));
            Assert.False(manager.ResetAllCommand.CanExecute(null));
        }

        [Fact]
        public void ApplyFilterRaisesExactlyOneCollectionChangedPerFilterChange()
        {
            const int total = 3000;
            FakeStats steam = new() { InstallPath = null };
            var achievements = new List<AchievementDefinition>();
            for (var i = 0; i < total; i++)
            {
                var id = $"ACH_{i:D4}";
                achievements.Add(new AchievementDefinition { Id = id, Name = $"Achievement {i:D4}" });
                steam.SeedAchievement(id, i % 2 == 0);
            }

            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(achievements, Enumerable.Empty<StatDefinition>()));
            Assert.Equal(total, manager.Achievements.Count);

            var events = new List<NotifyCollectionChangedAction>();
            manager.Achievements.CollectionChanged += (_, e) => events.Add(e.Action);

            manager.SearchText = "0123";
            Assert.Single(events);
            Assert.Equal(NotifyCollectionChangedAction.Reset, events[0]);

            events.Clear();
            manager.Filter = AchievementFilter.Unlocked;
            Assert.Single(events);
            Assert.Equal(NotifyCollectionChangedAction.Reset, events[0]);
        }

        // ============================ sorting ============================

        [Fact]
        public void DefaultSortOrderPreservesSchemaOrder()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            Assert.Equal(new[] { "ACH_A", "ACH_B", "ACH_C" }, manager.Achievements.Select(a => a.Id));
        }

        [Fact]
        public void SortOrderAlphabeticalOrdersByName()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            manager.SortOrder = AchievementSortOrder.Alphabetical;

            // Alpha, Beta, Gamma -- already alphabetical, so this also proves the sort did
            // not just leave the schema order untouched by coincidence.
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, manager.Achievements.Select(a => a.Name));
        }

        [Fact]
        public void SortOrderUnlockStatusPutsLockedAchievementsFirst()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam)); // ACH_B starts unlocked; A and C start locked

            manager.SortOrder = AchievementSortOrder.UnlockStatus;

            var ids = manager.Achievements.Select(a => a.Id).ToList();
            Assert.Equal(new[] { "ACH_A", "ACH_C", "ACH_B" }, ids);
        }

        [Fact]
        public void SortOrderRarityPutsTheRarestFirstAndUnknownRarityLast()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            var a = manager.Achievements.Single(x => x.Id == "ACH_A");
            var b = manager.Achievements.Single(x => x.Id == "ACH_B");
            // ACH_C is deliberately left without a rarity value.
            a.RarityPercentage = 12.0;
            b.RarityPercentage = 1.4;

            manager.SortOrder = AchievementSortOrder.Rarity;

            Assert.Equal(new[] { "ACH_B", "ACH_A", "ACH_C" }, manager.Achievements.Select(x => x.Id));
        }

        [Fact]
        public void SortOrderHiddenStatusPutsHiddenAchievementsFirst()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            steam.SeedAchievement("ACH_B", false);
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Permission = 0, IsHidden = false },
                new() { Id = "ACH_B", Name = "Beta", Permission = 0, IsHidden = true },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(definitions, Enumerable.Empty<StatDefinition>()));

            manager.SortOrder = AchievementSortOrder.HiddenStatus;

            Assert.Equal(new[] { "ACH_B", "ACH_A" }, manager.Achievements.Select(a => a.Id));
        }

        // ============================ filtering ============================

        [Fact]
        public void FilterHiddenShowsOnlyHiddenAchievements()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            steam.SeedAchievement("ACH_B", false);
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Permission = 0, IsHidden = false },
                new() { Id = "ACH_B", Name = "Beta", Permission = 0, IsHidden = true },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(definitions, Enumerable.Empty<StatDefinition>()));

            manager.Filter = AchievementFilter.Hidden;

            Assert.Single(manager.Achievements);
            Assert.Equal("ACH_B", manager.Achievements[0].Id);
        }

        [Fact]
        public void FilterUltraRareShowsOnlyAchievementsUnderTheThreshold()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            manager.Achievements.Single(a => a.Id == "ACH_A").RarityPercentage = 1.4;
            manager.Achievements.Single(a => a.Id == "ACH_B").RarityPercentage = 42.0;
            // ACH_C is left with no rarity value at all.

            manager.Filter = AchievementFilter.UltraRare;

            Assert.Single(manager.Achievements);
            Assert.Equal("ACH_A", manager.Achievements[0].Id);
        }

        [Fact]
        public void SortAndFilterChangesPreserveAPendingEdit()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            Assert.True(manager.IsModified);

            manager.SortOrder = AchievementSortOrder.Alphabetical;
            manager.Filter = AchievementFilter.All;
            manager.SortOrder = AchievementSortOrder.Rarity;
            manager.Filter = AchievementFilter.Locked;
            manager.Filter = AchievementFilter.All;

            Assert.True(manager.IsModified);
            Assert.True(manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);
        }

        // ============================ hidden reveal ============================

        [Fact]
        public void RevealHiddenAchievementsTogglePropagatesToEveryAchievement()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Permission = 0, IsHidden = true },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(new UserGameStatsSchema(definitions, Enumerable.Empty<StatDefinition>()));

            var achievement = manager.Achievements[0];
            Assert.NotEqual("Alpha", achievement.DisplayName);

            manager.RevealHiddenAchievements = true;
            Assert.Equal("Alpha", achievement.DisplayName);

            manager.RevealHiddenAchievements = false;
            Assert.NotEqual("Alpha", achievement.DisplayName);
        }

        [Fact]
        public void ANewlyLoadedAchievementRespectsAnAlreadyActiveRevealToggle()
        {
            FakeStats steam = new() { InstallPath = null };
            steam.SeedAchievement("ACH_A", false);
            var definitions = new List<AchievementDefinition>
            {
                new() { Id = "ACH_A", Name = "Alpha", Permission = 0, IsHidden = true },
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.RevealHiddenAchievements = true;

            manager.Load(new UserGameStatsSchema(definitions, Enumerable.Empty<StatDefinition>()));

            Assert.Equal("Alpha", manager.Achievements[0].DisplayName);
        }

        // ============================ rarity population ============================

        [Fact]
        public async Task LoadRequestsGlobalPercentagesAndPopulatesThemOnceAvailable()
        {
            FakeStats steam = new() { InstallPath = null, GlobalPercentagesAvailable = false };
            steam.SeedGlobalPercentage("ACH_A", 1.4);
            steam.SeedGlobalPercentage("ACH_B", 55.0);
            steam.SeedGlobalPercentage("ACH_C", 30.0);

            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.RarityPollIntervalForTesting = TimeSpan.FromMilliseconds(5);
            manager.Load(BuildSchema(steam));

            Assert.True(steam.RequestGlobalAchievementPercentagesCallCount >= 1);
            Assert.Null(manager.Achievements.Single(a => a.Id == "ACH_A").RarityPercentage);

            // The cache "arrives" only now, matching how Steam answers asynchronously.
            steam.GlobalPercentagesAvailable = true;

            var achievement = manager.Achievements.Single(a => a.Id == "ACH_A");
            for (var i = 0; i < 100 && achievement.RarityPercentage.HasValue == false; i++)
            {
                await Task.Delay(10);
            }

            Assert.Equal(1.4, achievement.RarityPercentage);
            Assert.Equal(55.0, manager.Achievements.Single(a => a.Id == "ACH_B").RarityPercentage);
        }

        [Fact]
        public async Task RarityPollingGivesUpQuietlyWhenNothingEverBecomesAvailable()
        {
            FakeStats steam = new() { InstallPath = null, GlobalPercentagesAvailable = false };

            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.RarityPollIntervalForTesting = TimeSpan.FromMilliseconds(2);
            manager.Load(BuildSchema(steam));

            // Ten attempts at 2ms plus scheduling slack; generous so this isn't flaky.
            await Task.Delay(500);

            Assert.Null(manager.Achievements.Single(a => a.Id == "ACH_A").RarityPercentage);
            Assert.False(manager.IsBusy);
        }

        // ============================ queued store ============================

        [Fact]
        public async Task QueuedStoreProcessesEachAchievementIndividually()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.QueuedStoreDelayForTesting = TimeSpan.FromMilliseconds(1);
            manager.Load(BuildQueueableSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Achievements.First(a => a.Id == "ACH_C").IsUnlocked = true;

            await manager.QueuedStoreCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "ACH_A", "ACH_C" }, steam.StoredAchievements);
            // One StoreStats call per achievement, not one for the whole batch.
            Assert.Equal(2, steam.StoreCallCount);
            Assert.False(manager.IsModified);
            Assert.Equal(2, manager.QueuedStoreCompleted);
            Assert.Equal(2, manager.QueuedStoreTotal);
            Assert.False(manager.IsQueuedStoreRunning);
        }

        [Fact]
        public async Task QueuedStoreIncludesPendingStatisticsAlongsideTheFirstAchievement()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.QueuedStoreDelayForTesting = TimeSpan.FromMilliseconds(1);
            manager.Load(BuildQueueableSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Statistics[0].ValueText = "55";

            await manager.QueuedStoreCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "kills" }, steam.StoredStats);
            Assert.False(manager.IsModified);
        }

        [Fact]
        public async Task QueuedStoreCanBeCancelledPartwayThroughWithoutLosingWhatAlreadyStored()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.QueuedStoreDelayForTesting = TimeSpan.FromMilliseconds(200);
            manager.Load(BuildQueueableSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Achievements.First(a => a.Id == "ACH_C").IsUnlocked = true;

            var run = manager.QueuedStoreCommand.ExecuteAsync(null);

            // Give the first item time to store, then cancel before the delay finishes.
            await Task.Delay(50);
            Assert.True(manager.IsQueuedStoreRunning);
            manager.CancelQueuedStoreCommand.Execute(null);

            await run;

            Assert.Single(steam.StoredAchievements);
            Assert.Equal("ACH_A", steam.StoredAchievements[0]);
            Assert.False(manager.IsQueuedStoreRunning);

            // The un-stored achievement's edit is still pending, not silently discarded.
            Assert.True(manager.IsModified);
            Assert.True(manager.Achievements.First(a => a.Id == "ACH_C").IsModified);
        }

        [Fact]
        public async Task QueuedStoreStopsOnAStoreFailureAndLeavesTheEditPending()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.QueuedStoreDelayForTesting = TimeSpan.FromMilliseconds(1);
            manager.Load(BuildQueueableSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Achievements.First(a => a.Id == "ACH_C").IsUnlocked = true;

            var failures = new List<string>();
            manager.ErrorRaised += failures.Add;

            steam.StoreSucceeds = false;

            await manager.QueuedStoreCommand.ExecuteAsync(null);

            Assert.NotEmpty(failures);
            Assert.False(manager.IsQueuedStoreRunning);
            Assert.Equal(0, manager.QueuedStoreCompleted);
            Assert.True(manager.IsModified);
        }

        [Fact]
        public void QueuedStoreCommandCannotExecuteWithNothingPending()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            Assert.False(manager.QueuedStoreCommand.CanExecute(null));
        }

        // ============================ snapshot export/import ============================

        [Fact]
        public void BuildSnapshotCapturesTheCurrentlyPendingAchievementAndStatisticState()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            // ACH_A starts locked; stage it unlocked without storing.
            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;
            manager.Statistics[0].ValueText = "55";

            var snapshot = manager.BuildSnapshot();

            Assert.Equal(480u, snapshot.AppId);
            Assert.True(snapshot.Achievements.Single(a => a.Id == "ACH_A").IsAchieved);
            // Still unstored, so there is no real unlock time to report yet.
            Assert.Null(snapshot.Achievements.Single(a => a.Id == "ACH_A").UnlockTime);
            // ACH_B was already stored as achieved by BuildSchema, with a real unlock time.
            Assert.Equal(new DateTime(2024, 1, 1), snapshot.Achievements.Single(a => a.Id == "ACH_B").UnlockTime);
            Assert.Equal(55, snapshot.Statistics.Single(s => s.Id == "kills").Value);
        }

        [Fact]
        public void ImportRejectsASnapshotRecordedForADifferentApp()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            var errors = new List<string>();
            manager.ErrorRaised += errors.Add;

            var snapshot = new GameSnapshot
            {
                AppId = 999,
                Achievements = new List<AchievementSnapshotEntry> { new() { Id = "ACH_A", IsAchieved = true } },
            };

            var applied = manager.TryApplySnapshot(snapshot);

            Assert.False(applied);
            Assert.Single(errors);
            Assert.Contains("999", errors[0]);
            Assert.False(manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);
            Assert.False(manager.IsModified);
        }

        [Fact]
        public void ImportStagesMatchingAchievementsAndStatisticsWithoutStoringToSteam()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            var infos = new List<string>();
            manager.InfoRaised += infos.Add;

            var snapshot = new GameSnapshot
            {
                AppId = manager.AppId,
                Achievements = new List<AchievementSnapshotEntry>
                {
                    new() { Id = "ACH_A", IsAchieved = true },
                    new() { Id = "ACH_B", IsAchieved = false },
                },
                Statistics = new List<StatisticSnapshotEntry> { new() { Id = "kills", Value = 55 } },
            };

            var applied = manager.TryApplySnapshot(snapshot);

            Assert.True(applied);
            Assert.True(manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);
            Assert.False(manager.Achievements.First(a => a.Id == "ACH_B").IsUnlocked);
            Assert.Equal("55", manager.Statistics[0].ValueText);
            Assert.True(manager.IsModified);
            // Staged for review only -- nothing has actually reached Steam.
            Assert.Equal(0, steam.StoreCallCount);
            Assert.NotEmpty(infos);
        }

        [Fact]
        public void ImportSilentlySkipsAProtectedAchievementJustLikeABulkToggleWould()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam)); // ACH_C is protected (Permission = 1).

            var rejections = 0;
            manager.ProtectedChangeRejected += _ => rejections++;

            var snapshot = new GameSnapshot
            {
                AppId = manager.AppId,
                Achievements = new List<AchievementSnapshotEntry> { new() { Id = "ACH_C", IsAchieved = true } },
            };

            manager.TryApplySnapshot(snapshot);

            Assert.False(manager.Achievements.First(a => a.Id == "ACH_C").IsUnlocked);
            Assert.Equal(0, rejections);
            Assert.False(manager.IsModified);
        }

        [Fact]
        public void ImportSkipsIdsThatAreNotPartOfTheLoadedSchema()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());
            manager.Load(BuildSchema(steam));

            var infos = new List<string>();
            manager.InfoRaised += infos.Add;

            var snapshot = new GameSnapshot
            {
                AppId = manager.AppId,
                Achievements = new List<AchievementSnapshotEntry> { new() { Id = "ACH_NOPE", IsAchieved = true } },
                Statistics = new List<StatisticSnapshotEntry> { new() { Id = "not-a-real-stat", Value = 1 } },
            };

            var applied = manager.TryApplySnapshot(snapshot);

            Assert.True(applied);
            Assert.False(manager.IsModified);
            Assert.Contains(infos, m => m.Contains("did not match"));
        }

        [Fact]
        public async Task ExportSnapshotCommandWritesAFileAtTheDialogChosenPath()
        {
            FakeStats steam = new() { InstallPath = null };
            var dialogs = new FakeDialogService();
            AchievementManagerViewModel manager = new(steam, dialogs);
            manager.Load(BuildSchema(steam));

            manager.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;

            var path = Path.Combine(Path.GetTempPath(), $"sam-export-{Guid.NewGuid():N}.json");
            dialogs.SaveFilePathToReturn = path;
            try
            {
                var infos = new List<string>();
                manager.InfoRaised += infos.Add;

                await manager.ExportSnapshotCommand.ExecuteAsync(null);

                Assert.True(File.Exists(path));
                var snapshot = GameSnapshotSerializer.FromJson(File.ReadAllText(path));
                Assert.Equal(480u, snapshot.AppId);
                Assert.True(snapshot.Achievements.Single(a => a.Id == "ACH_A").IsAchieved);
                Assert.NotEmpty(infos);
            }
            finally
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public async Task ExportSnapshotCommandDoesNothingWhenTheSaveDialogIsCancelled()
        {
            FakeStats steam = new() { InstallPath = null };
            var dialogs = new FakeDialogService();
            AchievementManagerViewModel manager = new(steam, dialogs);
            manager.Load(BuildSchema(steam));

            var infos = new List<string>();
            manager.InfoRaised += infos.Add;

            await manager.ExportSnapshotCommand.ExecuteAsync(null);

            Assert.Single(dialogs.SaveFilePrompts);
            Assert.Empty(infos);
        }

        [Fact]
        public async Task ImportSnapshotCommandAppliesAFileWrittenToDiskByExport()
        {
            FakeStats exportSteam = new() { InstallPath = null };
            var exportDialogs = new FakeDialogService();
            AchievementManagerViewModel exporter = new(exportSteam, exportDialogs);
            exporter.Load(BuildSchema(exportSteam));
            exporter.Achievements.First(a => a.Id == "ACH_A").IsUnlocked = true;

            var path = Path.Combine(Path.GetTempPath(), $"sam-import-{Guid.NewGuid():N}.csv");
            exportDialogs.SaveFilePathToReturn = path;
            try
            {
                await exporter.ExportSnapshotCommand.ExecuteAsync(null);

                FakeStats importSteam = new() { InstallPath = null };
                var importDialogs = new FakeDialogService { OpenFilePathToReturn = path };
                AchievementManagerViewModel importer = new(importSteam, importDialogs);
                importer.Load(BuildSchema(importSteam));

                Assert.False(importer.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);

                await importer.ImportSnapshotCommand.ExecuteAsync(null);

                Assert.True(importer.Achievements.First(a => a.Id == "ACH_A").IsUnlocked);
                Assert.Equal(0, importSteam.StoreCallCount);
            }
            finally
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ExportSnapshotCommandCannotExecuteBeforeAnythingIsLoaded()
        {
            FakeStats steam = new() { InstallPath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());

            Assert.False(manager.ExportSnapshotCommand.CanExecute(null));

            manager.Load(BuildSchema(steam));

            Assert.True(manager.ExportSnapshotCommand.CanExecute(null));
        }

        // ============================ active account ============================

        [Fact]
        public void ActiveAccountPropertiesAreReadThroughFromTheSteamService()
        {
            FakeStats steam = new()
            {
                InstallPath = null,
                ActiveSteamId = 76561197960287930UL,
                ActivePersonaName = "Alice",
                ActiveAvatarFilePath = @"C:\Steam\config\avatars\abc123hash_full.jpg",
            };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());

            Assert.Equal(76561197960287930UL, manager.ActiveSteamId);
            Assert.Equal("76561197960287930", manager.ActiveSteamIdText);
            Assert.Equal("Alice", manager.ActivePersonaName);
            Assert.Equal("Alice", manager.ActiveAccountDisplayName);
            Assert.Equal(@"C:\Steam\config\avatars\abc123hash_full.jpg", manager.ActiveAvatarFilePath);
        }

        [Fact]
        public void ActiveAvatarFilePathIsNullWhenNoAvatarWasFound()
        {
            FakeStats steam = new() { InstallPath = null, ActiveAvatarFilePath = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());

            Assert.Null(manager.ActiveAvatarFilePath);
        }

        [Fact]
        public void ActiveAccountDisplayNameFallsBackToTheSteamIdWhenThePersonaNameIsUnknown()
        {
            FakeStats steam = new() { InstallPath = null, ActiveSteamId = 76561197960287930UL, ActivePersonaName = null };
            AchievementManagerViewModel manager = new(steam, new FakeDialogService());

            Assert.Null(manager.ActivePersonaName);
            Assert.Equal("76561197960287930", manager.ActiveAccountDisplayName);
        }
    }
}
