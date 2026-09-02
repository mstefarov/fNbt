using System;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing a single signed 32-bit integer. </summary>
    public sealed class NbtInt : NbtTag {
        /// <summary> Type of this tag (Int). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.Int; }
        }

        /// <summary> Value/payload of this tag (a single signed 32-bit integer). </summary>
        public int Value { get; set; }


        /// <summary> Creates an unnamed NbtInt tag with the default value of 0. </summary>
        public NbtInt() { }


        /// <summary> Creates an unnamed NbtInt tag with the given value. </summary>
        /// <param name="value"> Value to assign to this tag. </param>
        public NbtInt(int value)
            : this(null, value) { }


        /// <summary> Creates an NbtInt tag with the given name and the default value of 0. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtInt(string? tagName)
            : this(tagName, 0) { }


        /// <summary> Creates an NbtInt tag with the given name and value. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> Value to assign to this tag. </param>
        public NbtInt(string? tagName, int value) {
            name = tagName;
            Value = value;
        }


        /// <summary> Creates a copy of given NbtInt tag. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        public NbtInt(NbtInt other) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            name = other.name;
            Value = other.Value;
        }


        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            if (readStream.Selector != null && !readStream.Selector(this)) {
                readStream.ReadInt32();
                return false;
            }
            Value = readStream.ReadInt32();
            return true;
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.WriteTagHeader(NbtTagType.Int, Name);
            writeStream.Write(Value);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.Write(Value);
        }


        /// <inheritdoc />
        public override object Clone() {
            return new NbtInt(this);
        }


        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.Append(": ");
            sb.Append(Value);
        }
    }
}
