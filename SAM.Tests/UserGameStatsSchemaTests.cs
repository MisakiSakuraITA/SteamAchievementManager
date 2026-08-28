using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SAM.Core.Steam;
using SAM.Core.Steam.Schema;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// <see cref="UserGameStatsSchema"/>'s parser is exercised directly against a hand-built
    /// <see cref="KeyValue"/> tree, bypassing the binary reader entirely -- <c>Parse</c> is
    /// private, so these reach it with reflection the same way the rest of the suite reaches
    /// other implementation details that have no public seam.
    /// </summary>
    public class UserGameStatsSchemaTests
    {
        private static readonly MethodInfo _Parse =
            typeof(UserGameStatsSchema).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);

        private static UserGameStatsSchema Parse(KeyValue root, uint appId, string language = "english")
        {
            return (UserGameStatsSchema)_Parse.Invoke(null, new object[] { root, appId, language });
        }

        private static KeyValue IntStat(string id, string typeName = "int")
        {
            return new KeyValue
            {
                Name = id,
                Valid = true,
                Children = new List<KeyValue>
                {
                    new() { Name = "name", Type = KeyValueType.String, Valid = true, Value = id },
                    new() { Name = "type", Type = KeyValueType.String, Valid = true, Value = typeName },
                },
            };
        }

        private static KeyValue BuildRoot(uint appId, IEnumerable<KeyValue> statEntries)
        {
            KeyValue statsNode = new()
            {
                Name = "stats",
                Valid = true,
                Children = statEntries.ToList(),
            };
            KeyValue appNode = new()
            {
                Name = appId.ToString(),
                Valid = true,
                Children = new List<KeyValue> { statsNode },
            };
            return new KeyValue { Children = new List<KeyValue> { appNode } };
        }

        [Fact]
        public void AnUnrecognisedStatTypeIsSkippedRatherThanAbortingTheWholeSchema()
        {
            // A raw type code this build does not know -- a future Steam addition, or an
            // unusual schema -- sitting between two perfectly ordinary integer stats.
            KeyValue unknown = new()
            {
                Name = "mystery",
                Valid = true,
                Children = new List<KeyValue>
                {
                    new() { Name = "type", Type = KeyValueType.Int32, Valid = true, Value = 9999 },
                },
            };

            var root = BuildRoot(480, new[] { IntStat("kills"), unknown, IntStat("wins") });

            var schema = Parse(root, 480);

            Assert.NotNull(schema);
            Assert.Equal(2, schema.Stats.Count);
            Assert.Contains(schema.Stats, s => s.Id == "kills");
            Assert.Contains(schema.Stats, s => s.Id == "wins");
        }

        [Fact]
        public void AnInvalidStatEntryIsSkippedTheSameWay()
        {
            KeyValue invalid = new()
            {
                Name = "broken",
                Valid = true,
                Children = new List<KeyValue>(), // no "type" node at all
            };

            var root = BuildRoot(480, new[] { IntStat("kills"), invalid });

            var schema = Parse(root, 480);

            Assert.NotNull(schema);
            Assert.Single(schema.Stats);
            Assert.Equal("kills", schema.Stats[0].Id);
        }

        [Fact]
        public void MissingStatsNodeYieldsNullRatherThanThrowing()
        {
            KeyValue appNode = new() { Name = "480", Valid = true, Children = new List<KeyValue>() };
            KeyValue root = new() { Children = new List<KeyValue> { appNode } };

            var schema = Parse(root, 480);

            Assert.Null(schema);
        }
    }
}
