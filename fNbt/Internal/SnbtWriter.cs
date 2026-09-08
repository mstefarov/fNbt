using System;
using System.Globalization;
using System.Text;

namespace fNbt {
    // Prints a tag tree as SNBT text in one of the SnbtLayout shapes. The shape is the common
    // denominator of what Minecraft Java has read since 1.12 and what the NBT tools read: values
    // always in double quotes with only the quote and backslash escaped, keys bare only when no
    // parser could take them for anything else, Java's number spelling and suffixes, insertion
    // order.
    internal static class SnbtWriter {
        const string IndentUnit = "    ";

        // One builder per thread, kept between calls up to a size worth keeping: growing a fresh
        // one from nothing cost more allocation than the text itself on small documents
        [ThreadStatic] static StringBuilder? cachedBuilder;
        const int MaxCachedBuilderChars = 1 << 20;

        public static string Write(NbtTag tag, SnbtLayout layout) {
            StringBuilder sb = cachedBuilder ?? new StringBuilder(256);
            cachedBuilder = null;
            sb.Clear();
            WriteTag(sb, tag, layout, 0, NbtTag.MaxDepth);
            string text = sb.ToString();
            if (sb.Capacity <= MaxCachedBuilderChars) cachedBuilder = sb;
            return text;
        }


        static void WriteTag(StringBuilder sb, NbtTag tag, SnbtLayout layout, int level, int depthBudget) {
            switch (tag.TagType) {
                case NbtTagType.Byte:
                    // Bytes travel signed, as in Java
                    AppendInteger(sb, ((NbtByte)tag).SignedValue);
                    sb.Append('b');
                    break;
                case NbtTagType.Short:
                    AppendInteger(sb, ((NbtShort)tag).Value);
                    sb.Append('s');
                    break;
                case NbtTagType.Int:
                    AppendInteger(sb, ((NbtInt)tag).Value);
                    break;
                case NbtTagType.Long:
                    AppendInteger(sb, ((NbtLong)tag).Value);
                    sb.Append('L');
                    break;
                case NbtTagType.Float:
                    AppendFloat(sb, ((NbtFloat)tag).Value);
                    sb.Append('f');
                    break;
                case NbtTagType.Double:
                    AppendDouble(sb, ((NbtDouble)tag).Value);
                    sb.Append('d');
                    break;
                case NbtTagType.String:
                    AppendQuoted(sb, ((NbtString)tag).Value);
                    break;
                case NbtTagType.ByteArray:
                    WriteByteArray(sb, ((NbtByteArray)tag).Value, layout);
                    break;
                case NbtTagType.IntArray:
                    WriteIntArray(sb, ((NbtIntArray)tag).Value, layout);
                    break;
                case NbtTagType.LongArray:
                    WriteLongArray(sb, ((NbtLongArray)tag).Value, layout);
                    break;
                case NbtTagType.List:
                    WriteList(sb, (NbtList)tag, layout, level, depthBudget);
                    break;
                case NbtTagType.Compound:
                    WriteCompound(sb, (NbtCompound)tag, layout, level, depthBudget);
                    break;
                default:
                    throw new NbtFormatException("Cannot print a tag of type " + tag.TagType + " as SNBT.");
            }
        }


        #region Containers

        static void WriteList(StringBuilder sb, NbtList list, SnbtLayout layout, int level, int depthBudget) {
            int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
            if (list.Count == 0) {
                sb.Append("[]");
                return;
            }
            bool expand = layout == SnbtLayout.Indented && HasContainer(list);
            sb.Append('[');
            for (int i = 0; i < list.Count; i++) {
                NbtTag element = NbtList.TryUnwrap(list[i]);
                if (i > 0) sb.Append(',');
                BeginMember(sb, layout, expand, i, level + 1);
                WriteTag(sb, element, layout, level + 1, childDepthBudget);
            }
            EndContainer(sb, expand, level);
            sb.Append(']');
        }


        static void WriteCompound(StringBuilder sb, NbtCompound compound, SnbtLayout layout, int level,
                                  int depthBudget) {
            int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
            if (compound.Count == 0) {
                sb.Append("{}");
                return;
            }
            bool expand = layout == SnbtLayout.Indented;
            sb.Append('{');
            int i = 0;
            foreach (NbtTag child in compound) {
                if (i > 0) sb.Append(',');
                BeginMember(sb, layout, expand, i, level + 1);
                AppendKey(sb, child.Name!);
                sb.Append(layout == SnbtLayout.Compact ? ":" : ": ");
                WriteTag(sb, child, layout, level + 1, childDepthBudget);
                i++;
            }
            EndContainer(sb, expand, level);
            sb.Append('}');
        }


        // Elements print the way the game reads them: a wrapper compound stands for its value
        static bool HasContainer(NbtList list) {
            foreach (NbtTag element in list) {
                NbtTagType type = NbtList.TryUnwrap(element).TagType;
                if (type == NbtTagType.List || type == NbtTagType.Compound) return true;
            }
            return false;
        }


        static void BeginMember(StringBuilder sb, SnbtLayout layout, bool expand, int index, int level) {
            if (expand) {
                sb.Append('\n');
                for (int i = 0; i < level; i++) sb.Append(IndentUnit);
            } else if (index > 0 && layout != SnbtLayout.Compact) {
                sb.Append(' ');
            }
        }


        static void EndContainer(StringBuilder sb, bool expand, int level) {
            if (!expand) return;
            sb.Append('\n');
            for (int i = 0; i < level; i++) sb.Append(IndentUnit);
        }


        static void WriteByteArray(StringBuilder sb, byte[] values, SnbtLayout layout) {
            sb.Append("[B;");
            for (int i = 0; i < values.Length; i++) {
                BeginArrayElement(sb, layout, i);
                AppendInteger(sb, (sbyte)values[i]);
                sb.Append('B');
            }
            sb.Append(']');
        }


        static void WriteIntArray(StringBuilder sb, int[] values, SnbtLayout layout) {
            sb.Append("[I;");
            for (int i = 0; i < values.Length; i++) {
                BeginArrayElement(sb, layout, i);
                AppendInteger(sb, values[i]);
            }
            sb.Append(']');
        }


        static void WriteLongArray(StringBuilder sb, long[] values, SnbtLayout layout) {
            sb.Append("[L;");
            for (int i = 0; i < values.Length; i++) {
                BeginArrayElement(sb, layout, i);
                AppendInteger(sb, values[i]);
                sb.Append('L');
            }
            sb.Append(']');
        }


        // Invariant digits without a string per number where the runtime can format into a span
        static void AppendInteger(StringBuilder sb, long value) {
#if NETCOREAPP
            Span<char> digits = stackalloc char[20];
            value.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture);
            sb.Append(digits.Slice(0, written));
#else
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
#endif
        }


        static void BeginArrayElement(StringBuilder sb, SnbtLayout layout, int index) {
            if (index > 0) sb.Append(',');
            if (layout != SnbtLayout.Compact) sb.Append(' ');
        }

        #endregion


        #region Strings and keys

        // Double quotes always, where Minecraft's own printer switches to single quotes around a
        // value holding a double quote: 1.12 and 1.13 read no other delimiter. Only the quote and
        // backslashes are escaped. Control characters go out raw, which every version reads; the
        // 1.21.5+ escapes would not be.
        internal static void AppendQuoted(StringBuilder sb, string value) {
            sb.Append('"');
            foreach (char c in value) {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
        }


        static void AppendKey(StringBuilder sb, string key) {
            if (IsBareKey(key)) {
                sb.Append(key);
            } else {
                AppendQuoted(sb, key);
            }
        }


        // The 1.21.5 printer's rule, a subset of what every parser back to 1.12 takes bare: no
        // key that could read as a number or a boolean is left unquoted
        static bool IsBareKey(string key) {
            if (key.Length == 0 || !IsBareKeyStart(key[0])) return false;
            for (int i = 1; i < key.Length; i++) {
                char c = key[i];
                if (!IsBareKeyStart(c) && (c < '0' || c > '9') && c != '+' && c != '-') return false;
            }
            return !key.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                   !key.Equals("false", StringComparison.OrdinalIgnoreCase);
        }


        static bool IsBareKeyStart(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '_';
        }

        #endregion


        #region Numbers

        // Java's Float.toString and Double.toString, digit for digit: the fewest digits that read
        // back as the value but at least two, the closest such decimal, laid out plain with at
        // least one fractional digit when the decimal exponent is in [-3, 7), and as d.dddE<exp>
        // otherwise, with no plus sign or padding on the exponent. The runtime's shortest form
        // gives Java's digits on .NET Core after its one-digit forms get a second digit when
        // closer; FloatingDecimal gives them elsewhere.

        internal static void AppendFloat(StringBuilder sb, float value) {
            if (float.IsNaN(value)) {
                sb.Append("NaN");
            } else if (float.IsInfinity(value)) {
                sb.Append(value > 0 ? "Infinity" : "-Infinity");
            } else {
                AppendJavaLayout(sb, Shortest(value));
            }
        }


        internal static void AppendDouble(StringBuilder sb, double value) {
            if (double.IsNaN(value)) {
                sb.Append("NaN");
            } else if (double.IsInfinity(value)) {
                sb.Append(value > 0 ? "Infinity" : "-Infinity");
            } else {
                AppendJavaLayout(sb, Shortest(value));
            }
        }


#if NETCOREAPP
        // Java chooses the closest decimal with one or two digits when one digit round-trips.
        // Those cases can differ from R only in scientific notation (the smallest subnormals).
        static string Shortest<T>(T value) where T : IFormattable {
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.IndexOf('E') == (text[0] == '-' ? 2 : 1)
                ? value.ToString("G2", CultureInfo.InvariantCulture)
                : text;
        }
#else
        // .NET Framework's formatting is not correctly rounded past 15 digits and its "R" is not
        // shortest, so every number goes through exact arithmetic
        static string Shortest(float value) {
            return WithSign(FloatingDecimal.ShortestSingle(value), value);
        }


        static string Shortest(double value) {
            return WithSign(FloatingDecimal.ShortestDouble(value), value);
        }


        // .NET Framework formats negative zero as "0"; the sign comes from the bits instead
        static string WithSign(string text, double value) {
            if (BitConverter.DoubleToInt64Bits(value) < 0 && text[0] != '-') return "-" + text;
            return text;
        }
#endif


        // Re-lays out a finite number that .NET formatted with the invariant culture ("123.45",
        // "1E+17", "5E-324", "-0") into Java's layout
        static void AppendJavaLayout(StringBuilder sb, string text) {
            int start = 0;
            if (text[0] == '-') {
                sb.Append('-');
                start = 1;
            }
            int exponentAt = text.IndexOf('E', start);
            string mantissa = exponentAt < 0 ? text.Substring(start) : text.Substring(start, exponentAt - start);
            int exponent = exponentAt < 0 ? 0 : int.Parse(text.Substring(exponentAt + 1), CultureInfo.InvariantCulture);

            // The digits with the point removed, and where the point sits among them
            int dot = mantissa.IndexOf('.');
            string digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
            int pointAt = (dot < 0 ? mantissa.Length : dot) + exponent;

            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
            int end = digits.Length;
            while (end > lead + 1 && digits[end - 1] == '0') end--;
            digits = digits.Substring(lead, end - lead);
            pointAt -= lead;

            if (digits == "0") {
                sb.Append("0.0");
                return;
            }
            int decimalExponent = pointAt - 1;
            if (decimalExponent >= -3 && decimalExponent < 7) {
                if (pointAt <= 0) {
                    sb.Append("0.");
                    sb.Append('0', -pointAt);
                    sb.Append(digits);
                } else if (pointAt >= digits.Length) {
                    sb.Append(digits);
                    sb.Append('0', pointAt - digits.Length);
                    sb.Append(".0");
                } else {
                    sb.Append(digits, 0, pointAt);
                    sb.Append('.');
                    sb.Append(digits, pointAt, digits.Length - pointAt);
                }
            } else {
                sb.Append(digits[0]);
                sb.Append('.');
                if (digits.Length > 1) {
                    sb.Append(digits, 1, digits.Length - 1);
                } else {
                    sb.Append('0');
                }
                sb.Append('E');
                sb.Append(decimalExponent.ToString(CultureInfo.InvariantCulture));
            }
        }

        #endregion
    }
}
