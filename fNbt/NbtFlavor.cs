using System;
using System.Text;
using System.Threading;

namespace fNbt {
    /// <summary> Identifies one of the NBT wire encodings ("flavors"). Immutable; use the static instances. </summary>
    /// <remarks> A flavor describes an encoding, not a product or a file extension: Bedrock installs contain
    /// big-endian Java-flavor files, and network-flavor blobs occur embedded inside files. </remarks>
    public sealed class NbtFlavor {
        /// <summary> Java Edition 1.12 and later. Big-endian, named <c>TAG_Compound</c> root, all 13 tag types.
        /// Reads every older Java-flavor file. This is the default flavor. </summary>
        public static NbtFlavor Java { get; } =
            new NbtFlavor("Java", bigEndian: true, modifiedUtf8: true,
                          NbtTagType.LongArray, ushort.MaxValue);

        /// <summary> Java Edition 1.2.1 through 1.11. Same as <see cref="Java"/>, but predates
        /// <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaAnvil { get; } =
            new NbtFlavor("JavaAnvil", bigEndian: true, modifiedUtf8: true,
                          NbtTagType.IntArray, ushort.MaxValue);

        /// <summary> The original NBT format: Indev through Java Edition 1.1. Same as <see cref="Java"/>,
        /// but predates <c>TAG_Int_Array</c> and <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaLegacy { get; } =
            new NbtFlavor("JavaLegacy", bigEndian: true, modifiedUtf8: true,
                          NbtTagType.Compound, ushort.MaxValue);

        /// <summary> Java Edition network encoding, protocol 764 (1.20.2) and later. Big-endian like
        /// <see cref="Java"/>, but the root tag is unnamed and may be any tag type (e.g. <c>TAG_String</c>
        /// for text components), and a lone <c>TAG_End</c> byte means an absent document.
        /// Not a file format, though blobs of it may be embedded inside files. </summary>
        public static NbtFlavor JavaNetwork { get; } =
            new NbtFlavor("JavaNetwork", bigEndian: true, modifiedUtf8: true,
                          NbtTagType.LongArray, ushort.MaxValue,
                          hasRootName: false, allowsNonCompoundRoot: true);

        /// <summary> Bedrock Edition disk format: <c>level.dat</c>, <c>.mcstructure</c>, and LevelDB values.
        /// Little-endian, named <c>TAG_Compound</c> root, no <c>TAG_Long_Array</c>. Strings are capped at
        /// 32,767 bytes: the length prefix is a signed 16-bit value. </summary>
        public static NbtFlavor Bedrock { get; } =
            new NbtFlavor("Bedrock", bigEndian: false, modifiedUtf8: false,
                          NbtTagType.IntArray, short.MaxValue);

        /// <summary> Bedrock Edition network encoding, 0.16 and later. Little-endian, with
        /// <c>TAG_Int</c>/<c>TAG_Long</c> values and container lengths as zigzag varints, and string
        /// lengths as unsigned varints. Not a file format, though blobs of it may be embedded
        /// inside files. </summary>
        public static NbtFlavor BedrockNetwork { get; } =
            new NbtFlavor("BedrockNetwork", bigEndian: false, modifiedUtf8: false,
                          NbtTagType.IntArray, int.MaxValue,
                          usesVarInts: true);

        /// <summary> The profile of ClassicWorld (<c>.cw</c>) maps that the ClassiCube client can load:
        /// big-endian with a named <c>TAG_Compound</c> root, GZip-compressed, tag types 0-10, strings up
        /// to 256 bytes. Spec-compliant ClassicWorld files that use <c>TAG_Int_Array</c> match
        /// <see cref="JavaAnvil"/> instead. </summary>
        public static NbtFlavor ClassiCube { get; } =
            new NbtFlavor("ClassiCube", bigEndian: true, modifiedUtf8: true,
                          NbtTagType.Compound, 256);


        /// <summary> Short display name of this flavor, e.g. "Java" or "BedrockNetwork". </summary>
        public string Name { get; }

        /// <summary> Whether multi-byte values are big-endian. True for the Java flavors and ClassiCube,
        /// false for the Bedrock flavors. </summary>
        public bool BigEndian { get; }

        /// <summary> Whether <c>TAG_Int</c>/<c>TAG_Long</c> values and length prefixes use variable-length
        /// encoding. True only for <see cref="BedrockNetwork"/>. </summary>
        public bool UsesVarInts { get; }

        /// <summary> Whether strings are written as Java's modified UTF-8 (CESU-8 pairs for
        /// astral characters, the overlong <c>C0 80</c> form for NUL, lone surrogates preserved).
        /// True for the Java flavors and ClassiCube; the Bedrock flavors write standard UTF-8.
        /// Reads accept both encodings on every flavor. </summary>
        public bool UsesModifiedUtf8 { get; }

        /// <summary> Whether a document's root tag carries a name. False only for
        /// <see cref="JavaNetwork"/>. </summary>
        public bool HasRootName { get; }

        /// <summary> Whether a document's root may be a tag of any type. When false, the root must be a
        /// <c>TAG_Compound</c>. When true, a lone <c>TAG_End</c> byte is also valid and means an absent
        /// document. True only for <see cref="JavaNetwork"/>. </summary>
        public bool AllowsNonCompoundRoot { get; }

        /// <summary> The highest tag type this flavor's reference reader accepts. Enforced when
        /// validation is enabled (see <see cref="NbtOptions"/>); plain reads stay generous and accept
        /// every tag type. </summary>
        public NbtTagType MaxTagType { get; }

        /// <summary> The longest string, in encoded bytes, this flavor can carry: 65,535 for the Java
        /// flavors (unsigned 16-bit prefix), 32,767 for <see cref="Bedrock"/> (signed prefix), 256 for
        /// <see cref="ClassiCube"/> (client limit), unbounded for <see cref="BedrockNetwork"/> (varint
        /// prefix). Ceilings below 65,535 are enforced only when validation is enabled. </summary>
        public int MaxStringBytes { get; }

        // Whether validation with this flavor can reject anything inside a document that the
        // wire format itself allows. Root shape is not counted: the named-root entry points only
        // ever handle compound roots, and NbtCodec checks it on its own.
        internal bool HasRestrictions {
            get { return MaxTagType < NbtTagType.LongArray || MaxStringBytes < ushort.MaxValue; }
        }


        // Guard for the named-root APIs (NbtFile, NbtReader, NbtWriter), which cannot express
        // unnamed-root flavors. NbtCodec permits JavaNetwork.
        internal void EnsureUsableForFiles(string paramName) {
            if (!HasRootName) {
                throw new ArgumentException(
                    "The " + Name + " flavor has no root name; use NbtCodec to read and write it.", paramName);
            }
        }


        // Conformance pre-walk for write validation, run only for flavors with restrictions:
        // every tag type within the flavor's range, every name and string value within its ceiling.
        // Containers consume one unit from the same remaining-depth budget as the write walk,
        // so a maximally deep tree that writes cleanly also validates.
        internal void ValidateTree(NbtTag tag, int depthBudget) {
            if (tag.TagType > MaxTagType) {
                throw new NbtFormatException(
                    NbtTag.GetCanonicalTagName(tag.TagType) + " is not permitted by the " + Name + " flavor.");
            }
            if (tag.Name != null) {
                ValidateString(tag.Name);
            }
            switch (tag.TagType) {
                case NbtTagType.String:
                    ValidateString(((NbtString)tag).Value);
                    break;
                case NbtTagType.Compound:
                    int compoundChildBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    // Walk the concrete collections so their struct enumerators stay unboxed.
                    foreach (NbtTag child in ((NbtCompound)tag).tags.Values) {
                        ValidateTree(child, compoundChildBudget);
                    }
                    break;
                case NbtTagType.List:
                    int listChildBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    var list = (NbtList)tag;
                    // The element type is written even for empty lists, so it needs its own check
                    if (list.ListType > MaxTagType) {
                        throw new NbtFormatException(
                            NbtTag.GetCanonicalTagName(list.ListType) + " is not permitted by the " +
                            Name + " flavor.");
                    }
                    foreach (NbtTag child in list.tags) {
                        ValidateTree(child, listChildBudget);
                    }
                    break;
            }
        }


        internal void ValidateString(string value) {
            // Neither encoding exceeds 4 bytes per char, so short strings skip the exact count.
            // Encoding errors are still reported by NbtBinaryWriter when the string is emitted.
            if ((long)value.Length * 4 <= MaxStringBytes) return;
            long byteCount = NbtStringCodec.GetByteCount(value, UsesModifiedUtf8);
            if (byteCount > MaxStringBytes) {
                throw new NbtFormatException(
                    "String is " + byteCount + " bytes, but the " + Name +
                    " flavor allows at most " + MaxStringBytes + ".");
            }
        }


        // One shared default-options codec per flavor, backing NbtCodec.For.
        NbtCodec? defaultCodec;

        internal NbtCodec DefaultCodec {
            get {
                NbtCodec? codec = defaultCodec;
                if (codec == null) {
                    codec = new NbtCodec(this);
                    // A benign race: codecs are immutable, so either instance works
                    codec = Interlocked.CompareExchange(ref defaultCodec, codec, null) ?? codec;
                }
                return codec;
            }
        }


        NbtFlavor(string name, bool bigEndian, bool modifiedUtf8, NbtTagType maxTagType,
                  int maxStringBytes, bool usesVarInts = false,
                  bool hasRootName = true, bool allowsNonCompoundRoot = false) {
            Name = name;
            BigEndian = bigEndian;
            UsesModifiedUtf8 = modifiedUtf8;
            MaxTagType = maxTagType;
            MaxStringBytes = maxStringBytes;
            UsesVarInts = usesVarInts;
            HasRootName = hasRootName;
            AllowsNonCompoundRoot = allowsNonCompoundRoot;
        }


        /// <summary> Returns this flavor's <see cref="Name"/>. </summary>
        public override string ToString() {
            return Name;
        }
    }
}
