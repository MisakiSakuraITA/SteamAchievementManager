using System;
using System.IO;
using System.Text;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Drives <see cref="KeyValue.ReadAsBinary(Stream)"/> directly against hand-built byte
    /// streams in the format it reads: one byte of <see cref="KeyValueType"/>, a null-terminated
    /// UTF-8 name, an optional value, and a terminating <see cref="KeyValueType.End"/> per level.
    /// </summary>
    public class KeyValueBinaryReadTests
    {
        private static void WriteName(Stream stream, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(0);
        }

        /// <summary>
        /// <paramref name="levels"/> nested <see cref="KeyValueType.None"/> entries, an Int32
        /// leaf at the bottom, then one closing <see cref="KeyValueType.End"/> per level
        /// (including the root). A well-formed stream for exactly that many levels of nesting.
        /// </summary>
        private static byte[] BuildNested(int levels)
        {
            using MemoryStream stream = new();

            for (var i = 0; i < levels; i++)
            {
                stream.WriteByte((byte)KeyValueType.None);
                WriteName(stream, "level" + i);
            }

            stream.WriteByte((byte)KeyValueType.Int32);
            WriteName(stream, "leaf");
            stream.Write(BitConverter.GetBytes(1), 0, 4);

            for (var i = 0; i <= levels; i++)
            {
                stream.WriteByte((byte)KeyValueType.End);
            }

            return stream.ToArray();
        }

        /// <summary>Just the opening markers, with no leaf and no closings at all.</summary>
        private static byte[] BuildUnclosedNesting(int levels)
        {
            using MemoryStream stream = new();
            for (var i = 0; i < levels; i++)
            {
                stream.WriteByte((byte)KeyValueType.None);
                WriteName(stream, "level" + i);
            }
            return stream.ToArray();
        }

        [Fact]
        public void ReasonablyNestedDataParsesSuccessfully()
        {
            using MemoryStream stream = new(BuildNested(10), false);
            KeyValue root = new();

            Assert.True(root.ReadAsBinary(stream));

            var node = root;
            for (var i = 0; i < 10; i++)
            {
                Assert.NotNull(node.Children);
                node = Assert.Single(node.Children);
                Assert.Equal("level" + i, node.Name);
            }
        }

        [Fact]
        public void NestingWithinTheCapStillParsesSuccessfully()
        {
            // Comfortably past anything a real schema uses, but under the cap.
            using MemoryStream stream = new(BuildNested(100), false);
            KeyValue root = new();

            Assert.True(root.ReadAsBinary(stream));
        }

        [Fact]
        public void ExcessiveNestingFailsCleanlyInsteadOfOverflowingTheStack()
        {
            // Comfortably past the 128-level cap. Reaching this assertion at all is part of
            // what is being tested -- unbounded recursion here would never return.
            using MemoryStream stream = new(BuildUnclosedNesting(5000), false);
            KeyValue root = new();

            var result = root.ReadAsBinary(stream);

            Assert.False(result);
        }

        [Fact]
        public void TruncatedDataFailsCleanly()
        {
            var data = BuildNested(3);
            using MemoryStream stream = new(data[..(data.Length - 2)], false);
            KeyValue root = new();

            Assert.False(root.ReadAsBinary(stream));
        }
    }
}
