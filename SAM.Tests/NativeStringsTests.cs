using System.Runtime.InteropServices;
using System.Text;
using SAM.API;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises the IntPtr overloads of NativeStrings.PointerToString -- the only ones
    /// callable without writing unsafe pointer code, since this project does not otherwise
    /// need <c>&lt;AllowUnsafeBlocks&gt;</c>. They cover the same bounds-checking logic the
    /// pointer overloads delegate to.
    /// </summary>
    public class NativeStringsTests
    {
        [Fact]
        public void BoundedReaderStopsAtANullTerminatorWellInsideTheBound()
        {
            var bytes = Encoding.UTF8.GetBytes("hello");
            var buffer = Marshal.AllocHGlobal(32);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                Marshal.WriteByte(buffer, bytes.Length, 0);

                var result = NativeStrings.PointerToString(buffer, 32);

                Assert.Equal("hello", result);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void BoundedReaderStopsExactlyAtTheLengthWithoutReadingPastIt()
        {
            const int length = 16;
            // length + 1 bytes: the first `length` are the payload, with no null terminator
            // anywhere inside it. The extra byte one past the declared length is set to a
            // value that would visibly change the result if it were ever read, proving the
            // reader genuinely stops at the bound instead of reading one byte beyond it --
            // the exact off-by-one M-07 fixed.
            var buffer = Marshal.AllocHGlobal(length + 1);
            try
            {
                for (var i = 0; i < length; i++)
                {
                    Marshal.WriteByte(buffer, i, (byte)'A');
                }
                Marshal.WriteByte(buffer, length, (byte)'Z');

                var result = NativeStrings.PointerToString(buffer, length);

                Assert.Equal(new string('A', length), result);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void BoundedReaderOnAZeroLengthBufferReturnsEmptyWithoutReadingAnything()
        {
            // A one-byte allocation whose only byte is deliberately non-zero: length == 0
            // must short-circuit to string.Empty without ever dereferencing it.
            var buffer = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(buffer, 0, (byte)'A');

                var result = NativeStrings.PointerToString(buffer, 0);

                Assert.Equal(string.Empty, result);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void UnboundedReaderStopsAtTheEightKilobyteCapOnAnUnterminatedBuffer()
        {
            const int cap = 8 * 1024;
            const int size = cap + 256;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < size; i++)
                {
                    Marshal.WriteByte(buffer, i, (byte)'A'); // never zero -- no terminator anywhere
                }

                var result = NativeStrings.PointerToString(buffer);

                Assert.Equal(cap, result.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void UnboundedReaderStopsAtANullTerminatorWellUnderTheCap()
        {
            var bytes = Encoding.UTF8.GetBytes("a short native string");
            var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                Marshal.WriteByte(buffer, bytes.Length, 0);

                var result = NativeStrings.PointerToString(buffer);

                Assert.Equal("a short native string", result);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
