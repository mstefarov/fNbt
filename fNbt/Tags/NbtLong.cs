using System;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing a single signed 64-bit integer. </summary>
    public sealed class NbtLong : NbtTag {
        /// <summary> Type of this tag (Long). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.Long; }
        }

        /// <summary> Value/payload of this tag (a single signed 64-bit integer). </summary>
        public long Value { get; set; }


        /// <summary> Creates an unnamed NbtLong tag with the default value of 0. </summary>
        public NbtLong() { }


        /// <summary> Creates an unnamed NbtLong tag with the given value. </summary>
        /// <param name="value"> Value to assign to this tag. </param>
        public NbtLong(long value)
            : this(null, value) { }


        /// <summary> Creates an NbtLong tag with the given name and the default value of 0. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtLong(string? tagName)
            : this(tagName, 0) { }


        /// <summary> Creates an NbtLong tag with the given name and value. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> Value to assign to this tag. </param>
        public NbtLong(string? tagName, long value) {
            name = tagName;
            Value = value;
        }


        /// <summary> Creates a copy of given NbtLong tag. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        public NbtLong(NbtLong other) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            name = other.name;
            Value = other.Value;
        }


        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            if (readStream.Selector != null && !readStream.Selector(this)) {
                readStream.ReadInt64();
                return false;
            }
            Value = readStream.ReadInt64();
            return true;
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.WriteTagHeader(NbtTagType.Long, Name);
            writeStream.Write(Value);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.Write(Value);
        }


        /// <inheritdoc />
        public override object Clone() {
            return new NbtLong(this);
        }


        internal override NbtTag Clone(int depthBudget) {
            return new NbtLong(this);
        }


        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.Append(": ");
            sb.Append(Value);
        }
    }
}
