using System;
using System.Text;

namespace fNbt {
    // Shared string-encoding logic. Java flavors write modified UTF-8 (CESU-8 astral pairs,
    // overlong NUL, lone surrogates preserved); Bedrock flavors write standard UTF-8.
    // Decoding is lenient and flavor-independent: it accepts the union of both encodings and
    // throws NbtFormatException on truly malformed data instead of substituting U+FFFD.
    internal static class NbtStringCodec {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);


        // Printable ASCII (no NUL) is byte-identical in every encoding handled here and
        // dominates real tag names and values, so both directions fast-path it on one scan.
        public static bool IsAsciiNoNul(string value) {
#if NET8_0_OR_GREATER
            return !value.AsSpan().ContainsAnyExceptInRange('\u0001', '\u007F');
#else
            foreach (char c in value) {
                if (c == '\0' || c > '\u007F') return false;
            }
            return true;
#endif
        }


        static bool IsAscii(byte[] buffer, int offset, int count) {
#if NET8_0_OR_GREATER
            return !buffer.AsSpan(offset, count).ContainsAnyExceptInRange((byte)0x00, (byte)0x7F);
#else
            for (int i = offset; i < offset + count; i++) {
                if (buffer[i] > 0x7F) return false;
            }
            return true;
#endif
        }


        // True if any surrogate code unit is present, paired or not
        public static bool HasSurrogates(string value) {
#if NET8_0_OR_GREATER
            return value.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') >= 0;
#else
            foreach (char c in value) {
                if (c >= '\uD800' && c <= '\uDFFF') return true;
            }
            return false;
#endif
        }


        // True if the string encodes differently in modified UTF-8 than in standard UTF-8:
        // an embedded NUL or anything in the surrogate range.
        public static bool NeedsModifiedEncoding(string value) {
#if NET8_0_OR_GREATER
            ReadOnlySpan<char> span = value.AsSpan();
            return span.IndexOfAnyInRange('\uD800', '\uDFFF') >= 0 || span.IndexOf('\0') >= 0;
#else
            foreach (char c in value) {
                if (c == '\0' || (c >= '\uD800' && c <= '\uDFFF')) return true;
            }
            return false;
#endif
        }


        public static long GetByteCount(string value, bool modifiedUtf8) {
            if (modifiedUtf8 && NeedsModifiedEncoding(value)) {
                return GetModifiedByteCount(value);
            }
            try {
                return StrictUtf8.GetByteCount(value);
            } catch (EncoderFallbackException ex) {
                throw NbtFormatException.StringEncoding(ex);
            }
        }


        // The count is a long so that even a maximum-length string cannot overflow it
        public static long GetModifiedByteCount(string value) {
            long count = 0;
            foreach (char c in value) {
                if (c == '\0') count += 2;
                else if (c < 0x80) count += 1;
                else if (c < 0x800) count += 2;
                else count += 3;
            }
            return count;
        }


        // Encodes one UTF-16 code unit into the buffer; the caller guarantees 3 free bytes.
        // Surrogate halves each become 3 bytes (CESU-8), and NUL becomes the overlong C0 80.
        public static int EncodeModifiedChar(char c, byte[] buffer, int pos) {
            if (c != '\0' && c < 0x80) {
                buffer[pos++] = (byte)c;
            } else if (c < 0x800) {
                buffer[pos++] = (byte)(0xC0 | (c >> 6));
                buffer[pos++] = (byte)(0x80 | (c & 0x3F));
            } else {
                buffer[pos++] = (byte)(0xE0 | (c >> 12));
                buffer[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                buffer[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            return pos;
        }


        public static string Decode(byte[] buffer, int offset, int count) {
            if (count == 0) return "";
            if (IsAscii(buffer, offset, count)) {
                return Encoding.ASCII.GetString(buffer, offset, count);
            }
            // 0xC0 (overlong NUL) and 0xED (surrogate code units) are the only lead bytes where
            // valid Java output differs from standard UTF-8, so their absence means the
            // framework decoder gives the same answer.
            if (IsPlainUtf8(buffer, offset, count)) {
                try {
                    return StrictUtf8.GetString(buffer, offset, count);
                } catch (DecoderFallbackException ex) {
                    throw new NbtFormatException("String data is not valid UTF-8.", ex);
                }
            }
            return DecodeLenient(buffer, offset, count);
        }


        static bool IsPlainUtf8(byte[] buffer, int offset, int count) {
#if NET8_0_OR_GREATER
            return buffer.AsSpan(offset, count).IndexOfAny((byte)0xC0, (byte)0xED) < 0;
#else
            for (int i = offset; i < offset + count; i++) {
                byte b = buffer[i];
                if (b == 0xC0 || b == 0xED) return false;
            }
            return true;
#endif
        }


        // Accepts the union of standard UTF-8 and Java's modified UTF-8: 4-byte astral
        // sequences, CESU-8 surrogate pairs, the overlong NUL, and lone surrogates all decode.
        static string DecodeLenient(byte[] buffer, int offset, int count) {
#if NET8_0_OR_GREATER
            // charCount never exceeds count, so renting sidesteps a second full-size
            // allocation. A huge string would otherwise drop a throwaway char[] on the LOH.
            char[] chars = System.Buffers.ArrayPool<char>.Shared.Rent(count);
            try {
                return new string(chars, 0, DecodeLenientCore(buffer, offset, count, chars));
            } finally {
                System.Buffers.ArrayPool<char>.Shared.Return(chars);
            }
#else
            char[] chars = new char[count];
            return new string(chars, 0, DecodeLenientCore(buffer, offset, count, chars));
#endif
        }


        static int DecodeLenientCore(byte[] buffer, int offset, int count, char[] chars) {
            int charCount = 0;
            int i = offset;
            int end = offset + count;
            while (i < end) {
                byte b0 = buffer[i++];
                if (b0 < 0x80) {
                    chars[charCount++] = (char)b0;
                } else if ((b0 & 0xE0) == 0xC0) {
                    if (i >= end) throw Malformed();
                    byte b1 = buffer[i++];
                    if ((b1 & 0xC0) != 0x80) throw Malformed();
                    int unit = ((b0 & 0x1F) << 6) | (b1 & 0x3F);
                    // Overlong forms are invalid, except Java's C0 80 for U+0000
                    if (unit < 0x80 && !(b0 == 0xC0 && b1 == 0x80)) throw Malformed();
                    chars[charCount++] = (char)unit;
                } else if ((b0 & 0xF0) == 0xE0) {
                    if (i + 1 >= end) throw Malformed();
                    byte b1 = buffer[i++];
                    byte b2 = buffer[i++];
                    if ((b1 & 0xC0) != 0x80 || (b2 & 0xC0) != 0x80) throw Malformed();
                    int unit = ((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F);
                    if (unit < 0x800) throw Malformed();
                    // Surrogate code units pass through: CESU-8 pairs pair up naturally,
                    // and Java's lone surrogates survive a round trip
                    chars[charCount++] = (char)unit;
                } else if ((b0 & 0xF8) == 0xF0) {
                    if (i + 2 >= end) throw Malformed();
                    byte b1 = buffer[i++];
                    byte b2 = buffer[i++];
                    byte b3 = buffer[i++];
                    if ((b1 & 0xC0) != 0x80 || (b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80) throw Malformed();
                    int codePoint = ((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                    if (codePoint < 0x10000 || codePoint > 0x10FFFF) throw Malformed();
                    codePoint -= 0x10000;
                    chars[charCount++] = (char)(0xD800 | (codePoint >> 10));
                    chars[charCount++] = (char)(0xDC00 | (codePoint & 0x3FF));
                } else {
                    throw Malformed();
                }
            }
            return charCount;
        }


        static NbtFormatException Malformed() {
            return new NbtFormatException("String data is not valid UTF-8 or modified UTF-8.");
        }
    }
}
