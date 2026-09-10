using System;
using System.IO;

namespace fNbt {
    // DeflateStream does not report where the compressed data ends within its last read.
    // Keep those bytes so we can look for the checksum trailer.
    internal sealed class TrailerLocatingStream : Stream {
        readonly Stream baseStream;
        byte[] tail = Array.Empty<byte>();
        int tailLength;


        public TrailerLocatingStream(Stream stream) {
            NullableSupport.Assert(stream != null);
            baseStream = stream;
        }


        public override int Read(byte[] buffer, int offset, int count) {
            int bytesRead = baseStream.Read(buffer, offset, count);
            if (bytesRead > 0) {
                Buffer.BlockCopy(buffer, offset, Reserve(bytesRead), 0, bytesRead);
            }
            return bytesRead;
        }


#if NETCOREAPP
        public override int Read(Span<byte> buffer) {
            int bytesRead = baseStream.Read(buffer);
            if (bytesRead > 0) {
                buffer.Slice(0, bytesRead).CopyTo(Reserve(bytesRead));
            }
            return bytesRead;
        }
#endif


        byte[] Reserve(int length) {
            if (tail.Length < length) {
                tail = new byte[length];
            }
            tailLength = length;
            return tail;
        }


        // Call after reading DeflateStream to its end. To keep reads fast, we look for
        // the expected bytes instead of parsing the compressed data again. A match in
        // the wrong place can hide a damaged or missing trailer.
        public void ValidateTrailer(byte[] expected, string mismatchMessage) {
            int length = expected.Length;
            for (int start = tailLength - length; start >= 0; start--) {
                if (Matches(tail, start, expected, 0, length)) return;
            }

            // The trailer may be split across reads. Try the longest matching part first,
            // then read only what is still missing.
            byte[] rest = new byte[length];
            int have = 0;
            for (int inTail = Math.Min(length - 1, tailLength); inTail >= 0; inTail--) {
                if (!Matches(tail, tailLength - inTail, expected, 0, inTail)) continue;
                int need = length - inTail;
                while (have < need) {
                    int bytesRead = baseStream.Read(rest, have, need - have);
                    if (bytesRead <= 0) throw new InvalidDataException(mismatchMessage);
                    have += bytesRead;
                }
                if (Matches(rest, 0, expected, inTail, need)) return;
            }
            throw new InvalidDataException(mismatchMessage);
        }


        static bool Matches(byte[] a, int aOffset, byte[] b, int bOffset, int length) {
            for (int i = 0; i < length; i++) {
                if (a[aOffset + i] != b[bOffset + i]) return false;
            }
            return true;
        }


        public override void Flush() { }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return false; }
        }

        public override bool CanWrite {
            get { return false; }
        }

        public override long Length {
            get { throw new NotSupportedException(); }
        }

        public override long Position {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}
