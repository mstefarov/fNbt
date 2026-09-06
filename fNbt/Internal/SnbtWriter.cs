using System;
using System.Globalization;
using System.Text;

namespace fNbt {
    // Prints a tag tree as SNBT text in one of the SnbtLayout shapes. The shape is the common
    // denominator of what Minecraft Java has read since 1.12 and what the NBT tools read: values
    // always quoted with the game's own quote choice and only the delimiter and backslash escaped,
    // keys bare only when no parser could take them for anything else, Java's number spelling and
    // suffixes, insertion order.
    internal static class SnbtWriter {
        const string IndentUnit = "    ";

        public static string Write(NbtTag tag, SnbtLayout layout) {
            StringBuilder sb = new StringBuilder();
            WriteTag(sb, tag, layout, 0, NbtTag.MaxDepth);
            return sb.ToString();
        }


        static void WriteTag(StringBuilder sb, NbtTag tag, SnbtLayout layout, int level, int depthBudget) {
            switch (tag.TagType) {
                case NbtTagType.Byte:
                    // Bytes travel signed, as in Java
                    sb.Append(((sbyte)((NbtByte)tag).Value).ToString(CultureInfo.InvariantCulture)).Append('b');
                    break;
                case NbtTagType.Short:
                    sb.Append(((NbtShort)tag).Value.ToString(CultureInfo.InvariantCulture)).Append('s');
                    break;
                case NbtTagType.Int:
                    sb.Append(((NbtInt)tag).Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case NbtTagType.Long:
                    sb.Append(((NbtLong)tag).Value.ToString(CultureInfo.InvariantCulture)).Append('L');
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
            bool unwrap = list.ListType == NbtTagType.Compound;
            bool expand = layout == SnbtLayout.Indented && HasContainer(list, unwrap);
            sb.Append('[');
            for (int i = 0; i < list.Count; i++) {
                NbtTag element = unwrap ? Unwrap(list[i]) : list[i];
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


        // The game's own rule for the wrapper compounds it writes for mixed lists: inside a list
        // of compounds, a one-entry compound whose key is empty stands for its value
        static NbtTag Unwrap(NbtTag element) {
            NbtCompound compound = (NbtCompound)element;
            if (compound.Count == 1) {
                NbtTag? inner = compound[""];
                if (inner != null) return inner;
            }
            return element;
        }


        static bool HasContainer(NbtList list, bool unwrap) {
            foreach (NbtTag element in list) {
                NbtTagType type = (unwrap ? Unwrap(element) : element).TagType;
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
                sb.Append(((sbyte)values[i]).ToString(CultureInfo.InvariantCulture)).Append('B');
            }
            sb.Append(']');
        }


        static void WriteIntArray(StringBuilder sb, int[] values, SnbtLayout layout) {
            sb.Append("[I;");
            for (int i = 0; i < values.Length; i++) {
                BeginArrayElement(sb, layout, i);
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }


        static void WriteLongArray(StringBuilder sb, long[] values, SnbtLayout layout) {
            sb.Append("[L;");
            for (int i = 0; i < values.Length; i++) {
                BeginArrayElement(sb, layout, i);
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture)).Append('L');
            }
            sb.Append(']');
        }


        static void BeginArrayElement(StringBuilder sb, SnbtLayout layout, int index) {
            if (index > 0) sb.Append(',');
            if (layout != SnbtLayout.Compact) sb.Append(' ');
        }

        #endregion


        #region Strings and keys

        // Minecraft's StringTag.quoteAndEscape: the first quote character in the value picks the
        // other one as delimiter, and only the delimiter and backslashes are escaped. Control
        // characters go out raw, which every version reads; the 1.21.5+ escapes would not be.
        internal static void AppendQuoted(StringBuilder sb, string value) {
            char quote = '"';
            foreach (char c in value) {
                if (c == '"') {
                    quote = '\'';
                    break;
                } else if (c == '\'') {
                    break;
                }
            }
            sb.Append(quote);
            foreach (char c in value) {
                if (c == quote || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append(quote);
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

        // Java's Float.toString and Double.toString: the shortest digits that round-trip, laid out
        // plain with at least one fractional digit when the decimal exponent is in [-3, 7), and as
        // d.dddE<exp> otherwise, with no plus sign or padding on the exponent. .NET supplies the
        // digits; the layout is re-done here so the text matches what every other writer emits.

        internal static void AppendFloat(StringBuilder sb, float value) {
            if (float.IsNaN(value)) {
                sb.Append("NaN");
            } else if (float.IsInfinity(value)) {
                sb.Append(value > 0 ? "Infinity" : "-Infinity");
            } else {
                AppendJavaLayout(sb, ShortestFloat(value));
            }
        }


        internal static void AppendDouble(StringBuilder sb, double value) {
            if (double.IsNaN(value)) {
                sb.Append("NaN");
            } else if (double.IsInfinity(value)) {
                sb.Append(value > 0 ? "Infinity" : "-Infinity");
            } else {
                AppendJavaLayout(sb, ShortestDouble(value));
            }
        }


#if NETCOREAPP
        static string ShortestFloat(float value) {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }


        static string ShortestDouble(double value) {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
#else
        // .NET Framework's "R" is not shortest (15 digits, then 17), and its default is lossy. The
        // shortest of G7 to G9 (G15 to G17) that parses back exactly is the closest available; the
        // longest always does.
        static readonly string[] FloatProbes = { "G7", "G8" };
        static readonly string[] DoubleProbes = { "G15", "G16" };

        // TryParse, not Parse: a rounded-up maximum overflows, which .NET Framework reports as
        // an exception from Parse and as false from TryParse
        static string ShortestFloat(float value) {
            foreach (string format in FloatProbes) {
                string text = value.ToString(format, CultureInfo.InvariantCulture);
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float back) &&
                    back == value) {
                    return text;
                }
            }
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }


        static string ShortestDouble(double value) {
            foreach (string format in DoubleProbes) {
                string text = value.ToString(format, CultureInfo.InvariantCulture);
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double back) &&
                    back == value) {
                    return text;
                }
            }
            return value.ToString("G17", CultureInfo.InvariantCulture);
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
            int exponent = exponentAt < 0 ? 0 : ParseExponent(text, exponentAt + 1);

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

        // The exponent .NET wrote: an optional sign and a few digits
        static int ParseExponent(string text, int start) {
            int sign = 1;
            int i = start;
            if (text[i] == '+') {
                i++;
            } else if (text[i] == '-') {
                sign = -1;
                i++;
            }
            int value = 0;
            for (; i < text.Length; i++) {
                value = value * 10 + (text[i] - '0');
            }
            return sign * value;
        }

        #endregion
    }
}
