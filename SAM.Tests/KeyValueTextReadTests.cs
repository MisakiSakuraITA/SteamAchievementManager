using System.Text;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Drives <see cref="KeyValue.ParseText"/> directly against hand-written text, in the
    /// shape of Steam's own <c>config/loginusers.vdf</c>.
    /// </summary>
    public class KeyValueTextReadTests
    {
        private const string _Sample = @"
""users""
{
	""76561197960287930""
	{
		""AccountName""		""exampleaccount""
		""PersonaName""		""Example Person""
		""MostRecent""		""1""
	}
	""76561197960287931""
	{
		""AccountName""		""anotheraccount""
		""PersonaName""		""Another One""
		""MostRecent""		""0""
	}
}
";

        [Fact]
        public void ReadsANestedValueByChainedIndexer()
        {
            var kv = KeyValue.ParseText(_Sample);

            Assert.NotNull(kv);
            Assert.Equal("Example Person", kv["users"]["76561197960287930"]["PersonaName"].AsString(null));
            Assert.Equal("Another One", kv["users"]["76561197960287931"]["PersonaName"].AsString(null));
        }

        [Fact]
        public void LookupIsCaseInsensitiveOnKeyNames()
        {
            var kv = KeyValue.ParseText(_Sample);

            Assert.Equal("Example Person", kv["USERS"]["76561197960287930"]["personaname"].AsString(null));
        }

        [Fact]
        public void MissingSteamIdYieldsAnInvalidNodeRatherThanThrowing()
        {
            var kv = KeyValue.ParseText(_Sample);

            Assert.False(kv["users"]["11111111111111111"].Valid);
            Assert.Null(kv["users"]["11111111111111111"]["PersonaName"].AsString(null));
        }

        [Fact]
        public void LineCommentsAreIgnored()
        {
            const string text = @"
""users""
{
	// this whole account is a comment away from mattering
	""76561197960287930""
	{
		""PersonaName""		""Commented"" // trailing comment
	}
}
";
            var kv = KeyValue.ParseText(text);

            Assert.Equal("Commented", kv["users"]["76561197960287930"]["PersonaName"].AsString(null));
        }

        [Fact]
        public void EscapedQuotesInsideAValueAreUnescaped()
        {
            const string text = "\"users\"\n{\n\t\"1\"\n\t{\n\t\t\"PersonaName\"\t\"Say \\\"hi\\\"\"\n\t}\n}\n";

            var kv = KeyValue.ParseText(text);

            Assert.Equal("Say \"hi\"", kv["users"]["1"]["PersonaName"].AsString(null));
        }

        [Fact]
        public void EmptyInputParsesToAnEmptyValidRoot()
        {
            var kv = KeyValue.ParseText("");

            Assert.NotNull(kv);
            Assert.True(kv.Valid);
            Assert.Empty(kv.Children);
        }

        [Fact]
        public void AnUnbalancedClosingBraceFailsCleanlyRatherThanThrowing()
        {
            var kv = KeyValue.ParseText("\"users\"\n{\n\t\"1\"\n\t{\n\t}\n}\n}\n");

            // The stray trailing '}' has nothing left to close; the top-level reader simply
            // has no more names to read past it once its own (implicit) block is done, so
            // this is not expected to throw -- it is exercised here to document that.
            Assert.NotNull(kv);
        }

        [Fact]
        public void AKeyWithNoValueOrBlockFailsCleanlyRatherThanThrowing()
        {
            var kv = KeyValue.ParseText("\"users\"\n{\n\t\"orphaned-key\"\n}\n");

            Assert.Null(kv);
        }

        [Fact]
        public void NestingBeyondTheDepthLimitFailsCleanlyRatherThanOverflowingTheStack()
        {
            const int levels = 1000;
            StringBuilder text = new();
            for (var i = 0; i < levels; i++)
            {
                text.Append("\"level").Append(i).Append("\"\n{\n");
            }
            text.Append("\"leaf\"\t\"1\"\n");
            for (var i = 0; i < levels; i++)
            {
                text.Append("}\n");
            }

            var kv = KeyValue.ParseText(text.ToString());

            Assert.Null(kv);
        }
    }
}
