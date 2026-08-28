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
using System.Threading;
using System.Threading.Tasks;
using static SAM.Core.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Core.Steam.Schema
{
    /// <summary>
    /// The achievement and statistic definitions Steam caches on disk for a game.
    /// </summary>
    public sealed class UserGameStatsSchema
    {
        private readonly List<AchievementDefinition> _Achievements;
        private readonly List<StatDefinition> _Stats;

        public UserGameStatsSchema(
            IEnumerable<AchievementDefinition> achievements,
            IEnumerable<StatDefinition> stats)
        {
            this._Achievements = achievements == null ? new() : new(achievements);
            this._Stats = stats == null ? new() : new(stats);
        }

        public IReadOnlyList<AchievementDefinition> Achievements => this._Achievements;

        public IReadOnlyList<StatDefinition> Stats => this._Stats;

        public static string GetSchemaPath(string installPath, uint appId)
        {
            if (string.IsNullOrEmpty(installPath) == true)
            {
                return null;
            }

            return Path.Combine(installPath, "appcache", "stats", _($"UserGameStatsSchema_{appId}.bin"));
        }

        /// <summary>
        /// Reads and parses the schema off the UI thread, returning <see langword="null"/>
        /// when Steam has not cached one for this game.
        /// </summary>
        public static async Task<UserGameStatsSchema> LoadAsync(
            string installPath,
            uint appId,
            string language,
            CancellationToken cancellationToken)
        {
            var path = GetSchemaPath(installPath, appId);
            if (path == null)
            {
                return null;
            }

            var kv = await KeyValue.LoadAsBinaryAsync(path, cancellationToken).ConfigureAwait(false);
            if (kv == null)
            {
                return null;
            }

            return await Task
                .Run(() => Parse(kv, appId, language), cancellationToken)
                .ConfigureAwait(false);
        }

        private static UserGameStatsSchema Parse(KeyValue kv, uint appId, string language)
        {
            var stats = kv[appId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (stats.Valid == false || stats.Children == null)
            {
                return null;
            }

            List<AchievementDefinition> achievementDefinitions = new();
            List<StatDefinition> statDefinitions = new();

            foreach (var stat in stats.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                switch (ReadStatType(stat))
                {
                    case APITypes.UserStatType.Invalid:
                    {
                        break;
                    }

                    case APITypes.UserStatType.Integer:
                    {
                        var id = stat["name"].AsString("");
                        statDefinitions.Add(new IntegerStatDefinition()
                        {
                            Id = id,
                            DisplayName = GetLocalizedString(stat["display"]["name"], language, id),
                            MinValue = stat["min"].AsInteger(int.MinValue),
                            MaxValue = stat["max"].AsInteger(int.MaxValue),
                            MaxChange = stat["maxchange"].AsInteger(0),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            SetByTrustedGameServer = stat["bSetByTrustedGS"].AsBoolean(false),
                            DefaultValue = stat["default"].AsInteger(0),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Float:
                    case APITypes.UserStatType.AverageRate:
                    {
                        var id = stat["name"].AsString("");
                        statDefinitions.Add(new FloatStatDefinition()
                        {
                            Id = id,
                            DisplayName = GetLocalizedString(stat["display"]["name"], language, id),
                            MinValue = stat["min"].AsFloat(float.MinValue),
                            MaxValue = stat["max"].AsFloat(float.MaxValue),
                            MaxChange = stat["maxchange"].AsFloat(0.0f),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            DefaultValue = stat["default"].AsFloat(0.0f),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Achievements:
                    case APITypes.UserStatType.GroupAchievements:
                    {
                        ReadAchievements(stat, language, achievementDefinitions);
                        break;
                    }

                    default:
                    {
                        // A type this build does not recognise -- a future Steam addition, or
                        // an unusual schema -- must not take the rest of an otherwise valid
                        // schema down with it. Skip the entry exactly like an explicitly
                        // Invalid one rather than aborting the whole parse.
                        break;
                    }
                }
            }

            return new UserGameStatsSchema(achievementDefinitions, statDefinitions);
        }

        private static APITypes.UserStatType ReadStatType(KeyValue stat)
        {
            APITypes.UserStatType type;

            // schema in the new format?
            var typeNode = stat["type"];
            if (typeNode.Valid == true && typeNode.Type == KeyValueType.String)
            {
                if (Enum.TryParse((string)typeNode.Value, true, out type) == false)
                {
                    type = APITypes.UserStatType.Invalid;
                }
            }
            else
            {
                type = APITypes.UserStatType.Invalid;
            }

            // schema in the old format?
            if (type == APITypes.UserStatType.Invalid)
            {
                var typeIntNode = stat["type_int"];
                var rawType = typeIntNode.Valid == true
                    ? typeIntNode.AsInteger(0)
                    : typeNode.AsInteger(0);
                type = (APITypes.UserStatType)rawType;
            }

            return type;
        }

        private static void ReadAchievements(KeyValue stat, string language, List<AchievementDefinition> definitions)
        {
            if (stat.Children == null)
            {
                return;
            }

            foreach (var bits in stat.Children.Where(
                b => string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
            {
                if (bits.Valid == false || bits.Children == null)
                {
                    continue;
                }

                foreach (var bit in bits.Children)
                {
                    var id = bit["name"].AsString("");
                    definitions.Add(new()
                    {
                        Id = id,
                        Name = GetLocalizedString(bit["display"]["name"], language, id),
                        Description = GetLocalizedString(bit["display"]["desc"], language, ""),
                        IconNormal = bit["display"]["icon"].AsString(""),
                        IconLocked = bit["display"]["icon_gray"].AsString(""),
                        IsHidden = bit["display"]["hidden"].AsBoolean(false),
                        Permission = bit["permission"].AsInteger(0),
                    });
                }
            }
        }

        private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
        {
            var name = kv[language].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            if (language != "english")
            {
                name = kv["english"].AsString("");
                if (string.IsNullOrEmpty(name) == false)
                {
                    return name;
                }
            }

            name = kv.AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            return defaultValue;
        }
    }
}
