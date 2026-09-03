using System;
using System.IO;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace fNbt {
    // Read-only pass-through that accumulates the CRC-32 and byte count of everything read,
    // the two values a GZip trailer carries. Only the validated GZip load uses it, and it never
    // disposes the stream it wraps. On x64 with carry-less multiply the checksum folds 64 bytes
    // per step and costs nothing next to inflating; elsewhere a slicing-by-16 table serves.
    internal sealed class Crc32Stream : Stream {
        const int Slices = 16;
        static readonly uint[] Table = BuildTable();

        readonly Stream baseStream;
        uint crc = 0xFFFFFFFF;

        public long BytesRead { get; private set; }

        public uint Crc {
            get { return ~crc; }
        }


        public Crc32Stream(Stream stream) {
            NullableSupport.Assert(stream != null);
            baseStream = stream;
        }


        static uint[] BuildTable() {
            uint[] table = new uint[Slices * 256];
            for (uint i = 0; i < 256; i++) {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) {
                    value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
                }
                table[i] = value;
            }
            for (int slice = 1; slice < Slices; slice++) {
                for (int i = 0; i < 256; i++) {
                    uint previous = table[(slice - 1) * 256 + i];
                    table[slice * 256 + i] = table[previous & 0xFF] ^ (previous >> 8);
                }
            }
            return table;
        }


        // Pointers keep the bounds checks out of the loops. The 64-bit loads assume a
        // little-endian host, which every .NET platform is. Full optimization up front matters
        // here: a process that loads one file never reaches tier 1, and the tier-0 fold ran
        // six times slower.
#if NETCOREAPP
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        unsafe void Update(byte[] data, int offset, int count) {
            uint c = crc;
            fixed (byte* start = data)
            fixed (uint* table = Table) {
                byte* p = start + offset;
                byte* end = p + count;
#if NET8_0_OR_GREATER
                if (Pclmulqdq.IsSupported && Sse41.IsSupported && count >= 64) {
                    c = Fold(c, p, count, out int folded);
                    p += folded;
                }
#endif
                while (end - p >= 16) {
                    ulong one = *(ulong*)p ^ c;
                    ulong two = *(ulong*)(p + 8);
                    c = table[15 * 256 + (byte)one] ^ table[14 * 256 + (byte)(one >> 8)] ^
                        table[13 * 256 + (byte)(one >> 16)] ^ table[12 * 256 + (byte)(one >> 24)] ^
                        table[11 * 256 + (byte)(one >> 32)] ^ table[10 * 256 + (byte)(one >> 40)] ^
                        table[9 * 256 + (byte)(one >> 48)] ^ table[8 * 256 + (byte)(one >> 56)] ^
                        table[7 * 256 + (byte)two] ^ table[6 * 256 + (byte)(two >> 8)] ^
                        table[5 * 256 + (byte)(two >> 16)] ^ table[4 * 256 + (byte)(two >> 24)] ^
                        table[3 * 256 + (byte)(two >> 32)] ^ table[2 * 256 + (byte)(two >> 40)] ^
                        table[1 * 256 + (byte)(two >> 48)] ^ table[(byte)(two >> 56)];
                    p += 16;
                }
                while (p < end) {
                    c = table[(c ^ *p++) & 0xFF] ^ (c >> 8);
                }
            }
            crc = c;
        }


#if NET8_0_OR_GREATER
        // The folding method is Intel's "Fast CRC Computation for Generic Polynomials Using
        // PCLMULQDQ Instruction" (Gopal, Ozturk, Guilford, Wolrich, Feghali, Dixon, 2009,
        // https://www.intel.com/content/dam/www/public/us/en/documents/white-papers/fast-crc-computation-generic-polynomials-pclmulqdq-paper.pdf).
        // The constants for this reflected polynomial and the reduction sequence follow
        // Alexander Boyko's implementation of it in the Linux kernel, arch/x86/crypto/crc32-pclmul_asm.S
        // (https://github.com/torvalds/linux/blob/v6.6/arch/x86/crypto/crc32-pclmul_asm.S):
        // R1/R2 fold four 128-bit lanes 512 bits back, R3/R4 fold one lane 128 bits, R5 folds
        // 64 to 32 bits, then a Barrett reduction with u and P.
        static readonly Vector128<ulong> R2R1 = Vector128.Create(0x0000000154442bd4UL, 0x00000001c6e41596UL);
        static readonly Vector128<ulong> R4R3 = Vector128.Create(0x00000001751997d0UL, 0x00000000ccaa009eUL);
        static readonly Vector128<ulong> R5 = Vector128.Create(0x0000000163cd6124UL, 0UL);
        static readonly Vector128<ulong> RUpoly = Vector128.Create(0x00000001F7011641UL, 0x00000001DB710641UL);
        static readonly Vector128<ulong> Mask32 = Vector128.Create(0x00000000FFFFFFFFUL, 0UL);


        // Folds whole 16-byte blocks, at least 64 bytes, into the running register and reports
        // how many bytes it took; the caller finishes the tail with the table.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        static unsafe uint Fold(uint crc, byte* p, int count, out int folded) {
            Vector128<ulong> x1 = Sse2.LoadVector128((ulong*)p);
            Vector128<ulong> x2 = Sse2.LoadVector128((ulong*)(p + 16));
            Vector128<ulong> x3 = Sse2.LoadVector128((ulong*)(p + 32));
            Vector128<ulong> x4 = Sse2.LoadVector128((ulong*)(p + 48));
            x1 = Sse2.Xor(x1, Vector128.CreateScalar((ulong)crc));
            folded = 64;
            while (count - folded >= 64) {
                x1 = FoldStep(x1, R2R1, Sse2.LoadVector128((ulong*)(p + folded)));
                x2 = FoldStep(x2, R2R1, Sse2.LoadVector128((ulong*)(p + folded + 16)));
                x3 = FoldStep(x3, R2R1, Sse2.LoadVector128((ulong*)(p + folded + 32)));
                x4 = FoldStep(x4, R2R1, Sse2.LoadVector128((ulong*)(p + folded + 48)));
                folded += 64;
            }
            x1 = FoldStep(x1, R4R3, x2);
            x1 = FoldStep(x1, R4R3, x3);
            x1 = FoldStep(x1, R4R3, x4);
            while (count - folded >= 16) {
                x1 = FoldStep(x1, R4R3, Sse2.LoadVector128((ulong*)(p + folded)));
                folded += 16;
            }

            // 128 to 64 bits, 64 to 32 bits, then Barrett
            x1 = Sse2.Xor(Pclmulqdq.CarrylessMultiply(x1, R4R3, 0x10), Sse2.ShiftRightLogical128BitLane(x1, 8));
            Vector128<ulong> low = Sse2.And(x1, Mask32);
            x1 = Sse2.Xor(Sse2.ShiftRightLogical128BitLane(x1, 4), Pclmulqdq.CarrylessMultiply(low, R5, 0x00));
            Vector128<ulong> u = Pclmulqdq.CarrylessMultiply(Sse2.And(x1, Mask32), RUpoly, 0x00);
            Vector128<ulong> reduced = Pclmulqdq.CarrylessMultiply(Sse2.And(u, Mask32), RUpoly, 0x10);
            return Sse41.Extract(Sse2.Xor(reduced, x1).AsUInt32(), 1);
        }


        static Vector128<ulong> FoldStep(Vector128<ulong> x, Vector128<ulong> k, Vector128<ulong> data) {
            Vector128<ulong> low = Pclmulqdq.CarrylessMultiply(x, k, 0x00);
            Vector128<ulong> high = Pclmulqdq.CarrylessMultiply(x, k, 0x11);
            return Sse2.Xor(Sse2.Xor(low, high), data);
        }
#endif


        public override int Read(byte[] buffer, int offset, int count) {
            int bytesRead = baseStream.Read(buffer, offset, count);
            if (bytesRead > 0) {
                Update(buffer, offset, bytesRead);
                BytesRead += bytesRead;
            }
            return bytesRead;
        }


        public override int ReadByte() {
            int value = baseStream.ReadByte();
            if (value >= 0) {
                crc = Table[(crc ^ (uint)value) & 0xFF] ^ (crc >> 8);
                BytesRead++;
            }
            return value;
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
