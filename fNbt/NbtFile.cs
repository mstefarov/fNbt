using System;
using System.IO;
using System.IO.Compression;

namespace fNbt {
    /// <summary> Represents a complete NBT file. </summary>
    public sealed partial class NbtFile {
        // Size of buffers that are used to avoid frequent reads from / writes to compressed streams
        const int WriteBufferSize = 8 * 1024;

        // Size of buffers used for reading to/from files
        const int FileStreamBufferSize = 64 * 1024;

        /// <summary> Gets the file name used for most recent loading/saving of this file.
        /// May be <c>null</c>, if this <see cref="NbtFile"/> instance has not been loaded from, or saved to, a file. </summary>
        public string? FileName { get; private set; }

        /// <summary> Gets the compression method used for most recent loading/saving of this file.
        /// Defaults to <see cref="NbtCompression.AutoDetect"/>. </summary>
        public NbtCompression FileCompression { get; private set; }

        /// <summary> Root tag of this file. Must be a named CompoundTag. Defaults to an empty-named tag. </summary>
        /// <remarks> The assigned tag may already belong to another compound or list: <see cref="NbtFile"/> is not
        /// a container and does not set or clear <see cref="NbtTag.Parent"/>. Saving then writes only this subtree,
        /// as a standalone document. </remarks>
        /// <exception cref="ArgumentException"> If given tag is unnamed. </exception>
        /// <exception cref="ArgumentNullException"> If value is <c>null</c>. </exception>
        public NbtCompound RootTag {
            get { return rootTag; }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (value.Name == null) throw new ArgumentException("Root tag must be named.");
                rootTag = value;
            }
        }

        NbtCompound rootTag;

        /// <summary> Whether the current <see cref="NbtOptions.DefaultFlavor"/> is big-endian. </summary>
        [Obsolete("Use NbtOptions.DefaultFlavor instead. NbtFlavor.Java is big-endian, NbtFlavor.Bedrock little-endian.")]
        public static bool BigEndianByDefault {
            get { return NbtOptions.DefaultFlavor.BigEndian; }
        }

        /// <summary> The flavor this file reads and writes with, fixed at construction.
        /// To re-save a document under a different flavor, create a new <see cref="NbtFile"/> over the same
        /// <see cref="RootTag"/>: both files then share one tree. </summary>
        public NbtFlavor Flavor {
            get { return flavor; }
        }

        readonly NbtFlavor flavor;

        /// <summary> Whether this file's flavor is big-endian. </summary>
        [Obsolete("Use Flavor instead. To change encodings, create a new NbtFile over the same RootTag with the target flavor.")]
        public bool BigEndian {
            get { return flavor.BigEndian; }
        }

        // Validation and limit settings, fixed at construction (current defaults unless given)
        readonly bool validateOnRead;
        readonly bool validateOnWrite;
        readonly long maxAllocation;

        /// <summary> Gets or sets the default value of <see cref="BufferSize"/> property. Default is 8192.
        /// Set to 0 to disable buffering by default. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is negative. </exception>
        public static int DefaultBufferSize {
            get { return defaultBufferSize; }
            set {
                if (value < 0) {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "DefaultBufferSize cannot be negative.");
                }
                defaultBufferSize = value;
            }
        }

        static int defaultBufferSize = 8 * 1024;

        /// <summary> Gets or sets the size of internal buffer used for reading files and streams.
        /// Initialized to value of <see cref="DefaultBufferSize"/> property. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is negative. </exception>
        public int BufferSize {
            get { return bufferSize; }
            set {
                if (value < 0) {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "BufferSize cannot be negative.");
                }
                bufferSize = value;
            }
        }

        int bufferSize;


        #region Constructors

        /// <summary> Creates an empty <see cref="NbtFile"/> with the current defaults
        /// (<see cref="NbtOptions.DefaultFlavor"/> and the other <see cref="NbtOptions"/> defaults).
        /// <see cref="RootTag"/> will be set to an empty <see cref="NbtCompound"/> with a blank name (""). </summary>
        public NbtFile()
            : this(NbtOptions.ResolveDefaults()) { }


        /// <summary> Creates an empty <see cref="NbtFile"/> for the given flavor, with the current default
        /// policy settings. <see cref="RootTag"/> will be set to an empty <see cref="NbtCompound"/> with a blank
        /// name (""). </summary>
        /// <param name="flavor"> Encoding to read and write with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> The flavor has no root name;
        /// use <see cref="NbtCodec"/> for those. </exception>
        public NbtFile(NbtFlavor flavor)
            : this(NbtOptions.ResolveForFile(flavor, nameof(flavor))) { }


        NbtFile(NbtOptions.Resolved resolved) {
            flavor = resolved.Flavor;
            validateOnRead = resolved.ValidateOnRead;
            // Only restrictive flavors have anything to validate on write
            validateOnWrite = resolved.ValidateOnWrite && flavor.HasRestrictions;
            maxAllocation = resolved.MaxAllocation;
            BufferSize = DefaultBufferSize;
            rootTag = new NbtCompound("");
        }


        /// <summary> Creates an empty <see cref="NbtFile"/> with the given options.
        /// <see cref="RootTag"/> will be set to an empty <see cref="NbtCompound"/> with a blank name (""). </summary>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="options"/> or its <see cref="NbtOptions.Flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> The options' flavor has no root name;
        /// use <see cref="NbtCodec"/> for those. </exception>
        public NbtFile(NbtOptions options)
            : this(NbtOptions.ResolveForFile(options, nameof(options))) { }


        /// <summary> Creates a new NBT file with the given root tag and flavor, with the current
        /// default policy settings. The tag is used directly, not cloned. <see cref="NbtFile"/> is not a
        /// tag container, so a root tag may be shared between files: to re-save a loaded document
        /// under a different flavor, pass its <see cref="RootTag"/> here. </summary>
        /// <param name="rootTag"> Compound tag to set as the root tag. May not be <c>null</c>. </param>
        /// <param name="flavor"> Encoding to read and write with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="rootTag"/> or
        /// <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given <paramref name="rootTag"/> is unnamed;
        /// or the flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        public NbtFile(NbtCompound rootTag, NbtFlavor flavor)
            : this(flavor) {
            if (rootTag == null) throw new ArgumentNullException(nameof(rootTag));
            RootTag = rootTag;
        }


        /// <summary> Creates a new NBT file with the given root tag and options. </summary>
        /// <param name="rootTag"> Compound tag to set as the root tag. May not be <c>null</c>. </param>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="rootTag"/>, <paramref name="options"/>,
        /// or the options' <see cref="NbtOptions.Flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given <paramref name="rootTag"/> is unnamed;
        /// or if the options' flavor has no root name. </exception>
        public NbtFile(NbtCompound rootTag, NbtOptions options)
            : this(options) {
            if (rootTag == null) throw new ArgumentNullException(nameof(rootTag));
            RootTag = rootTag;
        }


        /// <summary> Creates a new NBT file with the given root tag. </summary>
        /// <param name="rootTag"> Compound tag to set as the root tag. May be <c>null</c>. </param>
        /// <exception cref="ArgumentException"> If given <paramref name="rootTag"/> is unnamed. </exception>
        public NbtFile(NbtCompound rootTag)
            : this() {
            if (rootTag == null) throw new ArgumentNullException(nameof(rootTag));
            RootTag = rootTag;
        }


        /// <summary> Loads NBT data from a file using the most common settings.
        /// Automatically detects compression, and reads with the current defaults
        /// (<see cref="NbtOptions.DefaultFlavor"/> and the other <see cref="NbtOptions"/> defaults). </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public NbtFile(string fileName)
            : this() {
            LoadFromFile(fileName, NbtCompression.AutoDetect, null);
        }

        #endregion


        #region Loading

        /// <summary> Loads NBT data from a file. Existing <see cref="RootTag"/> will be replaced. Compression will be auto-detected. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <returns> Number of bytes read from the file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public long LoadFromFile(string fileName) {
            return LoadFromFile(fileName, NbtCompression.AutoDetect, null);
        }


        /// <summary> Loads NBT data from a file. Existing <see cref="RootTag"/> will be replaced. </summary>
        /// <remarks> See <see cref="LoadFromStream(Stream, NbtCompression, TagSelector)"/> for how
        /// compressed loads verify checksums and where the load stops. </remarks>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped.
        /// No reference is stored to this callback after loading (don't worry about implicitly captured closures). May be <c>null</c>. </param>
        /// <returns> Number of bytes read from the file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public long LoadFromFile(string fileName, NbtCompression compression, TagSelector? selector) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));

            using (
                FileStream readFileStream = new FileStream(fileName,
                                                    FileMode.Open,
                                                    FileAccess.Read,
                                                    FileShare.Read,
                                                    FileStreamBufferSize,
                                                    FileOptions.SequentialScan)) {
                LoadFromStream(readFileStream, compression, selector);
                FileName = fileName;
                return readFileStream.Position;
            }
        }


        /// <summary> Loads NBT data from a byte array. Existing <see cref="RootTag"/> will be replaced. <see cref="FileName"/> will be set to null. </summary>
        /// <remarks> See <see cref="LoadFromStream(Stream, NbtCompression, TagSelector)"/> for how
        /// compressed loads verify checksums and where the load stops. </remarks>
        /// <param name="buffer"> Byte array from which data will be loaded. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream begins. Must not be negative. </param>
        /// <param name="length"> Maximum number of bytes to read from the given buffer. Must not be negative.
        /// An <see cref="EndOfStreamException"/> is thrown if NBT stream is longer than the given length. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped.
        /// No reference is stored to this callback after loading (don't worry about implicitly captured closures). May be <c>null</c>. </param>
        /// <returns> Number of bytes read from the buffer. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>;
        /// if <paramref name="index"/> or <paramref name="length"/> is less than zero;
        /// if the sum of <paramref name="index"/> and <paramref name="length"/> is greater than the length of <paramref name="buffer"/>. </exception>
        /// <exception cref="EndOfStreamException"> If NBT stream extends beyond the given <paramref name="length"/>. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public long LoadFromBuffer(byte[] buffer, int index, int length, NbtCompression compression,
                                   TagSelector? selector) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            using (MemoryStream ms = new MemoryStream(buffer, index, length)) {
                LoadFromStream(ms, compression, selector);
                FileName = null;
                return ms.Position;
            }
        }


        /// <summary> Loads NBT data from a byte array. Existing <see cref="RootTag"/> will be replaced. <see cref="FileName"/> will be set to null. </summary>
        /// <param name="buffer"> Byte array from which data will be loaded. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream begins. Must not be negative. </param>
        /// <param name="length"> Maximum number of bytes to read from the given buffer. Must not be negative.
        /// An <see cref="EndOfStreamException"/> is thrown if NBT stream is longer than the given length. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <returns> Number of bytes read from the buffer. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>;
        /// if <paramref name="index"/> or <paramref name="length"/> is less than zero;
        /// if the sum of <paramref name="index"/> and <paramref name="length"/> is greater than the length of <paramref name="buffer"/>. </exception>
        /// <exception cref="EndOfStreamException"> If NBT stream extends beyond the given <paramref name="length"/>. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public long LoadFromBuffer(byte[] buffer, int index, int length, NbtCompression compression) {
            return LoadFromBuffer(buffer, index, length, compression, null);
        }


        /// <summary> Loads NBT data from a stream. Existing <see cref="RootTag"/> will be replaced </summary>
        /// <remarks> Compressed loads check checksums. On .NET 6 and later, a document cut off
        /// inside its trailer may still load without error. A non-seekable GZip load, and every
        /// ZLib load on .NET Standard 2.0, verifies the trailer by searching the bytes fed to the
        /// decompressor last for the expected values, so a chance match there can hide a damaged
        /// or missing trailer. Seekable streams are left at their end, so the returned byte count is
        /// deterministic; non-seekable streams stay wherever decompression stopped, which can be
        /// past the document, since decompressors read ahead. Concatenated GZip members decompress
        /// as one document only on .NET Core and later and only from a seekable stream; otherwise
        /// the load reads the first member. Uncompressed loads stop exactly at the end of the
        /// document, leaving any trailing bytes in place. </remarks>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to <see cref="NbtCompression.AutoDetect"/>, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped.
        /// No reference is stored to this callback after loading (don't worry about implicitly captured closures). May be <c>null</c>. </param>
        /// <returns> Number of bytes read from the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If <paramref name="compression"/> is set to <see cref="NbtCompression.AutoDetect"/>, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public long LoadFromStream(Stream stream, NbtCompression compression, TagSelector? selector) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            FileName = null;

            // detect compression, based on the first byte
            if (compression == NbtCompression.AutoDetect) {
                FileCompression = DetectCompression(stream);
            } else {
                FileCompression = compression;
            }

            // prepare to count bytes read
            long startOffset = 0;
            if (stream.CanSeek) {
                startOffset = stream.Position;
            } else {
                stream = new ByteCountingStream(stream);
            }

            switch (FileCompression) {
                case NbtCompression.GZip:
                    if (stream.CanSeek) {
                        using (GZipStream decStream = new GZipStream(stream, CompressionMode.Decompress, true)) {
                            LoadAndDrain(decStream, selector);
                        }
                    } else {
                        // GZipStream reads on past a finished member looking for the next one, which
                        // blocks on a source that stays open, so it cannot be drained here
                        LoadNonSeekableGZip(stream, selector);
                    }
                    FinishCompressedLoad(stream);
                    break;

                case NbtCompression.None:
                    LoadFromStreamInternal(stream, selector);
                    break;

                case NbtCompression.ZLib:
#if NET6_0_OR_GREATER
                    // The built-in stream validates the checksum itself and stops at the end of
                    // the member, so draining it is safe on any source
                    try {
                        using (ZLibStream decStream = new ZLibStream(stream, CompressionMode.Decompress, true)) {
                            LoadAndDrain(decStream, selector);
                        }
                    } catch (IOException ex) when (ex.GetType().FullName == ZLibExceptionTypeName) {
                        throw new InvalidDataException("Failed to decompress ZLib data.", ex);
                    }
#else
                    ValidateZLibHeader(stream);
                    TrailerLocatingStream feed = new TrailerLocatingStream(stream);
                    using (ZLibStream decStream = new ZLibStream(feed, CompressionMode.Decompress, true)) {
                        LoadAndDrain(decStream, selector);
                        uint adler = (uint)decStream.Checksum;
                        byte[] trailer = { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler };
                        feed.ValidateTrailer(trailer, "Failed to decompress ZLib data: checksum trailer mismatch or missing.");
                    }
#endif
                    FinishCompressedLoad(stream);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(compression));
            }

            // report bytes read
            if (stream.CanSeek) {
                return stream.Position - startOffset;
            } else {
                return ((ByteCountingStream)stream).BytesRead;
            }
        }


        /// <summary> Loads NBT data from a stream. Existing <see cref="RootTag"/> will be replaced </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to <see cref="NbtCompression.AutoDetect"/>, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <returns> Number of bytes read from the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If <paramref name="compression"/> is set to <see cref="NbtCompression.AutoDetect"/>, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public long LoadFromStream(Stream stream, NbtCompression compression) {
            return LoadFromStream(stream, compression, null);
        }


        void LoadFromStreamInternal(Stream stream, TagSelector? tagSelector) {
            ReadCompoundHeader(stream);
            NbtBinaryReader reader = new NbtBinaryReader(stream, flavor, maxAllocation, validateOnRead) {
                Selector = tagSelector
            };
            NbtCompound rootCompound = new NbtCompound(reader.ReadString());
            rootCompound.ReadTag(reader, NbtTag.MaxDepth);
            RootTag = rootCompound;
        }


        // Every file flavor opens a document with a TAG_Compound type byte
        static void ReadCompoundHeader(Stream stream) {
            int firstByte = stream.ReadByte();
            if (firstByte < 0) throw new EndOfStreamException();
            if (firstByte != (int)NbtTagType.Compound) throw NbtFormatException.NotCompoundRoot();
        }

        #endregion


        #region Saving

        /// <summary> Saves this NBT file to a file. </summary>
        /// <remarks> The file is created or truncated up front, so a failed save can leave it
        /// partially written. If you are overwriting an existing file, write to a temp file first
        /// then use <c>File.Replace</c> to swap it with the original. </remarks>
        /// <param name="fileName"> File to write data to. May not be <c>null</c>. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be <see cref="NbtCompression.AutoDetect"/>. </param>
        /// <returns> Number of bytes written to the file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If <see cref="NbtCompression.AutoDetect"/> was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while creating the file. </exception>
        /// <exception cref="UnauthorizedAccessException"> Specified file is read-only, or a permission issue occurred. </exception>
        /// <exception cref="NbtFormatException"> If one of the <see cref="NbtCompound"/> tags contained unnamed tags;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if enabled validation rejects a tag type or string length for the flavor;
        /// or if tags are nested more than 512 levels deep. </exception>
        public long SaveToFile(string fileName, NbtCompression compression) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));

            using (
                FileStream saveFile = new FileStream(fileName,
                                              FileMode.Create,
                                              FileAccess.Write,
                                              FileShare.None,
                                              FileStreamBufferSize,
                                              FileOptions.SequentialScan)) {
                return SaveToStream(saveFile, compression);
            }
        }


        /// <summary> Saves this NBT file to a buffer. </summary>
        /// <param name="buffer"> Buffer to write data to. May not be <c>null</c>. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream should begin. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be <see cref="NbtCompression.AutoDetect"/>. </param>
        /// <returns> Number of bytes written to the buffer. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If <see cref="NbtCompression.AutoDetect"/> was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>;
        /// if <paramref name="index"/> is less than zero; or if <paramref name="index"/> is greater than the length of <paramref name="buffer"/>. </exception>
        /// <exception cref="NbtFormatException"> If one of the <see cref="NbtCompound"/> tags contained unnamed tags;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if enabled validation rejects a tag type or string length for the flavor;
        /// or if tags are nested more than 512 levels deep. </exception>
        public long SaveToBuffer(byte[] buffer, int index, NbtCompression compression) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            using (MemoryStream ms = new MemoryStream(buffer, index, buffer.Length - index)) {
                return SaveToStream(ms, compression);
            }
        }


        /// <summary> Saves this NBT file to a new byte array. </summary>
        /// <param name="compression"> Compression mode to use for saving. May not be <see cref="NbtCompression.AutoDetect"/>. </param>
        /// <returns> Byte array containing the serialized NBT data. </returns>
        /// <exception cref="ArgumentException"> If <see cref="NbtCompression.AutoDetect"/> was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If the serialized document does not fit in a single array. </exception>
        /// <exception cref="NbtFormatException"> If one of the <see cref="NbtCompound"/> tags contained unnamed tags;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if enabled validation rejects a tag type or string length for the flavor;
        /// or if tags are nested more than 512 levels deep. </exception>
        public byte[] SaveToBuffer(NbtCompression compression) {
            if (compression == NbtCompression.None) {
                // Sizing up front buys an exact array from a single write pass, and the sizing
                // walk validates as it goes.
                EnsureRootNamed();
                long size = NbtSizer.SizeDocument(rootTag, withName: true, flavor, validateOnWrite);
                if (size > int.MaxValue) {
                    throw new NotSupportedException("This NBT document is too large to save to a single buffer.");
                }
                byte[] buffer = ArrayAllocator.ForOverwrite<byte>((int)size);
                MemoryStream output = new MemoryStream(buffer, 0, buffer.Length, true, true);
                rootTag.WriteTag(new NbtBinaryWriter(output, flavor), NbtTag.MaxDepth);
                ArrayAllocator.EnsureFilled(buffer, output.Position);
                return buffer;
            }

#if NETCOREAPP
            // Compressed size cannot be known up front, so pooled segments stand in for the
            // buffers MemoryStream would double through and throw away.
            using (PooledSegmentStream pooled = new PooledSegmentStream()) {
                SaveToStream(pooled, compression);
                return pooled.ToArray();
            }
#else
            using (MemoryStream ms = new MemoryStream()) {
                SaveToStream(ms, compression);
                return ms.ToArray();
            }
#endif
        }


        /// <summary> Saves this NBT file to a stream. </summary>
        /// <param name="stream"> Stream to write data to. May not be <c>null</c>. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be <see cref="NbtCompression.AutoDetect"/>. </param>
        /// <returns> Number of bytes written to the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If <see cref="NbtCompression.AutoDetect"/> was given as the <paramref name="compression"/> mode; or if <paramref name="stream"/> does not support writing. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NbtFormatException"> If <see cref="RootTag"/> is unnamed;
        /// or if one of the <see cref="NbtCompound"/> tags contained unnamed tags;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if enabled validation rejects a tag type or string length for the flavor;
        /// or if tags are nested more than 512 levels deep. </exception>
        public long SaveToStream(Stream stream, NbtCompression compression) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            switch (compression) {
                case NbtCompression.AutoDetect:
                    throw new ArgumentException("AutoDetect is not a valid NbtCompression value for saving.");
                case NbtCompression.ZLib:
                case NbtCompression.GZip:
                case NbtCompression.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(compression));
            }

            EnsureRootNamed();
            if (validateOnWrite) {
                flavor.ValidateTree(rootTag, NbtTag.MaxDepth);
            }

            long startOffset = 0;
            if (stream.CanSeek) {
                startOffset = stream.Position;
            } else {
                stream = new ByteCountingStream(stream);
            }

            switch (compression) {
                case NbtCompression.ZLib:
#if NET6_0_OR_GREATER
                    // The framework stream writes the zlib header and Adler-32 trailer itself,
                    // with the checksum computed in native code
                    using (ZLibStream compressStream = new ZLibStream(stream, CompressionMode.Compress, true)) {
                        WriteRootBuffered(compressStream);
                    }
#else
                    int checksum;
                    using (ZLibStream compressStream = new ZLibStream(stream, CompressionMode.Compress, true)) {
                        // The header goes out once the compressor has accepted the stream
                        stream.WriteByte(0x78);
                        stream.WriteByte(0x01);
                        WriteRootBuffered(compressStream);
                        checksum = compressStream.Checksum;
                    }
                    byte[] checksumBytes = BitConverter.GetBytes(checksum);
                    if (BitConverter.IsLittleEndian) {
                        // Adler32 checksum is big-endian
                        Array.Reverse(checksumBytes);
                    }
                    stream.Write(checksumBytes, 0, checksumBytes.Length);
#endif
                    break;

                case NbtCompression.GZip:
                    using (GZipStream compressStream = new GZipStream(stream, CompressionMode.Compress, true)) {
                        WriteRootBuffered(compressStream);
                    }
                    break;

                case NbtCompression.None:
                    RootTag.WriteTag(new NbtBinaryWriter(stream, flavor), NbtTag.MaxDepth);
                    break;

                    // Can't be AutoDetect or unknown: parameter is already validated
            }

            if (stream.CanSeek) {
                return stream.Position - startOffset;
            } else {
                return ((ByteCountingStream)stream).BytesWritten;
            }
        }


        void EnsureRootNamed() {
            if (rootTag.Name == null) {
                // This may trigger if root tag has been renamed
                throw new NbtFormatException(
                    "Cannot save NbtFile: Root tag is not named. Its name may be an empty string, but not null.");
            }
        }


        // Compressors do badly with many small writes, so the tree is fed through a buffer
        void WriteRootBuffered(Stream compressor) {
            BufferedStream bufferedStream = new BufferedStream(compressor, WriteBufferSize);
            RootTag.WriteTag(new NbtBinaryWriter(bufferedStream, flavor), NbtTag.MaxDepth);
            bufferedStream.Flush();
        }

        #endregion


        /// <summary> Reads the root name from the given NBT file. Automatically detects
        /// compression, and reads with <see cref="NbtOptions.DefaultFlavor"/>. </summary>
        /// <param name="fileName"> Name of the file from which first tag will be read. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public static string ReadRootTagName(string fileName) {
            return ReadRootTagName(fileName, NbtCompression.AutoDetect, NbtOptions.DefaultFlavor);
        }


        /// <summary> Reads the root name from the given NBT file.
        /// Root names longer than 65,535 bytes fail with <see cref="NbtFormatException"/>. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <param name="compression"> Format in which the given file is compressed. </param>
        /// <param name="flavor"> Encoding to read with. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> The flavor has no root name; use <see cref="NbtCodec"/> for those. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public static string ReadRootTagName(string fileName, NbtCompression compression, NbtFlavor flavor) {
            if (fileName == null) {
                throw new ArgumentNullException(nameof(fileName));
            }
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            flavor.EnsureUsableForFiles(nameof(flavor));
            if (!File.Exists(fileName)) {
                throw new FileNotFoundException("Could not find the given NBT file.", fileName);
            }
            using (FileStream readFileStream = File.OpenRead(fileName)) {
                return ReadRootTagName(readFileStream, compression, flavor);
            }
        }


        /// <summary> Reads the root name from the given stream of NBT data.
        /// Root names longer than 65,535 bytes fail with <see cref="NbtFormatException"/>. </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to <see cref="NbtCompression.AutoDetect"/>, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading this stream. </param>
        /// <param name="flavor"> Encoding to read with. </param>
        /// <returns> Name of the root tag in the given stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> The flavor has no root name; use <see cref="NbtCodec"/> for those. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If compression is set to <see cref="NbtCompression.AutoDetect"/>, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public static string ReadRootTagName(Stream stream, NbtCompression compression, NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            flavor.EnsureUsableForFiles(nameof(flavor));
            return ReadRootTagNameInternal(stream, compression, flavor);
        }


        /// <summary> Reads the root name from the given NBT file. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <param name="compression"> Format in which the given file is compressed. </param>
        /// <param name="bigEndian"> Whether the file uses big-endian (default) or little-endian encoding. </param>
        /// <param name="bufferSize"> No longer used. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        [Obsolete("Use ReadRootTagName(string, NbtCompression, NbtFlavor) instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public static string ReadRootTagName(string fileName, NbtCompression compression, bool bigEndian,
                                             int bufferSize) {
            return ReadRootTagName(fileName, compression, bigEndian ? NbtFlavor.Java : NbtFlavor.Bedrock);
        }


        /// <summary> Reads the root name from the given stream of NBT data. </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to <see cref="NbtCompression.AutoDetect"/>, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading this stream. </param>
        /// <param name="bigEndian"> Whether the stream uses big-endian (default) or little-endian encoding. </param>
        /// <param name="bufferSize"> No longer used. </param>
        /// <returns> Name of the root tag in the given stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If compression is set to <see cref="NbtCompression.AutoDetect"/>, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        [Obsolete("Use ReadRootTagName(Stream, NbtCompression, NbtFlavor) instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public static string ReadRootTagName(Stream stream, NbtCompression compression, bool bigEndian,
                                             int bufferSize) {
            return ReadRootTagNameInternal(stream, compression, bigEndian ? NbtFlavor.Java : NbtFlavor.Bedrock);
        }


        static string ReadRootTagNameInternal(Stream stream, NbtCompression compression, NbtFlavor flavor) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            // detect compression, based on the first byte
            if (compression == NbtCompression.AutoDetect) {
                compression = DetectCompression(stream);
            }

            switch (compression) {
                case NbtCompression.GZip:
                    // Buffering the output would undo PeekStream by pulling a whole bufferSize at once.
                    using (GZipStream decStream = new GZipStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                        return GetRootNameInternal(decStream, flavor);
                    }

                case NbtCompression.None:
                    return GetRootNameInternal(stream, flavor);

                case NbtCompression.ZLib:
#if NET6_0_OR_GREATER
                    // Only validates the zlib header. The trailing checksum cannot be validated by peeking.
                    try {
                        using (ZLibStream decStream = new ZLibStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                            return GetRootNameInternal(decStream, flavor);
                        }
                    } catch (IOException ex) when (ex.GetType().FullName == ZLibExceptionTypeName) {
                        throw new InvalidDataException("Failed to decompress ZLib data.", ex);
                    }
#else
                    ValidateZLibHeader(stream);
                    using (DeflateStream decStream = new DeflateStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                        return GetRootNameInternal(decStream, flavor);
                    }
#endif

                default:
                    throw new ArgumentOutOfRangeException(nameof(compression));
            }
        }


        static string GetRootNameInternal(Stream stream, NbtFlavor flavor) {
            ReadCompoundHeader(stream);
            // No options reach this API, so bound the name by the largest file-flavor ceiling.
            // Otherwise a varint prefix could demand an arbitrarily large allocation.
            return new NbtBinaryReader(stream, flavor, maxAllocation: ushort.MaxValue).ReadString();
        }


        /// <summary> Prints contents of the root tag, and any child tags, to a string. </summary>
        public override string ToString() {
            return RootTag.ToString(NbtTag.DefaultIndentString);
        }


        /// <summary> Prints contents of the root tag, and any child tags, to a string.
        /// Indents the string using multiples of the given indentation string. </summary>
        /// <param name="indentString"> String to be used for indentation. </param>
        /// <returns> A string representing contents of this tag, and all child tags (if any). </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="indentString"/> is <c>null</c>. </exception>
        public string ToString(string indentString) {
            return RootTag.ToString(indentString);
        }
    }
}
