using System;
using System.IO;
using System.Text;

namespace fNbt {
    /// <summary> Writes NBT primitives to a stream, taking care of endianness and string
    /// encoding. Counterpart to <see cref="NbtBinaryReader"/>. </summary>
    internal sealed unsafe class NbtBinaryWriter {
        // Write at most 4 MiB at a time.
        public const int MaxWriteChunk = 4 * 1024 * 1024;

        // Encoding can be shared among all instances of NbtBinaryWriter, because it is stateless.
        static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);

        // Each instance needs its own encoder, because it maintains state. Only the chunked
        // long-string path uses it, so it waits until first need. Most writers never see a
        // string long enough to get there.
        Encoder? encoder;

        public Stream BaseStream {
            get {
                stream.Flush();
                return stream;
            }
        }

        readonly Stream stream;

        // Buffer used for temporary conversion
        const int BufferSize = 256;

        // UTF8 characters use at most 4 bytes each.
        const int MaxBufferedStringLength = BufferSize / 4;

        // Each NbtBinaryWriter needs to have its own instance of the buffer.
        readonly byte[] buffer = new byte[BufferSize];

        // Swap is only needed if endianness of the runtime differs from desired NBT stream
        readonly bool swapNeeded;

        readonly bool useVarInt;

        // Java flavors use modified UTF-8; clean BMP strings still take the standard path
        readonly bool modifiedUtf8;

        // The non-varint length prefix is an unsigned 16-bit byte count; validation may lower
        // the ceiling to the flavor's. Varint prefixes ignore this and cap at int.MaxValue.
        readonly int maxStringBytes;


        public NbtBinaryWriter(Stream output, NbtFlavor flavor, bool validate = false) {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (!output.CanWrite) throw new ArgumentException("Given stream must be writable.", nameof(output));
            stream = output;
            swapNeeded = (BitConverter.IsLittleEndian == flavor.BigEndian);
            useVarInt = flavor.UsesVarInts;
            modifiedUtf8 = flavor.UsesModifiedUtf8;
            maxStringBytes = validate ? Math.Min(ushort.MaxValue, flavor.MaxStringBytes) : ushort.MaxValue;
        }


        // Byte count of value in this writer's encoding, and whether it takes the modified
        // UTF-8 path. Throws for anything Write would refuse, so NbtWriter can measure a
        // string before committing any bytes and hand that result to the overload below.
        public int Measure(string value, out bool modified) {
            if (value == null) throw new ArgumentNullException(nameof(value));

            long numBytes;
            if (NbtStringCodec.IsAsciiNoNul(value)) {
                numBytes = value.Length;
                modified = false;
            } else if (modifiedUtf8 && NbtStringCodec.NeedsModifiedEncoding(value)) {
                numBytes = NbtStringCodec.GetModifiedByteCount(value);
                modified = true;
            } else {
                numBytes = GetStandardByteCount(value);
                modified = false;
            }
            if (numBytes > (useVarInt ? int.MaxValue : maxStringBytes)) {
                throw StringTooLong(numBytes);
            }
            return (int)numBytes;
        }


        // Writes a string whose encoding was already checked and counted by Measure.
        public void Write(string value, int numBytes, bool modified) {
            WritePrefix(numBytes);
            if (modified) {
                WriteModifiedBody(value);
            } else {
                WriteStandardBody(value, numBytes);
            }
        }


        public void Write(byte value) {
            stream.WriteByte(value);
        }


        public void Write(NbtTagType value) {
            stream.WriteByte((byte)value);
        }


        public void Write(short value) {
            unchecked {
                if (swapNeeded) {
                    buffer[0] = (byte)(value >> 8);
                    buffer[1] = (byte)value;
                } else {
                    buffer[0] = (byte)value;
                    buffer[1] = (byte)(value >> 8);
                }
            }
            stream.Write(buffer, 0, 2);
        }


        public void Write(int value) {
            if (useVarInt) {
                // Zigzag-encoded, like the reader's ReadInt32
                WriteUnsignedVarInt32((uint)(value << 1) ^ (uint)(value >> 31));
                return;
            }
            unchecked {
                if (swapNeeded) {
                    buffer[0] = (byte)(value >> 24);
                    buffer[1] = (byte)(value >> 16);
                    buffer[2] = (byte)(value >> 8);
                    buffer[3] = (byte)value;
                } else {
                    buffer[0] = (byte)value;
                    buffer[1] = (byte)(value >> 8);
                    buffer[2] = (byte)(value >> 16);
                    buffer[3] = (byte)(value >> 24);
                }
            }
            stream.Write(buffer, 0, 4);
        }


        public void Write(long value) {
            if (useVarInt) {
                WriteUnsignedVarInt64((ulong)(value << 1) ^ (ulong)(value >> 63));
                return;
            }
            unchecked {
                if (swapNeeded) {
                    buffer[0] = (byte)(value >> 56);
                    buffer[1] = (byte)(value >> 48);
                    buffer[2] = (byte)(value >> 40);
                    buffer[3] = (byte)(value >> 32);
                    buffer[4] = (byte)(value >> 24);
                    buffer[5] = (byte)(value >> 16);
                    buffer[6] = (byte)(value >> 8);
                    buffer[7] = (byte)value;
                } else {
                    buffer[0] = (byte)value;
                    buffer[1] = (byte)(value >> 8);
                    buffer[2] = (byte)(value >> 16);
                    buffer[3] = (byte)(value >> 24);
                    buffer[4] = (byte)(value >> 32);
                    buffer[5] = (byte)(value >> 40);
                    buffer[6] = (byte)(value >> 48);
                    buffer[7] = (byte)(value >> 56);
                }
            }
            stream.Write(buffer, 0, 8);
        }


        public void Write(float value) {
            ulong tmpValue = *(uint*)&value;
            unchecked {
                if (swapNeeded) {
                    buffer[0] = (byte)(tmpValue >> 24);
                    buffer[1] = (byte)(tmpValue >> 16);
                    buffer[2] = (byte)(tmpValue >> 8);
                    buffer[3] = (byte)tmpValue;
                } else {
                    buffer[0] = (byte)tmpValue;
                    buffer[1] = (byte)(tmpValue >> 8);
                    buffer[2] = (byte)(tmpValue >> 16);
                    buffer[3] = (byte)(tmpValue >> 24);
                }
            }
            stream.Write(buffer, 0, 4);
        }


        public void Write(double value) {
            ulong tmpValue = *(ulong*)&value;
            unchecked {
                if (swapNeeded) {
                    buffer[0] = (byte)(tmpValue >> 56);
                    buffer[1] = (byte)(tmpValue >> 48);
                    buffer[2] = (byte)(tmpValue >> 40);
                    buffer[3] = (byte)(tmpValue >> 32);
                    buffer[4] = (byte)(tmpValue >> 24);
                    buffer[5] = (byte)(tmpValue >> 16);
                    buffer[6] = (byte)(tmpValue >> 8);
                    buffer[7] = (byte)tmpValue;
                } else {
                    buffer[0] = (byte)tmpValue;
                    buffer[1] = (byte)(tmpValue >> 8);
                    buffer[2] = (byte)(tmpValue >> 16);
                    buffer[3] = (byte)(tmpValue >> 24);
                    buffer[4] = (byte)(tmpValue >> 32);
                    buffer[5] = (byte)(tmpValue >> 40);
                    buffer[6] = (byte)(tmpValue >> 48);
                    buffer[7] = (byte)(tmpValue >> 56);
                }
            }
            stream.Write(buffer, 0, 8);
        }


        // Based on BinaryWriter.Write(String). This is the tag-tree hot path, so the common
        // small-string case stays inline instead of going through the measured handoff.
        public void Write(string value) {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }

            int numBytes;
            if (NbtStringCodec.IsAsciiNoNul(value)) {
                // Byte-identical in both encodings, and the length needs no separate count
                numBytes = value.Length;
            } else if (modifiedUtf8 && NbtStringCodec.NeedsModifiedEncoding(value)) {
                // NULs and surrogates encode differently in modified UTF-8; everything else
                // is byte-identical in both encodings and stays on the standard path below
                WritePrefix(NbtStringCodec.GetModifiedByteCount(value));
                WriteModifiedBody(value);
                return;
            } else {
                numBytes = GetStandardByteCount(value);
            }
            if (useVarInt) {
                // BedrockNetwork length prefix is a plain unsigned varint
                WriteUnsignedVarInt32((uint)numBytes);
            } else {
                // The length prefix is an unsigned 16-bit byte count.
                // Refuse anything past the ceiling to avoid corrupting the stream.
                if (numBytes > maxStringBytes) throw StringTooLong(numBytes);
                Write((short)numBytes);
            }

            if (numBytes <= BufferSize) {
                // If the string fits entirely in the buffer, encode and write it as one
                Encoding.GetBytes(value, 0, value.Length, buffer, 0);
                stream.Write(buffer, 0, numBytes);
            } else {
                WriteChunked(value);
            }
        }


        static int GetStandardByteCount(string value) {
            try {
                return Encoding.GetByteCount(value);
            } catch (EncoderFallbackException ex) {
                throw new NbtFormatException(
                    "String contains a lone surrogate, which cannot be encoded as standard UTF-8.", ex);
            }
        }


        void WritePrefix(long numBytes) {
            if (useVarInt) {
                if (numBytes > int.MaxValue) throw StringTooLong(numBytes);
                WriteUnsignedVarInt32((uint)numBytes);
            } else {
                if (numBytes > maxStringBytes) throw StringTooLong(numBytes);
                Write((short)numBytes);
            }
        }


        void WriteStandardBody(string value, int numBytes) {
            if (numBytes <= BufferSize) {
                Encoding.GetBytes(value, 0, value.Length, buffer, 0);
                stream.Write(buffer, 0, numBytes);
            } else {
                WriteChunked(value);
            }
        }


        void WriteChunked(string value) {
            // Aggressively try to avoid allocations in this loop. Use an Encoder to handle
            // surrogate pairs that cross buffer boundaries correctly.
            if (encoder == null) encoder = Encoding.GetEncoder();
            int charStart = 0;
            int numLeft = value.Length;
            while (numLeft > 0) {
                int charCount = (numLeft > MaxBufferedStringLength) ? MaxBufferedStringLength : numLeft;
                int byteLen;
                fixed (char* pChars = value) {
                    fixed (byte* pBytes = buffer) {
                        byteLen = encoder.GetBytes(pChars + charStart, charCount, pBytes, BufferSize,
                                                   charCount == numLeft);
                    }
                }
                stream.Write(buffer, 0, byteLen);
                charStart += charCount;
                numLeft -= charCount;
            }
        }


        void WriteModifiedBody(string value) {
            int pos = 0;
            foreach (char c in value) {
                if (pos > BufferSize - 3) {
                    stream.Write(buffer, 0, pos);
                    pos = 0;
                }
                pos = NbtStringCodec.EncodeModifiedChar(c, buffer, pos);
            }
            if (pos > 0) {
                stream.Write(buffer, 0, pos);
            }
        }


        NbtFormatException StringTooLong(long numBytes) {
            long maximum = useVarInt ? int.MaxValue : maxStringBytes;
            return new NbtFormatException(
                "String is too long to write: " + numBytes + " bytes (maximum is " + maximum + ").");
        }


        void WriteUnsignedVarInt32(uint value) {
            if (value < 0x80) {
                stream.WriteByte((byte)value);
                return;
            }
            // Encode into the scratch buffer and issue one write, instead of one
            // Stream.WriteByte per encoded byte
            int pos = 0;
            do {
                buffer[pos++] = (byte)(value | 0x80);
                value >>= 7;
            } while (value >= 0x80);
            buffer[pos++] = (byte)value;
            stream.Write(buffer, 0, pos);
        }


        void WriteUnsignedVarInt64(ulong value) {
            if (value < 0x80) {
                stream.WriteByte((byte)value);
                return;
            }
            int pos = 0;
            do {
                buffer[pos++] = (byte)(value | 0x80);
                value >>= 7;
            } while (value >= 0x80);
            buffer[pos++] = (byte)value;
            stream.Write(buffer, 0, pos);
        }


        public void Write(byte[] data, int offset, int count) {
            int written = 0;
            while (written < count) {
                int toWrite = Math.Min(MaxWriteChunk, count - written);
                stream.Write(data, offset + written, toWrite);
                written += toWrite;
            }
        }


        public void Write(int[] data, int offset, int count) {
#if NET8_0_OR_GREATER
            if (!useVarInt) {
                ReadOnlySpan<int> source = data.AsSpan(offset, count);
                if (!swapNeeded) {
                    WriteSpanChunked(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source));
                } else {
                    // Reverse chunks into the scratch buffer, leaving the caller's array untouched
                    Span<int> chunk = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(buffer);
                    while (!source.IsEmpty) {
                        int n = Math.Min(chunk.Length, source.Length);
                        System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(
                            source.Slice(0, n), chunk.Slice(0, n));
                        stream.Write(buffer, 0, n * sizeof(int));
                        source = source.Slice(n);
                    }
                }
                return;
            }
#endif
            for (int i = 0; i < count; i++) {
                Write(data[offset + i]);
            }
        }


        public void Write(long[] data, int offset, int count) {
#if NET8_0_OR_GREATER
            if (!useVarInt) {
                ReadOnlySpan<long> source = data.AsSpan(offset, count);
                if (!swapNeeded) {
                    WriteSpanChunked(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source));
                } else {
                    Span<long> chunk = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, long>(buffer);
                    while (!source.IsEmpty) {
                        int n = Math.Min(chunk.Length, source.Length);
                        System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(
                            source.Slice(0, n), chunk.Slice(0, n));
                        stream.Write(buffer, 0, n * sizeof(long));
                        source = source.Slice(n);
                    }
                }
                return;
            }
#endif
            for (int i = 0; i < count; i++) {
                Write(data[offset + i]);
            }
        }


#if NET8_0_OR_GREATER
        void WriteSpanChunked(ReadOnlySpan<byte> data) {
            while (data.Length > MaxWriteChunk) {
                stream.Write(data.Slice(0, MaxWriteChunk));
                data = data.Slice(MaxWriteChunk);
            }
            stream.Write(data);
        }
#endif
    }
}
