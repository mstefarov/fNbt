#if NETCOREAPP
using System;
using System.Buffers;
using System.IO;

namespace fNbt {
    // Write-only Stream over an IBufferWriter<byte>, so the stream-based writers can fill
    // pipes and pooled buffers directly.
    internal sealed class BufferWriterStream : Stream {
        readonly IBufferWriter<byte> output;


        public BufferWriterStream(IBufferWriter<byte> output) {
            this.output = output;
        }


        public override void Write(byte[] buffer, int offset, int count) {
            output.Write(buffer.AsSpan(offset, count));
        }


        public override void Write(ReadOnlySpan<byte> buffer) {
            output.Write(buffer);
        }


        public override void WriteByte(byte value) {
            output.GetSpan(1)[0] = value;
            output.Advance(1);
        }


        public override void Flush() { }


        public override int Read(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }


        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }


        public override void SetLength(long value) {
            throw new NotSupportedException();
        }


        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
    }
}
#endif
