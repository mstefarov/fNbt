using System;
using System.IO;

namespace fNbt.Test {
    // Reports a Length of zero once reading has started, while Read keeps serving bytes: what a
    // buffered FileStream sees after another process truncates the file it already read ahead
    internal sealed class ShrinkingStream : Stream {
        readonly Stream baseStream;


        public ShrinkingStream(Stream baseStream) {
            this.baseStream = baseStream;
        }


        public override int Read(byte[] buffer, int offset, int count) {
            return baseStream.Read(buffer, offset, count);
        }


        public override long Position {
            get { return baseStream.Position; }
            set { baseStream.Position = value; }
        }

        public override long Length {
            get { return baseStream.Position == 0 ? baseStream.Length : 0; }
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
