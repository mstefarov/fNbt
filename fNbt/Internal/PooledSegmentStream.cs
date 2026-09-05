#if NETCOREAPP
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace fNbt {
    // Write-only stream for output whose size cannot be measured up front, compressed saves
    // above all. Segments come from the shared pool and go back on Dispose, so growing costs
    // no garbage and only the exact result array survives.
    internal sealed class PooledSegmentStream : Stream {
        const int FirstSegmentSize = 64 * 1024;
        const int MaxSegmentSize = 1024 * 1024;

        List<byte[]>? fullSegments;
        byte[] current;
        int currentPos;
        long completedLength;


        public PooledSegmentStream() {
            current = ArrayPool<byte>.Shared.Rent(FirstSegmentSize);
        }


        public override void Write(byte[] buffer, int offset, int count) {
            Write(buffer.AsSpan(offset, count));
        }


        public override void Write(ReadOnlySpan<byte> data) {
            while (!data.IsEmpty) {
                if (currentPos == current.Length) StartNewSegment();
                int n = Math.Min(data.Length, current.Length - currentPos);
                data.Slice(0, n).CopyTo(current.AsSpan(currentPos));
                currentPos += n;
                data = data.Slice(n);
            }
        }


        public override void WriteByte(byte value) {
            if (currentPos == current.Length) StartNewSegment();
            current[currentPos++] = value;
        }


        void StartNewSegment() {
            fullSegments ??= new List<byte[]>();
            fullSegments.Add(current);
            completedLength += current.Length;
            current = ArrayPool<byte>.Shared.Rent(Math.Min(current.Length * 2, MaxSegmentSize));
            currentPos = 0;
        }


        /// <summary> Assembles the exact result. Segments stay rented until Dispose. </summary>
        /// <exception cref="NotSupportedException"> Content exceeds one array's capacity. </exception>
        public byte[] ToArray() {
            long total = completedLength + currentPos;
            if (total > int.MaxValue) {
                throw new NotSupportedException("This NBT document is too large to save to a single buffer.");
            }
            byte[] result = ArrayAllocator.ForOverwrite<byte>((int)total, total);
            int position = 0;
            if (fullSegments != null) {
                foreach (byte[] segment in fullSegments) {
                    Buffer.BlockCopy(segment, 0, result, position, segment.Length);
                    position += segment.Length;
                }
            }
            Buffer.BlockCopy(current, 0, result, position, currentPos);
            return result;
        }


        protected override void Dispose(bool disposing) {
            if (disposing && current.Length > 0) {
                ArrayPool<byte>.Shared.Return(current);
                current = Array.Empty<byte>();
                if (fullSegments != null) {
                    foreach (byte[] segment in fullSegments) {
                        ArrayPool<byte>.Shared.Return(segment);
                    }
                    fullSegments = null;
                }
            }
            base.Dispose(disposing);
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
