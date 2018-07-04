using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using JetBrains.Annotations;

namespace fNbt {
    /// <summary> BinaryReader wrapper that takes care of reading primitives from an NBT stream,
    /// while taking care of endianness, string encoding, and skipping. </summary>
    public sealed class NbtBinaryReader : BinaryReader {
        readonly byte[] buffer = new byte[sizeof(double)];

        byte[] seekBuffer;
        const int SeekBufferSize = 8*1024;
        readonly bool swapNeeded;
        readonly byte[] stringConversionBuffer = new byte[64];

		public bool UseVarInt { get; set; }

		public NbtBinaryReader([NotNull] Stream input, bool bigEndian)
            : base(input) {
            swapNeeded = (BitConverter.IsLittleEndian == bigEndian);
        }


        public NbtTagType ReadTagType() {
            int type = ReadByte();
            if (type < 0) {
                throw new EndOfStreamException();
            } else if (type > (int)NbtTagType.IntArray) {
                throw new NbtFormatException("NBT tag type out of range: " + type);
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
            if (UseVarInt) {
                return ReadVarInt();
            } else {
                if (swapNeeded) {
                    return Swap(base.ReadInt32());
                } else {
                    return base.ReadInt32();
                }
            }
        }

		public int ReadVarInt()
		{
			return VarInt.ReadSInt32(BaseStream);
		}


	    public override long ReadInt64() {
		    if (UseVarInt) {
			    return ReadVarLong();
		    } else {
			    if (swapNeeded) {
				    return Swap(base.ReadInt64());
			    } else {
				    return base.ReadInt64();
			    }
		    }
	    }

	    public long ReadVarLong()
	    {
		    return VarInt.ReadSInt64(BaseStream);
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

	    public int ReadLenght() {
		    return (int)VarInt.ReadUInt32(BaseStream);
	    }

		public override string ReadString() {
            int length;
	        if (UseVarInt) {
				length = ReadLenght();
			}
			else {
				length = ReadInt16();
			}
			if (length < 0) {
                throw new NbtFormatException("Negative string length given!");
            }
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
                return Encoding.UTF8.GetString(stringConversionBuffer, 0, length);
            } else {
                byte[] stringData = ReadBytes(length);
                if (stringData.Length < length) {
                    throw new EndOfStreamException();
                }
                return Encoding.UTF8.GetString(stringData);
            }
        }


        public void Skip(int bytesToSkip) {
            if (bytesToSkip < 0) {
                throw new ArgumentOutOfRangeException("bytesToSkip");
            } else if (BaseStream.CanSeek) {
                BaseStream.Position += bytesToSkip;
            } else if (bytesToSkip != 0) {
                if (seekBuffer == null) seekBuffer = new byte[SeekBufferSize];
                int bytesSkipped = 0;
                while (bytesSkipped < bytesToSkip) {
                    int bytesToRead = Math.Min(SeekBufferSize, bytesToSkip - bytesSkipped);
                    int bytesReadThisTime = BaseStream.Read(seekBuffer, 0, bytesToRead);
                    if (bytesReadThisTime == 0) {
                        throw new EndOfStreamException();
                    }
                    bytesSkipped += bytesReadThisTime;
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
			short length;
			if (UseVarInt)
			{
				length = ReadByte();
			}
			else
			{
				length = ReadInt16();
			}
			if (length < 0) {
                throw new NbtFormatException("Negative string length given!");
            }
            Skip(length);
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


        [CanBeNull]
        public TagSelector Selector { get; set; }

    }

	internal static class VarInt
	{
		private static uint EncodeZigZag32(int n)
		{
			// Note:  the right-shift must be arithmetic
			return (uint)((n << 1) ^ (n >> 31));
		}

		private static int DecodeZigZag32(uint n)
		{
			return (int)(n >> 1) ^ -(int)(n & 1);
		}

		private static ulong EncodeZigZag64(long n)
		{
			return (ulong)((n << 1) ^ (n >> 63));
		}

		private static long DecodeZigZag64(ulong n)
		{
			return (long)(n >> 1) ^ -(long)(n & 1);
		}

		private static uint ReadRawVarInt32(Stream buf, int maxSize)
		{
			uint result = 0;
			int j = 0;
			int b0;

			do
			{
				b0 = buf.ReadByte(); // -1 if EOS
				if (b0 < 0) throw new EndOfStreamException("Not enough bytes for VarInt");

				result |= (uint)(b0 & 0x7f) << j++ * 7;

				if (j > maxSize)
				{
					throw new OverflowException("VarInt too big");
				}
			} while ((b0 & 0x80) == 0x80);

			return result;
		}

		private static ulong ReadRawVarInt64(Stream buf, int maxSize)
		{
			ulong result = 0;
			int j = 0;
			int b0;

			do
			{
				b0 = buf.ReadByte(); // -1 if EOS
				if (b0 < 0) throw new EndOfStreamException("Not enough bytes for VarInt");

				result |= (ulong)(b0 & 0x7f) << j++ * 7;

				if (j > maxSize)
				{
					throw new OverflowException("VarInt too big");
				}
			} while ((b0 & 0x80) == 0x80);

			return result;
		}

		private static void WriteRawVarInt32(Stream buf, uint value)
		{
			while ((value & -128) != 0)
			{
				buf.WriteByte((byte)((value & 0x7F) | 0x80));
				value >>= 7;
			}

			buf.WriteByte((byte)value);
		}

		private static void WriteRawVarInt64(Stream buf, ulong value)
		{
			while ((value & 0xFFFFFFFFFFFFFF80) != 0)
			{
				buf.WriteByte((byte)((value & 0x7F) | 0x80));
				value >>= 7;
			}

			buf.WriteByte((byte)value);
		}

		// Int

		public static void WriteInt32(Stream stream, int value)
		{
			WriteRawVarInt32(stream, (uint)value);
		}

		public static int ReadInt32(Stream stream)
		{
			return (int)ReadRawVarInt32(stream, 5);
		}

		public static void WriteSInt32(Stream stream, int value)
		{
			WriteRawVarInt32(stream, EncodeZigZag32(value));
		}

		public static int ReadSInt32(Stream stream)
		{
			return DecodeZigZag32(ReadRawVarInt32(stream, 5));
		}

		public static void WriteUInt32(Stream stream, uint value)
		{
			WriteRawVarInt32(stream, value);
		}

		public static uint ReadUInt32(Stream stream)
		{
			return ReadRawVarInt32(stream, 5);
		}

		// Long

		public static void WriteInt64(Stream stream, long value)
		{
			WriteRawVarInt64(stream, (ulong)value);
		}

		public static long ReadInt64(Stream stream)
		{
			return (long)ReadRawVarInt64(stream, 10);
		}

		public static void WriteSInt64(Stream stream, long value)
		{
			WriteRawVarInt64(stream, EncodeZigZag64(value));
		}

		public static long ReadSInt64(Stream stream)
		{
			return DecodeZigZag64(ReadRawVarInt64(stream, 10));
		}

		public static void WriteUInt64(Stream stream, ulong value)
		{
			WriteRawVarInt64(stream, value);
		}

		public static ulong ReadUInt64(Stream stream)
		{
			return ReadRawVarInt64(stream, 10);
		}
	}

}
