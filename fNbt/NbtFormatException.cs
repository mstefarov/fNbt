using System;

namespace fNbt {
    /// <summary> Exception thrown when a format violation is detected while
    /// parsing or serializing an NBT file. <see cref="SnbtParseException"/> is the form
    /// thrown for SNBT text, and carries the position. </summary>
    [Serializable]
    public class NbtFormatException : Exception {
        internal NbtFormatException(string message)
            : base(message) { }

        internal NbtFormatException(string message, Exception innerException)
            : base(message, innerException) { }


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


        internal static NbtFormatException StringEncoding(Exception ex) {
            return new NbtFormatException("String contains a lone surrogate, which cannot be encoded as standard UTF-8.", ex);
        }


        internal const string DepthLimitMessage =
            "NBT tags are nested deeper than the supported limit (512 levels).";

        internal static NbtFormatException DepthLimit() {
            return new NbtFormatException(DepthLimitMessage);
        }
    }
}
