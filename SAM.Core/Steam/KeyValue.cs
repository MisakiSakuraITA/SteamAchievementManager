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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.IO;

namespace SAM.Core.Steam
{
    public class KeyValue
    {
        private const long _MaximumSchemaLength = 64 * 1024 * 1024;

        // Real schemas nest a handful of levels deep (app id, "stats", each entry, "display",
        // language). This is far beyond any of that and exists only to turn a maliciously or
        // corruptly deep chain of None nodes into a clean parse failure instead of a
        // StackOverflowException, which .NET cannot catch and would take the whole process
        // down with it.
        private const int _MaximumNestingDepth = 128;

        private static readonly KeyValue _Invalid = new();
        public string Name = "<root>";
        public KeyValueType Type = KeyValueType.None;
        public object Value;
        public bool Valid;

        public List<KeyValue> Children = null;

        public KeyValue this[string key]
        {
            get
            {
                if (this.Children == null)
                {
                    return _Invalid;
                }

                var child = this.Children.SingleOrDefault(
                    c => string.Compare(c.Name, key, StringComparison.InvariantCultureIgnoreCase) == 0);

                if (child == null)
                {
                    return _Invalid;
                }

                return child;
            }
        }

        public string AsString(string defaultValue)
        {
            if (this.Valid == false)
            {
                return defaultValue;
            }

            if (this.Value == null)
            {
                return defaultValue;
            }

            return this.Value.ToString();
        }

        public int AsInteger(int defaultValue)
        {
            if (this.Valid == false)
            {
                return defaultValue;
            }

            switch (this.Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                {
                    return int.TryParse((string)this.Value, out int value) == false
                        ? defaultValue
                        : value;
                }

                case KeyValueType.Int32:
                {
                    return (int)this.Value;
                }

                case KeyValueType.Float32:
                {
                    return (int)((float)this.Value);
                }

                case KeyValueType.UInt64:
                {
                    return (int)((ulong)this.Value & 0xFFFFFFFF);
                }
            }

            return defaultValue;
        }

        public float AsFloat(float defaultValue)
        {
            if (this.Valid == false)
            {
                return defaultValue;
            }

            switch (this.Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                {
                    return float.TryParse((string)this.Value, out float value) == false
                        ? defaultValue
                        : value;
                }

                case KeyValueType.Int32:
                {
                    return (int)this.Value;
                }

                case KeyValueType.Float32:
                {
                    return (float)this.Value;
                }

                case KeyValueType.UInt64:
                {
                    return (ulong)this.Value & 0xFFFFFFFF;
                }
            }

            return defaultValue;
        }

        public bool AsBoolean(bool defaultValue)
        {
            if (this.Valid == false)
            {
                return defaultValue;
            }

            switch (this.Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                {
                    return int.TryParse((string)this.Value, out int value) == false
                        ? defaultValue
                        : value != 0;
                }

                case KeyValueType.Int32:
                {
                    return ((int)this.Value) != 0;
                }

                case KeyValueType.Float32:
                {
                    return ((int)((float)this.Value)) != 0;
                }

                case KeyValueType.UInt64:
                {
                    return ((ulong)this.Value) != 0;
                }
            }

            return defaultValue;
        }

        public override string ToString()
        {
            if (this.Valid == false)
            {
                return "<invalid>";
            }

            if (this.Type == KeyValueType.None)
            {
                return this.Name;
            }

            return $"{this.Name} = {this.Value}";
        }

        /// <summary>
        /// Reads and parses a binary key-values file without blocking the caller: the read
        /// is asynchronous and the parse runs on the thread pool. Schemas for large games
        /// run to several megabytes, which is long enough to stall the UI noticeably.
        /// </summary>
        public static async Task<KeyValue> LoadAsBinaryAsync(string path, CancellationToken cancellationToken)
        {
            var data = await AsyncFile
                .TryReadAllBytesAsync(path, _MaximumSchemaLength, cancellationToken)
                .ConfigureAwait(false);
            if (data == null)
            {
                return null;
            }

            return await Task.Run(() => ParseBinary(data), cancellationToken).ConfigureAwait(false);
        }

        private static KeyValue ParseBinary(byte[] data)
        {
            try
            {
                using (MemoryStream input = new(data, false))
                {
                    KeyValue kv = new();
                    if (kv.ReadAsBinary(input) == false)
                    {
                        return null;
                    }
                    return kv;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Only the outermost call can meaningfully ask whether the whole stream was
        /// consumed -- a nested node's own read always stops well short of the end of the
        /// buffer, at whatever byte its enclosing level continues from. That check belongs
        /// here, once, rather than at every recursion depth.
        /// </summary>
        public bool ReadAsBinary(Stream input)
        {
            return this.ReadAsBinary(input, 0) && input.Position == input.Length;
        }

        private bool ReadAsBinary(Stream input, int depth)
        {
            if (depth > _MaximumNestingDepth)
            {
                return false;
            }

            this.Children = new();
            try
            {
                while (true)
                {
                    var type = (KeyValueType)input.ReadValueU8();

                    if (type == KeyValueType.End)
                    {
                        break;
                    }

                    KeyValue current = new()
                    {
                        Type = type,
                        Name = input.ReadStringUnicode(),
                    };

                    switch (type)
                    {
                        case KeyValueType.None:
                        {
                            // The depth limit only bites here, but a failure at any depth has
                            // to unwind every level above it rather than being treated as an
                            // empty child -- otherwise parsing would carry on from a stream
                            // position the format never actually reached.
                            if (current.ReadAsBinary(input, depth + 1) == false)
                            {
                                return false;
                            }
                            break;
                        }

                        case KeyValueType.String:
                        {
                            current.Valid = true;
                            current.Value = input.ReadStringUnicode();
                            break;
                        }

                        case KeyValueType.WideString:
                        {
                            throw new FormatException("wstring is unsupported");
                        }

                        case KeyValueType.Int32:
                        {
                            current.Valid = true;
                            current.Value = input.ReadValueS32();
                            break;
                        }

                        case KeyValueType.UInt64:
                        {
                            current.Valid = true;
                            current.Value = input.ReadValueU64();
                            break;
                        }

                        case KeyValueType.Float32:
                        {
                            current.Valid = true;
                            current.Value = input.ReadValueF32();
                            break;
                        }

                        case KeyValueType.Color:
                        {
                            current.Valid = true;
                            current.Value = input.ReadValueU32();
                            break;
                        }

                        case KeyValueType.Pointer:
                        {
                            current.Valid = true;
                            current.Value = input.ReadValueU32();
                            break;
                        }

                        default:
                        {
                            throw new FormatException();
                        }
                    }

                    if (input.Position >= input.Length)
                    {
                        throw new FormatException();
                    }

                    this.Children.Add(current);
                }

                this.Valid = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads and parses one of Steam's small, human-readable text config files (e.g.
        /// <c>config/loginusers.vdf</c>) -- unlike <see cref="LoadAsBinaryAsync"/>, these are
        /// a handful of kilobytes at most and read synchronously without a second thought.
        /// Returns <see langword="null"/> for a missing file or anything this parser cannot
        /// make sense of, exactly like the binary reader.
        /// </summary>
        public static KeyValue LoadAsText(string path)
        {
            try
            {
                if (File.Exists(path) == false)
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                if (bytes.LongLength > _MaximumSchemaLength)
                {
                    return null;
                }

                // Encoding.UTF8.GetString does not strip a byte-order mark on its own; left
                // in place it would otherwise tokenize as a bogus leading value.
                const char byteOrderMark = (char)0xFEFF;
                var text = Encoding.UTF8.GetString(bytes).TrimStart(byteOrderMark);
                return ParseText(text);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Internal rather than private purely so it can be driven directly by tests without disk access.</summary>
        internal static KeyValue ParseText(string text)
        {
            try
            {
                var tokens = TokenizeText(text);
                var position = 0;
                KeyValue root = new() { Valid = true, Children = new() };
                ReadTextChildren(tokens, ref position, root, 0);
                return root;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Splits Valve's text key-values grammar into quoted strings, bare (unquoted) tokens,
        /// and the two brace characters, discarding <c>//</c> line comments and whitespace.
        /// </summary>
        private static List<string> TokenizeText(string text)
        {
            List<string> tokens = new();
            var i = 0;
            var length = text.Length;

            while (i < length)
            {
                var c = text[i];

                if (char.IsWhiteSpace(c) == true)
                {
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < length && text[i + 1] == '/')
                {
                    while (i < length && text[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                if (c == '{' || c == '}')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    StringBuilder value = new();
                    while (i < length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < length)
                        {
                            switch (text[i + 1])
                            {
                                case 'n': value.Append('\n'); i += 2; continue;
                                case 't': value.Append('\t'); i += 2; continue;
                                case '\\': value.Append('\\'); i += 2; continue;
                                case '"': value.Append('"'); i += 2; continue;
                            }
                        }
                        value.Append(text[i]);
                        i++;
                    }
                    i++; // closing quote, if the string was ever terminated
                    tokens.Add(value.ToString());
                    continue;
                }

                var start = i;
                while (i < length && char.IsWhiteSpace(text[i]) == false && text[i] != '{' && text[i] != '}')
                {
                    i++;
                }
                tokens.Add(text.Substring(start, i - start));
            }

            return tokens;
        }

        /// <summary>
        /// Reads a sequence of "name value" and "name { ... }" pairs into <paramref name="parent"/>,
        /// stopping at a matching close brace (or the end of the token stream, for the
        /// implicit top-level block). Depth-bounded for the same reason as the binary reader:
        /// a malformed or adversarial file must fail cleanly rather than overflow the stack.
        /// </summary>
        private static void ReadTextChildren(List<string> tokens, ref int position, KeyValue parent, int depth)
        {
            if (depth > _MaximumNestingDepth)
            {
                throw new FormatException("Nested too deeply.");
            }

            while (position < tokens.Count)
            {
                var name = tokens[position];
                if (name == "}")
                {
                    position++;
                    return;
                }

                position++;
                if (position >= tokens.Count)
                {
                    throw new FormatException("Unexpected end of input.");
                }

                var next = tokens[position];
                if (next == "{")
                {
                    position++;
                    KeyValue child = new() { Name = name, Valid = true, Children = new() };
                    ReadTextChildren(tokens, ref position, child, depth + 1);
                    parent.Children.Add(child);
                }
                else if (next == "}")
                {
                    throw new FormatException("Expected a value or a block.");
                }
                else
                {
                    position++;
                    parent.Children.Add(new KeyValue { Name = name, Type = KeyValueType.String, Valid = true, Value = next });
                }
            }
        }
    }
}
