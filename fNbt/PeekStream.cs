using System;
using System.IO;

namespace fNbt {
    // .NET Core 3.0 through 8.0 inflate all the input they are handed, however little the caller asked
    // for, and DeflateStream hands them 8 KiB at a time. That turns a 20-byte peek into 128 KiB of work.
    // .NET Framework and .NET 9+ don't.
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
