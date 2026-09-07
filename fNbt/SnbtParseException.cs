using System;

namespace fNbt {
    /// <summary> Exception thrown by <see cref="NbtTag.ParseSnbt(string)"/> when the text is not
    /// SNBT. Carries where parsing stopped; catching <see cref="NbtFormatException"/> catches it too. </summary>
    [Serializable]
    public sealed class SnbtParseException : NbtFormatException {
        /// <summary> Zero-based index into the text where parsing failed. </summary>
        public int Index { get; }

        /// <summary> One-based line of that position. </summary>
        public int Line { get; }

        /// <summary> One-based column of that position, in UTF-16 characters. </summary>
        public int Column { get; }

        internal SnbtParseException(string message, int index, int line, int column)
            : base(message) {
            Index = index;
            Line = line;
            Column = column;
        }
    }
}
