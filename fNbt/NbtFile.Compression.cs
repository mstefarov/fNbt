using System;
using System.IO;
using System.IO.Compression;

namespace fNbt {
    partial class NbtFile {
        // Read to the end to finish the checksum check, including data after the root tag.
        void LoadAndDrain(Stream decompressed, TagSelector? selector) {
            Stream input = bufferSize > 0 ? new BufferedStream(decompressed, bufferSize) : decompressed;
            LoadFromStreamInternal(input, selector);
#if NETCOREAPP
            Span<byte> buffer = stackalloc byte[4096];
            while (input.Read(buffer) > 0) { }
#else
            byte[] buffer = new byte[4096];
            while (input.Read(buffer, 0, buffer.Length) > 0) { }
#endif
        }


        // DeflateStream does not wait for another GZip member. We read the header and
        // check the trailer ourselves.
        void LoadNonSeekableGZip(Stream stream, TagSelector? selector) {
            SkipGZipHeader(stream);
            TrailerLocatingStream feed = new TrailerLocatingStream(stream);
            using (DeflateStream deflate = new DeflateStream(feed, CompressionMode.Decompress, true)) {
                Crc32Stream crcStream = new Crc32Stream(deflate);
                LoadAndDrain(crcStream, selector);
                uint crc = crcStream.Crc;
                uint size = (uint)crcStream.BytesRead;
                byte[] trailer = {
                    (byte)crc, (byte)(crc >> 8), (byte)(crc >> 16), (byte)(crc >> 24),
                    (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24)
                };
                feed.ValidateTrailer(trailer, "Failed to decompress GZip data: checksum trailer mismatch or missing.");
            }
        }


        // RFC 1952: ten fixed bytes, then the optional fields FLG announces. The optional header
        // CRC is the low 16 bits of the CRC-32 over everything before it, so the header is read
        // through the checksum stream and compared when the field is present.
        static void SkipGZipHeader(Stream stream) {
            Crc32Stream header = new Crc32Stream(stream);
            int id1 = header.ReadByte();
            int id2 = header.ReadByte();
            int method = header.ReadByte();
            int flags = header.ReadByte();
            if (flags < 0) throw new EndOfStreamException();
            if (id1 != 0x1F || id2 != 0x8B || method != 8 || (flags & 0xE0) != 0) {
                throw new InvalidDataException("Invalid GZip header.");
            }
            SkipHeaderBytes(header, 6);
            if ((flags & 0x04) != 0) {
                int low = header.ReadByte();
                int high = header.ReadByte();
                if (high < 0) throw new EndOfStreamException();
                SkipHeaderBytes(header, low | (high << 8));
            }
            if ((flags & 0x08) != 0) SkipHeaderString(header);
            if ((flags & 0x10) != 0) SkipHeaderString(header);
            if ((flags & 0x02) != 0) {
                uint expected = header.Crc & 0xFFFF;
                int low = stream.ReadByte();
                int high = stream.ReadByte();
                if (high < 0) throw new EndOfStreamException();
                if ((uint)(low | (high << 8)) != expected) {
                    throw new InvalidDataException("Failed to decompress GZip data: header checksum mismatch.");
                }
            }
        }


        static void SkipHeaderBytes(Stream stream, int count) {
            for (int i = 0; i < count; i++) {
                if (stream.ReadByte() < 0) throw new EndOfStreamException();
            }
        }


        static void SkipHeaderString(Stream stream) {
            int value;
            do {
                value = stream.ReadByte();
                if (value < 0) throw new EndOfStreamException();
            } while (value != 0);
        }


        // Read-ahead hides where the document ends, so seekable sources are left at their end for a
        // deterministic byte count. Non-seekable sources stay put, since reading on could block.
        static void FinishCompressedLoad(Stream stream) {
            if (stream.CanSeek) {
                stream.Position = stream.Length;
            }
        }


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

                default:
                    // A plausible ZLib CMF names deflate and a window no larger than 32 KiB.
                    // Full header validation belongs to the decompressor.
                    if (firstByte <= 0x78 && (firstByte & 0x0F) == 8) {
                        compression = NbtCompression.ZLib;
                        break;
                    }
                    throw new InvalidDataException("Could not auto-detect compression format.");
            }
            stream.Seek(-1, SeekOrigin.Current);
            return compression;
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

#if NET6_0_OR_GREATER
        // ZLibStream throws this on a bad header or checksum. We just want to re-wrap it in a nicer exception.
        const string ZLibExceptionTypeName = "System.IO.Compression.ZLibException";
#endif
    }
}