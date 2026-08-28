using System;
using System.Collections.Generic;
using System.IO;

namespace fNbt {
    /// <summary> An efficient writer for writing NBT data directly to streams.
    /// Each instance of NbtWriter writes one complete file. 
    /// NbtWriter enforces all constraints of the NBT file format
    /// EXCEPT checking for duplicate tag names within a compound. </summary>
    public sealed class NbtWriter {
        const int MaxStreamCopyBufferSize = 8 * 1024;

        readonly NbtBinaryWriter writer;
        readonly NbtFlavor flavor;
        // Lowered from LongArray only when write validation is on for a restricting flavor
        readonly NbtTagType maxTagType = NbtTagType.LongArray;
        NbtTagType listType;
        NbtTagType parentType;
        int listIndex;
        int listSize;
        Stack<NbtWriterNode>? nodes;


        /// <summary> Initializes a new instance of the NbtWriter class, with default options
        /// (<see cref="NbtFlavor.Java"/>, validation on write). </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="rootTagName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable. </exception>
        public NbtWriter(Stream stream, string rootTagName)
            : this(stream, rootTagName, NbtOptions.Default) { }


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
        /// with otherwise-default options. </summary>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="rootTagName"> Name to give to the root tag (written immediately). </param>
        /// <param name="flavor"> Encoding to write with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/>, <paramref name="rootTagName"/>,
        /// or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not writable;
        /// or the flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public NbtWriter(Stream stream, string rootTagName, NbtFlavor flavor)
            : this(stream, rootTagName, MakeOptions(flavor)) { }


        static NbtOptions MakeOptions(NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            return new NbtOptions { Flavor = flavor };
        }


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
        /// <exception cref="NotSupportedException"> The options' flavor is not yet supported. </exception>
        public NbtWriter(Stream stream, string rootTagName, NbtOptions options) {
            if (rootTagName == null) throw new ArgumentNullException(nameof(rootTagName));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Flavor == null) {
                throw new ArgumentNullException(nameof(options), "Options must name a flavor.");
            }
            options.Flavor.EnsureUsableForFiles(nameof(options));
            flavor = options.Flavor;
            writer = new NbtBinaryWriter(stream, flavor.BigEndian, modifiedUtf8: flavor.UsesModifiedUtf8);
            if (options.ValidateOnWrite && flavor.HasRestrictions) {
                maxTagType = flavor.MaxTagType;
                writer.SetMaxStringBytes(flavor.MaxStringBytes);
            }
            writer.Write((byte)NbtTagType.Compound);
            writer.Write(rootTagName);
            parentType = NbtTagType.Compound;
        }


        /// <summary> Gets whether the root tag has been closed.
        /// No more tags may be written after the root tag has been closed. </summary>
        public bool IsDone { get; private set; }

        /// <summary> Gets the underlying stream of the NbtWriter. </summary>
        public Stream BaseStream {
            get { return writer.BaseStream; }
        }


        #region Compounds and Lists

        /// <summary> Begins an unnamed compound tag. </summary>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named compound tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// tags are nested more than 512 levels deep. </exception>
        public void BeginCompound() {
            EnforceConstraints(null, NbtTagType.Compound);
            GoDown(NbtTagType.Compound);
        }


        /// <summary> Begins a named compound tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed compound tag was expected -OR- a tag of a different type was expected -OR-
        /// tags are nested more than 512 levels deep. </exception>
        public void BeginCompound(string tagName) {
            EnforceConstraints(tagName, NbtTagType.Compound);
            GoDown(NbtTagType.Compound);

            writer.Write((byte)NbtTagType.Compound);
            writer.Write(tagName);
        }


        /// <summary> Ends a compound tag. </summary>
        /// <exception cref="NbtFormatException"> Not currently in a compound. </exception>
        public void EndCompound() {
            if (IsDone || parentType != NbtTagType.Compound) {
                throw new NbtFormatException("Not currently in a compound.");
            }
            GoUp();
            writer.Write(NbtTagType.End);
        }


        /// <summary> Begins an unnamed list tag. </summary>
        /// <param name="elementType"> Type of elements of this list. </param>
        /// <param name="size"> Number of elements in this list. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named list tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// tags are nested more than 512 levels deep. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="size"/> is negative -OR-
        /// <paramref name="elementType"/> is not a valid list element type
        /// (End is allowed only when <paramref name="size"/> is 0). </exception>
        public void BeginList(NbtTagType elementType, int size) {
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size), "List size may not be negative.");
            }
            // Modern Minecraft (Java and Bedrock) uses TAG_End as the element type of an empty list.
            if (elementType < NbtTagType.Byte || elementType > NbtTagType.LongArray) {
                if (elementType != NbtTagType.End || size != 0) {
                    throw new ArgumentOutOfRangeException(nameof(elementType));
                }
            }
            EnforceConstraints(null, NbtTagType.List);
            GoDown(NbtTagType.List);
            listType = elementType;
            listSize = size;

            writer.Write((byte)elementType);
            writer.Write(size);
        }


        /// <summary> Begins an unnamed list tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="elementType"> Type of elements of this list. </param>
        /// <param name="size"> Number of elements in this list. Must not be negative. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed list tag was expected -OR- a tag of a different type was expected -OR-
        /// tags are nested more than 512 levels deep. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="size"/> is negative -OR-
        /// <paramref name="elementType"/> is not a valid list element type
        /// (End is allowed only when <paramref name="size"/> is 0). </exception>
        public void BeginList(string tagName, NbtTagType elementType, int size) {
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size), "List size may not be negative.");
            }
            // Modern Minecraft (Java and Bedrock) uses TAG_End as the element type of an empty list.
            if (elementType < NbtTagType.Byte || elementType > NbtTagType.LongArray) {
                if (elementType != NbtTagType.End || size != 0) {
                    throw new ArgumentOutOfRangeException(nameof(elementType));
                }
            }
            EnforceConstraints(tagName, NbtTagType.List);
            GoDown(NbtTagType.List);
            listType = elementType;
            listSize = size;

            writer.Write((byte)NbtTagType.List);
            writer.Write(tagName);
            writer.Write((byte)elementType);
            writer.Write(size);
        }


        /// <summary> Ends a list tag. </summary>
        /// <exception cref="NbtFormatException"> Not currently in a list -OR-
        /// not all list elements have been written yet. </exception>
        public void EndList() {
            if (parentType != NbtTagType.List || IsDone) {
                throw new NbtFormatException("Not currently in a list.");
            } else if (listIndex < listSize) {
                throw new NbtFormatException("Cannot end list: not all list elements have been written yet. " +
                                             "Expected: " + listSize + ", written: " + listIndex);
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
            EnforceConstraints(null, NbtTagType.Byte);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed byte tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The unsigned byte to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteByte(string tagName, byte value) {
            EnforceConstraints(tagName, NbtTagType.Byte);
            writer.Write((byte)NbtTagType.Byte);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed double tag. </summary>
        /// <param name="value"> The eight-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named double tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteDouble(double value) {
            EnforceConstraints(null, NbtTagType.Double);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed byte tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The unsigned byte to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed byte tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteDouble(string tagName, double value) {
            EnforceConstraints(tagName, NbtTagType.Double);
            writer.Write((byte)NbtTagType.Double);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed float tag. </summary>
        /// <param name="value"> The four-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named float tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteFloat(float value) {
            EnforceConstraints(null, NbtTagType.Float);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed float tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The four-byte floating-point value to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed float tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteFloat(string tagName, float value) {
            EnforceConstraints(tagName, NbtTagType.Float);
            writer.Write((byte)NbtTagType.Float);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed int tag. </summary>
        /// <param name="value"> The four-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named int tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteInt(int value) {
            EnforceConstraints(null, NbtTagType.Int);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed int tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The four-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed int tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteInt(string tagName, int value) {
            EnforceConstraints(tagName, NbtTagType.Int);
            writer.Write((byte)NbtTagType.Int);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed long tag. </summary>
        /// <param name="value"> The eight-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named long tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteLong(long value) {
            EnforceConstraints(null, NbtTagType.Long);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed long tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The eight-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed long tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteLong(string tagName, long value) {
            EnforceConstraints(tagName, NbtTagType.Long);
            writer.Write((byte)NbtTagType.Long);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed short tag. </summary>
        /// <param name="value"> The two-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named short tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
        public void WriteShort(short value) {
            EnforceConstraints(null, NbtTagType.Short);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed short tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The two-byte signed integer to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed short tag was expected -OR- a tag of a different type was expected. </exception>
        public void WriteShort(string tagName, short value) {
            EnforceConstraints(tagName, NbtTagType.Short);
            writer.Write((byte)NbtTagType.Short);
            writer.Write(tagName);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed string tag. </summary>
        /// <param name="value"> The string to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named string tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded -OR-
        /// <paramref name="value"/> is longer than the flavor's limit (65,535 bytes for the Java flavors). </exception>
        public void WriteString(string value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            EnforceConstraints(null, NbtTagType.String);
            writer.Write(value);
        }


        /// <summary> Writes an unnamed string tag. </summary>
        /// <param name="tagName"> Name to give to this compound tag. May not be null. </param>
        /// <param name="value"> The string to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed string tag was expected -OR- a tag of a different type was expected -OR-
        /// <paramref name="value"/> is longer than the flavor's limit (65,535 bytes for the Java flavors). </exception>
        public void WriteString(string tagName, string value) {
            if (value == null) throw new ArgumentNullException(nameof(value));
            EnforceConstraints(tagName, NbtTagType.String);
            writer.Write((byte)NbtTagType.String);
            writer.Write(tagName);
            writer.Write(value);
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
            EnforceConstraints(null, NbtTagType.ByteArray);
            writer.Write(count);
            writer.Write(data, offset, count);
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
            EnforceConstraints(tagName, NbtTagType.ByteArray);
            writer.Write((byte)NbtTagType.ByteArray);
            writer.Write(tagName);
            writer.Write(count);
            writer.Write(data, offset, count);
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
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (!dataSource.CanRead) {
                throw new ArgumentException("Given stream does not support reading.", nameof(dataSource));
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            }
            int bufferSize = Math.Min(count, MaxStreamCopyBufferSize);
            var streamCopyBuffer = new byte[bufferSize];
            WriteByteArray(dataSource, count, streamCopyBuffer);
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
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (!dataSource.CanRead) {
                throw new ArgumentException("Given stream does not support reading.", nameof(dataSource));
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            } else if (buffer.Length == 0 && count > 0) {
                throw new ArgumentException("buffer size must be greater than 0 when count is greater than 0", nameof(buffer));
            }
            EnforceConstraints(null, NbtTagType.ByteArray);
            WriteByteArrayFromStreamImpl(dataSource, count, buffer);
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
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            }
            int bufferSize = Math.Min(count, MaxStreamCopyBufferSize);
            var streamCopyBuffer = new byte[bufferSize];
            WriteByteArray(tagName, dataSource, count, streamCopyBuffer);
        }


        /// <summary> Writes an unnamed byte array tag, copying data from another stream. </summary>
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
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (!dataSource.CanRead) {
                throw new ArgumentException("Given stream does not support reading.", nameof(dataSource));
            } else if (count < 0) {
                throw new ArgumentOutOfRangeException(nameof(count), "count may not be negative");
            } else if (buffer.Length == 0 && count > 0) {
                throw new ArgumentException("buffer size must be greater than 0 when count is greater than 0", nameof(buffer));
            }
            EnforceConstraints(tagName, NbtTagType.ByteArray);
            writer.Write((byte)NbtTagType.ByteArray);
            writer.Write(tagName);
            WriteByteArrayFromStreamImpl(dataSource, count, buffer);
        }


        /// <summary> Writes an unnamed int array tag, copying data from an array. </summary>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named int array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
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
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteIntArray(int[] data, int offset, int count) {
            CheckArray(data, offset, count);
            EnforceConstraints(null, NbtTagType.IntArray);
            writer.Write(count);
            for (int i = offset; i < count; i++) {
                writer.Write(data[i]);
            }
        }


        /// <summary> Writes a named int array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this int array tag. May not be null. </param>
        /// <param name="data"> An int array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed int array tag was expected -OR- a tag of a different type was expected. </exception>
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
        /// an unnamed int array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteIntArray(string tagName, int[] data, int offset, int count) {
            CheckArray(data, offset, count);
            EnforceConstraints(tagName, NbtTagType.IntArray);
            writer.Write((byte)NbtTagType.IntArray);
            writer.Write(tagName);
            writer.Write(count);
            for (int i = offset; i < count; i++) {
                writer.Write(data[i]);
            }
        }

        /// <summary> Writes an unnamed long array tag, copying data from an array. </summary>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// a named long array tag was expected -OR- a tag of a different type was expected -OR-
        /// the size of a parent list has been exceeded. </exception>
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
        /// the size of a parent list has been exceeded. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteLongArray(long[] data, int offset, int count) {
            CheckArray(data, offset, count);
            EnforceConstraints(null, NbtTagType.LongArray);
            writer.Write(count);
            for (int i = offset; i < count; i++) {
                writer.Write(data[i]);
            }
        }


        /// <summary> Writes a named long array tag, copying data from an array. </summary>
        /// <param name="tagName"> Name to give to this long array tag. May not be null. </param>
        /// <param name="data"> A long array containing the data to write. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// an unnamed long array tag was expected -OR- a tag of a different type was expected. </exception>
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
        /// an unnamed long array tag was expected -OR- a tag of a different type was expected. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="offset"/> or
        /// <paramref name="count"/> is negative. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> or
        /// <paramref name="data"/> is null </exception>
        /// <exception cref="ArgumentException"> <paramref name="count"/> is greater than
        /// <paramref name="offset"/> subtracted from the array length. </exception>
        public void WriteLongArray(string tagName, long[] data, int offset, int count) {
            CheckArray(data, offset, count);
            EnforceConstraints(tagName, NbtTagType.LongArray);
            writer.Write((byte)NbtTagType.LongArray);
            writer.Write(tagName);
            writer.Write(count);
            for (int i = offset; i < count; i++) {
                writer.Write(data[i]);
            }
        }

        #endregion


        /// <summary> Writes a NbtTag object, and all of its child tags, to stream.
        /// Use this method sparingly with NbtWriter -- constructing NbtTag objects defeats the purpose of this class.
        /// If you already have lots of NbtTag objects, you might as well use NbtFile to write them all at once. </summary>
        /// <param name="tag"> Tag to write. Must not be null. </param>
        /// <exception cref="NbtFormatException"> No more tags can be written -OR-
        /// given tag is unacceptable at this time -OR- its tree is nested more than 512 levels deep -OR-
        /// a string inside it is longer than the flavor's limit (65,535 bytes for the Java flavors). </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is null </exception>
        public void WriteTag(NbtTag tag) {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            EnforceConstraints(tag.Name, tag.TagType);
            if (maxTagType < NbtTagType.LongArray) {
                // Only the subtree needs the pre-walk; per-call writes are checked inline
                flavor.ValidateTree(tag, 0);
            }
            if (tag.Name != null) {
                tag.WriteTag(writer);
            } else {
                tag.WriteData(writer);
            }
        }


        /// <summary> Ensures that file has been written in its entirety, with no tags left open.
        /// This method is for verification only, and does not actually write any data. 
        /// Calling this method is optional (but probably a good idea, to catch any usage errors). </summary>
        /// <exception cref="NbtFormatException"> Not all tags have been closed yet. </exception>
        public void Finish() {
            if (!IsDone) {
                throw new NbtFormatException("Cannot finish: not all tags have been closed yet.");
            }
        }


        void GoDown(NbtTagType thisType) {
            if (nodes == null) {
                nodes = new Stack<NbtWriterNode>();
            }
            // The stack holds every open container except the root, so add 1 for the depth check.
            if (nodes.Count + 1 >= NbtTag.MaxDepth) {
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
            var newNode = new NbtWriterNode {
                ParentType = parentType,
                ListType = listType,
                ListSize = listSize,
                ListIndex = listIndex
            };
            nodes.Push(newNode);

            parentType = thisType;
            listType = NbtTagType.Unknown;
            listSize = 0;
            listIndex = 0;
        }


        void GoUp() {
            if (nodes == null || nodes.Count == 0) {
                IsDone = true;
            } else {
                NbtWriterNode oldNode = nodes.Pop();
                parentType = oldNode.ParentType;
                listType = oldNode.ListType;
                listSize = oldNode.ListSize;
                listIndex = oldNode.ListIndex;
            }
        }


        void EnforceConstraints(string? name, NbtTagType desiredType) {
            if (IsDone) {
                throw new NbtFormatException("Cannot write any more tags: root tag has been closed.");
            }
            if (desiredType > maxTagType) {
                throw new NbtFormatException(
                    NbtTag.GetCanonicalTagName(desiredType) + " is not permitted by the " + flavor.Name + " flavor.");
            }
            if (parentType == NbtTagType.List) {
                if (name != null) {
                    throw new NbtFormatException("Expecting an unnamed tag.");
                } else if (listType != desiredType) {
                    throw new NbtFormatException("Unexpected tag type (expected: " + listType + ", given: " +
                                                 desiredType);
                } else if (listIndex >= listSize) {
                    throw new NbtFormatException("Given list size exceeded.");
                }
                listIndex++;
            } else if (name == null) {
                throw new NbtFormatException("Expecting a named tag.");
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
            NullableSupport.Assert(dataSource != null);
            NullableSupport.Assert(buffer != null);
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
