using System;
using System.IO;

namespace fNbt {
    // Feeds a raw DeflateStream and remembers the last chunk it handed over. A raw inflater
    // stops at the end of the final block and never reads on, so once it reports the end the
    // container trailer starts somewhere inside that chunk, or right after it in the source.
    // Given the trailer bytes the checksum says to expect, ValidateTrailer finds them without
    // seeking and without reading past them.
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


        // Call after the inflater has been read to its end. The trailer is the first place the
        // expected bytes appear, searched from the end of the remembered chunk; a match that
        // straddles the chunk's end is completed from the source, reading only what the match
        // still needs. A false match costs a byte count that is off by a few bytes, at odds of
        // about one in 2^32 per candidate position. A trailer that cannot be found, or a source
        // that ends before the trailer is complete, fails with the caller's message.
        public void ValidateTrailer(byte[] expected, string mismatchMessage) {
            int length = expected.Length;
            for (int start = tailLength - length; start >= 0; start--) {
                if (Matches(tail, start, expected, 0, length)) return;
            }

            // Bytes of the trailer that already arrived sit at the very end of the chunk; the rest
            // are the next bytes of the source. Longer prefixes are tried first, and every read
            // stays inside the trailer under whichever hypothesis holds.
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
