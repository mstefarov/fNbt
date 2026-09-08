using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace fNbt {
    // Reads SNBT text into a tag tree. The grammar is Minecraft Java 1.21.5's, widened to understand
    // older game versions and common tools. For example:
    // - an unquoted token that is not a modern number falls back to the old (1.12 to 1.21.4) reading,
    //   which made it a string or an infinity
    // - suffixed NaN and Infinity read as numbers
    // - empty quoted keys are allowed
    // - mixed lists become the wrapper compounds the game stores on disk.
    internal sealed class SnbtParser {
        readonly string text;
        int pos;

        // Element start positions of the lists being read, innermost last, for depth errors
        List<int>? elementStarts;


        SnbtParser(string text, int start) {
            this.text = text;
            pos = start;
        }


        // The whole text is one value, plus surrounding whitespace
        public static NbtTag ParseWhole(string text) {
            SnbtParser parser = new SnbtParser(text, 0);
            parser.SkipByteOrderMark();
            NbtTag tag = parser.ReadValue(NbtTag.MaxDepth);
            parser.SkipWhitespace();
            if (parser.pos < text.Length) throw parser.Error("Unexpected trailing data");
            return tag;
        }


        // One value starting at index; whatever follows it is left alone
        public static NbtTag ParseValue(string text, int index, out int charsConsumed) {
            SnbtParser parser = new SnbtParser(text, index);
            parser.SkipByteOrderMark();
            NbtTag tag = parser.ReadValue(NbtTag.MaxDepth);
            charsConsumed = parser.pos - index;
            return tag;
        }


        #region Values

        NbtTag ReadValue(int depthBudget) {
            SkipWhitespace();
            if (pos >= text.Length) throw Error("Expected a value");
            switch (text[pos]) {
                case '{':
                    return ReadCompound(depthBudget);
                case '[':
                    return ReadListOrArray(depthBudget);
                case '"':
                case '\'':
                    return new NbtString(ReadQuotedString());
                default:
                    return ReadUnquoted(depthBudget);
            }
        }


        NbtTag ReadUnquoted(int depthBudget) {
            int start = pos;
            if (CanStartNumber(text[pos])) {
                numberSpaced = false;
                NbtTag? number = TryReadNumber(NbtTagType.Int);
                if (number != null && !RunsIntoToken()) return number;
                // The whitespace the grammar allows inside a number can carry it into the next word,
                // which might cause problems like:
                // - "1.5 foo" took the f as a suffix
                // - "0 1" read as a leading zero
                // - "1.5 e999" overflowed
                // Read it again without that whitespace so the word stays separate.
                // A number that runs straight into more token characters (1abc, 1.5.2, 1bx) is
                // an old-era unquoted string, not a number plus trailing data.
                if (numberSpaced) {
                    pos = start;
                    spacedNumbers = false;
                    number = TryReadNumber(NbtTagType.Int);
                    spacedNumbers = true;
                    if (number != null && !RunsIntoToken()) return number;
                }
                pos = start;
            }

            while (pos < text.Length && IsUnquotedChar(text[pos])) pos++;
            if (pos == start) throw Error("Expected a value");
            string token = text.Substring(start, pos - start);

            int afterToken = pos;
            SkipWhitespace();
            if (pos < text.Length && text[pos] == '(') {
                pos++;
                return ReadOperation(token, start, depthBudget);
            }
            pos = afterToken;

            if (token.Equals("true", StringComparison.OrdinalIgnoreCase)) return new NbtByte(1);
            if (token.Equals("false", StringComparison.OrdinalIgnoreCase)) return new NbtByte(0);
            NbtTag? nonFinite = TryNonFinite(token);
            if (nonFinite != null) return nonFinite;
            NbtTag? old = TryOldOverflow(token);
            if (old != null) return old;
            return new NbtString(token);
        }


        bool RunsIntoToken() {
            return pos < text.Length && IsUnquotedChar(text[pos]);
        }


        static bool CanStartNumber(char c) {
            return (c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.';
        }


        static bool IsUnquotedChar(char c) {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   c == '_' || c == '-' || c == '.' || c == '+';
        }

        #endregion


        #region Numbers

        // Modern grammar allows whitespace between the parts of a number; off while a number
        // that ran into the next word is read again, which only helps when the first reading
        // skipped some
        bool spacedNumbers = true;
        bool numberSpaced;

        void SkipNumberWhitespace() {
            if (!spacedNumbers) return;
            int before = pos;
            SkipWhitespace();
            if (pos != before) numberSpaced = true;
        }


        // The modern number grammar: sign, then a float form (a dot, an exponent or an f/d suffix)
        // or an integer form (decimal, 0x hex, 0b binary, underscores, an optional signedness letter
        // before the type letter). An unsuffixed integer takes defaultType: int on its own, the
        // element type inside an array. Whitespace is skipped before every part.
        // Returns null, with pos unspecified, when the text is not a modern number;
        // the caller rewinds and falls back to the old reading.
        NbtTag? TryReadNumber(NbtTagType defaultType) {
            bool negative = false;
            if (text[pos] == '+' || text[pos] == '-') {
                negative = text[pos] == '-';
                pos++;
                SkipNumberWhitespace();
            }
            if (pos >= text.Length) return null;
            int afterSign = pos;
            NbtTag? tag = TryReadFloat(negative);
            if (tag != null) return tag;
            pos = afterSign;
            return TryReadInteger(negative, defaultType);
        }


        NbtTag? TryReadFloat(bool negative) {
            int wholeStart = pos;
            int wholeDigits = ScanDigitRun(10);
            if (wholeDigits < 0) return null;
            int end = pos;
            SkipNumberWhitespace();

            bool hasDot = false;
            int fractionDigits = 0;
            if (pos < text.Length && text[pos] == '.') {
                hasDot = true;
                pos++;
                end = pos;
                SkipNumberWhitespace();
                fractionDigits = ScanDigitRun(10);
                if (fractionDigits < 0) return null;
                if (fractionDigits > 0) end = pos;
                SkipNumberWhitespace();
            }
            if (wholeDigits == 0 && fractionDigits == 0) return null;

            bool hasExponent = false;
            if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E')) {
                bool spaced = pos > end;
                pos++;
                SkipNumberWhitespace();
                if (pos < text.Length && (text[pos] == '+' || text[pos] == '-')) {
                    pos++;
                    SkipNumberWhitespace();
                }
                if (ScanDigitRun(10) <= 0) {
                    // An e that starts the next word (1.5 exp) leaves the number as it was; one
                    // glued to the number (1.5e) refuses the whole token
                    if (!spaced) return null;
                    pos = end;
                } else {
                    hasExponent = true;
                    end = pos;
                    SkipNumberWhitespace();
                }
            }

            bool isFloat = false;
            bool hasSuffix = false;
            if (pos < text.Length) {
                char c = text[pos];
                if (c == 'f' || c == 'F') {
                    isFloat = hasSuffix = true;
                } else if (c == 'd' || c == 'D') {
                    hasSuffix = true;
                }
                if (hasSuffix) end = pos + 1;
            }
            if (!hasDot && !hasExponent && !hasSuffix) return null;
            pos = end;
            int literalEnd = hasSuffix ? end - 1 : end;

            // The runtime reads the literal as written, unless the whitespace and underscores the
            // grammar allows sit inside it, which asks for a copy without them
            string? cleaned = null;
            for (int i = wholeStart; i < literalEnd; i++) {
                if (text[i] == '_' || IsWhitespace(text[i])) {
                    cleaned = CleanLiteral(negative, wholeStart, literalEnd);
                    break;
                }
            }
#if NETCOREAPP
            if (cleaned == null) {
                ReadOnlySpan<char> literal = text.AsSpan(wholeStart, literalEnd - wholeStart);
                if (isFloat) {
                    if (!float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out float single)) return null;
                    if (negative) single = -single;
                    return float.IsInfinity(single) ? null : new NbtFloat(single);
                }
                if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return null;
                if (negative) value = -value;
                return double.IsInfinity(value) ? null : new NbtDouble(value);
            }
#else
            cleaned ??= (negative ? "-" : "") + text.Substring(wholeStart, literalEnd - wholeStart);
#endif
            if (isFloat) {
                bool parsed = float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float f);
#if NETCOREAPP
                if (!parsed) return null;
#else
                // .NET Framework's parser can land a unit off, drops the sign of zero, and reports
                // overflow for texts just above the largest value that IEEE rounds down to it;
                // the correction settles all three, from infinity when the runtime refused
                f = FloatingDecimal.CorrectSingle(cleaned, parsed ? f : float.PositiveInfinity);
#endif
                if (float.IsInfinity(f)) return null;
                return new NbtFloat(f);
            }
            bool parsedDouble = double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
#if NETCOREAPP
            if (!parsedDouble) return null;
#else
            d = FloatingDecimal.CorrectDouble(cleaned, parsedDouble ? d : double.PositiveInfinity);
#endif
            if (double.IsInfinity(d)) return null;
            return new NbtDouble(d);
        }


        // The literal between the positions with the grammar's whitespace and underscores
        // removed, signed, as the runtime and the correction read it
        string CleanLiteral(bool negative, int start, int end) {
            StringBuilder sb = new StringBuilder(end - start + 1);
            if (negative) sb.Append('-');
            for (int i = start; i < end; i++) {
                char c = text[i];
                if (c != '_' && !IsWhitespace(c)) sb.Append(c);
            }
            return sb.ToString();
        }


        NbtTag? TryReadInteger(bool negative, NbtTagType defaultType) {
            if (!TryReadIntegerValue(negative, defaultType, out long value, out NbtTagType type)) return null;
            switch (type) {
                case NbtTagType.Byte:
                    return new NbtByte(unchecked((byte)value));
                case NbtTagType.Short:
                    return new NbtShort(unchecked((short)value));
                case NbtTagType.Int:
                    return new NbtInt(unchecked((int)value));
                default:
                    return new NbtLong(value);
            }
        }


        // The modern integer grammar, accumulated digit by digit while scanning rather than parsed
        // from a substring. The value is the bits the literal's type stores on the wire, sign
        // extended to a long: the wire byte is signed, so 255b, 255ub and -1b all come out as -1,
        // and TryReadInteger casts that back to the unsigned NbtByte.Value.
        bool TryReadIntegerValue(bool negative, NbtTagType defaultType, out long value, out NbtTagType type) {
            value = 0;
            type = defaultType;
            int radix = 10;
            int digitsStart;
            int digitsEnd;
            int end;
            if (text[pos] == '0') {
                digitsStart = pos;
                pos++;
                digitsEnd = pos;
                end = pos;
                SkipNumberWhitespace();
                if (pos < text.Length && (text[pos] == 'x' || text[pos] == 'X')) {
                    pos++;
                    SkipNumberWhitespace();
                    digitsStart = pos;
                    if (ScanDigitRun(16) <= 0) return false;
                    digitsEnd = pos;
                    radix = 16;
                    end = pos;
                } else if (pos < text.Length && (text[pos] == 'b' || text[pos] == 'B')) {
                    int beforeB = pos;
                    pos++;
                    SkipNumberWhitespace();
                    int binaryStart = pos;
                    if (ScanDigitRun(2) > 0) {
                        digitsStart = binaryStart;
                        digitsEnd = pos;
                        radix = 2;
                        end = pos;
                    } else {
                        // Just a zero; the b is its byte suffix
                        pos = beforeB;
                    }
                } else {
                    // Any digit after the zero is a leading zero, which the modern grammar refuses
                    if (ScanDigitRun(10) > 0) return false;
                    pos = end;
                }
            } else {
                digitsStart = pos;
                if (ScanDigitRun(10) <= 0) return false;
                digitsEnd = pos;
                end = pos;
            }
            SkipNumberWhitespace();

            // Suffix: [u|s] then b|s|i|l, or a lone type letter; a lone s is a short
            bool? unsigned = null;
            if (pos < text.Length) {
                char c = text[pos];
                bool signedness = c == 'u' || c == 'U' || c == 's' || c == 'S';
                // The game skips whitespace before every terminal, the type letter included
                int letterAt = pos + 1;
                while (signedness && spacedNumbers && letterAt < text.Length && IsWhitespace(text[letterAt])) letterAt++;
                if (letterAt > pos + 1) numberSpaced = true;
                if (signedness && letterAt < text.Length && TryTypeLetter(text[letterAt], out NbtTagType prefixed)) {
                    unsigned = c == 'u' || c == 'U';
                    type = prefixed;
                    end = letterAt + 1;
                } else if (TryTypeLetter(c, out NbtTagType plain)) {
                    type = plain;
                    end = pos + 1;
                }
            }
            pos = end;
            // Hex and binary literals are unsigned by default, as in the game. So is a non-negative
            // decimal byte: NbtByte's value is unsigned while the wire's is signed, so 255b and -1b
            // are the same byte here, where the modern grammar refuses 128b through 255b and the
            // old parser read them as strings.
            if (unsigned == null) unsigned = radix != 10 || (type == NbtTagType.Byte && !negative);

            ulong magnitude = 0;
            for (int i = digitsStart; i < digitsEnd; i++) {
                char c = text[i];
                if (c == '_') continue;
                int digit = c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
                if (magnitude > (ulong.MaxValue - (ulong)digit) / (ulong)radix) return false;
                magnitude = magnitude * (ulong)radix + (ulong)digit;
            }
            return TryIntegerValue(type, unsigned.Value, negative, magnitude, out value);
        }


        static bool TryTypeLetter(char c, out NbtTagType type) {
            switch (c) {
                case 'b':
                case 'B':
                    type = NbtTagType.Byte;
                    return true;
                case 's':
                case 'S':
                    type = NbtTagType.Short;
                    return true;
                case 'i':
                case 'I':
                    type = NbtTagType.Int;
                    return true;
                case 'l':
                case 'L':
                    type = NbtTagType.Long;
                    return true;
                default:
                    type = NbtTagType.Unknown;
                    return false;
            }
        }


        // Range-checks the literal for its type and signedness and gives the value the wire
        // carries, sign extended from the type's width: an unsigned 240ub and a signed -16sb are
        // the same byte, -16
        static bool TryIntegerValue(NbtTagType type, bool unsigned, bool negative, ulong magnitude, out long value) {
            int bits = type == NbtTagType.Byte ? 8 : type == NbtTagType.Short ? 16 : type == NbtTagType.Int ? 32 : 64;
            value = 0;
            ulong max = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            long raw;
            if (unsigned) {
                if (negative || magnitude > max) return false;
                raw = unchecked((long)magnitude);
            } else {
                ulong limit = (bits == 64 ? 1UL << 63 : (1UL << (bits - 1))) - (negative ? 0UL : 1UL);
                if (magnitude > limit) return false;
                raw = negative ? unchecked(-(long)magnitude) : (long)magnitude;
            }
            value = bits == 64 ? raw : (raw << (64 - bits)) >> (64 - bits);
            return true;
        }


        // Moves past a run of digits in the given radix with underscores between them. Returns
        // the digit count, 0 when none is present, or -1 when an underscore starts or ends the
        // run, which no reading accepts.
        int ScanDigitRun(int radix) {
            int start = pos;
            int digits = 0;
            while (pos < text.Length && (IsDigit(text[pos], radix) || text[pos] == '_')) {
                if (text[pos] != '_') digits++;
                pos++;
            }
            if (pos == start) return 0;
            if (text[start] == '_' || text[pos - 1] == '_') return -1;
            return digits;
        }


        static bool IsDigit(char c, int radix) {
            if (c >= '0' && c <= '9') return c - '0' < radix;
            if (radix == 16) return (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            return false;
        }


        // NaNf, Infinityd, -Infinityf: what the game and most tools print for non-finite values
        // and what none of them read back
        static NbtTag? TryNonFinite(string token) {
            char suffix = token[token.Length - 1];
            bool isFloat = suffix == 'f' || suffix == 'F';
            if (!isFloat && suffix != 'd' && suffix != 'D') return null;
            string name = token.Substring(0, token.Length - 1);
            bool negative = name.Length > 0 && name[0] == '-';
            if (name.Length > 0 && (negative || name[0] == '+')) name = name.Substring(1);
            double value;
            if (name.Equals("NaN", StringComparison.OrdinalIgnoreCase)) {
                value = double.NaN;
            } else if (name.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) {
                value = negative ? double.NegativeInfinity : double.PositiveInfinity;
            } else {
                return null;
            }
            return isFloat ? new NbtFloat((float)value) : (NbtTag)new NbtDouble(value);
        }


        // The old parser's float and double patterns, reached only when the modern grammar has
        // refused the token, which for these shapes means the value overflowed: the old parser
        // stored an infinity, and so does this
        static readonly Regex OldFloat = new Regex(
            @"^[-+]?(?:[0-9]+\.?|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?[fF]$", RegexOptions.CultureInvariant);

        static readonly Regex OldDouble = new Regex(
            @"^[-+]?(?:(?:[0-9]+\.?|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?[dD]|(?:[0-9]+\.|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?)$",
            RegexOptions.CultureInvariant);

        static NbtTag? TryOldOverflow(string token) {
            // Plain identifiers never match, so they skip the regexes
            if (!CanStartNumber(token[0])) return null;
            bool isFloat = OldFloat.IsMatch(token);
            if (!isFloat && !OldDouble.IsMatch(token)) return null;
            double value = token[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            return isFloat ? new NbtFloat((float)value) : (NbtTag)new NbtDouble(value);
        }

        #endregion


        #region Strings

        string ReadQuotedString() {
            int errorAt = pos;
            char quote = text[pos++];
            StringBuilder? sb = null;
            int segmentStart = pos;
            while (true) {
                if (pos >= text.Length) throw Error("Unclosed quoted string", errorAt);
                char c = text[pos];
                if (c == quote) {
                    pos++;
                    if (sb == null) return text.Substring(segmentStart, pos - 1 - segmentStart);
                    sb.Append(text, segmentStart, pos - 1 - segmentStart);
                    return sb.ToString();
                }
                if (c != '\\') {
                    pos++;
                    continue;
                }
                sb ??= new StringBuilder();
                sb.Append(text, segmentStart, pos - segmentStart);
                pos++;
                ReadEscape(sb);
                segmentStart = pos;
            }
        }


        // pos is just past the backslash. The game's escape letters are terminals that skip
        // whitespace first, so a backslash followed by a newline and an n is a newline.
        void ReadEscape(StringBuilder sb) {
            int errorAt = pos - 1;
            SkipWhitespace();
            if (pos >= text.Length) throw Error("Unclosed quoted string", errorAt);
            char c = text[pos++];
            switch (c) {
                case '\\':
                case '"':
                case '\'':
                    sb.Append(c);
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 's':
                    sb.Append(' ');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 'x':
                    AppendCodePoint(sb, ReadHex(2, errorAt), errorAt);
                    break;
                case 'u':
                    AppendCodePoint(sb, ReadHex(4, errorAt), errorAt);
                    break;
                case 'U':
                    AppendCodePoint(sb, ReadHex(8, errorAt), errorAt);
                    break;
                case 'N':
                    throw Error("Unicode character names (\\N{...}) are not supported", errorAt);
                default:
                    throw Error("Invalid escape sequence '\\" + c + "'", errorAt);
            }
        }


        long ReadHex(int count, int errorAt) {
            if (pos + count > text.Length) throw Error("Expected " + count + " hex digits", errorAt);
            long value = 0;
            for (int i = 0; i < count; i++) {
                char c = text[pos + i];
                if (!IsDigit(c, 16)) throw Error("Expected " + count + " hex digits", errorAt);
                value = value * 16 + (c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
            }
            pos += count;
            return value;
        }


        void AppendCodePoint(StringBuilder sb, long codePoint, int errorAt) {
            if (codePoint <= 0xFFFF) {
                // Lone surrogates included, as the game stores them
                sb.Append((char)codePoint);
            } else if (codePoint <= 0x10FFFF) {
                sb.Append(char.ConvertFromUtf32((int)codePoint));
            } else {
                throw Error("Invalid Unicode character value: U+" + codePoint.ToString("X8", CultureInfo.InvariantCulture),
                            errorAt);
            }
        }

        #endregion


        #region Operations

        // pos is just past the opening parenthesis. A call nests like a container, so it spends
        // depth like one.
        NbtTag ReadOperation(string name, int errorAt, int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            List<NbtTag> args = new List<NbtTag>();
            SkipWhitespace();
            if (pos < text.Length && text[pos] == ')') {
                pos++;
            } else {
                while (true) {
                    args.Add(ReadValue(childDepthBudget));
                    SkipWhitespace();
                    if (pos >= text.Length) throw Error("Expected ',' or ')'");
                    char c = text[pos++];
                    if (c == ')') break;
                    if (c != ',') throw Error("Expected ',' or ')'", pos - 1);
                    SkipWhitespace();
                    if (pos < text.Length && text[pos] == ')') {
                        pos++;
                        break;
                    }
                }
            }

            if (name.Equals("bool", StringComparison.OrdinalIgnoreCase) && args.Count == 1) {
                NbtTag arg = args[0];
                switch (arg.TagType) {
                    case NbtTagType.Byte:
                    case NbtTagType.Short:
                    case NbtTagType.Int:
                    case NbtTagType.Long:
                        return new NbtByte(arg.LongValue != 0 ? (byte)1 : (byte)0);
                    case NbtTagType.Float:
                    case NbtTagType.Double:
                        return new NbtByte(arg.DoubleValue != 0 ? (byte)1 : (byte)0);
                    default:
                        throw Error("Expected a number or a boolean", errorAt);
                }
            }
            if (name.Equals("uuid", StringComparison.OrdinalIgnoreCase) && args.Count == 1) {
                if (args[0] is NbtString s && TryParseUuid(s.Value, out int[] words)) {
                    return new NbtIntArray(words);
                }
                throw Error("Expected a string representing a valid UUID", errorAt);
            }
            throw Error("No such operation: " + name + "/" + args.Count, errorAt);
        }


        // Java's UUID.fromString, which the game's uuid() calls: at most 36 characters, exactly
        // five groups between dashes, each an optional plus sign and any number of hex digits
        // (leading zeros included) whose value fits a signed long, masked to the group's width
        // (32, 16, 16, 16 and 48 bits)
        static bool TryParseUuid(string value, out int[] words) {
            words = new int[4];
            if (value.Length > 36) return false;
            string[] groups = value.Split('-');
            if (groups.Length != 5) return false;
            ulong[] parts = new ulong[5];
            for (int i = 0; i < 5; i++) {
                string group = groups[i];
                int start = group.Length > 0 && group[0] == '+' ? 1 : 0;
                if (group.Length == start) return false;
                for (int j = start; j < group.Length; j++) {
                    char c = group[j];
                    if (!IsDigit(c, 16) || parts[i] > (long.MaxValue >> 4)) return false;
                    parts[i] = parts[i] * 16 + (ulong)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
                }
            }
            ulong high = ((parts[0] & 0xffffffffUL) << 32) | ((parts[1] & 0xffffUL) << 16) | (parts[2] & 0xffffUL);
            ulong low = ((parts[3] & 0xffffUL) << 48) | (parts[4] & 0xffffffffffffUL);
            words[0] = unchecked((int)(high >> 32));
            words[1] = unchecked((int)high);
            words[2] = unchecked((int)(low >> 32));
            words[3] = unchecked((int)low);
            return true;
        }

        #endregion


        #region Containers

        NbtCompound ReadCompound(int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            pos++;
            NbtCompound compound = new NbtCompound();
            SkipWhitespace();
            if (pos < text.Length && text[pos] == '}') {
                pos++;
                return compound;
            }
            while (true) {
                SkipWhitespace();
                string key = ReadKey();
                SkipWhitespace();
                if (pos >= text.Length || text[pos] != ':') throw Error("Expected ':'");
                pos++;
                NbtTag value = ReadValue(childDepthBudget);
                value.Name = key;
                // A later duplicate replaces the earlier value in place, as in every game version
                compound[key] = value;

                SkipWhitespace();
                if (pos >= text.Length) throw Error("Expected ',' or '}'");
                char c = text[pos++];
                if (c == '}') return compound;
                if (c != ',') throw Error("Expected ',' or '}'", pos - 1);
                SkipWhitespace();
                if (pos < text.Length && text[pos] == '}') {
                    pos++;
                    return compound;
                }
            }
        }


        string ReadKey() {
            if (pos >= text.Length) throw Error("Expected a key");
            char c = text[pos];
            if (c == '"' || c == '\'') return ReadQuotedString();
            int start = pos;
            uint hash = 2166136261u;
            while (pos < text.Length && IsUnquotedChar(text[pos])) {
                hash = (hash ^ text[pos]) * 16777619u;
                pos++;
            }
            if (pos == start) throw Error("Expected a key");
            return CachedKey(start, pos - start, hash);
        }


        // Keys repeat within a document and across documents (the same state names in every
        // palette entry), so short unquoted keys come from a small process-wide table. A slot
        // holds one string, compared character by character before it is used, so a stale or
        // concurrently replaced entry costs a miss and nothing else.
        const int KeyCacheSlots = 1024;
        const int KeyCacheMaxLength = 48;
        static readonly string?[] keyCache = new string?[KeyCacheSlots];

        string CachedKey(int start, int length, uint hash) {
            if (length > KeyCacheMaxLength) return text.Substring(start, length);
            int slot = (int)(hash & (KeyCacheSlots - 1));
            string? cached = keyCache[slot];
#if NETCOREAPP
            if (cached != null && cached.AsSpan().SequenceEqual(text.AsSpan(start, length))) return cached;
#else
            if (cached != null && cached.Length == length && string.CompareOrdinal(cached, 0, text, start, length) == 0) {
                return cached;
            }
#endif
            string key = text.Substring(start, length);
            keyCache[slot] = key;
            return key;
        }


        NbtTag ReadListOrArray(int depthBudget) {
            int bracketAt = pos;
            pos++;
            int afterBracket = pos;
            SkipWhitespace();
            if (pos < text.Length) {
                // Either case: the game takes uppercase only, and a lowercase letter before a
                // semicolon could not have meant anything else
                char letter = char.ToUpperInvariant(text[pos]);
                if (letter == 'B' || letter == 'I' || letter == 'L') {
                    pos++;
                    SkipWhitespace();
                    if (pos < text.Length && text[pos] == ';') {
                        pos++;
                        // An array is a value, not a container: it takes no nesting level here,
                        // as it takes none when the tag layer walks a tree
                        return ReadArray(letter, depthBudget);
                    }
                }
            }
            pos = afterBracket;

            int childDepthBudget = ConsumeDepthBudget(depthBudget, bracketAt);
            List<int> starts = elementStarts ??= new List<int>();
            int mark = starts.Count;
            List<NbtTag> elements = ReadElements(childDepthBudget, starts);
            if (elements.Count == 0) return new NbtList(NbtTagType.End);
            // Stored the way the game saves it: mixed types become wrapper compounds
            NbtList list = NbtList.FromParsed(elements);
            if (list.ListType == NbtTagType.Compound) {
                // A wrapper is a nesting level the text did not show, so an element that used the
                // whole budget below this list no longer fits under it
                for (int i = 0; i < elements.Count; i++) {
                    if (list[i] != elements[i] && ContainerDepth(elements[i]) >= childDepthBudget) {
                        throw Error(NbtTag.DepthLimitMessage.TrimEnd('.'), starts[mark + i]);
                    }
                }
            }
            starts.RemoveRange(mark, starts.Count - mark);
            return list;
        }


        // Elements up to and including the closing bracket, with one trailing comma allowed;
        // each element's start position goes on the list, for depth errors reported where the
        // element is
        List<NbtTag> ReadElements(int childDepthBudget, List<int> starts) {
            List<NbtTag> elements = new List<NbtTag>();
            SkipWhitespace();
            if (pos < text.Length && text[pos] == ']') {
                pos++;
                return elements;
            }
            while (true) {
                SkipWhitespace();
                starts.Add(pos);
                elements.Add(ReadValue(childDepthBudget));
                SkipWhitespace();
                if (pos >= text.Length) throw Error("Expected ',' or ']'");
                char c = text[pos++];
                if (c == ']') return elements;
                if (c != ',') throw Error("Expected ',' or ']'", pos - 1);
                SkipWhitespace();
                if (pos < text.Length && text[pos] == ']') {
                    pos++;
                    return elements;
                }
            }
        }




        // An array: integer literals fold straight into values, with no tag in between, and
        // anything else goes through the tag it makes, so that true, false and bool() still
        // count. An unsuffixed element takes the array's type, and any integer that fits the
        // element in either signedness is taken: [B;0xFF], [B;255ub], [I;1b] and [L;1] are all fine.
        NbtTag ReadArray(char letter, int depthBudget) {
            NbtTagType elementType = letter == 'B' ? NbtTagType.Byte : letter == 'I' ? NbtTagType.Int : NbtTagType.Long;
            long min = letter == 'B' ? sbyte.MinValue : letter == 'I' ? int.MinValue : long.MinValue;
            long max = letter == 'B' ? byte.MaxValue : letter == 'I' ? uint.MaxValue : long.MaxValue;
            List<long> values = new List<long>();
            SkipWhitespace();
            if (pos < text.Length && text[pos] == ']') {
                pos++;
            } else {
                while (true) {
                    values.Add(ReadArrayValue(elementType, depthBudget, min, max));
                    SkipWhitespace();
                    if (pos >= text.Length) throw Error("Expected ',' or ']'");
                    char c = text[pos++];
                    if (c == ']') break;
                    if (c != ',') throw Error("Expected ',' or ']'", pos - 1);
                    SkipWhitespace();
                    if (pos < text.Length && text[pos] == ']') {
                        pos++;
                        break;
                    }
                }
            }
            switch (letter) {
                case 'B': {
                    byte[] bytes = new byte[values.Count];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = unchecked((byte)values[i]);
                    return new NbtByteArray(bytes);
                }
                case 'I': {
                    int[] ints = new int[values.Count];
                    for (int i = 0; i < ints.Length; i++) ints[i] = unchecked((int)values[i]);
                    return new NbtIntArray(ints);
                }
                default:
                    return new NbtLongArray(values.ToArray());
            }
        }


        long ReadArrayValue(NbtTagType elementType, int depthBudget, long min, long max) {
            SkipWhitespace();
            int start = pos;
            if (pos < text.Length && CanStartNumber(text[pos])) {
                bool negative = false;
                if (text[pos] == '+' || text[pos] == '-') {
                    negative = text[pos] == '-';
                    pos++;
                    SkipWhitespace();
                }
                if (pos < text.Length && TryReadIntegerValue(negative, elementType, out long value, out _) &&
                    !(pos < text.Length && IsUnquotedChar(text[pos]))) {
                    if (value < min || value > max) throw Error("Array element out of range", start);
                    return value;
                }
                pos = start;
            }
            // An element can only be a number, so a container fails here instead of after being
            // parsed: nested arrays would otherwise recurse without spending depth
            if (pos < text.Length && (text[pos] == '[' || text[pos] == '{')) {
                throw Error("Invalid array element type", start);
            }
            return ArrayElement(ReadValue(depthBudget), min, max, start);
        }


        long ArrayElement(NbtTag element, long min, long max, int errorAt) {
            long value;
            switch (element.TagType) {
                case NbtTagType.Byte:
                    value = ((NbtByte)element).SignedValue;
                    break;
                case NbtTagType.Short:
                case NbtTagType.Int:
                case NbtTagType.Long:
                    value = element.LongValue;
                    break;
                default:
                    throw Error("Invalid array element type", errorAt);
            }
            if (value < min || value > max) throw Error("Array element out of range", errorAt);
            return value;
        }


        int ConsumeDepthBudget(int depthBudget) {
            return ConsumeDepthBudget(depthBudget, pos);
        }


        int ConsumeDepthBudget(int depthBudget, int errorAt) {
            if (depthBudget <= 0) throw Error(NbtTag.DepthLimitMessage.TrimEnd('.'), errorAt);
            return depthBudget - 1;
        }


        // Container levels in a parsed subtree, wrapper compounds included
        static int ContainerDepth(NbtTag tag) {
            int deepest = 0;
            if (tag is NbtList list) {
                foreach (NbtTag child in list) {
                    int depth = ContainerDepth(child);
                    if (depth > deepest) deepest = depth;
                }
                return deepest + 1;
            }
            if (tag is NbtCompound compound) {
                foreach (NbtTag child in compound) {
                    int depth = ContainerDepth(child);
                    if (depth > deepest) deepest = depth;
                }
                return deepest + 1;
            }
            return 0;
        }

        #endregion


        #region Plumbing

        void SkipWhitespace() {
            while (pos < text.Length && IsWhitespace(text[pos])) pos++;
        }


        // Java's Character.isWhitespace, which both game parsers skip: .NET's set plus the four
        // information separators U+001C to U+001F. .NET also counts U+0085 and the non-breaking
        // spaces, which the game does not; they cannot appear outside quotes in text the game
        // reads, so taking them as whitespace changes nothing that parses.
        static bool IsWhitespace(char c) {
            return char.IsWhiteSpace(c) || (c >= '\u001C' && c <= '\u001F');
        }


        // For text read without stripping its byte order mark
        void SkipByteOrderMark() {
            if (pos < text.Length && text[pos] == '\uFEFF') pos++;
        }


        SnbtParseException Error(string message) {
            return Error(message, pos);
        }


        SnbtParseException Error(string message, int index) {
            int line = 1;
            int column = 1;
            for (int i = 0; i < index && i < text.Length; i++) {
                if (text[i] == '\n') {
                    line++;
                    column = 1;
                } else {
                    column++;
                }
            }
            return new SnbtParseException(
                message + " at index " + index + " (line " + line + ", column " + column + ").", index, line, column);
        }

        #endregion
    }
}
