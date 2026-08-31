using System;
using System.Diagnostics;
using System.IO;

namespace fNbt {
    /// <summary> Standalone reader for NBT primitives from a stream, taking care of endianness,
    /// string encoding, and skipping. Multi-byte values compose directly in wire order. </summary>
    internal sealed unsafe class NbtBinaryReader {
        readonly Stream stream;
        readonly byte[] buffer = new byte[sizeof(double)];

        byte[]? seekBuffer;
        const int SeekBufferSize = 8 * 1024;
        // Byte-swapped bulk reads work this many bytes at a time, so the reversal pass runs
        // over data still in cache. 64 KiB divides evenly by every element size.
        const int ReverseChunkBytes = 64 * 1024;
        readonly bool bigEndian;
        readonly bool useVarInt;
        readonly byte[] stringConversionBuffer = new byte[64];

        // Bounded name cache backing ReadTagName. Capped so that a huge all-unique document
        // cannot grow an unbounded side table, and activated late so a reader that parses
        // one small document never pays for a table at all.
        NameCacheEntry[]? nameCache;
        int nameCacheCount;
        int namesRead;
        int nameLookups;
        int nameHits;
        bool nameCacheDisabled;
        const int NameCacheActivation = 64;
        const int NameCacheInitialSlots = 128;
        const int NameCacheMaxSlots = 2048;
        const int NameCacheMaxProbe = 8;

        struct NameCacheEntry {
            public uint Hash;
            public byte[]? Bytes;
            public string Value;
        }

        // Opt-in limits, set at most once by whichever entry point owns this reader, before
        // any parsing. Defaults keep every check a single always-false comparison.
        // maxStringBytes folds MaxAllocation in and guards reads, which allocate; skipped
        // strings enforce only the flavor's own ceiling, since skips allocate nothing.
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

        public NbtBinaryReader(Stream input, bool bigEndian, bool useVarInt) {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!input.CanRead) throw new ArgumentException("Given stream must be readable.", nameof(input));
            stream = input;
            this.bigEndian = bigEndian;
            this.useVarInt = useVarInt;
        }


        public Stream BaseStream {
            get { return stream; }
        }


        public bool UsesVarInt {
            get { return useVarInt; }
        }


        public byte ReadByte() {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            return (byte)value;
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


        public short ReadInt16() {
            FillBuffer(sizeof(short));
            unchecked {
                if (bigEndian) {
                    return (short)((buffer[0] << 8) | buffer[1]);
                }
                return (short)(buffer[0] | (buffer[1] << 8));
            }
        }


        public int ReadInt32() {
            if (useVarInt) {
                uint raw = ReadUnsignedVarInt32();
                return (int)(raw >> 1) ^ -(int)(raw & 1);
            }
            FillBuffer(sizeof(int));
            unchecked {
                if (bigEndian) {
                    return (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
                }
                return buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
            }
        }


        public long ReadInt64() {
            if (useVarInt) {
                ulong raw = ReadUnsignedVarInt64();
                return (long)(raw >> 1) ^ -(long)(raw & 1);
            }
            FillBuffer(sizeof(long));
            unchecked {
                uint high, low;
                if (bigEndian) {
                    high = (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
                    low = (uint)((buffer[4] << 24) | (buffer[5] << 16) | (buffer[6] << 8) | buffer[7]);
                } else {
                    low = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
                    high = (uint)(buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24));
                }
                return (long)(((ulong)high << 32) | low);
            }
        }


        // BedrockNetwork varints, 7 bits per byte, least-significant group first.
        // TAG_Int/TAG_Long values and container lengths are zigzag-encoded on top of
        // these; string lengths use the plain unsigned form. The last byte a width allows
        // has room only for the leftover bits (4 and 1); anything above them is an overflow.
        uint ReadUnsignedVarInt32() {
            uint result = 0;
            for (int shift = 0; shift < 28; shift += 7) {
                byte b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            byte last = ReadByte();
            if (last > 0x0F) throw VarIntError(32, last);
            return result | ((uint)last << 28);
        }


        ulong ReadUnsignedVarInt64() {
            ulong result = 0;
            for (int shift = 0; shift < 63; shift += 7) {
                byte b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            byte last = ReadByte();
            if (last > 0x01) throw VarIntError(64, last);
            return result | ((ulong)last << 63);
        }


        static NbtFormatException VarIntError(int bits, byte last) {
            return new NbtFormatException((last & 0x80) != 0
                ? "VarInt" + bits + " is too long."
                : "VarInt" + bits + " overflows " + bits + " bits.");
        }


        public float ReadSingle() {
            FillBuffer(sizeof(float));
            uint bits;
            unchecked {
                if (bigEndian) {
                    bits = (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
                } else {
                    bits = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
                }
            }
            return *(float*)&bits;
        }


        public double ReadDouble() {
            FillBuffer(sizeof(double));
            unchecked {
                uint high, low;
                if (bigEndian) {
                    high = (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
                    low = (uint)((buffer[4] << 24) | (buffer[5] << 16) | (buffer[6] << 8) | buffer[7]);
                } else {
                    low = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
                    high = (uint)(buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24));
                }
                return BitConverter.Int64BitsToDouble((long)(((ulong)high << 32) | low));
            }
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


        public string ReadString() {
            int length = ReadStringLength(maxStringBytes);
            if (length < stringConversionBuffer.Length) {
                FillStringConversionBuffer(length);
                return NbtStringCodec.Decode(stringConversionBuffer, 0, length);
            }
            return ReadLongString(length);
        }


        void FillStringConversionBuffer(int length) {
            int stringBytesRead = 0;
            while (stringBytesRead < length) {
                int bytesToRead = length - stringBytesRead;
                int bytesReadThisTime = BaseStream.Read(stringConversionBuffer, stringBytesRead, bytesToRead);
                if (bytesReadThisTime == 0) {
                    throw new EndOfStreamException();
                }
                stringBytesRead += bytesReadThisTime;
            }
        }


        string ReadLongString(int length) {
            // Varint prefixes can declare huge lengths, so check plausibility before
            // allocating. Small strings skip the check: they read at most 64 bytes.
            EnsureCanRead(length);
            var stringData = new byte[length];
            ReadExactly(stringData, length);
            return NbtStringCodec.Decode(stringData, 0, length);
        }


        void ReadExactly(byte[] destination, int count) {
            int totalRead = 0;
            while (totalRead < count) {
                int bytesRead = stream.Read(destination, totalRead, count - totalRead);
                if (bytesRead == 0) throw new EndOfStreamException();
                totalRead += bytesRead;
            }
        }


        /// <summary> Reads a length-prefixed string like <see cref="ReadString"/>, but returns
        /// the cached instance when the same name bytes repeat. Real documents draw tag names
        /// from a small set, so most reads allocate nothing. Entries are keyed by encoded bytes;
        /// alternate encodings of one name get separate entries that decode equal. </summary>
        public string ReadTagName() {
            int length = ReadStringLength(maxStringBytes);
            if (length == 0) return "";
            if (length >= stringConversionBuffer.Length) {
                return ReadLongString(length);
            }
            FillStringConversionBuffer(length);
            if (nameCache == null) {
                // Tiny documents never repay a table, and one that proved useless stays off
                if (nameCacheDisabled || ++namesRead < NameCacheActivation) {
                    return NbtStringCodec.Decode(stringConversionBuffer, 0, length);
                }
                nameCache = new NameCacheEntry[NameCacheInitialSlots];
            }
            return LookUpName(length);
        }


        string LookUpName(int length) {
            // A document of mostly unique names keeps missing; drop the cache so reads
            // stop paying for hashing and probes
            if ((++nameLookups & 1023) == 0 && nameHits * 4 < nameLookups) {
                nameCache = null;
                nameCacheDisabled = true;
                return NbtStringCodec.Decode(stringConversionBuffer, 0, length);
            }
            byte[] buffer = stringConversionBuffer;
            uint hash = 2166136261u;
            for (int i = 0; i < length; i++) {
                hash = (hash ^ buffer[i]) * 16777619u;
            }
            NameCacheEntry[] cache = nameCache!;
            int mask = cache.Length - 1;
            int index = (int)hash & mask;
            for (int probe = 0; probe < NameCacheMaxProbe; probe++) {
                ref NameCacheEntry entry = ref cache[index];
                byte[]? entryBytes = entry.Bytes;
                if (entryBytes == null) {
                    string value = NbtStringCodec.Decode(buffer, 0, length);
                    // Insertion stops at half load once the table cannot grow further;
                    // lookups keep working for what was already cached
                    if (nameCacheCount * 2 < cache.Length) {
                        var keyCopy = new byte[length];
                        Buffer.BlockCopy(buffer, 0, keyCopy, 0, length);
                        entry.Hash = hash;
                        entry.Bytes = keyCopy;
                        entry.Value = value;
                        if (++nameCacheCount * 2 >= cache.Length && cache.Length < NameCacheMaxSlots) {
                            GrowNameCache();
                        }
                    }
                    return value;
                }
                if (entry.Hash == hash && entryBytes.Length == length &&
                    NameBytesEqual(entryBytes, buffer, length)) {
                    nameHits++;
                    return entry.Value;
                }
                index = (index + 1) & mask;
            }
            // A probe run this long means dense collisions; don't make the cluster worse
            return NbtStringCodec.Decode(buffer, 0, length);
        }


        static bool NameBytesEqual(byte[] entryBytes, byte[] buffer, int length) {
#if NET8_0_OR_GREATER
            return entryBytes.AsSpan().SequenceEqual(buffer.AsSpan(0, length));
#else
            for (int i = 0; i < length; i++) {
                if (entryBytes[i] != buffer[i]) return false;
            }
            return true;
#endif
        }


        void GrowNameCache() {
            NameCacheEntry[] oldCache = nameCache!;
            var newCache = new NameCacheEntry[oldCache.Length * 4];
            int mask = newCache.Length - 1;
            foreach (NameCacheEntry entry in oldCache) {
                if (entry.Bytes == null) continue;
                int index = (int)entry.Hash & mask;
                while (newCache[index].Bytes != null) {
                    index = (index + 1) & mask;
                }
                newCache[index] = entry;
            }
            nameCache = newCache;
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
                // Varint elements have no fixed width, so they are decoded one at a time and
                // discarded, which applies the read path's width and overflow rules
                if (typeof(T) == typeof(int)) {
                    for (int i = 0; i < elementCount; i++) ReadUnsignedVarInt32();
                } else {
                    for (int i = 0; i < elementCount; i++) ReadUnsignedVarInt64();
                }
                return;
            }
            Skip((long)elementCount * sizeof(T));
        }


        void FillBuffer(int numBytes) {
            int offset = 0;
            do {
                int num = stream.Read(buffer, offset, numBytes - offset);
                if (num == 0) throw new EndOfStreamException();
                offset += num;
            } while (offset < numBytes);
        }


        public void SkipString() {
            Skip(ReadStringLength(flavorMaxStringBytes));
        }


        public void SkipPayload(NbtTagType type, int depthBudget) {
            int tagCount = 0, endTagCount = 0;
            SkipPayload(type, depthBudget, ref tagCount, ref endTagCount);
        }


        // Discards one tag's payload without constructing tags, counting names or lists as
        // NbtReader would: tagCount gets named children and list elements, endTagCount gets
        // TAG_End markers. Applies the same wire tolerances, flavor limits, and depth budget
        // as a parsing walk. MaxAllocation does not apply: skips allocate nothing.
        public void SkipPayload(NbtTagType type, int depthBudget, ref int tagCount, ref int endTagCount) {
            switch (type) {
                case NbtTagType.Byte:
                    ReadByte();
                    break;

                case NbtTagType.Short:
                    ReadInt16();
                    break;

                case NbtTagType.Int:
                    ReadInt32();
                    break;

                case NbtTagType.Long:
                    ReadInt64();
                    break;

                case NbtTagType.Float:
                    Skip<float>(1);
                    break;

                case NbtTagType.Double:
                    Skip<double>(1);
                    break;

                case NbtTagType.String:
                    SkipString();
                    break;

                case NbtTagType.ByteArray:
                    // Negative lengths are tolerated as empty, like the parsing walk
                    Skip<byte>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.IntArray:
                    Skip<int>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.LongArray:
                    Skip<long>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.List: {
                    int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    NbtTagType elementType = ReadListHeader(out int length);
                    SkipListElements(elementType, length, childDepthBudget, ref tagCount, ref endTagCount);
                    break;
                }

                case NbtTagType.Compound: {
                    int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    while (true) {
                        NbtTagType childType = ReadTagType();
                        if (childType == NbtTagType.End) {
                            endTagCount++;
                            break;
                        }
                        tagCount++;
                        SkipString();
                        SkipPayload(childType, childDepthBudget, ref tagCount, ref endTagCount);
                    }
                    break;
                }

                default:
                    throw new NbtFormatException("Unsupported tag type found in NBT_Compound: " + type);
            }
        }


        // Discards list elements whose header was already consumed. Fixed-width elements skip
        // in one step; varint ints/longs decode one at a time through Skip<T>.
        public void SkipListElements(NbtTagType elementType, int count, int childDepthBudget,
                                     ref int tagCount, ref int endTagCount) {
            // An empty list's element type is decorative and may even be TAG_End
            if (count <= 0) return;
            unchecked {
                tagCount += count;
            }
            switch (elementType) {
                case NbtTagType.Byte:
                    Skip<byte>(count);
                    break;

                case NbtTagType.Short:
                    Skip<short>(count);
                    break;

                case NbtTagType.Int:
                    Skip<int>(count);
                    break;

                case NbtTagType.Long:
                    Skip<long>(count);
                    break;

                case NbtTagType.Float:
                    Skip<float>(count);
                    break;

                case NbtTagType.Double:
                    Skip<double>(count);
                    break;

                case NbtTagType.String:
                    for (int i = 0; i < count; i++) SkipString();
                    break;

                case NbtTagType.ByteArray:
                    for (int i = 0; i < count; i++) Skip<byte>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.IntArray:
                    for (int i = 0; i < count; i++) Skip<int>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.LongArray:
                    for (int i = 0; i < count; i++) Skip<long>(Math.Max(0, ReadInt32()));
                    break;

                case NbtTagType.List:
                case NbtTagType.Compound:
                    for (int i = 0; i < count; i++) {
                        SkipPayload(elementType, childDepthBudget, ref tagCount, ref endTagCount);
                    }
                    break;

                default:
                    // ReadListHeader rejects TAG_End on non-empty lists, so only a corrupt
                    // caller-supplied type can land here
                    throw new NbtFormatException("Unsupported tag type found in a list: " + elementType);
            }
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
            var result = new byte[length];
            ReadExactly(result, length);
            return result;
        }


        public int[] ReadInt32Array(int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Array.Empty<int>();
            EnsureAllocation((long)length * sizeof(int));
            // Varint elements are at least one byte each; fixed-width math would over-estimate
            EnsureCanRead(useVarInt ? length : (long)length * sizeof(int));
            int[] result = new int[length];
#if NET8_0_OR_GREATER
            if (!useVarInt) {
                Span<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(result.AsSpan());
                if (bigEndian != BitConverter.IsLittleEndian) {
                    stream.ReadExactly(bytes);
                } else {
                    // Read and reverse in chunks that stay cache-hot, instead of two whole-array passes
                    while (!bytes.IsEmpty) {
                        Span<byte> chunk = bytes.Slice(0, Math.Min(ReverseChunkBytes, bytes.Length));
                        stream.ReadExactly(chunk);
                        Span<int> elements = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(chunk);
                        System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(elements, elements);
                        bytes = bytes.Slice(chunk.Length);
                    }
                }
                return result;
            }
#endif
            for (int i = 0; i < length; i++) result[i] = ReadInt32();
            return result;
        }


        public long[] ReadInt64Array(int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Array.Empty<long>();
            EnsureAllocation((long)length * sizeof(long));
            EnsureCanRead(useVarInt ? length : (long)length * sizeof(long));
            long[] result = new long[length];
#if NET8_0_OR_GREATER
            if (!useVarInt) {
                Span<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(result.AsSpan());
                if (bigEndian != BitConverter.IsLittleEndian) {
                    stream.ReadExactly(bytes);
                } else {
                    while (!bytes.IsEmpty) {
                        Span<byte> chunk = bytes.Slice(0, Math.Min(ReverseChunkBytes, bytes.Length));
                        stream.ReadExactly(chunk);
                        Span<long> elements = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, long>(chunk);
                        System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(elements, elements);
                        bytes = bytes.Slice(chunk.Length);
                    }
                }
                return result;
            }
#endif
            for (int i = 0; i < length; i++) result[i] = ReadInt64();
            return result;
        }


        public TagSelector? Selector { get; set; }
    }
}
