using System;
using System.Globalization;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing an array of signed 64-bit integers. </summary>
    public sealed class NbtLongArray : NbtTag {
        /// <summary> Type of this tag (LongArray). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.LongArray; }
        }

        /// <summary> Value/payload of this tag (an array of signed 64-bit integers). Value is stored as-is and is NOT cloned. May not be <c>null</c>. </summary>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        public long[] Value {
            get { return longs; }
            set {
                if (value == null) {
                    throw new ArgumentNullException(nameof(value));
                }
                longs = value;
            }
        }

        long[] longs;


        /// <summary> Creates an unnamed NbtLongArray tag, containing an empty array of longs. </summary>
        public NbtLongArray()
            : this((string?)null) { }


        /// <summary> Creates an unnamed NbtLongArray tag, containing the given array of longs. </summary>
        /// <param name="value"> Long array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given long array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a long[]) and then set the Value property yourself. </remarks>
        public NbtLongArray(long[] value)
            : this(null, value) { }


        /// <summary> Creates an NbtLongArray tag with the given name, containing an empty array of longs. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtLongArray(string? tagName) {
            name = tagName;
            longs = Array.Empty<long>();
        }


        /// <summary> Creates an NbtLongArray tag with the given name, containing the given array of longs. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> Long array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given long array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a long[]) and then set the Value property yourself. </remarks>
        public NbtLongArray(string? tagName, long[] value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            name = tagName;
            longs = (long[])value.Clone();
        }


        /// <summary> Creates a deep copy of given NbtLongArray. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <remarks> Long array of given tag will be cloned. </remarks>
        public NbtLongArray(NbtLongArray other) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            name = other.name;
            longs = (long[])other.longs.Clone();
        }


        /// <summary> Gets or sets a long at the given index. </summary>
        /// <param name="index"> The zero-based index of the element to get or set. </param>
        /// <returns> The long at the specified index. </returns>
        /// <exception cref="IndexOutOfRangeException"> <paramref name="index"/> is outside the array bounds. </exception>
        public new long this[int index] {
            get { return Value[index]; }
            set { Value[index] = value; }
        }


        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            // Negative lengths are tolerated as empty, exceeding Minecraft's own readers on purpose
            int length = Math.Max(0, readStream.ReadInt32());

            if (readStream.Selector != null && !readStream.Selector(this)) {
                readStream.Skip<long>(length);
                return false;
            }
            Value = readStream.ReadInt64Array(length);
            return true;
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            writeStream.WriteTagHeader(NbtTagType.LongArray, Name);
            WriteData(writeStream, depthBudget);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            long[] data = Value;
            writeStream.Write(data.Length);
            writeStream.Write(data, 0, data.Length);
        }


        /// <inheritdoc />
        public override object Clone() {
            return new NbtLongArray(this);
        }


        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.AppendFormat(CultureInfo.InvariantCulture, ": [{0} longs]", longs.Length);
        }
    }
}
