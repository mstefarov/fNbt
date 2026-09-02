using System;

namespace fNbt {
    /// <summary> Exception thrown when a format violation is detected while
    /// parsing or serializing an NBT file. </summary>
    [Serializable]
    public sealed class NbtFormatException : Exception {
        internal NbtFormatException(string message)
            : base(message) { }

        internal NbtFormatException(string message, Exception innerException)
            : base(message, innerException) { }


        // Conditions that several layers detect, worded once

        internal static NbtFormatException NotPermitted(NbtFlavor flavor, NbtTagType type) {
            return new NbtFormatException(
                NbtTag.GetCanonicalTagName(type) + " is not permitted by the " + flavor.Name + " flavor.");
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
    }
}
