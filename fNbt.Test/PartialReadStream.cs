using System;
using System.IO;

namespace fNbt.Test {
    class PartialReadStream : Stream {
        readonly Stream baseStream;
        readonly int increment;

        public PartialReadStream(Stream baseStream)
            : this(baseStream, 1) { }


        public PartialReadStream(Stream baseStream, int increment) {
            if (baseStream == null) throw new ArgumentNullException(nameof(baseStream));
            this.baseStream = baseStream;
            this.increment = increment;
        }


        public override void Flush() {
            baseStream.Flush();
        }


        public override long Seek(long offset, SeekOrigin origin) {
            return baseStream.Seek(offset, origin);
        }


        public override void SetLength(long value) {
            baseStream.SetLength(value);
        }


        public override int Read(byte[] buffer, int offset, int count) {
            int bytesToRead = Math.Min(increment, count);
            return baseStream.Read(buffer, offset, bytesToRead);
        }


        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }


        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return baseStream.CanSeek; }
        }

        public override bool CanWrite {
            get { return false; }
        }

        public override long Length {
            get { return baseStream.Length; }
        }

        public override long Position {
            get { return baseStream.Position; }
            set { baseStream.Position = value; }
        }
    }
}
