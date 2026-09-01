using System;
using System.IO;

namespace fNbt {
    // Limits a decompressor's read-ahead during a root-name peek. Inflaters may process
    // everything they are handed, so capping input is what keeps a peek cheap.
    internal sealed class PeekStream : Stream {
        // Enough to keep inflate fed, little enough that a peek stays cheap.
        const int ReadChunk = 64;

        readonly Stream baseStream;


        public PeekStream(Stream stream) {
            NullableSupport.Assert(stream != null);
            baseStream = stream;
        }


        public override int Read(byte[] buffer, int offset, int count) {
            return baseStream.Read(buffer, offset, Math.Min(count, ReadChunk));
        }


#if NETCOREAPP
        public override int Read(Span<byte> buffer) {
            return baseStream.Read(buffer.Slice(0, Math.Min(buffer.Length, ReadChunk)));
        }
#endif


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
