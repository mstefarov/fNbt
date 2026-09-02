using System;
using System.Globalization;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing an array of signed 32-bit integers. </summary>
    public sealed class NbtIntArray : NbtTag {
        /// <summary> Type of this tag (IntArray). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.IntArray; }
        }

        /// <summary> Value/payload of this tag (an array of signed 32-bit integers). Value is stored as-is and is NOT cloned. May not be <c>null</c>. </summary>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        public int[] Value {
            get { return ints; }
            set {
                if (value == null) {
                    throw new ArgumentNullException(nameof(value));
                }
                ints = value;
            }
        }

        int[] ints;


        /// <summary> Creates an unnamed NbtIntArray tag, containing an empty array of ints. </summary>
        public NbtIntArray()
            : this((string?)null) { }


        /// <summary> Creates an unnamed NbtIntArray tag, containing the given array of ints. </summary>
        /// <param name="value"> Int array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given int array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a int[]) and then set the Value property yourself. </remarks>
        public NbtIntArray(int[] value)
            : this(null, value) { }


        /// <summary> Creates an NbtIntArray tag with the given name, containing an empty array of ints. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtIntArray(string? tagName) {
            name = tagName;
            ints = Array.Empty<int>();
        }


        /// <summary> Creates an NbtIntArray tag with the given name, containing the given array of ints. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> Int array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given int array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a int[]) and then set the Value property yourself. </remarks>
        public NbtIntArray(string? tagName, int[] value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            name = tagName;
            ints = (int[])value.Clone();
        }


        /// <summary> Creates a deep copy of given NbtIntArray. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <remarks> Int array of given tag will be cloned. </remarks>
        public NbtIntArray(NbtIntArray other) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            name = other.name;
            ints = (int[])other.Value.Clone();
        }


        /// <summary> Gets or sets an integer at the given index. </summary>
        /// <param name="tagIndex"> The zero-based index of the element to get or set. </param>
        /// <returns> The integer at the specified index. </returns>
        /// <exception cref="IndexOutOfRangeException"> <paramref name="tagIndex"/> is outside the array bounds. </exception>
        public new int this[int tagIndex] {
            get { return Value[tagIndex]; }
            set { Value[tagIndex] = value; }
        }


        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            // Negative lengths are tolerated as empty, exceeding Minecraft's own readers on purpose
            int length = Math.Max(0, readStream.ReadInt32());

            if (readStream.Selector != null && !readStream.Selector(this)) {
                readStream.Skip<int>(length);
                return false;
            }

            Value = readStream.ReadInt32Array(length);
            return true;
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.Write(NbtTagType.IntArray);
            if (Name == null) throw new NbtFormatException("Name is null");
            writeStream.Write(Name);
            WriteData(writeStream, depthBudget);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            int[] data = Value;
            writeStream.Write(data.Length);
            writeStream.Write(data, 0, data.Length);
        }


        /// <inheritdoc />
        public override object Clone() {
            return new NbtIntArray(this);
        }


        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.AppendFormat(CultureInfo.InvariantCulture, ": [{0} ints]", ints.Length);
        }
    }
}
