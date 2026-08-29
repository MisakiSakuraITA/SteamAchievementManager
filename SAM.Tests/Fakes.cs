using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SAM.Core.Steam;
using SAM.Core.ViewModels;

namespace SAM.Tests
{
    /// <summary>Stands in for a running Steam client's library surface.</summary>
    internal sealed class FakeLibrary : ISteamLibraryService
    {
        private readonly HashSet<uint> _Owned = new();
        private readonly Dictionary<uint, string> _Names = new();
        private readonly Dictionary<uint, string> _Capsules = new();

        public int OwnershipQueryCount;

        public string CurrentLanguage { get; set; } = "english";

        public bool IsConnected { get; private set; } = true;

        public bool IsDisposed { get; private set; }

        public ulong ActiveSteamId { get; set; } = 76561197960287930UL;

        public string ActivePersonaName { get; set; } = "TestUser";

        public event Action<uint> AppDataChanged;

        public event Action Disconnected;

        public void Add(uint appId, string name, string capsuleUrl = null)
        {
            this._Owned.Add(appId);
            this._Names[appId] = name;
            if (capsuleUrl != null)
            {
                this._Capsules[appId] = capsuleUrl;
            }
        }

        public bool OwnsApp(uint appId)
        {
            this.OwnershipQueryCount++;
            return this._Owned.Contains(appId);
        }

        public string GetAppName(uint appId) => this._Names.TryGetValue(appId, out var n) ? n : null;

        public string GetCapsuleUrl(uint appId) => this._Capsules.TryGetValue(appId, out var c) ? c : null;

        public void RunCallbacks()
        {
        }

        /// <summary>Simulates Steam filling in (or changing) metadata after the fact.</summary>
        public void Rename(uint appId, string name, string capsuleUrl = null)
        {
            this._Names[appId] = name;
            if (capsuleUrl != null)
            {
                this._Capsules[appId] = capsuleUrl;
            }
            this.AppDataChanged?.Invoke(appId);
        }

        public void SimulateDisconnect()
        {
            this.IsConnected = false;
            this.Disconnected?.Invoke();
        }

        public void Dispose() => this.IsDisposed = true;
    }

    /// <summary>Stands in for a running Steam client's stats surface.</summary>
    internal sealed class FakeStats : ISteamStatsService
    {
        private readonly Dictionary<string, bool> _Achievements = new();
        private readonly Dictionary<string, DateTime?> _UnlockTimes = new();
        private readonly Dictionary<string, int> _IntStats = new();
        private readonly Dictionary<string, float> _FloatStats = new();
        private readonly Dictionary<string, double> _GlobalPercentages = new();

        public readonly List<string> StoredAchievements = new();
        public readonly List<string> StoredStats = new();

        public bool StoreSucceeds = true;
        public bool SetAchievementSucceeds = true;
        public int StoreCallCount;
        public int ResetCallCount;
        public bool LastResetIncludedAchievements;

        /// <summary>
        /// Whether a seeded global percentage is actually readable yet, mirroring how the
        /// real cache isn't populated until Steam finishes answering
        /// RequestGlobalAchievementPercentages. Defaults true so tests that don't care about
        /// that asynchrony can seed a value and read it back immediately.
        /// </summary>
        public bool GlobalPercentagesAvailable = true;

        public int RequestGlobalAchievementPercentagesCallCount;

        public uint AppId { get; set; } = 480;

        public string AppName { get; set; } = "Spacewar";

        public string CurrentLanguage { get; set; } = "english";

        public string InstallPath { get; set; }

        public bool IsConnected { get; private set; } = true;

        public bool IsDisposed { get; private set; }

        public ulong ActiveSteamId { get; set; } = 76561197960287930UL;

        public string ActivePersonaName { get; set; } = "TestUser";

        public event Action<int> UserStatsReceived;

        public event Action Disconnected;

        public void SeedAchievement(string id, bool achieved, DateTime? unlockTime = null)
        {
            this._Achievements[id] = achieved;
            this._UnlockTimes[id] = unlockTime;
        }

        public void SeedInt(string id, int value) => this._IntStats[id] = value;

        public void SeedFloat(string id, float value) => this._FloatStats[id] = value;

        public void SeedGlobalPercentage(string id, double percentage) => this._GlobalPercentages[id] = percentage;

        public bool RequestUserStats() => true;

        public void RaiseStatsReceived(int result) => this.UserStatsReceived?.Invoke(result);

        public void RunCallbacks()
        {
        }

        public void SimulateDisconnect()
        {
            this.IsConnected = false;
            this.Disconnected?.Invoke();
        }

        public bool TryGetAchievement(string id, out bool isAchieved, out DateTime? unlockTime)
        {
            unlockTime = null;
            if (this._Achievements.TryGetValue(id, out isAchieved) == false)
            {
                return false;
            }
            this._UnlockTimes.TryGetValue(id, out unlockTime);
            return true;
        }

        public bool SetAchievement(string id, bool isAchieved)
        {
            if (this.SetAchievementSucceeds == false)
            {
                return false;
            }
            this._Achievements[id] = isAchieved;
            this.StoredAchievements.Add(id);
            return true;
        }

        public bool TryGetIntegerStat(string id, out int value) => this._IntStats.TryGetValue(id, out value);

        public bool TryGetFloatStat(string id, out float value) => this._FloatStats.TryGetValue(id, out value);

        public bool SetIntegerStat(string id, int value)
        {
            this._IntStats[id] = value;
            this.StoredStats.Add(id);
            return true;
        }

        public bool SetFloatStat(string id, float value)
        {
            this._FloatStats[id] = value;
            this.StoredStats.Add(id);
            return true;
        }

        public bool StoreStats()
        {
            this.StoreCallCount++;
            return this.StoreSucceeds;
        }

        public bool ResetAllStats(bool includeAchievements)
        {
            this.ResetCallCount++;
            this.LastResetIncludedAchievements = includeAchievements;
            return true;
        }

        public void RequestGlobalAchievementPercentages() => this.RequestGlobalAchievementPercentagesCallCount++;

        public bool TryGetGlobalAchievementPercentage(string id, out double percentage)
        {
            if (this.GlobalPercentagesAvailable == false ||
                this._GlobalPercentages.TryGetValue(id, out percentage) == false)
            {
                percentage = 0d;
                return false;
            }
            return true;
        }

        public IReadOnlyList<string> AchievementIds => this._Achievements.Keys.ToList();

        public void Dispose() => this.IsDisposed = true;
    }

    /// <summary>
    /// Scripts a sequence of yes/no answers to <see cref="IDialogService.ShowConfirmationAsync"/>
    /// and records exactly what it was asked, so a test can assert on both the questions and
    /// their order without a real message box ever appearing.
    /// </summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public readonly List<(string Title, string Message, DialogSeverity Severity)> Calls = new();
        public readonly Queue<bool> Answers = new();

        /// <summary>What <see cref="ShowSaveFileAsync"/> hands back; null simulates the user cancelling.</summary>
        public string SaveFilePathToReturn;

        /// <summary>What <see cref="ShowOpenFileAsync"/> hands back; null simulates the user cancelling.</summary>
        public string OpenFilePathToReturn;

        public readonly List<string> SaveFilePrompts = new();
        public int OpenFilePromptCount;

        public Task<bool> ShowConfirmationAsync(string title, string message, DialogSeverity severity)
        {
            this.Calls.Add((title, message, severity));
            var answer = this.Answers.Count > 0 ? this.Answers.Dequeue() : false;
            return Task.FromResult(answer);
        }

        public Task<string> ShowSaveFileAsync(string suggestedFileName)
        {
            this.SaveFilePrompts.Add(suggestedFileName);
            return Task.FromResult(this.SaveFilePathToReturn);
        }

        public Task<string> ShowOpenFileAsync()
        {
            this.OpenFilePromptCount++;
            return Task.FromResult(this.OpenFilePathToReturn);
        }
    }
}
