using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace fNbt {
    /// <summary> Base class for different kinds of named binary tags. </summary>
    public abstract class NbtTag : ICloneable {
        // Reading, writing, cloning, and printing are all recursive.
        // A stack overflow cannot be caught, so every recursive walk caps open containers instead.
        // Matches Minecraft's own limit. Real NBT is nowhere near this deep.
        internal const int MaxDepth = 512;

        internal const string DepthLimitMessage =
            "NBT tags are nested deeper than the supported limit (512 levels).";

        /// <summary> Parent compound tag, either NbtList or NbtCompound, if any.
        /// May be <c>null</c> for detached tags. </summary>
        public NbtTag? Parent { get; internal set; }

        /// <summary> Type of this tag. </summary>
        public abstract NbtTagType TagType { get; }

        /// <summary> Returns true if tags of this type have a value attached.
        /// All tags except Compound, List, and End have values. </summary>
        public bool HasValue {
            get {
                switch (TagType) {
                    case NbtTagType.Compound:
                    case NbtTagType.End:
                    case NbtTagType.List:
                    case NbtTagType.Unknown:
                        return false;
                    default:
                        return true;
                }
            }
        }

        /// <summary> Name of this tag. May be <c>null</c>.
        /// Renaming a tag that resides in an <c>NbtCompound</c> re-keys it in that compound. </summary>
        /// <exception cref="ArgumentNullException"> If <paramref name="value"/> is <c>null</c>, and <c>Parent</c> tag is an NbtCompound.
        /// Name of tags inside an <c>NbtCompound</c> may not be null. </exception>
        /// <exception cref="ArgumentException"> If this tag resides in an <c>NbtCompound</c>, and a sibling tag with the name already exists. </exception>
        /// <exception cref="InvalidOperationException"> If this tag's parent compound no longer contains it,
        /// which most likely indicates unsynchronized modification from multiple threads. </exception>
        public string? Name {
            get { return name; }
            set {
                if (name == value) {
                    return;
                }

                if (Parent is NbtCompound parentAsCompound) {
                    if (value == null) {
                        throw new ArgumentNullException(nameof(value),
                                                        "Name of tags inside an NbtCompound may not be null.");
                    } else if (name != null) {
                        parentAsCompound.RenameTag(this, name, value);
                    }
                }

                name = value;
            }
        }

        // Used by impls to bypass setter checks (and avoid side effects) when initializing state
        internal string? name;

        /// <summary> Gets the full name of this tag, including all parent tag names, separated by dots.
        /// Unnamed tags show up as empty strings. </summary>
        public string Path {
            get {
                if (Parent == null) {
                    return Name ?? "";
                }
                // Built iteratively: more efficient than recursion and no risk of stack overflow.
                List<NbtTag> segments = new List<NbtTag>();
                for (NbtTag? tag = this; tag != null; tag = tag.Parent) {
                    segments.Add(tag);
                }
                StringBuilder sb = new StringBuilder();
                for (int i = segments.Count - 1; i >= 0; i--) {
                    NbtTag tag = segments[i];
                    if (tag.Parent is NbtList parentAsList) {
                        sb.Append('[').Append(parentAsList.IndexOf(tag)).Append(']');
                    } else {
                        if (tag.Parent != null) sb.Append('.');
                        sb.Append(tag.Name);
                    }
                }
                return sb.ToString();
            }
        }


        // Whether the given tag is this tag or one of its ancestors.
        // Used to reject additions that would create a reference cycle.
        internal bool IsDescendantOf(NbtTag tag) {
            // Only containers have descendants, so the walk is skipped for value tags
            if (!(tag is NbtCompound || tag is NbtList)) return false;
            for (NbtTag? t = this; t != null; t = t.Parent) {
                if (ReferenceEquals(t, tag)) return true;
            }
            return false;
        }

        // depthBudget is the number of container tags that this call and its descendants may
        // still enter. Value tags leave it untouched; compounds and lists consume one.
        internal abstract bool ReadTag(NbtBinaryReader readStream, int depthBudget);

        internal abstract void WriteTag(NbtBinaryWriter writeStream, int depthBudget);

        // WriteData does not write the tag's ID byte or the name
        internal abstract void WriteData(NbtBinaryWriter writeStream, int depthBudget);


        // Called exactly once when a recursive walk enters a compound or list. Returning the
        // child budget makes the off-by-one rule common to reading, writing, cloning, and validation.
        internal static int ConsumeDepthBudget(int depthBudget) {
            if (depthBudget <= 0) throw new NbtFormatException(DepthLimitMessage);
            return depthBudget - 1;
        }

        #region Shortcuts
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations

        /// <summary> Gets or sets the tag with the specified name. May return <c>null</c>. </summary>
        /// <returns> The tag with the specified key. Null if tag with the given name was not found. </returns>
        /// <param name="tagName"> The name of the tag to get or set. Must match tag's actual name. </param>
        /// <exception cref="InvalidOperationException"> If used on a tag that is not NbtCompound. </exception>
        /// <remarks> ONLY APPLICABLE TO NbtCompound OBJECTS!
        /// Included in NbtTag base class for programmers' convenience, to avoid extra type casts. </remarks>
        public virtual NbtTag? this[string tagName] {
            get { throw new InvalidOperationException("String indexers only work on NbtCompound tags."); }
            set { throw new InvalidOperationException("String indexers only work on NbtCompound tags."); }
        }

        /// <summary> Gets or sets the tag at the specified index. </summary>
        /// <returns> The tag at the specified index. </returns>
        /// <param name="tagIndex"> The zero-based index of the tag to get or set. </param>
        /// <exception cref="ArgumentOutOfRangeException"> tagIndex is not a valid index in this tag. </exception>
        /// <exception cref="ArgumentNullException"> Given tag is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> Given tag's type does not match ListType. </exception>
        /// <exception cref="InvalidOperationException"> If used on a tag that is not NbtList. </exception>
        /// <remarks> ONLY APPLICABLE TO NbtList OBJECTS! The array tags hide this indexer with their own, so those work only through their own type.
        /// Included in NbtTag base class for programmers' convenience, to avoid extra type casts. </remarks>
        public virtual NbtTag this[int tagIndex] {
            get { throw new InvalidOperationException("Integer indexers only work on NbtList tags."); }
            set { throw new InvalidOperationException("Integer indexers only work on NbtList tags."); }
        }

        /// <summary> Returns the value of this tag, cast as a byte.
        /// Only supported by NbtByte tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than NbtByte. </exception>
        public byte ByteValue {
            get {
                if (TagType == NbtTagType.Byte) {
                    return ((NbtByte)this).Value;
                } else {
                    throw new InvalidCastException("Cannot get ByteValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a short (16-bit signed integer).
        /// Only supported by NbtByte and NbtShort. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public short ShortValue {
            get {
                switch (TagType) {
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value;
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value;
                    default:
                        throw new InvalidCastException("Cannot get ShortValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as an int (32-bit signed integer).
        /// Only supported by NbtByte, NbtShort, and NbtInt. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public int IntValue {
            get {
                switch (TagType) {
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value;
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value;
                    case NbtTagType.Int:
                        return ((NbtInt)this).Value;
                    default:
                        throw new InvalidCastException("Cannot get IntValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a long (64-bit signed integer).
        /// Only supported by NbtByte, NbtShort, NbtInt, and NbtLong. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public long LongValue {
            get {
                switch (TagType) {
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value;
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value;
                    case NbtTagType.Int:
                        return ((NbtInt)this).Value;
                    case NbtTagType.Long:
                        return ((NbtLong)this).Value;
                    default:
                        throw new InvalidCastException("Cannot get LongValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a float (single-precision floating point number).
        /// Only supported by NbtFloat and, with loss of precision, by NbtDouble, NbtByte, NbtShort, NbtInt, and NbtLong. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public float FloatValue {
            get {
                switch (TagType) {
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value;
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value;
                    case NbtTagType.Int:
                        return ((NbtInt)this).Value;
                    case NbtTagType.Long:
                        return ((NbtLong)this).Value;
                    case NbtTagType.Float:
                        return ((NbtFloat)this).Value;
                    case NbtTagType.Double:
                        return (float)((NbtDouble)this).Value;
                    default:
                        throw new InvalidCastException("Cannot get FloatValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a double (double-precision floating point number).
        /// Only supported by NbtFloat, NbtDouble, and, with loss of precision, by NbtByte, NbtShort, NbtInt, and NbtLong. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public double DoubleValue {
            get {
                switch (TagType) {
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value;
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value;
                    case NbtTagType.Int:
                        return ((NbtInt)this).Value;
                    case NbtTagType.Long:
                        return ((NbtLong)this).Value;
                    case NbtTagType.Float:
                        return ((NbtFloat)this).Value;
                    case NbtTagType.Double:
                        return ((NbtDouble)this).Value;
                    default:
                        throw new InvalidCastException("Cannot get DoubleValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a byte array.
        /// Only supported by NbtByteArray tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than NbtByteArray. </exception>
        public byte[] ByteArrayValue {
            get {
                if (TagType == NbtTagType.ByteArray) {
                    return ((NbtByteArray)this).Value;
                } else {
                    throw new InvalidCastException("Cannot get ByteArrayValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as an int array.
        /// Only supported by NbtIntArray tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than NbtIntArray. </exception>
        public int[] IntArrayValue {
            get {
                if (TagType == NbtTagType.IntArray) {
                    return ((NbtIntArray)this).Value;
                } else {
                    throw new InvalidCastException("Cannot get IntArrayValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a long array.
        /// Only supported by NbtLongArray tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than NbtLongArray. </exception>
        public long[] LongArrayValue {
            get {
                if (TagType == NbtTagType.LongArray) {
                    return ((NbtLongArray)this).Value;
                } else {
                    throw new InvalidCastException("Cannot get LongArrayValue from " + GetCanonicalTagName(TagType));
                }
            }
        }

        /// <summary> Returns the value of this tag, cast as a string.
        /// Returns exact value for NbtString, and stringified (using InvariantCulture) value for NbtByte, NbtDouble, NbtFloat, NbtInt, NbtLong, and NbtShort.
        /// Not supported by NbtCompound, NbtList, NbtByteArray, NbtIntArray, or NbtLongArray. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public string StringValue {
            get {
                switch (TagType) {
                    case NbtTagType.String:
                        return ((NbtString)this).Value;
                    case NbtTagType.Byte:
                        return ((NbtByte)this).Value.ToString(CultureInfo.InvariantCulture);
                    case NbtTagType.Double:
                        return ((NbtDouble)this).Value.ToString(CultureInfo.InvariantCulture);
                    case NbtTagType.Float:
                        return ((NbtFloat)this).Value.ToString(CultureInfo.InvariantCulture);
                    case NbtTagType.Int:
                        return ((NbtInt)this).Value.ToString(CultureInfo.InvariantCulture);
                    case NbtTagType.Long:
                        return ((NbtLong)this).Value.ToString(CultureInfo.InvariantCulture);
                    case NbtTagType.Short:
                        return ((NbtShort)this).Value.ToString(CultureInfo.InvariantCulture);
                    default:
                        throw new InvalidCastException("Cannot get StringValue from " + GetCanonicalTagName(TagType));
                }
            }
        }
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
        #endregion


        /// <summary> Returns a canonical (Notchy) name for the given NbtTagType,
        /// e.g. "TAG_Byte_Array" for NbtTagType.ByteArray </summary>
        /// <param name="type"> NbtTagType to name. </param>
        /// <returns> String representing the canonical name of a tag,
        /// or null of given TagType does not have a canonical name (e.g. Unknown). </returns>
        public static string? GetCanonicalTagName(NbtTagType type) {
            return type switch {
                NbtTagType.Byte => "TAG_Byte",
                NbtTagType.ByteArray => "TAG_Byte_Array",
                NbtTagType.Compound => "TAG_Compound",
                NbtTagType.Double => "TAG_Double",
                NbtTagType.End => "TAG_End",
                NbtTagType.Float => "TAG_Float",
                NbtTagType.Int => "TAG_Int",
                NbtTagType.IntArray => "TAG_Int_Array",
                NbtTagType.LongArray => "TAG_Long_Array",
                NbtTagType.List => "TAG_List",
                NbtTagType.Long => "TAG_Long",
                NbtTagType.Short => "TAG_Short",
                NbtTagType.String => "TAG_String",
                _ => null,
            };
        }


        // The one tag-type-to-constructor map. Parse paths validate the type before calling.
        // Inlined so each parse site keeps its own jump table.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NbtTag Create(NbtTagType type) {
            switch (type) {
                case NbtTagType.Byte: return new NbtByte();
                case NbtTagType.Short: return new NbtShort();
                case NbtTagType.Int: return new NbtInt();
                case NbtTagType.Long: return new NbtLong();
                case NbtTagType.Float: return new NbtFloat();
                case NbtTagType.Double: return new NbtDouble();
                case NbtTagType.ByteArray: return new NbtByteArray();
                case NbtTagType.String: return new NbtString();
                case NbtTagType.List: return new NbtList();
                case NbtTagType.Compound: return new NbtCompound();
                case NbtTagType.IntArray: return new NbtIntArray();
                case NbtTagType.LongArray: return new NbtLongArray();
                default:
                    throw new NbtFormatException("NBT tag type out of range: " + (int)type);
            }
        }


        /// <summary> Prints contents of this tag, and any child tags, to a string.
        /// Indents the string using multiples of the given indentation string. </summary>
        /// <returns> A string representing contents of this tag, and all child tags (if any). </returns>
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public override string ToString() {
            return ToString(DefaultIndentString);
        }


        /// <summary> Creates a deep copy of this tag. </summary>
        /// <returns> A new NbtTag object that is a deep copy of this instance. </returns>
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public object Clone() {
            return Clone(MaxDepth);
        }


        // Depth-budgeted clone: the one copy each tag implements. Compounds and lists spend the
        // budget on their children and throw for ridiculously deep nesting instead of
        // overflowing the stack; leaves ignore it.
        internal abstract NbtTag Clone(int depthBudget);


        /// <summary> Prints contents of this tag, and any child tags, to a string.
        /// Indents the string using multiples of the given indentation string. </summary>
        /// <param name="indentString"> String to be used for indentation. </param>
        /// <returns> A string representing contents of this tag, and all child tags (if any). </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="indentString"/> is <c>null</c>. </exception>
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public string ToString(string indentString) {
            if (indentString == null) throw new ArgumentNullException(nameof(indentString));
            StringBuilder sb = new StringBuilder();
            PrettyPrint(sb, indentString, 0, MaxDepth);
            return sb.ToString();
        }


        /// <summary> Prints this tag and its children as SNBT (stringified NBT), the text notation
        /// Minecraft Java uses in commands and <c>.snbt</c> files, in the layout given by
        /// <see cref="SnbtOptions.DefaultWriteLayout"/>. The tag's <see cref="Name"/> is not written:
        /// SNBT has no root name, and tags inside a compound are written with their keys already. </summary>
        /// <remarks> The text is readable by every Minecraft Java version since 1.12 and by the
        /// common NBT tools: strings are always quoted, with the delimiter Minecraft would choose and
        /// only that delimiter and backslashes escaped; keys are bare when they consist of letters,
        /// digits and <c>._+-</c>, start with a letter, <c>.</c> or <c>_</c>, and are not <c>true</c>
        /// or <c>false</c>; numbers carry Java's suffixes and spelling (<c>1b</c>, <c>1s</c>, <c>1L</c>,
        /// <c>1.0f</c>, <c>1.0E7d</c>); compounds keep insertion order. Inside a list of compounds, a
        /// one-entry compound whose key is empty prints as its value, the form Minecraft 1.21.5 and
        /// later store on disk for lists of mixed types. Two things no Minecraft version reads back:
        /// <c>NaN</c> and infinities, printed as <c>NaNf</c> or <c>Infinityd</c> the way Minecraft
        /// prints them, and an empty key. <see cref="ParseSnbt(string)"/> reads both. </remarks>
        /// <returns> The SNBT text. </returns>
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public string ToSnbt() {
            return SnbtWriter.Write(this, SnbtOptions.DefaultWriteLayout);
        }


        /// <summary> Prints this tag and its children as SNBT (stringified NBT) with the given
        /// options. See <see cref="ToSnbt()"/> for what is written. </summary>
        /// <param name="options"> Settings to use, read when the call starts. </param>
        /// <returns> The SNBT text. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="options"/> is <c>null</c>. </exception>
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public string ToSnbt(SnbtOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return SnbtWriter.Write(this, options.WriteLayout);
        }


        /// <summary> Parses one SNBT (stringified NBT) value, which must be the whole text apart from
        /// surrounding whitespace. Returns an unnamed tag of any type. </summary>
        /// <remarks> Accepts every syntax Minecraft Java has used since 1.12 plus what common NBT
        /// tools write, and reads the union of them: the 1.21.5 grammar (hex, binary and underscored
        /// numbers, signedness suffixes, the full escape set, <c>bool()</c> and <c>uuid()</c>,
        /// lists of mixed types, one trailing comma) with the earlier parser's readings wherever the
        /// modern one refuses (a token such as <c>1st</c>, <c>007</c> or <c>300b</c> is a string, an
        /// overflowing float is an infinity). <c>NaNf</c>, <c>Infinityd</c> and the like read as the
        /// numbers they name, and a quoted empty key is allowed. A list of mixed types becomes a list
        /// of compounds with each element under an empty key, the form Minecraft 1.21.5 and later
        /// store on disk; an empty list has the <c>End</c> element type. A byte order mark at the
        /// start of the text is skipped. <c>\N{name}</c> escapes are not supported. </remarks>
        /// <param name="text"> The SNBT text. </param>
        /// <returns> The parsed tag, unnamed. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="text"/> is <c>null</c>. </exception>
        /// <exception cref="NbtFormatException"> The text is not SNBT, has anything but whitespace
        /// after the value, or nests deeper than 512 levels. <see cref="NbtFormatException.Index"/>
        /// gives the position. </exception>
        public static NbtTag ParseSnbt(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return SnbtParser.ParseWhole(text);
        }


        /// <summary> Parses one SNBT (stringified NBT) value starting at the given index, stops
        /// after it, and reports how many characters it consumed. Text after the value is left
        /// alone, for pulling a value out of a longer command line. See <see cref="ParseSnbt(string)"/>
        /// for what is accepted. </summary>
        /// <param name="text"> Text containing the SNBT value. </param>
        /// <param name="index"> Position at which the value starts; leading whitespace is skipped. </param>
        /// <param name="charsConsumed"> Number of characters from <paramref name="index"/> to the end
        /// of the value. </param>
        /// <returns> The parsed tag, unnamed. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="text"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="index"/> is negative or past
        /// the end of the text. </exception>
        /// <exception cref="NbtFormatException"> No value starts at the index, or the value nests
        /// deeper than 512 levels. <see cref="NbtFormatException.Index"/> gives the position within
        /// <paramref name="text"/>. </exception>
        public static NbtTag ParseSnbt(string text, int index, out int charsConsumed) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (index < 0 || index > text.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return SnbtParser.ParseValue(text, index, out charsConsumed);
        }


        internal abstract void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget);


        // The indented 'TAG_X("name")' prefix shared by every PrettyPrint override
        internal void PrettyPrintHeader(StringBuilder sb, string indentString, int indentLevel) {
            for (int i = 0; i < indentLevel; i++) {
                sb.Append(indentString);
            }
            sb.Append(GetCanonicalTagName(TagType));
            if (!String.IsNullOrEmpty(Name)) {
                sb.AppendFormat(CultureInfo.InvariantCulture, "(\"{0}\")", Name);
            }
        }


        // The header plus a brace-wrapped, indented child list, shared by compound and list tags
        internal void PrettyPrintContainer(StringBuilder sb, string indentString, int indentLevel, int depthBudget,
                                           ICollection<NbtTag> children) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.AppendFormat(CultureInfo.InvariantCulture, ": {0} entries {{", children.Count);
            if (children.Count > 0) {
                sb.Append('\n');
                foreach (NbtTag child in children) {
                    child.PrettyPrint(sb, indentString, indentLevel + 1, childDepthBudget);
                    sb.Append('\n');
                }
                for (int i = 0; i < indentLevel; i++) {
                    sb.Append(indentString);
                }
            }
            sb.Append('}');
        }

        /// <summary> String to use for indentation in NbtTag's and NbtFile's ToString() methods by default. </summary>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        public static string DefaultIndentString {
            get { return defaultIndentString; }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                defaultIndentString = value;
            }
        }

        static string defaultIndentString = "  ";
    }
}
