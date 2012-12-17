using System;
using System.IO;
using System.IO.Compression;
using JetBrains.Annotations;

namespace LibNbt {
    /// <summary> Represents a complete NBT file. </summary>
    public sealed class NbtFile {
        // buffer used to avoid frequent reads from / writes to compressed streams
        const int BufferSize = 8192;

        /// <summary> Gets the file name used for most recent loading/saving of this file.
        /// May be <c>null</c>, if this NbtFile instance has not been loaded from, or saved to, a file. </summary>
        [CanBeNull]
        public string FileName { get; private set; }


        /// <summary> Gets the compression method used for most recent loading/saving of this file.
        /// Defaults to AutoDetect. </summary>
        public NbtCompression FileCompression { get; private set; }


        /// <summary> Root tag of this file. Must be a named CompoundTag. Defaults to <c>null</c>. </summary>
        /// <exception cref="ArgumentException"> If given tag is unnamed. </exception>
        [NotNull]
        public NbtCompound RootTag {
            get {
                return rootTag;
            }
            set {
                if( value == null )
                    throw new ArgumentNullException( "value" );
                if( value.Name == null )
                    throw new ArgumentException( "Root tag must be named." );
                rootTag = value;
            }
        }

        NbtCompound rootTag;


        /// <summary> Creates a new NBT file with the given root tag. </summary>
        /// <param name="rootTag"> Compound tag to set as the root tag. May be <c>null</c>. </param>
        /// <exception cref="ArgumentException"> If given rootTag is unnamed. </exception>
        public NbtFile( [NotNull] NbtCompound rootTag ) {
            if( rootTag == null )
                throw new ArgumentNullException( "rootTag" );
            RootTag = rootTag;
        }


        /// <summary> Loads NBT data from a file. Automatically detects compression. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public NbtFile( [NotNull] string fileName ) {
            if( fileName == null )
                throw new ArgumentNullException( "fileName" );
            LoadFromFile( fileName, NbtCompression.AutoDetect, null );
        }


        /// <summary> Loads NBT data from a file. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped. May be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public NbtFile( [NotNull] string fileName, NbtCompression compression, [CanBeNull] TagSelector selector ) {
            if( fileName == null )
                throw new ArgumentNullException( "fileName" );
            LoadFromFile( fileName, compression, selector );
        }


        /// <summary> Loads NBT data from a stream. FileName will be set to null. </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped. May be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="NotSupportedException"> If compression is set to AutoDetect, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, decompressing failed, or given stream does not support reading. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        public NbtFile( [NotNull] Stream stream, NbtCompression compression, [CanBeNull] TagSelector selector ) {
            LoadFromStream( stream, compression, selector );
        }


        /// <summary> Loads NBT data from a byte array. FileName will be set to null. </summary>
        /// <param name="buffer"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream begins. Must not be negative. </param>
        /// <param name="length"> Maximum number of bytes to read from the given buffer. Must not be negative.
        /// An EndOfStreamException is thrown if NBT stream is longer than the given length. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <param name="selector"> Optional callback to select which tags to load into memory. Root may not be skipped.
        /// No reference is stored to this callback after loading (don't worry about implicitly captured closures). May be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>;
        /// if <paramref name="index"/> or <paramref name="length"/> is less than zero;
        /// if the sum of <paramref name="index"/> and <paramref name="length"/> is greater than the length of <paramref name="buffer"/>. </exception>
        /// <exception cref="EndOfStreamException"> If NBT stream extends beyond the given <paramref name="length"/>. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        public NbtFile( [NotNull] byte[] buffer, int index, int length, NbtCompression compression, [CanBeNull] TagSelector selector ) {
            LoadFromBuffer(buffer, index, length, compression, selector);
        }


        /// <summary> Loads NBT data from a file. Existing RootTag will be replaced. Compression will be auto-detected. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <returns> Number of bytes read from the file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public int LoadFromFile( [NotNull] string fileName ) {
            return LoadFromFile( fileName, NbtCompression.AutoDetect, null );
        }


        /// <summary> Loads NBT data from a file. Existing RootTag will be replaced. </summary>
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
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        public int LoadFromFile( [NotNull] string fileName, NbtCompression compression,
                                 [CanBeNull] TagSelector selector ) {
            if( fileName == null )
                throw new ArgumentNullException( "fileName" );

            using( FileStream readFileStream = File.OpenRead( fileName ) ) {
                LoadFromStream( readFileStream, compression, selector );
                FileName = fileName;
                return (int)readFileStream.Position;
            }
        }


        /// <summary> Loads NBT data from a byte array. Existing RootTag will be replaced. FileName will be set to null. </summary>
        /// <param name="buffer"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="index"> The index into <paramref name="buffer"/> at which the stream begins. Must not be negative. </param>
        /// <param name="length"> Maximum number of bytes to read from the given buffer. Must not be negative.
        /// An EndOfStreamException is thrown if NBT stream is longer than the given length. </param>
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
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        public int LoadFromBuffer( [NotNull] byte[] buffer, int index, int length, NbtCompression compression,
                                   [CanBeNull] TagSelector selector ) {
            if( buffer == null )
                throw new ArgumentNullException( "buffer" );

            using( MemoryStream ms = new MemoryStream( buffer, index, length ) ) {
                LoadFromStream( ms, compression, selector );
                FileName = null;
                return (int)ms.Position;
            }
        }


        /// <summary> Loads NBT data from a stream. Existing RootTag will be replaced </summary>
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
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        public int LoadFromStream( [NotNull] Stream stream, NbtCompression compression,
                                    [CanBeNull] TagSelector selector ) {
            if( stream == null )
                throw new ArgumentNullException( "stream" );

            FileName = null;
            FileCompression = compression;

            // detect compression, based on the first byte
            if( compression == NbtCompression.AutoDetect ) {
                compression = DetectCompression( stream );
            }

            long startPosition = stream.Position;

            switch( compression ) {
            case NbtCompression.GZip:
                using( var decStream = new GZipStream( stream, CompressionMode.Decompress, true ) ) {
                    LoadFromStreamInternal( new BufferedStream( decStream, BufferSize ), selector );
                }
                break;

            case NbtCompression.None:
                LoadFromStreamInternal( stream, selector );
                break;

            case NbtCompression.ZLib:
                if( stream.ReadByte() != 0x78 ) {
                    throw new InvalidDataException( "Incorrect ZLib header. Expected 0x78 0x9C" );
                }
                stream.ReadByte();
                using( var decStream = new DeflateStream( stream, CompressionMode.Decompress, true ) ) {
                    LoadFromStreamInternal( new BufferedStream( decStream, BufferSize ), selector );
                }
                break;

            default:
                throw new ArgumentOutOfRangeException( "compression" );
            }

            return (int)(stream.Position - startPosition);
        }


        static NbtCompression DetectCompression( Stream stream ) {
            NbtCompression compression;
            if( !stream.CanSeek ) {
                throw new NotSupportedException( "Cannot auto-detect compression on a stream that's not seekable." );
            }
            int firstByte = stream.ReadByte();
            switch( firstByte ) {
            case -1:
                throw new EndOfStreamException();

            case (byte)NbtTagType.Compound: // 0x0A
                compression = NbtCompression.None;
                break;

            case 0x1F:
                // gzip magic number
                compression = NbtCompression.GZip;
                break;

            case 0x78:
                // zlib header
                compression = NbtCompression.ZLib;
                break;

            default:
                throw new InvalidDataException( "Could not auto-detect compression format." );
            }
            stream.Seek( -1, SeekOrigin.Current );
            return compression;
        }


        void LoadFromStreamInternal( [NotNull] Stream stream, [CanBeNull] TagSelector tagSelector ) {
            if( stream == null )
                throw new ArgumentNullException( "stream" );

            // Make sure the first byte in this file is the tag for a TAG_Compound
            if( stream.ReadByte() != (int)NbtTagType.Compound ) {
                throw new NbtFormatException( "Given NBT stream does not start with a TAG_Compound" );
            }
            NbtReader reader = new NbtReader( stream ) {
                Selector = tagSelector
            };

            var rootCompound = new NbtCompound( reader.ReadString() );
            rootCompound.ReadTag( reader );
            RootTag = rootCompound;
        }


        /// <summary> Saves this NBT file to a stream. Nothing is written to stream if RootTag is <c>null</c>. </summary>
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
        /// or if an NbtList tag had Unknown list type and no elements. </exception>
        public int SaveToFile( [NotNull] string fileName, NbtCompression compression ) {
            if( fileName == null )
                throw new ArgumentNullException( "fileName" );

            using( FileStream saveFile = File.Create( fileName ) ) {
                return SaveToStream( saveFile, compression );
            }
        }


        /// <summary> Saves this NBT file to a stream. Nothing is written to stream if RootTag is <c>null</c>. </summary>
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
        /// or if an NbtList tag had Unknown list type and no elements. </exception>
        public int SaveToBuffer( [NotNull] byte[] buffer, int index, NbtCompression compression ) {
            if( buffer == null )
                throw new ArgumentNullException( "buffer" );

            using( MemoryStream ms = new MemoryStream( buffer, index, buffer.Length - index ) ) {
                SaveToStream( ms, compression );
                return (int)ms.Position;
            }
        }


        /// <summary> Saves this NBT file to a stream. Nothing is written to stream if RootTag is <c>null</c>. </summary>
        /// <param name="compression"> Compression mode to use for saving. May not be AutoDetect. </param>
        /// <returns> Byte array containing the serialized NBT data. </returns>
        /// <exception cref="ArgumentException"> If AutoDetect was given as the <paramref name="compression"/> mode. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="InvalidDataException"> If given stream does not support writing. </exception>
        /// <exception cref="UnauthorizedAccessException"> Specified file is read-only, or a permission issue occurred. </exception>
        /// <exception cref="NbtFormatException"> If one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements. </exception>
        public byte[] SaveToBuffer( NbtCompression compression ) {
            using( MemoryStream ms = new MemoryStream() ) {
                SaveToStream( ms, compression );
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
        /// <exception cref="NbtFormatException"> If one of the NbtCompound tags contained unnamed tags;
        /// or if an NbtList tag had Unknown list type and no elements. </exception>
        public int SaveToStream( [NotNull] Stream stream, NbtCompression compression ) {
            if( stream == null )
                throw new ArgumentNullException( "stream" );

            switch( compression ) {
            case NbtCompression.AutoDetect:
                throw new ArgumentException( "AutoDetect is not a valid NbtCompression value for saving." );
            case NbtCompression.ZLib:
            case NbtCompression.GZip:
            case NbtCompression.None:
                break;
            default:
                throw new ArgumentOutOfRangeException( "compression" );
            }

            long startPosition = stream.Position;

            switch( compression ) {
            case NbtCompression.ZLib:
                stream.WriteByte( 0x78 );
                stream.WriteByte( 0x01 );
                int checksum;
                using( var compressStream = new ZLibStream( stream, CompressionMode.Compress, true ) ) {
                    BufferedStream bufferedStream = new BufferedStream( compressStream, BufferSize );
                    RootTag.WriteTag( new NbtWriter( bufferedStream ), true );
                    bufferedStream.Flush();
                    checksum = compressStream.Checksum;
                }
                byte[] checksumBytes = BitConverter.GetBytes( checksum );
                if( BitConverter.IsLittleEndian ) {
                    // Adler32 checksum is big-endian
                    Array.Reverse( checksumBytes );
                }
                stream.Write( checksumBytes, 0, checksumBytes.Length );
                break;

            case NbtCompression.GZip:
                using( var compressStream = new GZipStream( stream, CompressionMode.Compress, true ) ) {
                    // use a buffered stream to avoid gzipping in small increments (which has a lot of overhead)
                    BufferedStream bufferedStream = new BufferedStream( compressStream, BufferSize );
                    RootTag.WriteTag( new NbtWriter( bufferedStream ), true );
                    bufferedStream.Flush();
                }
                break;

            case NbtCompression.None:
                RootTag.WriteTag( new NbtWriter( stream ), true );
                break;
            }

            return (int)( stream.Position - startPosition );
        }


        /// <summary> Reads the root name from the given NBT file. Automatically detects compression. </summary>
        /// <param name="fileName"> Name of the file from which first tag will be read. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        [NotNull]
        public static string ReadRootTagName( [NotNull] string fileName ) {
            return ReadRootTagName( fileName, NbtCompression.AutoDetect );
        }


        /// <summary> Reads the root name from the given NBT file. </summary>
        /// <param name="fileName"> Name of the file from which data will be loaded. </param>
        /// <param name="compression"> Format in which the given file is compressed. </param>
        /// <returns> Name of the root tag in the given NBT file. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="fileName"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="FileNotFoundException"> If given file was not found. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, or decompressing failed. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        /// <exception cref="IOException"> If an I/O error occurred while reading the file. </exception>
        [NotNull]
        public static string ReadRootTagName( [NotNull] string fileName, NbtCompression compression ) {
            if( fileName == null )
                throw new ArgumentNullException( "fileName" );
            if( !File.Exists( fileName ) ) {
                throw new FileNotFoundException( "Could not find the given NBT file.",
                                                 fileName );
            }
            using( FileStream readFileStream = File.OpenRead( fileName ) ) {
                return ReadRootTagName( readFileStream, compression );
            }
        }


        /// <summary> Reads the root name from the given stream of NBT data. </summary>
        /// <param name="stream"> Stream from which data will be loaded. If compression is set to AutoDetect, this stream must support seeking. </param>
        /// <param name="compression"> Compression method to use for loading/saving this file. </param>
        /// <returns> Name of the root tag in the given stream. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If an unrecognized/unsupported value was given for <paramref name="compression"/>. </exception>
        /// <exception cref="NotSupportedException"> If compression is set to AutoDetect, but the stream is not seekable. </exception>
        /// <exception cref="EndOfStreamException"> If file ended earlier than expected. </exception>
        /// <exception cref="InvalidDataException"> If file compression could not be detected, decompressing failed, or given stream does not support reading. </exception>
        /// <exception cref="NbtFormatException"> If an error occured while parsing data in NBT format. </exception>
        [NotNull]
        public static string ReadRootTagName( [NotNull] Stream stream, NbtCompression compression ) {
            if( stream == null )
                throw new ArgumentNullException( "stream" );

            // detect compression, based on the first byte
            if( compression == NbtCompression.AutoDetect ) {
                compression = DetectCompression( stream );
            }

            switch( compression ) {
            case NbtCompression.GZip:
                using( var decStream = new GZipStream( stream, CompressionMode.Decompress, true ) ) {
                    return GetRootNameInternal( new BufferedStream( decStream, BufferSize ) );
                }

            case NbtCompression.None:
                return GetRootNameInternal( stream );

            case NbtCompression.ZLib:
                if( stream.ReadByte() != 0x78 ) {
                    throw new InvalidDataException( "Incorrect ZLib header. Expected 0x78 0x9C" );
                }
                stream.ReadByte();
                using( var decStream = new DeflateStream( stream, CompressionMode.Decompress, true ) ) {
                    return GetRootNameInternal( new BufferedStream( decStream, BufferSize ) );
                }

            default:
                throw new ArgumentOutOfRangeException( "compression" );
            }
        }


        /// <summary> Renames the root tag. </summary>
        /// <param name="newTagName"> New name to give to the root tag. May be <c>null</c>. </param>
        public void RenameRootTag( string newTagName ) {
            rootTag.Name = newTagName;
        }


        [NotNull]
        static string GetRootNameInternal( [NotNull] Stream stream ) {
            if( stream == null )
                throw new ArgumentNullException( "stream" );
            NbtReader reader = new NbtReader( stream );

            if( reader.ReadTagType() != NbtTagType.Compound ) {
                throw new NbtFormatException( "Given NBT stream does not start with a TAG_Compound" );
            }

            return reader.ReadString();
        }

        public override string ToString()
        {
            return RootTag.ToString();
        }
    }
}