using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace fNbt {
    /// <summary> Represents a complete NBT file. </summary>
    public sealed class NbtFile {
        // Size of buffers that are used to avoid frequent reads from / writes to compressed streams
        const int WriteBufferSize = 8 * 1024;

        // Size of buffers used for reading to/from files
        const int FileStreamBufferSize = 64 * 1024;

        /// <summary> Gets the file name used for most recent loading/saving of this file.
        /// May be <c>null</c>, if this <c>NbtFile</c> instance has not been loaded from, or saved to, a file. </summary>
        public string? FileName { get; private set; }

        /// <summary> Gets the compression method used for most recent loading/saving of this file.
        /// Defaults to AutoDetect. </summary>
        public NbtCompression FileCompression { get; private set; }

        /// <summary> Root tag of this file. Must be a named CompoundTag. Defaults to an empty-named tag. </summary>
        /// <remarks> The assigned tag may already belong to another compound or list: <c>NbtFile</c> is not
        /// a container and does not set or clear <c>Parent</c>. Saving then writes only this subtree,
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

        /// <summary> The flavor new NbtFiles are created with when no options are given.
        /// Defaults to <see cref="NbtFlavor.Java"/>. </summary>
        /// <exception cref="ArgumentNullException"> value is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> value is a flavor without a root name;
        /// use <see cref="NbtCodec"/> for those. </exception>
        /// <exception cref="NotSupportedException"> value is a flavor that is not yet supported. </exception>
        public static NbtFlavor DefaultFlavor {
            get { return defaultFlavor; }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                value.EnsureUsableForFiles(nameof(value));
                defaultFlavor = value;
            }
        }

        static NbtFlavor defaultFlavor = NbtFlavor.Java;

        /// <summary> Whether new NbtFiles should default to big-endian encoding (default: true). </summary>
        [Obsolete("Use DefaultFlavor instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public static bool BigEndianByDefault {
            get { return DefaultFlavor.BigEndian; }
            set { DefaultFlavor = value ? NbtFlavor.Java : NbtFlavor.Bedrock; }
        }

        /// <summary> The flavor this file reads and writes with. Initialized from
        /// <see cref="DefaultFlavor"/>, or from the options given at construction. </summary>
        /// <exception cref="ArgumentNullException"> value is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> value is a flavor without a root name;
        /// use <see cref="NbtCodec"/> for those. </exception>
        /// <exception cref="NotSupportedException"> value is a flavor that is not yet supported. </exception>
        public NbtFlavor Flavor {
            get { return flavor; }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                value.EnsureUsableForFiles(nameof(value));
                flavor = value;
            }
        }

        NbtFlavor flavor;

        /// <summary> Whether this file should read/write tags in big-endian encoding format. </summary>
        [Obsolete("Use Flavor instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public bool BigEndian {
            get { return flavor.BigEndian; }
            set { flavor = value ? NbtFlavor.Java : NbtFlavor.Bedrock; }
        }

        // Validation and limit settings, fixed at construction (default options unless given)
        readonly bool validateOnRead;
        readonly bool validateOnWrite = true;
        readonly bool disallowTrailingData;
        readonly long maxAllocation = long.MaxValue;

        /// <summary> Gets or sets the default value of <c>BufferSize</c> property. Default is 8192. 
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
        /// Initialized to value of <c>DefaultBufferSize</c> property. </summary>
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

        /// <summary> Creates an empty NbtFile with the <see cref="DefaultFlavor"/> and default options.
        /// RootTag will be set to an empty <c>NbtCompound</c> with a blank name (""). </summary>
        public NbtFile() {
            flavor = DefaultFlavor;
            BufferSize = DefaultBufferSize;
            rootTag = new NbtCompound("");
        }


        /// <summary> Creates an empty NbtFile with the given options.
        /// RootTag will be set to an empty <c>NbtCompound</c> with a blank name (""). </summary>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="options"/> or its <c>Flavor</c> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> The options' flavor has no root name;
        /// use <see cref="NbtCodec"/> for those. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <c>MaxAllocation</c> is zero or negative. </exception>
        /// <exception cref="NotSupportedException"> The options' flavor is not yet supported. </exception>
        public NbtFile(NbtOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Flavor == null) {
                throw new ArgumentNullException(nameof(options), "Options must name a flavor.");
            }
            if (options.MaxAllocation <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options), options.MaxAllocation,
                                                      "MaxAllocation must be positive.");
            }
            options.Flavor.EnsureUsableForFiles(nameof(options));
            flavor = options.Flavor;
            validateOnRead = options.ValidateOnRead;
            validateOnWrite = options.ValidateOnWrite;
            disallowTrailingData = options.DisallowTrailingData;
            maxAllocation = options.MaxAllocation ?? long.MaxValue;
            BufferSize = DefaultBufferSize;
            rootTag = new NbtCompound("");
        }


        /// <summary> Creates a new NBT file with the given root tag and options. </summary>
        /// <param name="rootTag"> Compound tag to set as the root tag. May not be <c>null</c>. </param>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="rootTag"/>, <paramref name="options"/>,
        /// or the options' <c>Flavor</c> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given <paramref name="rootTag"/> is unnamed;
        /// or if the options' flavor has no root name. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <c>MaxAllocation</c> is zero or negative. </exception>
        /// <exception cref="NotSupportedException"> The options' flavor is not yet supported. </exception>
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
        /// Automatically detects compression. Assumes the file to be big-endian, and uses default buffer size. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public NbtFile(string fileName)
            : this() {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            LoadFromFile(fileName, NbtCompression.AutoDetect, null);
        }

        #endregion


        #region Loading

        /// <summary> Loads NBT data from a file. Existing <c>RootTag</c> will be replaced. Compression will be auto-detected. </summary>
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


        /// <summary> Loads NBT data from a file. Existing <c>RootTag</c> will be replaced. </summary>
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
                var readFileStream = new FileStream(fileName,
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


        /// <summary> Loads NBT data from a byte array. Existing <c>RootTag</c> will be replaced. <c>FileName</c> will be set to null. </summary>
        /// <param name="buffer"> Stream from which data will be loaded. If <paramref name="compression"/> is set to AutoDetect, this stream must support seeking. </param>
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

            using (var ms = new MemoryStream(buffer, index, length)) {
                LoadFromStream(ms, compression, selector);
                FileName = null;
                return ms.Position;
            }
        }


        /// <summary> Loads NBT data from a byte array. Existing <c>RootTag</c> will be replaced. <c>FileName</c> will be set to null. </summary>
        /// <param name="buffer"> Stream from which data will be loaded. If <paramref name="compression"/> is set to AutoDetect, this stream must support seeking. </param>
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
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            using (var ms = new MemoryStream(buffer, index, length)) {
                LoadFromStream(ms, compression, null);
                FileName = null;
                return ms.Position;
            }
        }


        /// <summary> Loads NBT data from a stream. Existing <c>RootTag</c> will be replaced </summary>
        /// <remarks> Compressed data is read to the end of the stream: the decompressor reads ahead in
        /// chunks, so the document's exact extent is unknowable, and reading it all guarantees the
        /// container checksum gets validated. Uncompressed loads stop exactly at the end of the
        /// document, leaving any trailing bytes in place. </remarks>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped.
        /// No reference is stored to this callback after loading (don't worry about implicitly captured closures). May be <c>null</c>. </param>
        /// <returns> Number of bytes read from the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If <paramref name="compression"/> is set to AutoDetect, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, decompressing failed, or given stream does not support reading. </exception>
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
                    using (var decStream = new GZipStream(stream, CompressionMode.Decompress, true)) {
                        if (bufferSize > 0) {
                            var bufferedStream = new BufferedStream(decStream, bufferSize);
                            LoadFromStreamInternal(bufferedStream, selector);
                            DrainToEnd(bufferedStream);
                        } else {
                            LoadFromStreamInternal(decStream, selector);
                            DrainToEnd(decStream);
                        }
                    }
                    FinishCompressedLoad(stream);
                    break;

                case NbtCompression.None:
                    LoadFromStreamInternal(stream, selector);
                    if (disallowTrailingData) {
                        EnsureNoTrailingData(stream);
                    }
                    break;

                case NbtCompression.ZLib:
#if NET6_0_OR_GREATER
                    // Built-in ZLibStream is faster and validates the checksum too
                    try {
                        using (var decStream = new System.IO.Compression.ZLibStream(stream, CompressionMode.Decompress, true)) {
                            if (bufferSize > 0) {
                                var bufferedStream = new BufferedStream(decStream, bufferSize);
                                LoadFromStreamInternal(bufferedStream, selector);
                                DrainToEnd(bufferedStream);
                            } else {
                                LoadFromStreamInternal(decStream, selector);
                                DrainToEnd(decStream);
                            }
                        }
                    } catch (IOException ex) when (ex.GetType().FullName == ZLibExceptionTypeName) {
                        throw new InvalidDataException("Failed to decompress ZLib data.", ex);
                    }
#else
                    ValidateZLibHeader(stream);
                    using (var decStream = new ZLibStream(stream, CompressionMode.Decompress, true)) {
                        if (bufferSize > 0) {
                            var bufferedStream = new BufferedStream(decStream, bufferSize);
                            LoadFromStreamInternal(bufferedStream, selector);
                            DrainToEnd(bufferedStream);
                        } else {
                            LoadFromStreamInternal(decStream, selector);
                            DrainToEnd(decStream);
                        }
                        ValidateZLibChecksum(stream, startOffset, decStream.Checksum);
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


        /// <summary> Loads NBT data from a stream. Existing <c>RootTag</c> will be replaced </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <returns> Number of bytes read from the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If <paramref name="compression"/> is set to AutoDetect, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, decompressing failed, or given stream does not support reading. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public long LoadFromStream(Stream stream, NbtCompression compression) {
            return LoadFromStream(stream, compression, null);
        }


        // Reading a decompressor to its end forces it to process the container's trailer, so
        // checksum validation cannot depend on how the input happened to be chunked.
        static void DrainToEnd(Stream stream) {
            byte[] buffer = new byte[4096];
            while (stream.Read(buffer, 0, buffer.Length) > 0) { }
        }


        // Opt-in strict check for uncompressed loads: the document must end exactly where the
        // stream does. Compressed loads consume the stream regardless, so they have nothing to check.
        static void EnsureNoTrailingData(Stream stream) {
            bool hasTrailingData = stream.CanSeek
                ? stream.Position < stream.Length
                : stream.ReadByte() >= 0;
            if (hasTrailingData) {
                throw new NbtFormatException("Trailing data found after the NBT document.");
            }
        }


        // Compressed loads consume the source stream to its end: the decompressor reads ahead in
        // chunks, so the document's exact extent within the stream is unknowable. Consuming the
        // rest makes the returned byte count deterministic instead of chunking-dependent.
        static void FinishCompressedLoad(Stream stream) {
            if (stream.CanSeek) {
                stream.Position = stream.Length;
            } else {
                DrainToEnd(stream);
            }
        }


#if !NET6_0_OR_GREATER
        // After draining, the document runs to the end of the stream, so the last four bytes are
        // the big-endian Adler-32 trailer. Non-seekable streams cannot be validated: the
        // decompressor buffers past the deflate data, leaving the trailer out of reach.
        static void ValidateZLibChecksum(Stream stream, long startOffset, int computedChecksum) {
            if (!stream.CanSeek) return;
            long length = stream.Length;
            // 2-byte header + deflate data + 4-byte trailer; anything shorter failed already
            if (length - startOffset < 7) return;
            stream.Position = length - 4;
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            int b3 = stream.ReadByte();
            uint expected = (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
            if (expected != (uint)computedChecksum) {
                throw new InvalidDataException("Failed to decompress ZLib data: checksum mismatch.");
            }
        }
#endif


        static NbtCompression DetectCompression(Stream stream) {
            NbtCompression compression;
            if (!stream.CanSeek) {
                throw new NotSupportedException("Cannot auto-detect compression on a stream that's not seekable.");
            }
            int firstByte = stream.ReadByte();
            switch (firstByte) {
                case -1:
                    throw new EndOfStreamException();

                case (byte)NbtTagType.Compound: // 0x0A
                    compression = NbtCompression.None;
                    break;

                case 0x1F:
                    // GZip magic number
                    compression = NbtCompression.GZip;
                    break;

                case 0x78:
                    // ZLib header
                    compression = NbtCompression.ZLib;
                    break;

                default:
                    throw new InvalidDataException("Could not auto-detect compression format.");
            }
            stream.Seek(-1, SeekOrigin.Current);
            return compression;
        }


        void LoadFromStreamInternal(Stream stream, TagSelector? tagSelector) {
            // Make sure the first byte in this file is the tag for a TAG_Compound
            int firstByte = stream.ReadByte();
            if (firstByte < 0) {
                throw new EndOfStreamException();
            }
            if (firstByte != (int)NbtTagType.Compound) {
                throw new NbtFormatException("Given NBT stream does not start with a TAG_Compound");
            }
            var reader = new NbtBinaryReader(stream, flavor.BigEndian) {
                Selector = tagSelector
            };
            NbtFlavor? readValidationFlavor = (validateOnRead && flavor.HasRestrictions) ? flavor : null;
            if (maxAllocation != long.MaxValue || readValidationFlavor != null) {
                reader.SetLimits(maxAllocation, readValidationFlavor);
            }

            var rootCompound = new NbtCompound(reader.ReadString());
            rootCompound.ReadTag(reader);
            RootTag = rootCompound;
        }

        #endregion


        #region Saving

        /// <summary> Saves this NBT file to a file. Nothing is written if RootTag is <c>null</c>. </summary>
        /// <remarks> The file is created or truncated up front, so a failed save can leave it
        /// partially written. If you are overwriting an existing file, write to a temp file first
        /// then use <c>File.Replace</c> to swap it with the original. </remarks>
        /// <param name="fileName"> File to write data to. May not be <c>null</c>. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be AutoDetect. </param>
        /// <returns> Number of bytes written to the file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If AutoDetect was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="InvalidDataException"> If given stream does not support writing. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while creating the file. </exception>
        /// <exception cref="UnauthorizedAccessException"> Specified file is read-only, or a permission issue occurred. </exception>
        /// <exception cref="NbtFormatException"> If one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if tags are nested more than 512 levels deep. </exception>
        public long SaveToFile(string fileName, NbtCompression compression) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));

            using (
                var saveFile = new FileStream(fileName,
                                              FileMode.Create,
                                              FileAccess.Write,
                                              FileShare.None,
                                              FileStreamBufferSize,
                                              FileOptions.SequentialScan)) {
                return SaveToStream(saveFile, compression);
            }
        }


        /// <summary> Saves this NBT file to a buffer. Nothing is written if RootTag is <c>null</c>. </summary>
        /// <param name="buffer"> Buffer to write data to. May not be <c>null</c>. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream should begin. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be AutoDetect. </param>
        /// <returns> Number of bytes written to the buffer. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If AutoDetect was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>;
        /// if <paramref name="index"/> is less than zero; or if <paramref name="index"/> is greater than the length of <paramref name="buffer"/>. </exception>
        /// <exception cref="InvalidDataException"> If given stream does not support writing. </exception>
        /// <exception cref="UnauthorizedAccessException"> Specified file is read-only, or a permission issue occurred. </exception>
        /// <exception cref="NbtFormatException"> If one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if tags are nested more than 512 levels deep. </exception>
        public long SaveToBuffer(byte[] buffer, int index, NbtCompression compression) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            using (var ms = new MemoryStream(buffer, index, buffer.Length - index)) {
                return SaveToStream(ms, compression);
            }
        }


        /// <summary> Saves this NBT file to a new byte array. Returns an empty array if RootTag is <c>null</c>. </summary>
        /// <param name="compression"> Compression mode to use for saving. May not be AutoDetect. </param>
        /// <returns> Byte array containing the serialized NBT data. </returns>
        /// <exception cref="ArgumentException"> If AutoDetect was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="InvalidDataException"> If given stream does not support writing. </exception>
        /// <exception cref="NotSupportedException"> If the serialized document does not fit in a single array. </exception>
        /// <exception cref="UnauthorizedAccessException"> Specified file is read-only, or a permission issue occurred. </exception>
        /// <exception cref="NbtFormatException"> If one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
        /// or if tags are nested more than 512 levels deep. </exception>
        public byte[] SaveToBuffer(NbtCompression compression) {
            if (compression == NbtCompression.None) {
                // Uncompressed size can be measured up front, since counting writes copies nothing. Growing
                // a MemoryStream and copying it out instead costs about five times the payload.
                var counter = new ByteCountingStream(Stream.Null);
                SaveToStream(counter, NbtCompression.None);
                if (counter.BytesWritten > int.MaxValue) {
                    throw new NotSupportedException("This NBT document is too large to save to a single buffer.");
                }
                var buffer = new byte[counter.BytesWritten];
                SaveToStream(new MemoryStream(buffer, 0, buffer.Length, true, true), NbtCompression.None);
                return buffer;
            }

            using (var ms = new MemoryStream()) {
                SaveToStream(ms, compression);
                return ms.ToArray();
            }
        }


        /// <summary> Saves this NBT file to a stream. Nothing is written to stream if RootTag is <c>null</c>. </summary>
        /// <param name="stream"> Stream to write data to. May not be <c>null</c>. </param>
        /// <param name="compression"> Compression mode to use for saving. May not be AutoDetect. </param>
        /// <returns> Number of bytes written to the stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If AutoDetect was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="InvalidDataException"> If given stream does not support writing. </exception>
        /// <exception cref="NbtFormatException"> If RootTag is null;
        /// or if RootTag is unnamed;
        /// or if one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements;
        /// or if a string is longer than the flavor's limit (65,535 bytes for the Java flavors);
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

            if (rootTag.Name == null) {
                // This may trigger if root tag has been renamed
                throw new NbtFormatException(
                    "Cannot save NbtFile: Root tag is not named. Its name may be an empty string, but not null.");
            }
            if (validateOnWrite && flavor.HasRestrictions) {
                flavor.ValidateTree(rootTag, 0);
            }

            long startOffset = 0;
            if (stream.CanSeek) {
                startOffset = stream.Position;
            } else {
                stream = new ByteCountingStream(stream);
            }

            switch (compression) {
                case NbtCompression.ZLib:
                    stream.WriteByte(0x78);
                    stream.WriteByte(0x01);
                    int checksum;
                    using (var compressStream = new ZLibStream(stream, CompressionMode.Compress, true)) {
                        var bufferedStream = new BufferedStream(compressStream, WriteBufferSize);
                        RootTag.WriteTag(new NbtBinaryWriter(bufferedStream, flavor.BigEndian, modifiedUtf8: flavor.UsesModifiedUtf8));
                        bufferedStream.Flush();
                        checksum = compressStream.Checksum;
                    }
                    byte[] checksumBytes = BitConverter.GetBytes(checksum);
                    if (BitConverter.IsLittleEndian) {
                        // Adler32 checksum is big-endian
                        Array.Reverse(checksumBytes);
                    }
                    stream.Write(checksumBytes, 0, checksumBytes.Length);
                    break;

                case NbtCompression.GZip:
                    using (var compressStream = new GZipStream(stream, CompressionMode.Compress, true)) {
                        // use a buffered stream to avoid GZipping in small increments (which has a lot of overhead)
                        var bufferedStream = new BufferedStream(compressStream, WriteBufferSize);
                        RootTag.WriteTag(new NbtBinaryWriter(bufferedStream, flavor.BigEndian, modifiedUtf8: flavor.UsesModifiedUtf8));
                        bufferedStream.Flush();
                    }
                    break;

                case NbtCompression.None:
                    var writer = new NbtBinaryWriter(stream, flavor.BigEndian, modifiedUtf8: flavor.UsesModifiedUtf8);
                    RootTag.WriteTag(writer);
                    break;

                    // Can't be AutoDetect or unknown: parameter is already validated
            }

            if (stream.CanSeek) {
                return stream.Position - startOffset;
            } else {
                return ((ByteCountingStream)stream).BytesWritten;
            }
        }

        #endregion


        /// <summary> Reads the root name from the given NBT file. Automatically detects compression. </summary>
        /// <param name="fileName"> Name of the file from which first tag will be read. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public static string ReadRootTagName(string fileName) {
            return ReadRootTagName(fileName, NbtCompression.AutoDetect, DefaultFlavor.BigEndian, defaultBufferSize);
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
        public static string ReadRootTagName(string fileName, NbtCompression compression, bool bigEndian,
                                             int bufferSize) {
            if (fileName == null) {
                throw new ArgumentNullException(nameof(fileName));
            }
            if (!File.Exists(fileName)) {
                throw new FileNotFoundException("Could not find the given NBT file.", fileName);
            }
            using (FileStream readFileStream = File.OpenRead(fileName)) {
                return ReadRootTagName(readFileStream, compression, bigEndian, bufferSize);
            }
        }


        /// <summary> Reads the root name from the given stream of NBT data. </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading this stream. </param>
        /// <param name="bigEndian"> Whether the stream uses big-endian (default) or little-endian encoding. </param>
        /// <param name="bufferSize"> No longer used. </param>
        /// <returns> Name of the root tag in the given stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If compression is set to AutoDetect, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, decompressing failed, or given stream does not support reading. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public static string ReadRootTagName(Stream stream, NbtCompression compression, bool bigEndian,
                                             int bufferSize) {
            // bufferSize param is no longer used because it caused perf problems due to over-reading on netcore.
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            // detect compression, based on the first byte
            if (compression == NbtCompression.AutoDetect) {
                compression = DetectCompression(stream);
            }

            switch (compression) {
                case NbtCompression.GZip:
                    // Buffering the output would undo PeekStream by pulling a whole bufferSize at once.
                    using (var decStream = new GZipStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                        return GetRootNameInternal(decStream, bigEndian);
                    }

                case NbtCompression.None:
                    return GetRootNameInternal(stream, bigEndian);

                case NbtCompression.ZLib:
#if NET6_0_OR_GREATER
                    // Only validates the zlib header. The trailing checksum cannot be validated by peeking.
                    try {
                        using (var decStream = new System.IO.Compression.ZLibStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                            return GetRootNameInternal(decStream, bigEndian);
                        }
                    } catch (IOException ex) when (ex.GetType().FullName == ZLibExceptionTypeName) {
                        throw new InvalidDataException("Failed to decompress ZLib data.", ex);
                    }
#else
                    ValidateZLibHeader(stream);
                    using (var decStream = new DeflateStream(new PeekStream(stream), CompressionMode.Decompress, true)) {
                        return GetRootNameInternal(decStream, bigEndian);
                    }
#endif

                default:
                    throw new ArgumentOutOfRangeException(nameof(compression));
            }
        }


        static string GetRootNameInternal(Stream stream, bool bigEndian) {
            NullableSupport.Assert(stream != null);
            int firstByte = stream.ReadByte();
            if (firstByte < 0) {
                throw new EndOfStreamException();
            } else if (firstByte != (int)NbtTagType.Compound) {
                throw new NbtFormatException("Given NBT stream does not start with a TAG_Compound");
            }
            var reader = new NbtBinaryReader(stream, bigEndian);

            return reader.ReadString();
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


#if !NET6_0_OR_GREATER
        // netstandard2.0 reads ZLib through a DeflateStream, so the two header bytes are checked
        // manually. Full loads validate the Adler-32 trailer separately; peeks cannot.
        static void ValidateZLibHeader(Stream stream) {
            int cmf = stream.ReadByte();
            int flg = stream.ReadByte();
            // Compression method must be deflate, window at most 32 KiB,
            // no preset dictionary, and the check bits must make sense.
            if (cmf < 0 || flg < 0 ||
                (cmf & 0x0F) != 8 || (cmf >> 4) > 7 ||
                (flg & 0x20) != 0 || ((cmf << 8) | flg) % 31 != 0) {
                throw new InvalidDataException("Invalid ZLib header.");
            }
        }
#endif

        // ZLibStream throws this on a bad header or checksum. We just want to re-wrap it in a nicer exception.
        const string ZLibExceptionTypeName = "System.IO.Compression.ZLibException";
    }
}
