#if NETCOREAPP
using System;
using System.Buffers;
using System.IO;

namespace fNbt {
    // Write-only Stream over an IBufferWriter<byte>, so the stream-based writers can fill
    // pipes and pooled buffers directly. Writes stage into one outstanding region and are
    // handed to the buffer writer in batches: per-byte GetSpan/Advance transactions would
    // otherwise dominate small-tag output. Owners flush before handing control back to
    // whoever gave them the buffer writer.
    internal sealed class BufferWriterStream : Stream {
        // Past this size, staging just adds a copy; hand the span to the writer directly.
        const int DirectWriteCutoff = 512;

        readonly IBufferWriter<byte> output;
        Memory<byte> current;
        int position;


        public BufferWriterStream(IBufferWriter<byte> output) {
            this.output = output;
        }


        public override void Write(byte[] buffer, int offset, int count) {
            Write(buffer.AsSpan(offset, count));
        }


        public override void Write(ReadOnlySpan<byte> buffer) {
            if (buffer.Length <= current.Length - position) {
                buffer.CopyTo(current.Span.Slice(position));
                position += buffer.Length;
                return;
            }
            Flush();
            if (buffer.Length >= DirectWriteCutoff) {
                output.Write(buffer);
                return;
            }
            current = output.GetMemory(buffer.Length);
            buffer.CopyTo(current.Span);
            position = buffer.Length;
        }


        public override void WriteByte(byte value) {
            if (position >= current.Length) {
                Flush();
                current = output.GetMemory(1);
            }
            current.Span[position++] = value;
        }


        // Commits staged bytes. The region is dropped rather than kept: Advance invalidates it.
        public override void Flush() {
            if (position > 0) {
                output.Advance(position);
                current = default;
                position = 0;
            }
        }


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
