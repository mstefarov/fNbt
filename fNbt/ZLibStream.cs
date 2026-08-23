using System;
using System.IO;
using System.IO.Compression;

namespace fNbt {
    /// <summary> DeflateStream wrapper that calculates Adler32 checksum of the written data,
    /// to allow writing ZLib header (RFC-1950). </summary>
    internal sealed class ZLibStream : DeflateStream {
        uint adler32A = 1,
             adler32B;

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
    }
}
