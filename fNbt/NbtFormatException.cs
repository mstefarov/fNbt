using System;

namespace fNbt {
    /// <summary> Exception thrown when a format violation is detected while
    /// parsing or serializing an NBT file. </summary>
    [Serializable]
    public sealed class NbtFormatException : Exception {
        /// <summary> Zero-based index into the SNBT text where parsing failed, or -1 when this
        /// exception did not come from parsing SNBT. </summary>
        public int Index { get; }

        internal NbtFormatException(string message)
            : base(message) {
            Index = -1;
        }

        internal NbtFormatException(string message, Exception innerException)
            : base(message, innerException) {
            Index = -1;
        }

        internal NbtFormatException(string message, int index)
            : base(message) {
            Index = index;
        }


        // Conditions that several layers detect, worded once

        internal static NbtFormatException NotPermitted(NbtFlavor flavor, NbtTagType type) {
            return new NbtFormatException(
                NbtTag.GetCanonicalTagName(type) + " is not permitted by the " + flavor.Name + " flavor.");
        }


        internal static NbtFormatException StringTooLong(NbtFlavor flavor, long byteCount) {
            return new NbtFormatException(
                "String is " + byteCount + " bytes, but the " + flavor.Name +
                " flavor allows at most " + flavor.MaxStringBytes + ".");
        }


        internal static NbtFormatException NotCompoundRoot() {
            return new NbtFormatException("Given NBT stream does not start with a TAG_Compound.");
        }


        internal static NbtFormatException UnnamedChild() {
            return new NbtFormatException("Tags inside a compound must be named.");
        }


        internal static NbtFormatException UnknownListType() {
            return new NbtFormatException("NbtList had no elements and an Unknown ListType");
        }

        internal static NbtFormatException StringEncoding(Exception ex) {
            return new NbtFormatException("String contains a lone surrogate, which cannot be encoded as standard UTF-8.", ex);
        }
    }
}
