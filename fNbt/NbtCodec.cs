using System;
#if NETCOREAPP
using System.Buffers;
#endif
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace fNbt {
    /// <summary> Reads and writes single NBT documents ("blobs") with a fixed set of
    /// <see cref="NbtOptions"/>, resolved once at construction. Create one per context and reuse
    /// it; instances are immutable and safe to share between threads. </summary>
    /// <remarks> Blobs carry no file-level framing and are never compressed at this layer:
    /// network packet payloads, LevelDB values, and NBT embedded inside other formats. Reads
    /// stop exactly at the end of one document, leaving trailing bytes in place, and accept any
    /// root tag type unless <see cref="NbtOptions.ValidateOnRead"/> is set. Writes enforce the
    /// flavor's root rules, and its conformance rules when <see cref="NbtOptions.ValidateOnWrite"/>
    /// is set. For one-off use with the current default options, <see cref="For"/> returns a
    /// cached per-flavor instance. The .NET 8 build also reads from <c>ReadOnlySpan&lt;byte&gt;</c> and
    /// writes to <c>IBufferWriter&lt;byte&gt;</c>, for pooled buffers and pipelines. </remarks>
    public sealed class NbtCodec {
        /// <summary> Returns a cached codec with the current default policy settings for the
        /// given flavor, for one-off use: <c>NbtCodec.For(NbtFlavor.Bedrock).ReadTag(stream)</c>.
        /// A returned codec never changes; after a policy default changes, a later call returns
        /// a fresh instance built from the new defaults. </summary>
        /// <param name="flavor"> Encoding to read and write. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        public static NbtCodec For(NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            return flavor.DefaultCodec;
        }

        /// <summary> The flavor this codec reads and writes, fixed at construction. </summary>
        public NbtFlavor Flavor {
            get { return flavor; }
        }

        readonly NbtFlavor flavor;
        readonly long maxAllocation;
        readonly bool validateOnWrite;
        // Each read flag is on only when read validation is on and the flavor actually
        // restricts that aspect: tag types and strings inside the document, or the root shape
        readonly bool validateOnRead;
        readonly bool requireCompoundRootOnRead;


        /// <summary> Creates a codec for the given flavor with the current validation and
        /// allocation defaults. </summary>
        /// <param name="flavor"> Encoding to read and write. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        public NbtCodec(NbtFlavor flavor)
            : this(NbtOptions.ResolveForCodec(flavor, nameof(flavor))) { }


        /// <summary> Creates a codec with the given options. </summary>
        /// <param name="options"> Settings to copy. Later changes to these options do not
        /// affect this codec. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="options"/> or its <see cref="NbtOptions.Flavor"/> is <c>null</c>. </exception>
        public NbtCodec(NbtOptions options)
            : this(NbtOptions.ResolveForCodec(options, nameof(options))) { }


        internal NbtCodec(NbtOptions.Resolved resolved) {
            flavor = resolved.Flavor;
            maxAllocation = resolved.MaxAllocation;
            validateOnWrite = resolved.ValidateOnWrite && flavor.HasRestrictions;
            validateOnRead = resolved.ValidateOnRead;
            requireCompoundRootOnRead = resolved.ValidateOnRead && !flavor.AllowsNonCompoundRoot;
        }


        #region Reading

        /// <summary> Reads one NBT document from the given stream. The stream is left positioned
        /// exactly past the end of the document. </summary>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="EndOfStreamException"> If the stream ends before the document does. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or consists of a lone
        /// <c>TAG_End</c> byte (use <see cref="TryReadTag(Stream,out NbtTag)"/> to accept absent documents). </exception>
        public NbtTag ReadTag(Stream stream) {
            return ReadTagInternal(stream, null);
        }


        /// <summary> Reads one NBT document from the given stream, requiring a specific root tag type. </summary>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="expectedRootType"> Root tag type that the document must have. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="expectedRootType"/> is not a concrete tag type. </exception>
        /// <exception cref="EndOfStreamException"> If the stream ends before the document does. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or its root tag type does not match
        /// <paramref name="expectedRootType"/>. </exception>
        public NbtTag ReadTag(Stream stream, NbtTagType expectedRootType) {
            CheckExpectedRootType(expectedRootType);
            return ReadTagInternal(stream, expectedRootType);
        }


        /// <summary> Reads one NBT document from the given buffer. </summary>
        /// <param name="buffer"> Buffer to read from. </param>
        /// <param name="index"> Index in <paramref name="buffer"/> at which the document begins. </param>
        /// <param name="length"> Maximum number of bytes the document may occupy. Trailing bytes past the
        /// document's actual end are ignored. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes the document occupied. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="index"/> or <paramref name="length"/>
        /// do not describe a valid range within <paramref name="buffer"/>. </exception>
        /// <exception cref="EndOfStreamException"> If the document extends past the given <paramref name="length"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or consists of a lone
        /// <c>TAG_End</c> byte. </exception>
        public NbtTag ReadTag(byte[] buffer, int index, int length, out int bytesConsumed) {
            return ReadBuffer(buffer, index, length, null, out bytesConsumed);
        }


        /// <summary> Reads one NBT document from the given buffer, requiring a specific root tag type. </summary>
        /// <param name="buffer"> Buffer to read from. </param>
        /// <param name="index"> Index in <paramref name="buffer"/> at which the document begins. </param>
        /// <param name="length"> Maximum number of bytes the document may occupy. Trailing bytes past the
        /// document's actual end are ignored. </param>
        /// <param name="expectedRootType"> Root tag type that the document must have. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes the document occupied. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="index"/> or <paramref name="length"/>
        /// do not describe a valid range within <paramref name="buffer"/>; or <paramref name="expectedRootType"/>
        /// is not a concrete tag type. </exception>
        /// <exception cref="EndOfStreamException"> If the document extends past the given <paramref name="length"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or its root tag type does not match
        /// <paramref name="expectedRootType"/>. </exception>
        public NbtTag ReadTag(byte[] buffer, int index, int length, NbtTagType expectedRootType,
                              out int bytesConsumed) {
            CheckExpectedRootType(expectedRootType);
            return ReadBuffer(buffer, index, length, expectedRootType, out bytesConsumed);
        }


        NbtTag ReadBuffer(byte[] buffer, int index, int length, NbtTagType? expectedRootType,
                          out int bytesConsumed) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            using (MemoryStream ms = new MemoryStream(buffer, index, length)) {
                NbtTag tag = ReadTagInternal(ms, expectedRootType);
                bytesConsumed = (int)ms.Position;
                return tag;
            }
        }


        /// <summary> Attempts to read one NBT document from the given stream. Returns <c>false</c> when
        /// the stream is already at its end, or (for flavors that allow non-compound roots) when the
        /// document is a lone <c>TAG_End</c> byte meaning "absent". Anything else is parsed as a complete
        /// document, and a partial one still throws. </summary>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="tag"> Set to the root tag, or <c>null</c> if no tag was present. </param>
        /// <returns> Whether a tag was read. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="EndOfStreamException"> If the stream ends partway through a document. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, or fails enabled validation. </exception>
        public bool TryReadTag(Stream stream, [NotNullWhen(true)] out NbtTag? tag) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            int firstByte = stream.ReadByte();
            if (firstByte < 0) {
                tag = null;
                return false;
            }
            tag = ParseDocument(CreateReader(stream), firstByte, null);
            return tag != null;
        }


        /// <summary> Attempts to read one NBT document from the given buffer. Returns <c>false</c> when
        /// <paramref name="length"/> is zero, or (for flavors that allow non-compound roots) when the
        /// document is a lone <c>TAG_End</c> byte meaning "absent". </summary>
        /// <param name="buffer"> Buffer to read from. </param>
        /// <param name="index"> Index in <paramref name="buffer"/> at which the document begins. </param>
        /// <param name="length"> Maximum number of bytes the document may occupy. </param>
        /// <param name="tag"> Set to the root tag, or <c>null</c> if no tag was present. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes consumed. </param>
        /// <returns> Whether a tag was read. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="index"/> or <paramref name="length"/>
        /// do not describe a valid range within <paramref name="buffer"/>. </exception>
        /// <exception cref="EndOfStreamException"> If the document extends past the given <paramref name="length"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, or fails enabled validation. </exception>
        public bool TryReadTag(byte[] buffer, int index, int length,
                               [NotNullWhen(true)] out NbtTag? tag, out int bytesConsumed) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            using (MemoryStream ms = new MemoryStream(buffer, index, length)) {
                bool result = TryReadTag(ms, out tag);
                bytesConsumed = (int)ms.Position;
                return result;
            }
        }


#if NETCOREAPP
        /// <summary> Reads one NBT document from the given span. The span is touched only during
        /// the call, so pooled or stack memory is fine: the returned tags hold copies of their data. </summary>
        /// <param name="buffer"> Bytes to read from. Trailing bytes past the document's actual end are ignored. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes the document occupied. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="EndOfStreamException"> If the document extends past the end of <paramref name="buffer"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or consists of a lone
        /// <c>TAG_End</c> byte (use <see cref="TryReadTag(ReadOnlySpan{byte},out NbtTag,out int)"/> to accept
        /// absent documents). </exception>
        /// <remarks> Only the .NET 8 build has this overload. </remarks>
        public NbtTag ReadTag(ReadOnlySpan<byte> buffer, out int bytesConsumed) {
            return ReadSpan(buffer, null, out bytesConsumed);
        }


        /// <summary> Reads one NBT document from the given span, requiring a specific root tag type. </summary>
        /// <param name="buffer"> Bytes to read from. Trailing bytes past the document's actual end are ignored. </param>
        /// <param name="expectedRootType"> Root tag type that the document must have. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes the document occupied. </param>
        /// <returns> The root tag. Its <c>Name</c> is <c>null</c> for flavors without root names. </returns>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="expectedRootType"/> is not a concrete tag type. </exception>
        /// <exception cref="EndOfStreamException"> If the document extends past the end of <paramref name="buffer"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or its root tag type does not match
        /// <paramref name="expectedRootType"/>. </exception>
        /// <remarks> Only the .NET 8 build has this overload. </remarks>
        public NbtTag ReadTag(ReadOnlySpan<byte> buffer, NbtTagType expectedRootType, out int bytesConsumed) {
            CheckExpectedRootType(expectedRootType);
            return ReadSpan(buffer, expectedRootType, out bytesConsumed);
        }


        /// <summary> Attempts to read one NBT document from the given span. Returns <c>false</c> when
        /// the span is empty, or (for flavors that allow non-compound roots) when the document is a
        /// lone <c>TAG_End</c> byte meaning "absent". </summary>
        /// <param name="buffer"> Bytes to read from. Trailing bytes past the document's actual end are ignored. </param>
        /// <param name="tag"> Set to the root tag, or <c>null</c> if no tag was present. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes consumed. </param>
        /// <returns> Whether a tag was read. </returns>
        /// <exception cref="EndOfStreamException"> If the document extends past the end of <paramref name="buffer"/>. </exception>
        /// <exception cref="NbtFormatException"> If the document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, or fails enabled validation. </exception>
        /// <remarks> Only the .NET 8 build has this overload. </remarks>
        public bool TryReadTag(ReadOnlySpan<byte> buffer, [NotNullWhen(true)] out NbtTag? tag,
                               out int bytesConsumed) {
            if (buffer.IsEmpty) {
                tag = null;
                bytesConsumed = 0;
                return false;
            }
            unsafe {
                fixed (byte* bytes = buffer) {
                    using (UnmanagedMemoryStream stream = new UnmanagedMemoryStream(bytes, buffer.Length)) {
                        bool result = TryReadTag(stream, out tag);
                        bytesConsumed = (int)stream.Position;
                        return result;
                    }
                }
            }
        }


        // Pins the span for the duration of the parse, so the stream-based reader can walk it
        // in place. Nothing keeps the pointer past the call. An empty span pins to null, hence
        // the early exit.
        unsafe NbtTag ReadSpan(ReadOnlySpan<byte> buffer, NbtTagType? expectedRootType, out int bytesConsumed) {
            if (buffer.IsEmpty) throw new EndOfStreamException();
            fixed (byte* bytes = buffer) {
                using (UnmanagedMemoryStream stream = new UnmanagedMemoryStream(bytes, buffer.Length)) {
                    NbtTag tag = ReadTagInternal(stream, expectedRootType);
                    bytesConsumed = (int)stream.Position;
                    return tag;
                }
            }
        }
#endif


        /// <summary> Lazily reads back-to-back NBT documents from the given stream until it ends,
        /// e.g. a Bedrock LevelDB value holding several roots. Enumeration ends cleanly when the stream
        /// runs out exactly on a document boundary; a partially-present document throws. </summary>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <returns> A lazy sequence of root tags. The stream is read as the sequence is enumerated. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="EndOfStreamException"> If the stream ends partway through a document. </exception>
        /// <exception cref="NbtFormatException"> If a document is malformed, nested more than 512 levels
        /// deep, exceeds a configured limit, fails enabled validation, or consists of a lone
        /// <c>TAG_End</c> byte. </exception>
        public IEnumerable<NbtTag> ReadConcatenatedTags(Stream stream) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return ReadConcatenatedTagsIterator(stream);
        }

        #endregion


        #region Writing

        /// <summary> Writes one NBT document to the given stream. </summary>
        /// <param name="tag"> Root tag to write. For flavors that allow non-compound roots, <c>null</c>
        /// writes an absent document (a lone <c>TAG_End</c> byte); other flavors require an
        /// <see cref="NbtCompound"/>. A <c>null</c> root name is written as an empty string. </param>
        /// <param name="stream"> Stream to write to. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>;
        /// or <paramref name="tag"/> is <c>null</c> and the flavor requires a compound root. </exception>
        /// <exception cref="NbtFormatException"> If <paramref name="tag"/> is not a compound and the flavor requires one;
        /// if enabled validation rejects a tag type or string length; if a compound contains unnamed tags;
        /// if a string is too long;
        /// or if tags are nested more than 512 levels deep. </exception>
        public void WriteTag(NbtTag? tag, Stream stream) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (tag == null) {
                EnsureAbsentDocumentAllowed();
                stream.WriteByte((byte)NbtTagType.End);
                return;
            }
            ValidateDocument(tag);
            WriteDocument(tag, CreateWriter(stream));
        }


        /// <summary> Writes back-to-back NBT documents to the given stream, e.g. a Bedrock LevelDB
        /// value holding several roots. Mirrors <see cref="ReadConcatenatedTags"/>, and is cheaper
        /// than repeated <see cref="WriteTag(NbtTag?,Stream)"/> calls when documents are many. </summary>
        /// <param name="tags"> Root tags to write, one document each. May not contain <c>null</c>:
        /// absent documents cannot appear in a concatenated stream. </param>
        /// <param name="stream"> Stream to write to. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> or <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="tags"/> contains a <c>null</c> tag.
        /// Documents before it are already written when this throws. </exception>
        /// <exception cref="NbtFormatException"> If a tag is not a compound and the flavor requires one;
        /// if enabled validation rejects a tag type or string length; if a compound contains unnamed tags;
        /// if a string is too long;
        /// or if tags are nested more than 512 levels deep. Documents before the offending one
        /// are already written when this throws. </exception>
        public void WriteConcatenatedTags(IEnumerable<NbtTag> tags, Stream stream) {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            NbtBinaryWriter? writer = null;
            foreach (NbtTag tag in tags) {
                if (tag == null) {
                    throw new ArgumentException(
                        "Sequence contains a null tag. Absent documents cannot appear in a concatenated stream.",
                        nameof(tags));
                }
                ValidateDocument(tag);
                if (writer == null) writer = CreateWriter(stream);
                WriteDocument(tag, writer);
            }
        }


#if NETCOREAPP
        /// <summary> Writes one NBT document to the given buffer writer, such as a pipe or an
        /// <c>ArrayBufferWriter</c>. </summary>
        /// <param name="tag"> Root tag to write. For flavors that allow non-compound roots, <c>null</c>
        /// writes an absent document (a lone <c>TAG_End</c> byte); other flavors require an
        /// <see cref="NbtCompound"/>. A <c>null</c> root name is written as an empty string. </param>
        /// <param name="output"> Buffer writer to write to. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="output"/> is <c>null</c>;
        /// or <paramref name="tag"/> is <c>null</c> and the flavor requires a compound root. </exception>
        /// <exception cref="NbtFormatException"> If <paramref name="tag"/> is not a compound and the flavor requires one;
        /// if enabled validation rejects a tag type or string length; if a compound contains unnamed tags;
        /// if a string is too long;
        /// or if tags are nested more than 512 levels deep. </exception>
        /// <remarks> Only the .NET 8 build has this overload. </remarks>
        public void WriteTag(NbtTag? tag, IBufferWriter<byte> output) {
            if (output == null) throw new ArgumentNullException(nameof(output));
            BufferWriterStream stream = new BufferWriterStream(output);
            try {
                WriteTag(tag, stream);
            } finally {
                // Staged bytes reach the output even on failure, matching what the
                // stream-based overload leaves behind
                stream.Flush();
            }
        }


        /// <summary> Writes back-to-back NBT documents to the given buffer writer. Behaves like
        /// <see cref="WriteConcatenatedTags(IEnumerable{NbtTag},Stream)"/>. </summary>
        /// <param name="tags"> Root tags to write, one document each. May not contain <c>null</c>. </param>
        /// <param name="output"> Buffer writer to write to. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> or <paramref name="output"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="tags"/> contains a <c>null</c> tag.
        /// Documents before it are already written when this throws. </exception>
        /// <exception cref="NbtFormatException"> If a tag is not a compound and the flavor requires one;
        /// if enabled validation rejects a tag type or string length; if a compound contains unnamed tags;
        /// if a string is too long;
        /// or if tags are nested more than 512 levels deep. Documents before the offending one
        /// are already written when this throws. </exception>
        /// <remarks> Only the .NET 8 build has this overload. </remarks>
        public void WriteConcatenatedTags(IEnumerable<NbtTag> tags, IBufferWriter<byte> output) {
            if (output == null) throw new ArgumentNullException(nameof(output));
            BufferWriterStream stream = new BufferWriterStream(output);
            try {
                WriteConcatenatedTags(tags, stream);
            } finally {
                stream.Flush();
            }
        }
#endif


        void ValidateDocument(NbtTag tag) {
            ValidateRoot(tag);
            if (validateOnWrite) {
                flavor.ValidateTree(tag, NbtTag.MaxDepth);
            }
        }


        void EnsureAbsentDocumentAllowed() {
            if (!flavor.AllowsNonCompoundRoot) {
                throw new ArgumentNullException("tag",
                                                flavor.Name + " requires a TAG_Compound root and cannot represent an absent document.");
            }
        }


        void ValidateRoot(NbtTag tag) {
            if (!flavor.AllowsNonCompoundRoot && tag.TagType != NbtTagType.Compound) {
                throw new NbtFormatException(
                    flavor.Name + " requires a TAG_Compound root, but given tag is " +
                    NbtTag.GetCanonicalTagName(tag.TagType));
            }
        }


        void WriteDocument(NbtTag tag, NbtBinaryWriter writer) {
            writer.Write(tag.TagType);
            if (flavor.HasRootName) {
                writer.Write(tag.Name ?? "");
            }
            tag.WriteData(writer, NbtTag.MaxDepth);
        }


        NbtBinaryWriter CreateWriter(Stream stream) {
            return new NbtBinaryWriter(stream, flavor);
        }


        /// <summary> Writes one NBT document to a new byte array of exactly the right size. </summary>
        /// <param name="tag"> Root tag to write. For flavors that allow non-compound roots, <c>null</c>
        /// writes an absent document (a lone <c>TAG_End</c> byte); other flavors require an
        /// <see cref="NbtCompound"/>. A <c>null</c> root name is written as an empty string. </param>
        /// <returns> Byte array containing the serialized document. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is <c>null</c> and the flavor
        /// requires a compound root. </exception>
        /// <exception cref="NotSupportedException"> The document does not fit in a single array. </exception>
        /// <exception cref="NbtFormatException"> If <paramref name="tag"/> is not a compound and the flavor requires one;
        /// if enabled validation rejects a tag type or string length; if a compound contains unnamed tags;
        /// if a string is too long;
        /// or if tags are nested more than 512 levels deep. </exception>
        public byte[] WriteTag(NbtTag? tag) {
            // An absent document is a lone TAG_End byte
            if (tag == null) {
                EnsureAbsentDocumentAllowed();
                return new[] { (byte)NbtTagType.End };
            }
            // Sizing the tree up front buys an exact array from a single write pass, and the
            // sizing walk validates as it goes
            ValidateRoot(tag);
            long size = NbtSizer.SizeDocument(tag, flavor.HasRootName, flavor, validateOnWrite);
            if (size > int.MaxValue) {
                throw new NotSupportedException("This NBT document is too large to fit in a single buffer.");
            }
            byte[] result = ArrayAllocator.ForOverwrite<byte>((int)size);
            MemoryStream output = new MemoryStream(result, 0, result.Length, true);
            WriteDocument(tag, CreateWriter(output));
            ArrayAllocator.EnsureFilled(result, output.Position);
            return result;
        }

        #endregion


        static void CheckExpectedRootType(NbtTagType expectedRootType) {
            if (expectedRootType <= NbtTagType.End || expectedRootType > NbtTagType.LongArray) {
                throw new ArgumentOutOfRangeException(nameof(expectedRootType));
            }
        }


        NbtTag ReadTagInternal(Stream stream, NbtTagType? expectedRootType) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            int firstByte = stream.ReadByte();
            if (firstByte < 0) {
                throw new EndOfStreamException();
            }
            NbtTag? tag = ParseDocument(CreateReader(stream), firstByte, expectedRootType);
            if (tag == null) {
                throw new NbtFormatException(
                    "Document contains no tag (a lone TAG_End byte). Use TryReadTag to accept absent documents.");
            }
            return tag;
        }


        // Parses one document whose first (tag type) byte has already been consumed.
        // Returns null for an absent document: a lone TAG_End byte, valid only when the flavor
        // permits non-compound roots.
        NbtTag? ParseDocument(NbtBinaryReader reader, int typeByte, NbtTagType? expectedRootType) {
            if (typeByte == (int)NbtTagType.End) {
                if (flavor.AllowsNonCompoundRoot) return null;
                throw new NbtFormatException("Document may not start with a TAG_End byte.");
            }
            // The same range and flavor-ceiling checks every nested tag type gets
            NbtTagType tagType = reader.RequireValidTagType((byte)typeByte);
            if (expectedRootType != null && tagType != expectedRootType.Value) {
                throw new NbtFormatException(
                    "Expected a root tag of type " + NbtTag.GetCanonicalTagName(expectedRootType.Value) +
                    ", but found " + NbtTag.GetCanonicalTagName(tagType));
            }
            if (requireCompoundRootOnRead && tagType != NbtTagType.Compound) {
                throw new NbtFormatException(
                    flavor.Name + " requires a TAG_Compound root, but found " +
                    NbtTag.GetCanonicalTagName(tagType) + ".");
            }
            NbtTag tag = NbtTag.Create(tagType);
            if (flavor.HasRootName) {
                tag.name = reader.ReadString();
            }
            tag.ReadTag(reader, NbtTag.MaxDepth);
            return tag;
        }


        NbtBinaryReader CreateReader(Stream stream) {
            return new NbtBinaryReader(stream, flavor, maxAllocation, validateOnRead);
        }


        IEnumerable<NbtTag> ReadConcatenatedTagsIterator(Stream stream) {
            // One binary reader serves the whole sequence; each document walk receives a fresh
            // full depth budget.
            NbtBinaryReader? reader = null;
            while (true) {
                int firstByte = stream.ReadByte();
                if (firstByte < 0) yield break;
                if (reader == null) reader = CreateReader(stream);
                NbtTag? tag = ParseDocument(reader, firstByte, null);
                if (tag == null) {
                    throw new NbtFormatException("Absent document (a lone TAG_End byte) in a concatenated stream.");
                }
                yield return tag;
            }
        }


    }
}
