using System;
using System.IO;

namespace fNbt.Test {
    // Reports Position and Length shifted by Shift, standing in for a stream too large to build
    internal sealed class FarPositionStream : Stream {
        readonly Stream baseStream;
        public long Shift;


        public FarPositionStream(Stream baseStream) {
            this.baseStream = baseStream;
        }


        public override int Read(byte[] buffer, int offset, int count) {
            return baseStream.Read(buffer, offset, count);
        }


        public override long Position {
            get { return baseStream.Position + Shift; }
            set { baseStream.Position = value - Shift; }
        }

        public override long Length {
            get { return baseStream.Length + Shift; }
        }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return true; }
        }

        public override bool CanWrite {
            get { return false; }
        }

        public override void Flush() { }

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
