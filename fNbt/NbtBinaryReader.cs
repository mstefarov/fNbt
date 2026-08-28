using System;
using System.Diagnostics;
using System.IO;

namespace fNbt {
    /// <summary> BinaryReader wrapper that takes care of reading primitives from an NBT stream,
    /// while taking care of endianness, string encoding, and skipping. </summary>
    internal sealed class NbtBinaryReader : BinaryReader {
        readonly byte[] buffer = new byte[sizeof(double)];

        byte[]? seekBuffer;
        const int SeekBufferSize = 8 * 1024;
        readonly bool swapNeeded;
        readonly bool useVarInt;
        readonly byte[] stringConversionBuffer = new byte[64];
        int depth;

        // Opt-in limits, set once by NbtCodec before parsing. Defaults keep every check a
        // single always-false comparison. maxStringBytes folds MaxAllocation in and guards
        // reads, which allocate; skips allocate nothing, so they enforce only the flavor's
        // own ceiling via flavorMaxStringBytes.
        long maxAllocation = long.MaxValue;
        int maxStringBytes = int.MaxValue;
        int flavorMaxStringBytes = int.MaxValue;
        NbtTagType maxTagType = NbtTagType.LongArray;
        string? tagTypeLimitSource;


        internal void SetLimits(long newMaxAllocation, NbtFlavor? readValidationFlavor) {
            maxAllocation = newMaxAllocation;
            maxStringBytes = (int)Math.Min(int.MaxValue, newMaxAllocation);
            if (readValidationFlavor != null) {
                maxTagType = readValidationFlavor.MaxTagType;
                flavorMaxStringBytes = readValidationFlavor.MaxStringBytes;
                maxStringBytes = Math.Min(maxStringBytes, flavorMaxStringBytes);
                tagTypeLimitSource = readValidationFlavor.Name;
            }
        }


        // Opt-in cap on any single allocation driven by a length declared in the input
        public void EnsureAllocation(long byteCount) {
            if (byteCount > maxAllocation) {
                throw new NbtFormatException(
                    "Declared data size (" + byteCount + " bytes) exceeds the MaxAllocation limit (" +
                    maxAllocation + " bytes).");
            }
        }


        // Parsing nested tags is recursive. Check for ridiculously nested tags before the stack runs out.
        public void IncreaseDepth() {
            if (depth >= NbtTag.MaxDepth) {
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
            depth++;
        }


        public void DecreaseDepth() {
            depth--;
        }


        public NbtBinaryReader(Stream input, bool bigEndian, bool useVarInt)
            : base(input) {
            swapNeeded = (BitConverter.IsLittleEndian == bigEndian);
            this.useVarInt = useVarInt;
        }


        public bool UsesVarInt {
            get { return useVarInt; }
        }


        public NbtTagType ReadTagType() {
            return RequireValidTagType(ReadByte()); // ReadByte throws at end of stream
        }


        public NbtTagType RequireValidTagType(byte type) {
            // maxTagType is LongArray unless read validation lowered it, so the common case
            // stays a single comparison
            if (type > (byte)maxTagType) {
                if (type > (byte)NbtTagType.LongArray) {
                    throw new NbtFormatException("NBT tag type out of range: " + type);
                }
                throw new NbtFormatException(
                    NbtTag.GetCanonicalTagName((NbtTagType)type) + " is not permitted by the " +
                    tagTypeLimitSource + " flavor.");
            }
            return (NbtTagType)type;
        }


        public override short ReadInt16() {
            if (swapNeeded) {
                return Swap(base.ReadInt16());
            } else {
                return base.ReadInt16();
            }
        }


        public override int ReadInt32() {
            if (useVarInt) {
                uint raw = ReadUnsignedVarInt32();
                return (int)(raw >> 1) ^ -(int)(raw & 1);
            }
            if (swapNeeded) {
                return Swap(base.ReadInt32());
            } else {
                return base.ReadInt32();
            }
        }


        public override long ReadInt64() {
            if (useVarInt) {
                ulong raw = ReadUnsignedVarInt64();
                return (long)(raw >> 1) ^ -(long)(raw & 1);
            }
            if (swapNeeded) {
                return Swap(base.ReadInt64());
            } else {
                return base.ReadInt64();
            }
        }


        // BedrockNetwork varints, 7 bits per byte, least-significant group first.
        // TAG_Int/TAG_Long values and container lengths are zigzag-encoded on top of
        // these; string lengths use the plain unsigned form.
        uint ReadUnsignedVarInt32() {
            uint result = 0;
            int shift = 0;
            while (true) {
                byte b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 35) throw new NbtFormatException("VarInt32 is too long.");
            }
        }


        ulong ReadUnsignedVarInt64() {
            ulong result = 0;
            int shift = 0;
            while (true) {
                byte b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 70) throw new NbtFormatException("VarInt64 is too long.");
            }
        }


        public override float ReadSingle() {
            if (swapNeeded) {
                FillBuffer(sizeof(float));
                Array.Reverse(buffer, 0, sizeof(float));
                return BitConverter.ToSingle(buffer, 0);
            } else {
                return base.ReadSingle();
            }
        }


        public override double ReadDouble() {
            if (swapNeeded) {
                FillBuffer(sizeof(double));
                Array.Reverse(buffer);
                return BitConverter.ToDouble(buffer, 0);
            }
            return base.ReadDouble();
        }


        // The prefix is an unsigned 16-bit byte count in Java (valid up to 65,535 bytes), and
        // an unsigned varint in BedrockNetwork. Comparing as uint also rejects varint lengths
        // past int.MaxValue with a format error instead of an overflow.
        int ReadStringLength(int limit) {
            uint length = useVarInt ? ReadUnsignedVarInt32() : (ushort)ReadInt16();
            if (length > (uint)limit) {
                throw new NbtFormatException(
                    "Declared string length (" + length + " bytes) exceeds the configured limit (" +
                    limit + " bytes).");
            }
            return (int)length;
        }


        public override string ReadString() {
            int length = ReadStringLength(maxStringBytes);
            if (length < stringConversionBuffer.Length) {
                int stringBytesRead = 0;
                while (stringBytesRead < length) {
                    int bytesToRead = length - stringBytesRead;
                    int bytesReadThisTime = BaseStream.Read(stringConversionBuffer, stringBytesRead, bytesToRead);
                    if (bytesReadThisTime == 0) {
                        throw new EndOfStreamException();
                    }
                    stringBytesRead += bytesReadThisTime;
                }
                return NbtStringCodec.Decode(stringConversionBuffer, 0, length);
            } else {
                // Varint prefixes can declare huge lengths, so check plausibility before
                // allocating. Small strings skip the check: they read at most 64 bytes.
                EnsureCanRead(length);
                byte[] stringData = ReadBytes(length);
                if (stringData.Length < length) {
                    throw new EndOfStreamException();
                }
                return NbtStringCodec.Decode(stringData, 0, length);
            }
        }


        // Reads a list's element-type byte and length with the shared wire tolerances:
        // negative lengths count as empty, and an empty list accepts any type byte.
        public NbtTagType ReadListHeader(out int length) {
            byte rawListType = ReadByte();
            length = ReadInt32();
            if (length <= 0) {
                length = 0;
                return rawListType <= (byte)NbtTagType.LongArray
                    ? (NbtTagType)rawListType
                    : NbtTagType.End;
            }
            NbtTagType listType = RequireValidTagType(rawListType);
            if (listType == NbtTagType.End) {
                throw new NbtFormatException("A non-empty list may not have TAG_End as its element type.");
            }
            return listType;
        }


        void Skip(long bytesToSkip) {
            if (bytesToSkip < 0) {
                throw new ArgumentOutOfRangeException(nameof(bytesToSkip));
            } else if (BaseStream.CanSeek) {
                // Setting Position past the end succeeds silently, so a corrupt length would
                // cause problems or corruption at some later read. Check up front so it fails here.
                long remaining = BaseStream.Length - BaseStream.Position;
                if (bytesToSkip > remaining) {
                    throw new EndOfStreamException();
                }
                BaseStream.Position += bytesToSkip;
            } else if (bytesToSkip != 0) {
                if (seekBuffer == null) seekBuffer = new byte[SeekBufferSize];
                long bytesSkipped = 0;
                while (bytesSkipped < bytesToSkip) {
                    int bytesToRead = (int)Math.Min(SeekBufferSize, bytesToSkip - bytesSkipped);
                    int bytesReadThisTime = BaseStream.Read(seekBuffer, 0, bytesToRead);
                    if (bytesReadThisTime == 0) {
                        throw new EndOfStreamException();
                    }
                    bytesSkipped += bytesReadThisTime;
                }
            }
        }


        // Converts element count to a byte count here, taking care not to overflow.
        public unsafe void Skip<T>(int elementCount) where T : unmanaged {
            if (useVarInt && (typeof(T) == typeof(int) || typeof(T) == typeof(long))) {
                // Varint elements have no fixed width, so they are skipped one at a time,
                // enforcing the same width limits as the read path
                SkipVarInts(elementCount, typeof(T) == typeof(int) ? 5 : 10);
                return;
            }
            Skip((long)elementCount * sizeof(T));
        }


        void SkipVarInts(int elementCount, int maxBytesEach) {
            for (int i = 0; i < elementCount; i++) {
                int bytesRead = 0;
                while ((ReadByte() & 0x80) != 0) {
                    if (++bytesRead >= maxBytesEach) {
                        throw new NbtFormatException(
                            maxBytesEach == 5 ? "VarInt32 is too long." : "VarInt64 is too long.");
                    }
                }
            }
        }


        new void FillBuffer(int numBytes) {
            int offset = 0;
            do {
                int num = BaseStream.Read(buffer, offset, numBytes - offset);
                if (num == 0) throw new EndOfStreamException();
                offset += num;
            } while (offset < numBytes);
        }


        public void SkipString() {
            Skip(ReadStringLength(flavorMaxStringBytes));
        }


        // Rejects impossible array/list lengths that can't fit in the remaining stream,
        // to prevent massive allocations. Only seekable streams can be efficiently checked.
        public void EnsureCanRead(long byteCount) {
            if (BaseStream.CanSeek && byteCount > BaseStream.Length - BaseStream.Position) {
                throw new EndOfStreamException();
            }
        }


        public byte[] ReadArray(int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Array.Empty<byte>();
            EnsureAllocation(length);
            EnsureCanRead(length);
            byte[] result = ReadBytes(length);
            if (result.Length < length) throw new EndOfStreamException();
            return result;
        }


        public int[] ReadInt32Array(int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Array.Empty<int>();
            EnsureAllocation((long)length * sizeof(int));
            // Varint elements are at least one byte each; fixed-width math would over-estimate
            EnsureCanRead(useVarInt ? length : (long)length * sizeof(int));
            int[] result = new int[length];
            for (int i = 0; i < length; i++) result[i] = ReadInt32();
            return result;
        }


        public long[] ReadInt64Array(int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Array.Empty<long>();
            EnsureAllocation((long)length * sizeof(long));
            EnsureCanRead(useVarInt ? length : (long)length * sizeof(long));
            long[] result = new long[length];
            for (int i = 0; i < length; i++) result[i] = ReadInt64();
            return result;
        }


        [DebuggerStepThrough]
        static short Swap(short v) {
            unchecked {
                return (short)((v >> 8) & 0x00FF |
                               (v << 8) & 0xFF00);
            }
        }


        [DebuggerStepThrough]
        static int Swap(int v) {
            unchecked {
                var v2 = (uint)v;
                return (int)((v2 >> 24) & 0x000000FF |
                             (v2 >> 8) & 0x0000FF00 |
                             (v2 << 8) & 0x00FF0000 |
                             (v2 << 24) & 0xFF000000);
            }
        }


        [DebuggerStepThrough]
        static long Swap(long v) {
            unchecked {
                return (Swap((int)v) & uint.MaxValue) << 32 |
                       Swap((int)(v >> 32)) & uint.MaxValue;
            }
        }


        public TagSelector? Selector { get; set; }
    }
}
