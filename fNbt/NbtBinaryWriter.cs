using System;
using System.IO;
using System.Text;

namespace fNbt {
    /// <summary> BinaryWriter wrapper that writes NBT primitives to a stream,
    /// while taking care of endianness and string encoding, and counting bytes written. </summary>
    internal sealed unsafe class NbtBinaryWriter {
        // Write at most 4 MiB at a time.
        public const int MaxWriteChunk = 4 * 1024 * 1024;

        // Encoding can be shared among all instances of NbtBinaryWriter, because it is stateless.
        static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);

        // Each instance has to have its own encoder, because it does maintain state.
        readonly Encoder encoder = Encoding.GetEncoder();

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

        // Lowered by NbtWriter when write validation is on for a flavor with a smaller ceiling
        int maxStringBytes = ushort.MaxValue;

        int depth;


        internal void SetMaxStringBytes(int value) {
            maxStringBytes = Math.Min(ushort.MaxValue, value);
        }


        // Writing a tag tree is recursive. Check for ridiculously nested tags before the stack runs out.
        public void IncreaseDepth() {
            if (depth >= NbtTag.MaxDepth) {
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
            depth++;
        }


        public void DecreaseDepth() {
            depth--;
        }


        public NbtBinaryWriter(Stream input, bool bigEndian)
            : this(input, bigEndian, false) { }


        public NbtBinaryWriter(Stream input, bool bigEndian, bool useVarInt) {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!input.CanWrite) throw new ArgumentException("Given stream must be writable", nameof(input));
            stream = input;
            swapNeeded = (BitConverter.IsLittleEndian == bigEndian);
            this.useVarInt = useVarInt;
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


        // Based on BinaryWriter.Write(String)
        public void Write(string value) {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }

            int numBytes = Encoding.GetByteCount(value);
            if (useVarInt) {
                // BedrockNetwork length prefix is a plain unsigned varint
                WriteUnsignedVarInt32((uint)numBytes);
            } else {
                // The length prefix is an unsigned 16-bit byte count.
                // Refuse anything past the ceiling to avoid corrupting the stream.
                if (numBytes > maxStringBytes) {
                    throw new NbtFormatException(
                        "String is too long to write: " + numBytes + " bytes (maximum is " +
                        maxStringBytes + ").");
                }
                Write((short)numBytes);
            }

            if (numBytes <= BufferSize) {
                // If the string fits entirely in the buffer, encode and write it as one
                Encoding.GetBytes(value, 0, value.Length, buffer, 0);
                stream.Write(buffer, 0, numBytes);
            } else {
                // Aggressively try to not allocate memory in this loop for runtime performance reasons.
                // Use an Encoder to write out the string correctly (handling surrogates crossing buffer
                // boundaries properly).  
                int charStart = 0;
                int numLeft = value.Length;
                while (numLeft > 0) {
                    // Figure out how many chars to process this round.
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
        }


        void WriteUnsignedVarInt32(uint value) {
            while (value >= 0x80) {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }


        void WriteUnsignedVarInt64(ulong value) {
            while (value >= 0x80) {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }


        public void Write(byte[] data, int offset, int count) {
            int written = 0;
            while (written < count) {
                int toWrite = Math.Min(MaxWriteChunk, count - written);
                stream.Write(data, offset + written, toWrite);
                written += toWrite;
            }
        }
    }
}
