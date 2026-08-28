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
    }
}
