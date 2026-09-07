using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace fNbt {
    /// <summary> An efficient writer for writing NBT data directly to streams.
    /// Each instance of NbtWriter writes one complete file.
    /// NbtWriter enforces the structural rules of the NBT format, except that it does not check for
    /// duplicate tag names within a compound. The flavor's own restrictions, such as its permitted
    /// tag types and string ceilings, are enforced only while <see cref="NbtOptions.ValidateOnWrite"/>
    /// is on. </summary>
    /// <remarks> Every check the writer can make up front runs before any byte is written or a
    /// list slot consumed, so a refused call leaves the writer as it was. A write that fails after
    /// committing bytes, an I/O error for example, leaves the writer in an error state where every
    /// later call throws <see cref="NbtFormatException"/>. See <see cref="IsInErrorState"/>. </remarks>
    public sealed class NbtWriter {
        const int MaxStreamCopyBufferSize = 8 * 1024;

        readonly NbtBinaryWriter writer;
        readonly NbtFlavor flavor;
        // True only when this writer enforces a flavor's optional conformance restrictions.
        readonly bool validates;
        // Lowered from LongArray only when write validation is on for a restricting flavor
        readonly NbtTagType maxTagType = NbtTagType.LongArray;

        // One entered compound or list. The hot parent context lives in a field and only its
        // ancestors are stacked, so nesting allocates no per-level objects.
        struct Node {
            public NbtTagType Type;
            public NbtTagType ElementType;
            public int Length;
            // Index of the next list element to be committed
            public int NextIndex;

            public Node(NbtTagType type, NbtTagType elementType = NbtTagType.Unknown, int length = 0) {
                Type = type;
                ElementType = elementType;
                Length = length;
                NextIndex = 0;
            }
        }

        Node container;
        Stack<Node>? ancestors;


        /// <summary> Initializes a new instance of the NbtWriter class with the current defaults
        /// (<see cref="NbtOptions.DefaultFlavor"/> and the other <c>NbtOptions</c> defaults). </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="rootTagName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable. </exception>
        public NbtWriter(Stream stream, string rootTagName)
            : this(stream, rootTagName, NbtOptions.ResolveDefaults()) { }


        /// <summary> Initializes a new instance of the NbtWriter class. </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <param name="bigEndian"> Whether NBT data should be in Big-Endian encoding. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="rootTagName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable. </exception>
        [Obsolete("Use NbtWriter(Stream, string, NbtFlavor) instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public NbtWriter(Stream stream, string rootTagName, bool bigEndian)
            : this(stream, rootTagName, bigEndian ? NbtFlavor.Java : NbtFlavor.Bedrock) { }


        /// <summary> Initializes a new instance of the NbtWriter class for the given flavor,
        /// with the current default policy settings. </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <param name="flavor"> Encoding to write with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/>, <paramref name="rootTagName"/>,
        /// or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable;
        /// or the flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        public NbtWriter(Stream stream, string rootTagName, NbtFlavor flavor)
            : this(stream, rootTagName, NbtOptions.ResolveForFile(flavor, nameof(flavor))) { }


        /// <summary> Initializes a new instance of the NbtWriter class with the given options.
        /// When write validation is on, the flavor's tag-type range and string ceiling are
        /// enforced as tags are written. </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/>, <paramref name="rootTagName"/>,
        /// <paramref name="options"/>, or the options' <c>Flavor</c> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable;
        /// or the options' flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <c>MaxAllocation</c> is zero or negative. </exception>
        public NbtWriter(Stream stream, string rootTagName, NbtOptions options)
            : this(stream, rootTagName, NbtOptions.ResolveForFile(options, nameof(options))) { }


        // Resolve validates MaxAllocation; writing itself allocates nothing based on input
        NbtWriter(Stream stream, string rootTagName, NbtOptions.Resolved resolved) {
            if (rootTagName == null) throw new ArgumentNullException(nameof(rootTagName));
            flavor = resolved.Flavor;
            validates = resolved.ValidateOnWrite && flavor.HasRestrictions;
            writer = new NbtBinaryWriter(stream, flavor, validates);
            if (validates) {
                maxTagType = flavor.MaxTagType;
            }
            int nameBytes = MeasureString(rootTagName, nameof(rootTagName), out bool nameModified);
            writer.Write((byte)NbtTagType.Compound);
            writer.Write(rootTagName, nameBytes, nameModified);
            container = new Node(NbtTagType.Compound);
        }


        /// <summary> The flavor this writer encodes with, fixed at construction. </summary>
        public NbtFlavor Flavor {
            get { return flavor; }
        }

        /// <summary> Gets whether the root tag has been closed.
        /// No more tags may be written after the root tag has been closed. </summary>
        public bool IsDone {
            get { return container.Type == NbtTagType.End; }
        }

        /// <summary> Gets whether an earlier write failed after emitting bytes, an I/O error for
        /// example. The document cannot be finished: every later write, End, and
        /// <see cref="Finish"/> call throws <see cref="NbtFormatException"/>, and the partial
        /// output should be discarded. A write refused before it emitted anything leaves the
        /// writer usable and does not set this. </summary>
        /// <remarks> A stream that reads this from inside its own Write sees <c>true</c> for the
        /// write in flight: that state and a failure share one sentinel. </remarks>
        public bool IsInErrorState {
            get { return IsFailed; }
        }

        /// <summary> Gets the underlying stream of the NbtWriter, flushing buffered output first. </summary>
        /// <exception cref="IOException"> Flushing the stream failed. </exception>
        public Stream BaseStream {
            get {
                // Deliberately not part of the state machine: a stream that reads this property
                // from its own Write, or a thread polling it, would otherwise see the in-flight
                // sentinel as a failure
                return writer.BaseStream;
            }
        }


        #region Compounds and Lists

        /// <summary> Begins an unnamed compound tag. </summary>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named compound tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// tags are nested more than 512 levels deep. </exception>
        public void BeginCompound() {
            ValidateConstraints(null, NbtTagType.Compound);
            EnsureCanGoDown();
            CommitTag();
            GoDown(new Node(NbtTagType.Compound));
        }


        /// <summary> Begins a named compound tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed compound tag was expected -OR- a tag of a different type was expected -OR-
        /// tags are nested more than 512 levels deep. </exception>
        public void BeginCompound(string tagName) {
            EnsureCanGoDown();
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Compound);
            CommitEmission(parentType);
            GoDown(new Node(NbtTagType.Compound));
        }


        /// <summary> Ends a compound tag. </summary>
        /// <exception cref="NbtFormatException"> Not currently in a compound -OR-
        /// an earlier write failed partway through. </exception>
        public void EndCompound() {
            ThrowIfFailed();
            if (IsDone || container.Type != NbtTagType.Compound) {
                throw new NbtFormatException("Not currently in a compound.");
            }
            NbtTagType compoundType = BeginEmission();
            writer.Write(NbtTagType.End);
            RestoreAfterEmission(compoundType);
            GoUp();
        }


        // Everything BeginList refuses regardless of the name: element type against the
        // wire format and the flavor ceiling, size against the format
        void ValidateListHeader(NbtTagType elementType, int size) {
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size), "List size may not be negative.");
            }
            // Modern Minecraft (Java and Bedrock) uses TAG_End as the element type of an empty list.
            if (elementType < NbtTagType.Byte || elementType > NbtTagType.LongArray) {
                if (elementType != NbtTagType.End || size != 0) {
                    throw new ArgumentOutOfRangeException(nameof(elementType));
                }
            }
            if (elementType > maxTagType) {
                throw NbtFormatException.NotPermitted(flavor, elementType);
            }
        }


        /// <summary> Begins an unnamed list tag. </summary>
        /// <param name="elementType"> Type of elements of this list. </param>
        /// <param name="size"> Number of elements in this list. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named list tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// tags are nested more than 512 levels deep -OR-
        /// enabled validation does not permit <paramref name="elementType"/> for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="size"/> is negative -OR-
        /// <paramref name="elementType"/> is not a valid list element type
        /// (End is allowed only when <paramref name="size"/> is 0). </exception>
        public void BeginList(NbtTagType elementType, int size) {
            ValidateListHeader(elementType, size);
            EnsureCanGoDown();
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.List);
            writer.Write((byte)elementType);
            writer.Write(size);
            CommitEmission(parentType);
            GoDown(new Node(NbtTagType.List, elementType, size));
        }


        /// <summary> Begins a named list tag. </summary>
        /// <param name="tagName"> Name to give to this list tag. May not be null. </param>
        /// <param name="elementType"> Type of elements of this list. </param>
        /// <param name="size"> Number of elements in this list. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed list tag was expected -OR- a tag of a different type was expected -OR-
        /// tags are nested more than 512 levels deep -OR-
        /// enabled validation does not permit <paramref name="elementType"/> for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="size"/> is negative -OR-
        /// <paramref name="elementType"/> is not a valid list element type
        /// (End is allowed only when <paramref name="size"/> is 0). </exception>
        public void BeginList(string tagName, NbtTagType elementType, int size) {
            ValidateListHeader(elementType, size);
            EnsureCanGoDown();
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.List);
            writer.Write((byte)elementType);
            writer.Write(size);
            CommitEmission(parentType);
            GoDown(new Node(NbtTagType.List, elementType, size));
        }


        /// <summary> Ends a list tag. </summary>
        /// <exception cref="NbtFormatException"> Not currently in a list -OR-
        /// not all list elements have been written yet -OR-
        /// an earlier write failed partway through. </exception>
        public void EndList() {
            ThrowIfFailed();
            if (container.Type != NbtTagType.List || IsDone) {
                throw new NbtFormatException("Not currently in a list.");
            } else if (container.NextIndex < container.Length) {
                throw new NbtFormatException("Cannot end list: not all list elements have been written yet. " +
                                             "Expected: " + container.Length + ", written: " +
                                             container.NextIndex);
            }
            GoUp();
        }

        #endregion


        #region Value Tags

        /// <summary> Writes an unnamed byte tag. </summary>
        /// <param name="value"> The unsigned byte to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named byte tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteByte(byte value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Byte);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named byte tag. </summary>
        /// <param name="tagName"> Name to give to this byte tag. May not be null. </param>
        /// <param name="value"> The unsigned byte to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteByte(string tagName, byte value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Byte);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed double tag. </summary>
        /// <param name="value"> The eight-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named double tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteDouble(double value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Double);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named double tag. </summary>
        /// <param name="tagName"> Name to give to this double tag. May not be null. </param>
        /// <param name="value"> The eight-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed double tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteDouble(string tagName, double value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Double);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed float tag. </summary>
        /// <param name="value"> The four-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named float tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteFloat(float value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Float);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named float tag. </summary>
        /// <param name="tagName"> Name to give to this float tag. May not be null. </param>
        /// <param name="value"> The four-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed float tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteFloat(string tagName, float value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Float);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed int tag. </summary>
        /// <param name="value"> The four-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named int tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteInt(int value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Int);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named int tag. </summary>
        /// <param name="tagName"> Name to give to this int tag. May not be null. </param>
        /// <param name="value"> The four-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed int tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteInt(string tagName, int value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Int);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed long tag. </summary>
        /// <param name="value"> The eight-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named long tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteLong(long value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Long);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named long tag. </summary>
        /// <param name="tagName"> Name to give to this long tag. May not be null. </param>
        /// <param name="value"> The eight-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed long tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteLong(string tagName, long value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Long);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed short tag. </summary>
        /// <param name="value"> The two-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named short tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteShort(short value) {
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.Short);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named short tag. </summary>
        /// <param name="tagName"> Name to give to this short tag. May not be null. </param>
        /// <param name="value"> The two-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed short tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteShort(string tagName, short value) {
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.Short);
            writer.Write(value);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed string tag. </summary>
        /// <param name="value"> The string to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named string tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// <paramref name="value"/> is longer than the flavor's limit (65,535 bytes for the Java flavors). </exception>
        public void WriteString(string value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateConstraints(null, NbtTagType.String);
            int valueBytes = MeasureString(value, nameof(value), out bool valueModified);
            NbtTagType parentType = BeginEmission();
            writer.Write(value, valueBytes, valueModified);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named string tag. </summary>
        /// <param name="tagName"> Name to give to this string tag. May not be null. </param>
        /// <param name="value"> The string to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed string tag was expected -OR- a tag of a different type was expected -OR-
        /// <paramref name="value"/> is longer than the flavor's limit (65,535 bytes for the Java flavors). </exception>
        public void WriteString(string tagName, string value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateConstraints(tagName, NbtTagType.String);
            int nameBytes = MeasureString(tagName, nameof(tagName), out bool nameModified);
            int valueBytes = MeasureString(value, nameof(value), out bool valueModified);
            NbtTagType parentType = BeginEmission();
            WriteHeader(NbtTagType.String, tagName, nameBytes, nameModified);
            writer.Write(value, valueBytes, valueModified);
            CommitEmission(parentType);
        }

        #endregion


        #region ByteArray, IntArray and LongArray

        /// <summary> Writes an unnamed byte array tag, copying data from an array. </summary>
        /// <param name="data"> A byte array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named byte array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        public void WriteByteArray(byte[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteByteArray(data, 0, data.Length);
        }


        /// <summary> Writes an unnamed byte array tag, copying data from an array. </summary>
        /// <param name="data"> A byte array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named byte array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteByteArray(byte[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.ByteArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named byte array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this byte array tag. May not be null. </param>
        /// <param name="data"> A byte array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        public void WriteByteArray(string tagName, byte[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteByteArray(tagName, data, 0, data.Length);
        }


        /// <summary> Writes a named byte array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this byte array tag. May not be null. </param>
        /// <param name="data"> A byte array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteByteArray(string tagName, byte[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.ByteArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }


        /// <summary> Writes an unnamed byte array tag, copying data from a stream. </summary>
        /// <remarks> A temporary buffer will be allocated, of size up to 8192 bytes.
        /// To manually specify a buffer, use one of the other WriteByteArray() overloads. </remarks>
        /// <param name="dataSource"> A Stream from which data will be copied. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named byte array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="dataSource"/> is null. </exception>
        /// <exception cref="ArgumentException"> Given stream does not support reading. </exception>
        public void WriteByteArray(Stream dataSource, int count) {
            WriteByteArray(dataSource, count, AllocateStreamCopyBuffer(dataSource, count));
        }


        /// <summary> Writes an unnamed byte array tag, copying data from a stream. </summary>
        /// <param name="dataSource"> A Stream from which data will be copied. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <param name="buffer"> Buffer to use for copying. Size must be greater than 0. Must not be null. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named byte array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="dataSource"/> is null. </exception>
        /// <exception cref="ArgumentException"> Given stream does not support reading -OR-
        /// <paramref name="buffer"/> size is 0. </exception>
        public void WriteByteArray(Stream dataSource, int count, byte[] buffer) {
            ValidateByteArraySource(dataSource, count, buffer);
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.ByteArray);
            WriteByteArrayFromStreamImpl(dataSource, count, buffer);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named byte array tag, copying data from a stream. </summary>
        /// <remarks> A temporary buffer will be allocated, of size up to 8192 bytes.
        /// To manually specify a buffer, use one of the other WriteByteArray() overloads. </remarks>
        /// <param name="tagName"> Name to give to this byte array tag. May not be null. </param>
        /// <param name="dataSource"> A Stream from which data will be copied. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="dataSource"/> is null. </exception>
        /// <exception cref="ArgumentException"> Given stream does not support reading. </exception>
        public void WriteByteArray(string tagName, Stream dataSource, int count) {
            WriteByteArray(tagName, dataSource, count, AllocateStreamCopyBuffer(dataSource, count));
        }


        /// <summary> Writes a named byte array tag, copying data from a stream. </summary>
        /// <param name="tagName"> Name to give to this byte array tag. May not be null. </param>
        /// <param name="dataSource"> A Stream from which data will be copied. </param>
        /// <param name="count"> The number of bytes to write. Must not be negative. </param>
        /// <param name="buffer"> Buffer to use for copying. Size must be greater than 0. Must not be null. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="dataSource"/> is null. </exception>
        /// <exception cref="ArgumentException"> Given stream does not support reading -OR-
        /// <paramref name="buffer"/> size is 0. </exception>
        public void WriteByteArray(string tagName, Stream dataSource, int count,
                                   byte[] buffer) {
            ValidateByteArraySource(dataSource, count, buffer);
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.ByteArray);
            WriteByteArrayFromStreamImpl(dataSource, count, buffer);
            CommitEmission(parentType);
        }


        // Shared argument checks for the stream-sourced WriteByteArray pairs above
        static byte[] AllocateStreamCopyBuffer(Stream dataSource, int count) {
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (!dataSource.CanRead) {
                throw new ArgumentException("Given stream does not support reading.", nameof(dataSource));
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            }
            return new byte[Math.Min(count, MaxStreamCopyBufferSize)];
        }


        static void ValidateByteArraySource(Stream dataSource, int count, byte[] buffer) {
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (!dataSource.CanRead) {
                throw new ArgumentException("Given stream does not support reading.", nameof(dataSource));
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            } else if (buffer.Length == 0 && count > 0) {
                throw new ArgumentException("buffer size must be greater than 0 when count is greater than 0",
                                            nameof(buffer));
            }
        }


        /// <summary> Writes an unnamed int array tag, copying data from an array. </summary>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named int array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// enabled validation does not permit TAG_Int_Array for the flavor. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        public void WriteIntArray(int[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteIntArray(data, 0, data.Length);
        }


        /// <summary> Writes an unnamed int array tag, copying data from an array. </summary>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of elements to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named int array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// enabled validation does not permit TAG_Int_Array for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteIntArray(int[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.IntArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named int array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this int array tag. May not be null. </param>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed int array tag was expected -OR- a tag of a different type was expected -OR-
        /// enabled validation does not permit TAG_Int_Array for the flavor. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        public void WriteIntArray(string tagName, int[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteIntArray(tagName, data, 0, data.Length);
        }


        /// <summary> Writes a named int array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this int array tag. May not be null. </param>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of elements to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed int array tag was expected -OR- a tag of a different type was expected -OR-
        /// enabled validation does not permit TAG_Int_Array for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteIntArray(string tagName, int[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.IntArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }

        /// <summary> Writes an unnamed long array tag, copying data from an array. </summary>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named long array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// enabled validation does not permit TAG_Long_Array for the flavor. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        public void WriteLongArray(long[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteLongArray(data, 0, data.Length);
        }


        /// <summary> Writes an unnamed long array tag, copying data from an array. </summary>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of elements to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named long array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// enabled validation does not permit TAG_Long_Array for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteLongArray(long[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginUnnamedTag(NbtTagType.LongArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }


        /// <summary> Writes a named long array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this long array tag. May not be null. </param>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed long array tag was expected -OR- a tag of a different type was expected -OR-
        /// enabled validation does not permit TAG_Long_Array for the flavor. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        public void WriteLongArray(string tagName, long[] data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteLongArray(tagName, data, 0, data.Length);
        }


        /// <summary> Writes a named long array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this long array tag. May not be null. </param>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <param name="offset"> The starting point in <paramref name="data"/> at which to begin writing. Must not be negative. </param>
        /// <param name="count"> The number of elements to write. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed long array tag was expected -OR- a tag of a different type was expected -OR-
        /// enabled validation does not permit TAG_Long_Array for the flavor. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteLongArray(string tagName, long[] data, int offset, int count) {
            CheckArray(data, offset, count);
            NbtTagType parentType = BeginNamedTag(tagName, NbtTagType.LongArray);
            writer.Write(count);
            writer.Write(data, offset, count);
            CommitEmission(parentType);
        }

        #endregion


        /// <summary> Writes a NbtTag object, and all of its child tags, to stream.
        /// Use this method sparingly with NbtWriter -- constructing NbtTag objects defeats the purpose of this class.
        /// If you already have lots of NbtTag objects, you might as well use NbtFile to write them all at once. </summary>
        /// <param name="tag"> Tag to write. Must not be null. </param>
        /// <remarks> With write validation on for a flavor with restrictions, the whole tree is
        /// checked before anything is written. Otherwise a tree that is too deep or holds an
        /// overlong string fails partway through and leaves the writer failed. </remarks>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// given tag is unacceptable at this time -OR- its tree, together with the containers
        /// currently open, is nested more than 512 levels deep -OR-
        /// a string inside it is longer than the flavor's limit (65,535 bytes for the Java flavors) -OR-
        /// enabled validation rejects a tag type or string length inside it for the flavor -OR-
        /// an earlier write failed partway through. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is null </exception>
        public void WriteTag(NbtTag tag) {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            NbtTagType type = tag.TagType;
            string? name = tag.Name;
            ValidateConstraints(name, type);
            // Refusals the tag layer would make before writing a byte happen here instead, ahead
            // of the emission window, so they leave the writer usable
            string? stringValue = null;
            switch (type) {
                case NbtTagType.Compound:
                    EnsureCanGoDown();
                    break;
                case NbtTagType.List:
                    EnsureCanGoDown();
                    break;
                case NbtTagType.String:
                    stringValue = ((NbtString)tag).Value;
                    break;
            }
            // Measured either way: with validation off nothing else checks them before emission.
            // The counts feed the writes below, so neither string is scanned a second time.
            int nameBytes = 0, valueBytes = 0;
            bool nameModified = false, valueModified = false;
            if (name != null) nameBytes = MeasureString(name, nameof(tag), out nameModified);
            if (stringValue != null) valueBytes = MeasureString(stringValue, nameof(tag), out valueModified);
            int depthBudget = NbtTag.MaxDepth - OpenContainerCount;
            if (validates) {
                // Walking an unrestricted tree twice is too expensive, so only restricting
                // flavors get the pre-walk; elsewhere the tree's own walk enforces the budget
                flavor.ValidateTree(tag, depthBudget);
            }
            NbtTagType parentType = BeginEmission();
            if (name != null) {
                WriteHeader(type, name, nameBytes, nameModified);
            }
            if (stringValue != null) {
                writer.Write(stringValue, valueBytes, valueModified);
            } else {
                tag.WriteData(writer, depthBudget);
            }
            CommitEmission(parentType);
        }


        /// <summary> Ensures that file has been written in its entirety, with no tags left open.
        /// This method is for verification only, and does not actually write any data. 
        /// Calling this method is optional (but probably a good idea, to catch any usage errors). </summary>
        /// <exception cref="NbtFormatException"> Not all tags have been closed yet -OR-
        /// an earlier write failed partway through. </exception>
        public void Finish() {
            ThrowIfFailed();
            if (!IsDone) {
                throw new NbtFormatException("Cannot finish: not all tags have been closed yet.");
            }
        }


        int OpenContainerCount {
            get {
                if (IsDone || IsFailed) return 0;
                return 1 + (ancestors?.Count ?? 0);
            }
        }


        bool IsFailed {
            get { return container.Type == NbtTagType.Unknown; }
        }


        void EnsureCanGoDown() {
            if (OpenContainerCount >= NbtTag.MaxDepth) {
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
        }


        void GoDown(Node newNode) {
            if (ancestors == null) {
                ancestors = new Stack<Node>();
            }
            ancestors.Push(container);
            container = newNode;
        }


        void GoUp() {
            if (ancestors == null || ancestors.Count == 0) {
                // default(Node).Type is TAG_End, our closed sentinel.
                container = default;
            } else {
                container = ancestors.Pop();
            }
        }


        void ValidateConstraints(string? name, NbtTagType desiredType) {
            ThrowIfFailed();
            if (IsDone) {
                throw new NbtFormatException("Cannot write any more tags: root tag has been closed.");
            }
            if (desiredType > maxTagType) {
                throw NbtFormatException.NotPermitted(flavor, desiredType);
            }
            if (container.Type == NbtTagType.List) {
                if (name != null) {
                    throw new NbtFormatException("Expecting an unnamed tag.");
                } else if (container.ElementType != desiredType) {
                    throw new NbtFormatException("Unexpected tag type (expected: " + container.ElementType +
                                                 ", given: " + desiredType);
                } else if (container.NextIndex >= container.Length) {
                    throw new NbtFormatException("Given list size exceeded.");
                }
            } else if (name == null) {
                throw new NbtFormatException("Expecting a named tag.");
            }
        }


        // The prologue every named write shares. Every refusal happens before the emission
        // window opens, and the window opens with the type byte and name already measured.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        NbtTagType BeginNamedTag(string tagName, NbtTagType type) {
            ValidateConstraints(tagName, type);
            int nameBytes = MeasureString(tagName, nameof(tagName), out bool nameModified);
            NbtTagType parentType = BeginEmission();
            WriteHeader(type, tagName, nameBytes, nameModified);
            return parentType;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        NbtTagType BeginUnnamedTag(NbtTagType type) {
            ValidateConstraints(null, type);
            return BeginEmission();
        }


        void CommitTag() {
            if (container.Type == NbtTagType.List) {
                container.NextIndex++;
            }
        }


        NbtTagType BeginEmission() {
            NbtTagType parentType = container.Type;
            // Unknown is both the in-flight and failed sentinel. Successful writes restore the
            // type below; any exception naturally leaves every later operation disabled.
            container.Type = NbtTagType.Unknown;
            return parentType;
        }


        void RestoreAfterEmission(NbtTagType parentType) {
            container.Type = parentType;
        }


        void CommitEmission(NbtTagType parentType) {
            RestoreAfterEmission(parentType);
            CommitTag();
        }


        int MeasureString(string value, string paramName, out bool modified) {
            if (value == null) throw new ArgumentNullException(paramName);
            return writer.Measure(value, out modified);
        }


        void WriteHeader(NbtTagType type, string name, int nameBytes, bool nameModified) {
            writer.Write((byte)type);
            writer.Write(name, nameBytes, nameModified);
        }


        void ThrowIfFailed() {
            if (IsFailed) {
                throw new NbtFormatException(
                    "Cannot write any more tags: a previous write may have partially written data.");
            }
        }


        static void CheckArray(Array data, int offset, int count) {
            if (data == null) {
                throw new ArgumentNullException(nameof(data));
            } else if (offset < 0) {
                throw new ArgumentOutOfRangeException(nameof(offset), "offset may not be negative.");
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative.");
            } else if ((data.Length - offset) < count) {
                throw new ArgumentException("count may not be greater than offset subtracted from the array length.");
            }
        }


        void WriteByteArrayFromStreamImpl(Stream dataSource, int count, byte[] buffer) {
            writer.Write(count);
            int maxBytesToWrite = Math.Min(buffer.Length, NbtBinaryWriter.MaxWriteChunk);
            int bytesWritten = 0;
            while (bytesWritten < count) {
                int bytesToRead = Math.Min(count - bytesWritten, maxBytesToWrite);
                int bytesRead = dataSource.Read(buffer, 0, bytesToRead);
                if (bytesRead == 0) {
                    // Actual stream length was less than the given count.
                    throw new EndOfStreamException();
                }
                writer.Write(buffer, 0, bytesRead);
                bytesWritten += bytesRead;
            }
        }


    }
}
