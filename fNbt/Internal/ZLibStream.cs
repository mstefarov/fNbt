#if !NET6_0_OR_GREATER
using System;
using System.IO;
using System.IO.Compression;

namespace fNbt {
    /// <summary> DeflateStream wrapper that calculates the Adler32 checksum of the data passing
    /// through it, in either direction, to support the ZLib container (RFC-1950): writing the
    /// trailer when compressing, and validating it when decompressing. net6+ builds use the
    /// framework's System.IO.Compression.ZLibStream in both directions instead. </summary>
    internal sealed class ZLibStream : DeflateStream {
        uint adler32A = 1,
             adler32B;

        byte[]? singleByteBuffer;

        const uint ChecksumModulus = 65521;

        // Most bytes that can be summed before the totals could overflow a uint. Chunking here is what
        // allows one modulus per chunk instead of two per byte.
        const int BytesPerModulus = 5552;

        public int Checksum {
            get { return unchecked((int)((adler32B << 16) | adler32A)); }
        }


        void UpdateChecksum(byte[] data, int offset, int length) {
            uint a = adler32A, b = adler32B;
            while (length > 0) {
                int chunk = Math.Min(BytesPerModulus, length);
                length -= chunk;
                for (int i = 0; i < chunk; i++) {
                    a += data[offset++];
                    b += a;
                }
                a %= ChecksumModulus;
                b %= ChecksumModulus;
            }
            adler32A = a;
            adler32B = b;
        }


        public ZLibStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : base(stream, mode, leaveOpen) { }


        public override void Write(byte[] array, int offset, int count) {
            UpdateChecksum(array, offset, count);
            base.Write(array, offset, count);
        }


        public override int Read(byte[] array, int offset, int count) {
            int bytesRead = base.Read(array, offset, count);
            if (bytesRead > 0) {
                UpdateChecksum(array, offset, bytesRead);
            }
            return bytesRead;
        }


        // Single-byte reads and writes route through this type's own bulk methods, so the
        // checksum sees every byte exactly once. Calling into the base instead would skip
        // the checksum on runtimes where DeflateStream has a single-byte fast path, and
        // count the byte twice where the Stream default falls back to the virtual bulk methods.
        public override int ReadByte() {
            byte[] buffer = singleByteBuffer ?? (singleByteBuffer = new byte[1]);
            int bytesRead = Read(buffer, 0, 1);
            return bytesRead == 0 ? -1 : buffer[0];
        }


        public override void WriteByte(byte value) {
            byte[] buffer = singleByteBuffer ?? (singleByteBuffer = new byte[1]);
            buffer[0] = value;
            Write(buffer, 0, 1);
        }
    }
}
#endif
