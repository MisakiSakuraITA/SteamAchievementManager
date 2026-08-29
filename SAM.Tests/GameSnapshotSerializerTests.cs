using System;
using System.Collections.Generic;
using SAM.Core.Snapshots;
using Xunit;

namespace SAM.Tests
{
    public class GameSnapshotSerializerTests
    {
        private static GameSnapshot Sample() => new()
        {
            AppId = 480,
            Timestamp = new DateTime(2026, 8, 28, 12, 30, 0, DateTimeKind.Utc),
            Achievements = new List<AchievementSnapshotEntry>
            {
                new() { Id = "ACH_WIN", IsAchieved = true, UnlockTime = new DateTime(2026, 8, 1, 9, 15, 0, DateTimeKind.Utc) },
                new() { Id = "ACH_SECRET", IsAchieved = false, UnlockTime = null },
            },
            Statistics = new List<StatisticSnapshotEntry>
            {
                new() { Id = "kills", Value = 42 },
                new() { Id = "distance", Value = 1234.5 },
            },
        };

        [Fact]
        public void JsonRoundTripsAllFields()
        {
            var original = Sample();

            var json = GameSnapshotSerializer.ToJson(original);
            var restored = GameSnapshotSerializer.FromJson(json);

            Assert.Equal(original.AppId, restored.AppId);
            Assert.Equal(original.Timestamp, restored.Timestamp);

            Assert.Equal(2, restored.Achievements.Count);
            Assert.Equal("ACH_WIN", restored.Achievements[0].Id);
            Assert.True(restored.Achievements[0].IsAchieved);
            Assert.Equal(original.Achievements[0].UnlockTime, restored.Achievements[0].UnlockTime);
            Assert.Equal("ACH_SECRET", restored.Achievements[1].Id);
            Assert.False(restored.Achievements[1].IsAchieved);
            Assert.Null(restored.Achievements[1].UnlockTime);

            Assert.Equal(2, restored.Statistics.Count);
            Assert.Equal("kills", restored.Statistics[0].Id);
            Assert.Equal(42, restored.Statistics[0].Value);
            Assert.Equal("distance", restored.Statistics[1].Id);
            Assert.Equal(1234.5, restored.Statistics[1].Value);
        }

        [Fact]
        public void CsvRoundTripsAllFields()
        {
            var original = Sample();

            var csv = GameSnapshotSerializer.ToCsv(original);
            var restored = GameSnapshotSerializer.FromCsv(csv);

            Assert.Equal(original.AppId, restored.AppId);
            Assert.Equal(original.Timestamp, restored.Timestamp);

            Assert.Equal(2, restored.Achievements.Count);
            Assert.Equal("ACH_WIN", restored.Achievements[0].Id);
            Assert.True(restored.Achievements[0].IsAchieved);
            Assert.Equal(original.Achievements[0].UnlockTime, restored.Achievements[0].UnlockTime);
            Assert.Equal("ACH_SECRET", restored.Achievements[1].Id);
            Assert.False(restored.Achievements[1].IsAchieved);
            Assert.Null(restored.Achievements[1].UnlockTime);

            Assert.Equal(2, restored.Statistics.Count);
            Assert.Equal("kills", restored.Statistics[0].Id);
            Assert.Equal(42, restored.Statistics[0].Value);
            Assert.Equal("distance", restored.Statistics[1].Id);
            Assert.Equal(1234.5, restored.Statistics[1].Value);
        }

        [Fact]
        public void CsvRoundTripsAnIdThatNeedsQuotingAndEscaping()
        {
            var original = new GameSnapshot
            {
                AppId = 480,
                Timestamp = DateTime.UtcNow,
                Achievements = new List<AchievementSnapshotEntry>
                {
                    new() { Id = "ACH_\"WEIRD\", ID", IsAchieved = true },
                },
                Statistics = new List<StatisticSnapshotEntry>(),
            };

            var csv = GameSnapshotSerializer.ToCsv(original);
            var restored = GameSnapshotSerializer.FromCsv(csv);

            Assert.Single(restored.Achievements);
            Assert.Equal("ACH_\"WEIRD\", ID", restored.Achievements[0].Id);
        }

        [Fact]
        public void CsvIgnoresATrailingBlankLine()
        {
            var csv = GameSnapshotSerializer.ToCsv(Sample()) + "\r\n\r\n";

            var restored = GameSnapshotSerializer.FromCsv(csv);

            Assert.Equal(2, restored.Achievements.Count);
            Assert.Equal(2, restored.Statistics.Count);
        }

        [Fact]
        public void EmptySnapshotRoundTripsAsEmptyListsRatherThanNull()
        {
            var empty = new GameSnapshot { AppId = 10, Timestamp = DateTime.UtcNow };

            var fromJson = GameSnapshotSerializer.FromJson(GameSnapshotSerializer.ToJson(empty));
            var fromCsv = GameSnapshotSerializer.FromCsv(GameSnapshotSerializer.ToCsv(empty));

            Assert.NotNull(fromJson.Achievements);
            Assert.Empty(fromJson.Achievements);
            Assert.NotNull(fromJson.Statistics);
            Assert.Empty(fromJson.Statistics);

            Assert.NotNull(fromCsv.Achievements);
            Assert.Empty(fromCsv.Achievements);
            Assert.NotNull(fromCsv.Statistics);
            Assert.Empty(fromCsv.Statistics);
        }

        [Fact]
        public void FromJsonRejectsContentThatIsNotASnapshot()
        {
            Assert.ThrowsAny<Exception>(() => GameSnapshotSerializer.FromJson("null"));
            Assert.ThrowsAny<Exception>(() => GameSnapshotSerializer.FromJson("not json at all"));
        }

        [Theory]
        [InlineData("backup.json", SnapshotFileFormat.Json)]
        [InlineData("backup.csv", SnapshotFileFormat.Csv)]
        [InlineData("backup.CSV", SnapshotFileFormat.Csv)]
        [InlineData("backup.txt", SnapshotFileFormat.Json)]
        [InlineData("backup", SnapshotFileFormat.Json)]
        public void DetectFormatGoesByFileExtension(string path, SnapshotFileFormat expected)
        {
            Assert.Equal(expected, GameSnapshotSerializer.DetectFormat(path));
        }
    }
}
