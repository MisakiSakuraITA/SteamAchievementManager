using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
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
    }
}
