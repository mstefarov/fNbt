using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace fNbt {
    /// <summary> Reads and writes single NBT documents ("blobs") with a fixed set of
    /// <see cref="NbtOptions"/>, resolved once at construction. Create one per context and reuse
    /// it; instances are immutable and safe to share between threads. </summary>
    /// <remarks> Blobs carry no file-level framing and are never compressed at this layer. Reads
    /// stop exactly at the end of one document, leaving trailing bytes in place, and accept any
    /// root tag type unless <see cref="NbtOptions.ValidateOnRead"/> is set. Writes enforce the
    /// flavor's root rules, and its conformance rules when <see cref="NbtOptions.ValidateOnWrite"/>
    /// is set. The <see cref="NbtBlob"/> statics are shorthand for default-options instances of
    /// this class. </remarks>
    public sealed class NbtCodec {
        /// <summary> The settings this codec was created with. </summary>
        public NbtOptions Options { get; }

        readonly NbtFlavor flavor;
        readonly long maxAllocation;
        readonly bool validateOnWrite;
        // Set only when read validation is on and the flavor restricts something
        readonly NbtFlavor? readValidationFlavor;


        /// <summary> Creates a codec for the given flavor with default options
        /// (validation on write only, no allocation limit). </summary>
        /// <param name="flavor"> Encoding to read and write. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public NbtCodec(NbtFlavor flavor)
            : this(MakeOptions(flavor)) { }


        static NbtOptions MakeOptions(NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            return new NbtOptions { Flavor = flavor };
        }


        /// <summary> Creates a codec with the given options. </summary>
        /// <param name="options"> Settings to use. Resolved once here; later changes to shared
        /// state (there is none: options are immutable) cannot affect this codec. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="options"/> or its <c>Flavor</c> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <c>MaxAllocation</c> is zero or negative. </exception>
        /// <exception cref="NotSupportedException"> The options' flavor is not yet supported. </exception>
        public NbtCodec(NbtOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Flavor == null) {
                throw new ArgumentNullException(nameof(options), "Options must name a flavor.");
            }
            if (options.MaxAllocation <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options), options.MaxAllocation,
                                                      "MaxAllocation must be positive.");
            }
            if (options.Flavor.UsesVarInts) {
                throw new NotSupportedException("The " + options.Flavor.Name + " flavor is not supported yet.");
            }
            Options = options;
            flavor = options.Flavor;
            maxAllocation = options.MaxAllocation ?? long.MaxValue;
            validateOnWrite = options.ValidateOnWrite && flavor.HasRestrictions;
            readValidationFlavor = (options.ValidateOnRead && flavor.HasRestrictions) ? flavor : null;
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
            if (expectedRootType <= NbtTagType.End || expectedRootType > NbtTagType.LongArray) {
                throw new ArgumentOutOfRangeException(nameof(expectedRootType));
            }
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
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            using (var ms = new MemoryStream(buffer, index, length)) {
                NbtTag tag = ReadTagInternal(ms, null);
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
            tag = ParseDocument(stream, firstByte, null);
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
            using (var ms = new MemoryStream(buffer, index, length)) {
                bool result = TryReadTag(ms, out tag);
                bytesConsumed = (int)ms.Position;
                return result;
            }
        }


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
        /// if a list has Unknown list type and no elements; if a string is too long;
        /// or if tags are nested more than 512 levels deep. </exception>
        public void WriteTag(NbtTag? tag, Stream stream) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (tag == null) {
                if (!flavor.AllowsNonCompoundRoot) {
                    throw new ArgumentNullException(nameof(tag),
                                                    flavor.Name + " requires a TAG_Compound root and cannot represent an absent document.");
                }
                stream.WriteByte((byte)NbtTagType.End);
                return;
            }
            if (!flavor.AllowsNonCompoundRoot && tag.TagType != NbtTagType.Compound) {
                throw new NbtFormatException(
                    flavor.Name + " requires a TAG_Compound root, but given tag is " +
                    NbtTag.GetCanonicalTagName(tag.TagType));
            }
            if (validateOnWrite) {
                ValidateForWrite(tag, 0);
            }
            var writer = new NbtBinaryWriter(stream, flavor.BigEndian);
            writer.Write(tag.TagType);
            if (flavor.HasRootName) {
                writer.Write(tag.Name ?? "");
            }
            tag.WriteData(writer);
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
        /// if a list has Unknown list type and no elements; if a string is too long;
        /// or if tags are nested more than 512 levels deep. </exception>
        public byte[] WriteTag(NbtTag? tag) {
            // Size is measured with a counting stream first, so the returned array is exact
            // and nothing is copied. Same trick as NbtFile.SaveToBuffer.
            var counter = new ByteCountingStream(Stream.Null);
            WriteTag(tag, counter);
            if (counter.BytesWritten > int.MaxValue) {
                throw new NotSupportedException("This NBT document is too large to fit in a single buffer.");
            }
            var result = new byte[counter.BytesWritten];
            WriteTag(tag, new MemoryStream(result, 0, result.Length, true));
            return result;
        }

        #endregion


        NbtTag ReadTagInternal(Stream stream, NbtTagType? expectedRootType) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            int firstByte = stream.ReadByte();
            if (firstByte < 0) {
                throw new EndOfStreamException();
            }
            NbtTag? tag = ParseDocument(stream, firstByte, expectedRootType);
            if (tag == null) {
                throw new NbtFormatException(
                    "Document contains no tag (a lone TAG_End byte). Use TryReadTag to accept absent documents.");
            }
            return tag;
        }


        // Parses one document whose first (tag type) byte has already been consumed.
        // Returns null for an absent document: a lone TAG_End byte, valid only when the flavor
        // permits non-compound roots.
        NbtTag? ParseDocument(Stream stream, int typeByte, NbtTagType? expectedRootType) {
            if (typeByte == (int)NbtTagType.End) {
                if (flavor.AllowsNonCompoundRoot) return null;
                throw new NbtFormatException("Document may not start with a TAG_End byte.");
            }
            if (typeByte > (int)NbtTagType.LongArray) {
                throw new NbtFormatException("NBT tag type out of range: " + typeByte);
            }
            var tagType = (NbtTagType)typeByte;
            if (expectedRootType != null && tagType != expectedRootType.Value) {
                throw new NbtFormatException(
                    "Expected a root tag of type " + NbtTag.GetCanonicalTagName(expectedRootType.Value) +
                    ", but found " + NbtTag.GetCanonicalTagName(tagType));
            }
            if (readValidationFlavor != null && tagType > readValidationFlavor.MaxTagType) {
                throw new NbtFormatException(
                    NbtTag.GetCanonicalTagName(tagType) + " is not permitted by the " +
                    readValidationFlavor.Name + " flavor.");
            }
            var reader = new NbtBinaryReader(stream, flavor.BigEndian);
            if (maxAllocation != long.MaxValue || readValidationFlavor != null) {
                reader.SetLimits(maxAllocation, readValidationFlavor);
            }
            NbtTag tag = CreateTag(tagType);
            if (flavor.HasRootName) {
                tag.name = reader.ReadString();
            }
            tag.ReadTag(reader);
            return tag;
        }


        IEnumerable<NbtTag> ReadConcatenatedTagsIterator(Stream stream) {
            while (true) {
                int firstByte = stream.ReadByte();
                if (firstByte < 0) yield break;
                NbtTag? tag = ParseDocument(stream, firstByte, null);
                if (tag == null) {
                    throw new NbtFormatException("Absent document (a lone TAG_End byte) in a concatenated stream.");
                }
                yield return tag;
            }
        }


        // Conformance pre-walk, run only for flavors with restrictions: every tag type must be
        // within the flavor's range, and every name and string value within its byte ceiling.
        void ValidateForWrite(NbtTag tag, int depth) {
            if (depth >= NbtTag.MaxDepth) {
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
            if (tag.TagType > flavor.MaxTagType) {
                throw new NbtFormatException(
                    NbtTag.GetCanonicalTagName(tag.TagType) + " is not permitted by the " + flavor.Name + " flavor.");
            }
            if (tag.Name != null) {
                ValidateStringFits(tag.Name);
            }
            switch (tag.TagType) {
                case NbtTagType.String:
                    ValidateStringFits(((NbtString)tag).Value);
                    break;
                case NbtTagType.Compound:
                    foreach (NbtTag child in (NbtCompound)tag) {
                        ValidateForWrite(child, depth + 1);
                    }
                    break;
                case NbtTagType.List:
                    foreach (NbtTag child in (NbtList)tag) {
                        ValidateForWrite(child, depth + 1);
                    }
                    break;
            }
        }


        void ValidateStringFits(string value) {
            // UTF-8 needs at most 4 bytes per char, so short strings skip the exact count
            if ((long)value.Length * 4 <= flavor.MaxStringBytes) return;
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > flavor.MaxStringBytes) {
                throw new NbtFormatException(
                    "String is " + byteCount + " bytes, but the " + flavor.Name +
                    " flavor allows at most " + flavor.MaxStringBytes + ".");
            }
        }


        static NbtTag CreateTag(NbtTagType type) {
            switch (type) {
                case NbtTagType.Byte: return new NbtByte();
                case NbtTagType.Short: return new NbtShort();
                case NbtTagType.Int: return new NbtInt();
                case NbtTagType.Long: return new NbtLong();
                case NbtTagType.Float: return new NbtFloat();
                case NbtTagType.Double: return new NbtDouble();
                case NbtTagType.ByteArray: return new NbtByteArray();
                case NbtTagType.String: return new NbtString();
                case NbtTagType.List: return new NbtList();
                case NbtTagType.Compound: return new NbtCompound();
                case NbtTagType.IntArray: return new NbtIntArray();
                case NbtTagType.LongArray: return new NbtLongArray();
                default:
                    throw new NbtFormatException("NBT tag type out of range: " + (int)type);
            }
        }
    }
}
