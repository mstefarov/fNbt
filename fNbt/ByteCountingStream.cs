using System;
using System.IO;

namespace fNbt {
    // Counts bytes read from and written to non-seekable streams.
    internal sealed class ByteCountingStream : Stream {
        readonly Stream baseStream;


        public ByteCountingStream(Stream stream) {
            NullableSupport.Assert(stream != null);
            baseStream = stream;
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
            int bytesActuallyRead = baseStream.Read(buffer, offset, count);
            BytesRead += bytesActuallyRead;
            return bytesActuallyRead;
        }


        public override void Write(byte[] buffer, int offset, int count) {
            baseStream.Write(buffer, offset, count);
            BytesWritten += count;
        }


#if NETCOREAPP
        // Without these, dotnet's span calls fall through Stream's compatibility shim,
        // which rents and copies a temporary array per call
        public override int Read(Span<byte> buffer) {
            int bytesActuallyRead = baseStream.Read(buffer);
            BytesRead += bytesActuallyRead;
            return bytesActuallyRead;
        }


        public override void Write(ReadOnlySpan<byte> buffer) {
            baseStream.Write(buffer);
            BytesWritten += buffer.Length;
        }
#endif


        // Straight to baseStream instead of base to avoid re-entering Read/Write.
        public override int ReadByte() {
            int value = baseStream.ReadByte();
            if (value >= 0) BytesRead++;
            return value;
        }


        public override void WriteByte(byte value) {
            baseStream.WriteByte(value);
            BytesWritten++;
        }


        public override bool CanRead => baseStream.CanRead;
        public override bool CanSeek => baseStream.CanSeek;
        public override bool CanWrite => baseStream.CanWrite;
        public override long Length => baseStream.Length;

        public override long Position {
            get { return baseStream.Position; }
            set { baseStream.Position = value; }
        }

        public long BytesRead { get; private set; }
        public long BytesWritten { get; private set; }
    }
}
