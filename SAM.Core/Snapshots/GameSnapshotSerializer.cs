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
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SAM.Core.Snapshots
{
    public enum SnapshotFileFormat
    {
        Json,
        Csv,
    }

    /// <summary>
    /// Reads and writes a <see cref="GameSnapshot"/> as either JSON or a self-contained CSV,
    /// as plain strings -- neither format here ever touches a file directly, so both can be
    /// exercised without disk access.
    /// </summary>
    public static class GameSnapshotSerializer
    {
        private static readonly JsonSerializerOptions _JsonOptions = new()
        {
            WriteIndented = true,
        };

        /// <summary>Picks a format from a file's extension; anything other than ".csv" is JSON.</summary>
        public static SnapshotFileFormat DetectFormat(string path)
        {
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var extension = System.IO.Path.GetExtension(path);
            return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) == true
                ? SnapshotFileFormat.Csv
                : SnapshotFileFormat.Json;
        }

        public static string ToJson(GameSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return JsonSerializer.Serialize(snapshot, _JsonOptions);
        }

        public static GameSnapshot FromJson(string json)
        {
            if (string.IsNullOrEmpty(json) == true)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var snapshot = JsonSerializer.Deserialize<GameSnapshot>(json, _JsonOptions);
            if (snapshot == null)
            {
                throw new FormatException("The file did not contain a recognizable snapshot.");
            }

            snapshot.Achievements ??= new List<AchievementSnapshotEntry>();
            snapshot.Statistics ??= new List<StatisticSnapshotEntry>();
            return snapshot;
        }

        #region CSV

        // One flat, self-describing table rather than two: each row carries a RecordType and
        // leaves whichever columns do not apply to it blank. A single "Meta" row carries the
        // AppId and Timestamp that would otherwise have to repeat on every achievement and
        // statistic row.
        private const string _CsvHeader = "RecordType,Id,IsAchieved,UnlockTime,Value,AppId,Timestamp";

        public static string ToCsv(GameSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var lines = new List<string> { _CsvHeader };

            lines.Add(FormatCsvRow(
                "Meta", "", "", "", "",
                snapshot.AppId.ToString(CultureInfo.InvariantCulture),
                snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture)));

            foreach (var achievement in snapshot.Achievements ?? Enumerable.Empty<AchievementSnapshotEntry>())
            {
                lines.Add(FormatCsvRow(
                    "Achievement",
                    achievement.Id ?? "",
                    achievement.IsAchieved.ToString(CultureInfo.InvariantCulture),
                    achievement.UnlockTime?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                    "", "", ""));
            }

            foreach (var statistic in snapshot.Statistics ?? Enumerable.Empty<StatisticSnapshotEntry>())
            {
                lines.Add(FormatCsvRow(
                    "Statistic",
                    statistic.Id ?? "",
                    "", "",
                    statistic.Value.ToString("R", CultureInfo.InvariantCulture),
                    "", ""));
            }

            return string.Join("\r\n", lines);
        }

        public static GameSnapshot FromCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv) == true)
            {
                throw new ArgumentNullException(nameof(csv));
            }

            var snapshot = new GameSnapshot();
            var rows = ParseCsv(csv);

            // rows[0], if present, is the header; data starts at rows[1].
            for (var i = 1; i < rows.Count; i++)
            {
                var fields = rows[i];
                if (fields.Count == 0 || string.IsNullOrEmpty(fields[0]) == true)
                {
                    continue;
                }

                string Field(int index) => index < fields.Count ? fields[index] : "";

                switch (fields[0])
                {
                    case "Meta":
                        if (uint.TryParse(Field(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == true)
                        {
                            snapshot.AppId = appId;
                        }
                        if (DateTime.TryParse(Field(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) == true)
                        {
                            snapshot.Timestamp = timestamp;
                        }
                        break;

                    case "Achievement":
                        snapshot.Achievements.Add(new AchievementSnapshotEntry
                        {
                            Id = Field(1),
                            IsAchieved = bool.TryParse(Field(2), out var isAchieved) == true && isAchieved,
                            UnlockTime = DateTime.TryParse(Field(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var unlockTime) == true
                                ? unlockTime
                                : (DateTime?)null,
                        });
                        break;

                    case "Statistic":
                        snapshot.Statistics.Add(new StatisticSnapshotEntry
                        {
                            Id = Field(1),
                            Value = double.TryParse(Field(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) == true ? value : 0d,
                        });
                        break;

                    // An unrecognized RecordType (e.g. from a newer version of this tool) is
                    // skipped rather than treated as a parse failure.
                }
            }

            return snapshot;
        }

        private static string FormatCsvRow(params string[] fields) => string.Join(",", fields.Select(EscapeCsvField));

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value) == true)
            {
                return "";
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// A minimal RFC 4180-style parser: quoted fields may contain commas, quotes (doubled),
        /// and embedded line breaks, so rows cannot simply be split on newline characters ahead
        /// of splitting fields on commas.
        /// </summary>
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (inQuotes == true)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    field.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;

                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;

                    case '\r':
                        i++;
                        break;

                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        i++;
                        break;

                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            // A final line with no trailing newline still has a field/row to flush.
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows.Where(r => (r.Count == 1 && r[0].Length == 0) == false).ToList();
        }

        #endregion
    }
}
