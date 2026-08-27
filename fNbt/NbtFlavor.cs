using System;
using System.Threading;

namespace fNbt {
    /// <summary> Identifies one of the NBT wire encodings ("flavors"). Immutable; use the static instances. </summary>
    /// <remarks> A flavor describes an encoding, not a product or a file extension: Bedrock installs contain
    /// big-endian Java-flavor files, and network-flavor blobs occur embedded inside files. </remarks>
    public sealed class NbtFlavor {
        /// <summary> Java Edition 1.12 and later. Big-endian, named <c>TAG_Compound</c> root, all 13 tag types.
        /// Reads every older Java-flavor file. This is the default flavor. </summary>
        public static NbtFlavor Java { get; } =
            new NbtFlavor("Java", true, NbtTagType.LongArray, ushort.MaxValue, NbtCompression.GZip);

        /// <summary> Java Edition 1.2.1 through 1.11. Same as <see cref="Java"/>, but predates
        /// <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaAnvil { get; } =
            new NbtFlavor("JavaAnvil", true, NbtTagType.IntArray, ushort.MaxValue, NbtCompression.GZip);

        /// <summary> The original NBT format: Indev through Java Edition 1.1. Same as <see cref="Java"/>,
        /// but predates <c>TAG_Int_Array</c> and <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaLegacy { get; } =
            new NbtFlavor("JavaLegacy", true, NbtTagType.Compound, ushort.MaxValue, NbtCompression.GZip);

        /// <summary> Java Edition network encoding, protocol 764 (1.20.2) and later. Big-endian like
        /// <see cref="Java"/>, but the root tag is unnamed and may be any tag type (e.g. <c>TAG_String</c>
        /// for text components), and a lone <c>TAG_End</c> byte means an absent document.
        /// Not a file format, though blobs of it may be embedded inside files. </summary>
        public static NbtFlavor JavaNetwork { get; } =
            new NbtFlavor("JavaNetwork", true, NbtTagType.LongArray, ushort.MaxValue, NbtCompression.None,
                          false, false, true);

        /// <summary> Bedrock Edition disk format: <c>level.dat</c>, <c>.mcstructure</c>, and LevelDB values.
        /// Little-endian, named <c>TAG_Compound</c> root, no <c>TAG_Long_Array</c>. Strings are capped at
        /// 32,767 bytes: the length prefix is a signed 16-bit value. </summary>
        public static NbtFlavor Bedrock { get; } =
            new NbtFlavor("Bedrock", false, NbtTagType.IntArray, short.MaxValue, NbtCompression.None);

        /// <summary> Bedrock Edition network encoding, 0.16 and later. Little-endian, with
        /// <c>TAG_Int</c>/<c>TAG_Long</c> values and container lengths as zigzag varints, and string
        /// lengths as unsigned varints. Not implemented yet: reading or writing currently throws
        /// <see cref="NotSupportedException"/>. </summary>
        public static NbtFlavor BedrockNetwork { get; } =
            new NbtFlavor("BedrockNetwork", false, NbtTagType.IntArray, int.MaxValue, NbtCompression.None,
                          true);

        /// <summary> The profile of ClassicWorld (<c>.cw</c>) maps that the ClassiCube client can load:
        /// big-endian with a named <c>TAG_Compound</c> root, GZip-compressed, tag types 0-10, strings up
        /// to 256 bytes. Spec-compliant ClassicWorld files that use <c>TAG_Int_Array</c> match
        /// <see cref="JavaAnvil"/> instead. </summary>
        public static NbtFlavor ClassiCube { get; } =
            new NbtFlavor("ClassiCube", true, NbtTagType.Compound, 256, NbtCompression.GZip);


        /// <summary> Short display name of this flavor, e.g. "Java" or "BedrockNetwork". </summary>
        public string Name { get; }

        /// <summary> Whether multi-byte values are big-endian. True for the Java flavors and ClassiCube,
        /// false for the Bedrock flavors. </summary>
        public bool BigEndian { get; }

        /// <summary> Whether <c>TAG_Int</c>/<c>TAG_Long</c> values and length prefixes use variable-length
        /// encoding. True only for <see cref="BedrockNetwork"/>. </summary>
        public bool UsesVarInts { get; }

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

        /// <summary> Compression conventionally used for this flavor's files. Informational: APIs that
        /// take an explicit <see cref="NbtCompression"/> are unaffected, and blobs are never
        /// compressed at the NBT layer. </summary>
        public NbtCompression DefaultCompression { get; }


        // Whether validation with this flavor can reject anything the wire format itself allows.
        // Flavors without restrictions skip validation entirely.
        internal bool HasRestrictions {
            get { return MaxTagType < NbtTagType.LongArray || MaxStringBytes < ushort.MaxValue; }
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


        NbtFlavor(string name, bool bigEndian, NbtTagType maxTagType, int maxStringBytes,
                  NbtCompression defaultCompression, bool usesVarInts = false,
                  bool hasRootName = true, bool allowsNonCompoundRoot = false) {
            Name = name;
            BigEndian = bigEndian;
            MaxTagType = maxTagType;
            MaxStringBytes = maxStringBytes;
            DefaultCompression = defaultCompression;
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
