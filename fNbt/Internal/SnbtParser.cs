using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace fNbt {
    // Reads SNBT text into a tag tree. The grammar is Minecraft Java 1.21.5's, widened wherever an
    // older game version or a common tool produces something 1.21.5 rejects, as long as text the
    // modern game accepts keeps its meaning: an unquoted token that is not a modern number falls back
    // to the classic (1.12 to 1.21.4) reading, which made it a string or an infinity; suffixed
    // NaN and Infinity read as numbers; empty quoted keys are allowed; mixed lists become the
    // wrapper compounds the game stores on disk.
    internal sealed class SnbtParser {
        readonly string text;
        int pos;


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
                NbtTag? number = TryReadNumber(NbtTagType.Int);
                // A number that runs straight into more token characters (1abc, 1.5.2, 1bx) is a
                // classic-era string, not a number plus trailing data
                if (number != null && !(pos < text.Length && IsUnquotedChar(text[pos]))) return number;
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
            NbtTag? classic = TryClassicFloat(token);
            if (classic != null) return classic;
            return new NbtString(token);
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

        // The modern number grammar: sign, then a float form (a dot, an exponent or an f/d suffix)
        // or an integer form (decimal, 0x hex, 0b binary, underscores, an optional signedness letter
        // before the type letter). An unsuffixed integer takes defaultType: int on its own, the
        // element type inside an array. Whitespace is skipped before every part, as the game's
        // terminals do. Returns null, with pos unspecified, when the text is not a modern number;
        // the caller rewinds and falls back to the classic reading.
        NbtTag? TryReadNumber(NbtTagType defaultType) {
            bool negative = false;
            if (text[pos] == '+' || text[pos] == '-') {
                negative = text[pos] == '-';
                pos++;
                SkipWhitespace();
            }
            if (pos >= text.Length) return null;
            int afterSign = pos;
            NbtTag? tag = TryReadFloat(negative, defaultType);
            if (tag != null) return tag;
            pos = afterSign;
            return TryReadInteger(negative, defaultType);
        }


        NbtTag? TryReadFloat(bool negative, NbtTagType defaultType) {
            string? whole = ReadDigitRun(10);
            if (whole == null) return null;
            int end = pos;
            SkipWhitespace();

            bool hasDot = false;
            string fraction = "";
            if (pos < text.Length && text[pos] == '.') {
                hasDot = true;
                pos++;
                end = pos;
                SkipWhitespace();
                string? run = ReadDigitRun(10);
                if (run == null) return null;
                fraction = run;
                if (fraction.Length > 0) end = pos;
                SkipWhitespace();
            }
            if (whole.Length == 0 && fraction.Length == 0) return null;

            bool hasExponent = false;
            string exponent = "";
            if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E')) {
                pos++;
                SkipWhitespace();
                bool exponentNegative = false;
                if (pos < text.Length && (text[pos] == '+' || text[pos] == '-')) {
                    exponentNegative = text[pos] == '-';
                    pos++;
                    SkipWhitespace();
                }
                string? run = ReadDigitRun(10);
                if (run == null || run.Length == 0) return null;
                hasExponent = true;
                exponent = (exponentNegative ? "-" : "") + run;
                end = pos;
                SkipWhitespace();
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

            string cleaned = (negative ? "-" : "") + whole.Replace("_", "") +
                             (hasDot ? "." + fraction.Replace("_", "") : "") +
                             (hasExponent ? "e" + exponent.Replace("_", "") : "");
            if (isFloat) {
                if (!float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                    float.IsInfinity(f)) {
                    return null;
                }
                return new NbtFloat(f);
            }
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ||
                double.IsInfinity(d)) {
                return null;
            }
            return new NbtDouble(d);
        }


        NbtTag? TryReadInteger(bool negative, NbtTagType defaultType) {
            int radix = 10;
            string? digits;
            int end;
            if (text[pos] == '0') {
                pos++;
                end = pos;
                digits = "0";
                SkipWhitespace();
                if (pos < text.Length && (text[pos] == 'x' || text[pos] == 'X')) {
                    pos++;
                    SkipWhitespace();
                    digits = ReadDigitRun(16);
                    if (digits == null || digits.Length == 0) return null;
                    radix = 16;
                    end = pos;
                } else if (pos < text.Length && (text[pos] == 'b' || text[pos] == 'B')) {
                    int beforeB = pos;
                    pos++;
                    SkipWhitespace();
                    string? binary = ReadDigitRun(2);
                    if (binary != null && binary.Length > 0) {
                        digits = binary;
                        radix = 2;
                        end = pos;
                    } else {
                        // Just a zero; the b is its byte suffix
                        pos = beforeB;
                    }
                } else {
                    // Any digit after the zero is a leading zero, which the modern grammar refuses
                    string? more = ReadDigitRun(10);
                    if (more != null && more.Length > 0) return null;
                    pos = end;
                }
            } else {
                digits = ReadDigitRun(10);
                if (digits == null || digits.Length == 0) return null;
                end = pos;
            }
            SkipWhitespace();

            // Suffix: [u|s] then b|s|i|l, or a lone type letter; a lone s is a short
            bool? unsigned = null;
            NbtTagType type = defaultType;
            if (pos < text.Length) {
                char c = text[pos];
                if ((c == 'u' || c == 'U' || c == 's' || c == 'S') && pos + 1 < text.Length &&
                    TryTypeLetter(text[pos + 1], out NbtTagType prefixed)) {
                    unsigned = c == 'u' || c == 'U';
                    type = prefixed;
                    end = pos + 2;
                } else if (TryTypeLetter(c, out NbtTagType plain)) {
                    type = plain;
                    end = pos + 1;
                }
            }
            pos = end;
            if (unsigned == null) unsigned = radix != 10;

            ulong magnitude = 0;
            foreach (char c in digits!) {
                if (c == '_') continue;
                int digit = c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
                if (magnitude > (ulong.MaxValue - (ulong)digit) / (ulong)radix) return null;
                magnitude = magnitude * (ulong)radix + (ulong)digit;
            }
            return MakeInteger(type, unsigned.Value, negative, magnitude);
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


        // Range-checks the literal for its type and signedness and stores it the way the wire
        // does: an unsigned 240ub and a signed -16sb are the same byte
        static NbtTag? MakeInteger(NbtTagType type, bool unsigned, bool negative, ulong magnitude) {
            int bits = type == NbtTagType.Byte ? 8 : type == NbtTagType.Short ? 16 : type == NbtTagType.Int ? 32 : 64;
            ulong max = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            long value;
            if (unsigned) {
                if (negative || magnitude > max) return null;
                value = unchecked((long)magnitude);
            } else {
                ulong limit = (bits == 64 ? 1UL << 63 : (1UL << (bits - 1))) - (negative ? 0UL : 1UL);
                if (magnitude > limit) return null;
                value = negative ? unchecked(-(long)magnitude) : (long)magnitude;
            }
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


        // A run of digits in the given radix with underscores between them. Empty when no digit
        // is present; null when an underscore starts or ends the run, which no reading accepts.
        string? ReadDigitRun(int radix) {
            int start = pos;
            while (pos < text.Length && (IsDigit(text[pos], radix) || text[pos] == '_')) pos++;
            if (pos == start) return "";
            if (text[start] == '_' || text[pos - 1] == '_') return null;
            return text.Substring(start, pos - start);
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


        // The classic parser's float and double patterns, reached only when the modern grammar has
        // refused the token, which for these shapes means the value overflowed: the classic parser
        // stored an infinity, and so does this
        static readonly Regex ClassicFloat = new Regex(
            @"^[-+]?(?:[0-9]+\.?|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?[fF]$", RegexOptions.CultureInvariant);

        static readonly Regex ClassicDouble = new Regex(
            @"^[-+]?(?:(?:[0-9]+\.?|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?[dD]|(?:[0-9]+\.|[0-9]*\.[0-9]+)(?:[eE][-+]?[0-9]+)?)$",
            RegexOptions.CultureInvariant);

        static NbtTag? TryClassicFloat(string token) {
            // Plain identifiers never match, so they skip the regexes
            if (!CanStartNumber(token[0])) return null;
            bool isFloat = ClassicFloat.IsMatch(token);
            if (!isFloat && !ClassicDouble.IsMatch(token)) return null;
            char last = token[token.Length - 1];
            string digits = last == 'f' || last == 'F' || last == 'd' || last == 'D'
                ? token.Substring(0, token.Length - 1)
                : token;
            if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
                // .NET Framework reports an overflow as a failure
                value = digits[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }
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


        // Java's UUID.fromString: five hex groups of at most 8, 4, 4, 4 and 12 digits, any of them
        // shorter, to the four big-endian ints Minecraft stores
        static bool TryParseUuid(string value, out int[] words) {
            words = new int[4];
            string[] groups = value.Split('-');
            int[] maxLengths = { 8, 4, 4, 4, 12 };
            if (groups.Length != 5) return false;
            ulong[] parts = new ulong[5];
            for (int i = 0; i < 5; i++) {
                if (groups[i].Length == 0 || groups[i].Length > maxLengths[i]) return false;
                foreach (char c in groups[i]) {
                    if (!IsDigit(c, 16)) return false;
                    parts[i] = parts[i] * 16 + (ulong)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
                }
            }
            ulong high = (parts[0] << 32) | (parts[1] << 16) | parts[2];
            ulong low = (parts[3] << 48) | parts[4];
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
            while (pos < text.Length && IsUnquotedChar(text[pos])) pos++;
            if (pos == start) throw Error("Expected a key");
            return text.Substring(start, pos - start);
        }


        NbtTag ReadListOrArray(int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
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
                        return ReadArray(letter, childDepthBudget);
                    }
                }
            }
            pos = afterBracket;

            List<NbtTag> elements = ReadElements(childDepthBudget);
            if (elements.Count == 0) return new NbtList(NbtTagType.End);
            NbtTagType type = elements[0].TagType;
            bool mixed = false;
            foreach (NbtTag element in elements) {
                if (element.TagType != type) {
                    mixed = true;
                    break;
                }
            }
            if (!mixed) return new NbtList(elements, type);

            // The game's on-disk form of a mixed list: every element that is not a plain compound
            // is wrapped in a compound under an empty key, wrapper-shaped compounds included, so
            // that unwrapping on print gives the list back
            for (int i = 0; i < elements.Count; i++) {
                NbtTag element = elements[i];
                if (element is NbtCompound c && !(c.Count == 1 && c.Contains(""))) continue;
                element.Name = "";
                elements[i] = new NbtCompound(new[] { element });
            }
            return new NbtList(elements, NbtTagType.Compound);
        }


        // Elements up to and including the closing bracket, with one trailing comma allowed.
        // Arrays ask for each element's start position, to report a bad element where it is.
        List<NbtTag> ReadElements(int childDepthBudget, NbtTagType numberType = NbtTagType.Int,
                                  List<int>? starts = null) {
            List<NbtTag> elements = new List<NbtTag>();
            SkipWhitespace();
            if (pos < text.Length && text[pos] == ']') {
                pos++;
                return elements;
            }
            while (true) {
                SkipWhitespace();
                starts?.Add(pos);
                elements.Add(ReadElement(childDepthBudget, numberType));
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


        // A value, except that inside an array an unsuffixed number takes the array's type. Int is
        // what ReadValue produces anyway, so lists skip the extra attempt.
        NbtTag ReadElement(int childDepthBudget, NbtTagType numberType) {
            SkipWhitespace();
            if (numberType != NbtTagType.Int && pos < text.Length && CanStartNumber(text[pos])) {
                int start = pos;
                NbtTag? number = TryReadNumber(numberType);
                if (number != null && !(pos < text.Length && IsUnquotedChar(text[pos]))) return number;
                pos = start;
            }
            return ReadValue(childDepthBudget);
        }


        // An unsuffixed element takes the array's type, and any integer tag whose value fits the
        // element, signed or unsigned, is taken: [B;0xFF], [B;255ub], [I;1b] and [L;1] are all
        // fine, as are true and false
        NbtTag ReadArray(char letter, int childDepthBudget) {
            NbtTagType elementType = letter == 'B' ? NbtTagType.Byte : letter == 'I' ? NbtTagType.Int : NbtTagType.Long;
            List<int> starts = new List<int>();
            List<NbtTag> elements = ReadElements(childDepthBudget, elementType, starts);
            switch (letter) {
                case 'B': {
                    byte[] values = new byte[elements.Count];
                    for (int i = 0; i < values.Length; i++) {
                        values[i] = unchecked((byte)ArrayElement(elements[i], -128, 255, starts[i]));
                    }
                    return new NbtByteArray(values);
                }
                case 'I': {
                    int[] values = new int[elements.Count];
                    for (int i = 0; i < values.Length; i++) {
                        values[i] = unchecked((int)ArrayElement(elements[i], int.MinValue, uint.MaxValue, starts[i]));
                    }
                    return new NbtIntArray(values);
                }
                default: {
                    long[] values = new long[elements.Count];
                    for (int i = 0; i < values.Length; i++) {
                        values[i] = ArrayElement(elements[i], long.MinValue, long.MaxValue, starts[i]);
                    }
                    return new NbtLongArray(values);
                }
            }
        }


        long ArrayElement(NbtTag element, long min, long max, int errorAt) {
            long value;
            switch (element.TagType) {
                case NbtTagType.Byte:
                    value = (sbyte)element.ByteValue;
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
            if (depthBudget <= 0) throw Error(NbtTag.DepthLimitMessage.TrimEnd('.'));
            return depthBudget - 1;
        }

        #endregion


        #region Plumbing

        void SkipWhitespace() {
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        }


        // For text read without stripping its byte order mark
        void SkipByteOrderMark() {
            if (pos < text.Length && text[pos] == '\uFEFF') pos++;
        }


        NbtFormatException Error(string message) {
            return Error(message, pos);
        }


        NbtFormatException Error(string message, int index) {
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
            return new NbtFormatException(
                message + " at index " + index + " (line " + line + ", column " + column + ").", index);
        }

        #endregion
    }
}
