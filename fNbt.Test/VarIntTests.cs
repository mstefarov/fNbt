using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Exercises the varint primitives directly, via InternalsVisibleTo. No public API reaches
    // them until the BedrockNetwork flavor is wired up, so these are the only coverage.
    [TestClass]
    public class VarIntTests {
        static byte[] WriteInt32(int value) {
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, false, true).Write(value);
                return ms.ToArray();
            }
        }


        static byte[] WriteInt64(long value) {
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, false, true).Write(value);
                return ms.ToArray();
            }
        }


        static NbtBinaryReader VarIntReader(byte[] bytes) {
            return new NbtBinaryReader(new MemoryStream(bytes), false, true);
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
                new NbtBinaryWriter(ms, false, true).Write("hi");
                CollectionAssert.AreEqual(new byte[] { 0x02, (byte)'h', (byte)'i' }, ms.ToArray());
            }

            // 200 bytes forces a two-byte length prefix (0xC8 0x01), NOT zigzag (which would be 0x90 0x03)
            string longString = new string('a', 200);
            using (var ms = new MemoryStream()) {
                new NbtBinaryWriter(ms, false, true).Write(longString);
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
        public void TruncatedVarIntThrows() {
            // Continuation bit set, then the stream ends
            Assert.Throws<EndOfStreamException>(() => VarIntReader(new byte[] { 0x80 }).ReadInt32());
            Assert.Throws<EndOfStreamException>(() => VarIntReader(new byte[] { 0xFF, 0xFF }).ReadInt64());
        }


        [TestMethod]
        public void BulkSkipOfVarIntElementsIsRefused() {
            // Byte math cannot skip variable-width elements; the tripwire must hold
            // until an element-wise skip exists.
            NbtBinaryReader reader = VarIntReader(new byte[] { 0x00, 0x00, 0x00, 0x00 });
            Assert.Throws<NotSupportedException>(() => reader.Skip<int>(4));
        }


        [TestMethod]
        public void FixedWidthTypesAreUnaffectedByVarIntMode() {
            // Shorts, floats, and doubles stay fixed-width little-endian in the varint encoding
            using (var ms = new MemoryStream()) {
                var writer = new NbtBinaryWriter(ms, false, true);
                writer.Write((short)-2);
                writer.Write(1.5f);
                writer.Write(-2.5);
                ms.Position = 0;

                var reader = new NbtBinaryReader(ms, false, true);
                Assert.AreEqual((short)-2, reader.ReadInt16());
                Assert.AreEqual(1.5f, reader.ReadSingle());
                Assert.AreEqual(-2.5, reader.ReadDouble());
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }
    }
}
