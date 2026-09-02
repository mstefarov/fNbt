using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Exercises the varint primitives directly, via InternalsVisibleTo.
    // BedrockNetworkTests covers the same encoding through the public API.
    [TestClass]
    public class VarIntTests {
        static byte[] WriteInt32(int value) {
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, NbtFlavor.BedrockNetwork).Write(value);
                return ms.ToArray();
            }
        }


        static byte[] WriteInt64(long value) {
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, NbtFlavor.BedrockNetwork).Write(value);
                return ms.ToArray();
            }
        }


        static NbtBinaryReader VarIntReader(byte[] bytes) {
            return new NbtBinaryReader(new MemoryStream(bytes), NbtFlavor.BedrockNetwork);
        }


        [TestMethod]
        public void ZigZagInt32KnownEncodings() {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, WriteInt32(0));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, WriteInt32(-1));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, WriteInt32(1));
            CollectionAssert.AreEqual(new byte[] { 0x03 }, WriteInt32(-2));
            CollectionAssert.AreEqual(new byte[] { 0x04 }, WriteInt32(2));
            CollectionAssert.AreEqual(new byte[] { 0x7E }, WriteInt32(63));
            CollectionAssert.AreEqual(new byte[] { 0x7F }, WriteInt32(-64));
            CollectionAssert.AreEqual(new byte[] { 0xFE, 0x01 }, WriteInt32(127));
            CollectionAssert.AreEqual(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x0F }, WriteInt32(int.MaxValue));
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, WriteInt32(int.MinValue));
        }


        [TestMethod]
        public void ZigZagInt64KnownEncodings() {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, WriteInt64(0L));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, WriteInt64(-1L));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, WriteInt64(1L));
            CollectionAssert.AreEqual(
                new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 },
                WriteInt64(long.MaxValue));
            CollectionAssert.AreEqual(
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 },
                WriteInt64(long.MinValue));
        }


        [TestMethod]
        public void ZigZagInt32RoundTrip() {
            int[] values = {
                0, 1, -1, 2, -2, 63, 64, -64, -65, 127, 128, -128, 300, -300,
                0x3FFF, 0x4000, -0x4000, -0x4001, 0x1FFFFF, 0xFFFFFF,
                int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1
            };
            foreach (int value in values) {
                byte[] encoded = WriteInt32(value);
                Assert.AreEqual(value, VarIntReader(encoded).ReadInt32(), "value " + value);
            }
        }


        [TestMethod]
        public void ZigZagInt64RoundTrip() {
            long[] values = {
                0L, 1L, -1L, 127L, -128L, 0x7FFFFFFFL, -0x80000000L,
                0x100000000L, -0x100000000L, long.MaxValue, long.MinValue
            };
            foreach (long value in values) {
                byte[] encoded = WriteInt64(value);
                Assert.AreEqual(value, VarIntReader(encoded).ReadInt64(), "value " + value);
            }
        }


        [TestMethod]
        public void StringLengthPrefixIsUnsignedVarInt() {
            // Short string: single-byte length prefix
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, NbtFlavor.BedrockNetwork).Write("hi");
                CollectionAssert.AreEqual(new byte[] { 0x02, (byte)'h', (byte)'i' }, ms.ToArray());
            }

            // 200 bytes forces a two-byte length prefix (0xC8 0x01), NOT zigzag (which would be 0x90 0x03)
            string longString = new string('a', 200);
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, NbtFlavor.BedrockNetwork).Write(longString);
                byte[] doc = ms.ToArray();
                Assert.AreEqual(0xC8, doc[0]);
                Assert.AreEqual(0x01, doc[1]);
                Assert.AreEqual(202, doc.Length);

                Assert.AreEqual(longString, VarIntReader(doc).ReadString());

                // SkipString must consume exactly the same bytes
                NbtBinaryReader skipper = VarIntReader(doc);
                skipper.SkipString();
                Assert.AreEqual(doc.Length, skipper.BaseStream.Position);
            }
        }


        [TestMethod]
        public void OverlongVarIntThrows() {
            byte[] junk32 = Enumerable.Repeat((byte)0x80, 6).ToArray();
            Assert.Throws<NbtFormatException>(() => VarIntReader(junk32).ReadInt32());

            byte[] junk64 = Enumerable.Repeat((byte)0x80, 11).ToArray();
            Assert.Throws<NbtFormatException>(() => VarIntReader(junk64).ReadInt64());
        }


        [TestMethod]
        public void OverflowingVarIntThrows() {
            // The fifth byte of a VarInt32 has room for 4 bits, and the tenth byte of a
            // VarInt64 for 1. Bits above those are an overflow, not something to shift away.
            Assert.Throws<NbtFormatException>(
                () => VarIntReader(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 }).ReadInt32());
            Assert.Throws<NbtFormatException>(
                () => VarIntReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }).ReadInt32());
            byte[] wide = Enumerable.Repeat((byte)0x80, 9).Concat(new byte[] { 0x02 }).ToArray();
            Assert.Throws<NbtFormatException>(() => VarIntReader(wide).ReadInt64());

            // String length prefixes go through the same decoder
            Assert.Throws<NbtFormatException>(
                () => VarIntReader(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 }).ReadString());

            // The bits that do fit still count
            Assert.AreEqual(int.MinValue, VarIntReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }).ReadInt32());
            byte[] max64 = Enumerable.Repeat((byte)0xFF, 9).Concat(new byte[] { 0x01 }).ToArray();
            Assert.AreEqual(long.MinValue, VarIntReader(max64).ReadInt64());

            // Skipping decodes the same way, so it rejects the same input
            Assert.Throws<NbtFormatException>(
                () => VarIntReader(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 }).Skip<int>(1));
            Assert.Throws<NbtFormatException>(() => VarIntReader(wide).Skip<long>(1));
        }


        [TestMethod]
        public void TruncatedVarIntThrows() {
            // Continuation bit set, then the stream ends
            Assert.Throws<EndOfStreamException>(() => VarIntReader(new byte[] { 0x80 }).ReadInt32());
            Assert.Throws<EndOfStreamException>(() => VarIntReader(new byte[] { 0xFF, 0xFF }).ReadInt64());
        }


        [TestMethod]
        public void VarIntElementsSkipElementWise() {
            // Byte math cannot skip variable-width elements, so ints and longs are
            // skipped one varint at a time
            byte[] doc = { 0x00, 0xD8, 0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x2A };
            NbtBinaryReader reader = VarIntReader(doc);
            reader.Skip<int>(3);
            Assert.AreEqual(42, reader.ReadByte());

            reader = VarIntReader(doc);
            reader.Skip<long>(3);
            Assert.AreEqual(42, reader.ReadByte());

            // Fixed-width element types keep plain byte math
            reader = VarIntReader(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x2A });
            reader.Skip<byte>(4);
            Assert.AreEqual(42, reader.ReadByte());
        }


        [TestMethod]
        public void SkippingOverlongVarIntThrows() {
            // The skip path enforces the same width limits as the read path
            byte[] junk32 = Enumerable.Repeat((byte)0x80, 6).ToArray();
            Assert.Throws<NbtFormatException>(() => VarIntReader(junk32).Skip<int>(1));

            byte[] junk64 = Enumerable.Repeat((byte)0x80, 11).ToArray();
            Assert.Throws<NbtFormatException>(() => VarIntReader(junk64).Skip<long>(1));

            // Ten continuation bytes are still a valid varint64 in progress; nine are fine
            byte[] max64 = Enumerable.Repeat((byte)0x80, 9).Concat(new byte[] { 0x01 }).ToArray();
            NbtBinaryReader reader = VarIntReader(max64);
            reader.Skip<long>(1);
            Assert.AreEqual(max64.Length, reader.BaseStream.Position);
        }


        [TestMethod]
        public void FixedWidthTypesAreUnaffectedByVarIntMode() {
            // Shorts, floats, and doubles stay fixed-width little-endian in the varint encoding
            using (var ms = new MemoryStream()) {
                var writer = new NbtBinaryWriter(ms, NbtFlavor.BedrockNetwork);
                writer.Write((short)-2);
                writer.Write(1.5f);
                writer.Write(-2.5);
                ms.Position = 0;

                var reader = new NbtBinaryReader(ms, NbtFlavor.BedrockNetwork);
                Assert.AreEqual((short)-2, reader.ReadInt16());
                Assert.AreEqual(1.5f, reader.ReadSingle());
                Assert.AreEqual(-2.5, reader.ReadDouble());
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }
    }
}
