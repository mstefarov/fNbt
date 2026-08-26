using System;

namespace fNbt {
    /// <summary> Identifies one of the NBT wire encodings ("flavors"). Immutable; use the static instances. </summary>
    /// <remarks> A flavor describes an encoding, not a product or a file extension: Bedrock installs contain
    /// big-endian Java-flavor files, and network-flavor blobs occur embedded inside files. </remarks>
    public sealed class NbtFlavor {
        /// <summary> Java Edition 1.12 and later. Big-endian, named <c>TAG_Compound</c> root, all 13 tag types.
        /// Reads every older Java-flavor file. This is the default flavor. </summary>
        public static NbtFlavor Java { get; } = new NbtFlavor("Java", true);

        /// <summary> Java Edition 1.2.1 through 1.11. Same as <see cref="Java"/>, but predates
        /// <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaAnvil { get; } = new NbtFlavor("JavaAnvil", true);

        /// <summary> The original NBT format: Indev through Java Edition 1.1. Same as <see cref="Java"/>,
        /// but predates <c>TAG_Int_Array</c> and <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor JavaLegacy { get; } = new NbtFlavor("JavaLegacy", true);

        /// <summary> Java Edition network encoding, protocol 764 (1.20.2) and later. Big-endian like
        /// <see cref="Java"/>, but the root tag is unnamed and may be any tag type (e.g. <c>TAG_String</c>
        /// for text components), and a lone <c>TAG_End</c> byte means an absent document.
        /// Not a file format, though blobs of it may be embedded inside files. </summary>
        public static NbtFlavor JavaNetwork { get; } = new NbtFlavor("JavaNetwork", true, false, false, true);

        /// <summary> Bedrock Edition disk format: <c>level.dat</c>, <c>.mcstructure</c>, and LevelDB values.
        /// Little-endian, named <c>TAG_Compound</c> root, no <c>TAG_Long_Array</c>. </summary>
        public static NbtFlavor Bedrock { get; } = new NbtFlavor("Bedrock", false);

        /// <summary> Bedrock Edition network encoding, 0.16 and later. Little-endian, with
        /// <c>TAG_Int</c>/<c>TAG_Long</c> values and container lengths as zigzag varints, and string
        /// lengths as unsigned varints. Not implemented yet: reading or writing currently throws
        /// <see cref="NotSupportedException"/>. </summary>
        public static NbtFlavor BedrockNetwork { get; } = new NbtFlavor("BedrockNetwork", false, true);

        /// <summary> ClassicWorld (<c>.cw</c>) maps, used by ClassiCube, MCGalaxy, and fCraft.
        /// Big-endian with a named <c>TAG_Compound</c> root, like <see cref="JavaAnvil"/>. </summary>
        public static NbtFlavor ClassicWorld { get; } = new NbtFlavor("ClassicWorld", true);


        /// <summary> Short display name of this flavor, e.g. "Java" or "BedrockNetwork". </summary>
        public string Name { get; }

        /// <summary> Whether multi-byte values are big-endian. True for the Java flavors and ClassicWorld,
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


        NbtFlavor(string name, bool bigEndian, bool usesVarInts = false,
                  bool hasRootName = true, bool allowsNonCompoundRoot = false) {
            Name = name;
            BigEndian = bigEndian;
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
