#if NETCOREAPP
using System;
using System.Buffers;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // The ReadOnlySpan and IBufferWriter overloads of NbtCodec, which only the .NET Core build has.
    [TestClass]
    public class NbtCodecSpanTests {
        static NbtCompound MakeRoot() {
            return new NbtCompound("r") {
                new NbtInt("id", 42),
                new NbtString("motd", "Hello, world!"),
                new NbtList("vals") { new NbtShort(1), new NbtShort(2) },
                new NbtByteArray("blob", new byte[] { 1, 2, 3, 4 })
            };
        }


        [TestMethod]
        public void SpanReadHonorsTheSliceExactly() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(MakeRoot());
            // Padding on both sides, and one trailing byte inside the slice to leave behind
            byte[] padded = new byte[] { 0xAA, 0xBB }.Concat(doc).Concat(new byte[] { 0xCC, 0xDD }).ToArray();

            NbtTag tag = codec.ReadTag(padded.AsSpan(2, doc.Length + 1), out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            NbtAssert.AreEqual(MakeRoot(), tag);

            // Stack memory works too, since nothing outlives the call
            Span<byte> stack = stackalloc byte[doc.Length];
            doc.CopyTo(stack);
            Assert.AreEqual(42, codec.ReadTag(stack, out _)["id"].IntValue);
        }


        [TestMethod]
        public void SpanReadTakesAnExpectedRootType() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);
            byte[] doc = codec.WriteTag(new NbtInt(42));
            Assert.AreEqual(42, codec.ReadTag(doc, NbtTagType.Int, out int bytesConsumed).IntValue);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.Throws<NbtFormatException>(() => codec.ReadTag(doc, NbtTagType.Compound, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => codec.ReadTag(doc, NbtTagType.End, out _));
        }


        [TestMethod]
        public void SpanReadReportsEmptyAndTruncatedInput() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(MakeRoot());

            Assert.Throws<EndOfStreamException>(() => codec.ReadTag(ReadOnlySpan<byte>.Empty, out _));
            Assert.Throws<EndOfStreamException>(() => codec.ReadTag(doc.AsSpan(0, doc.Length - 3), out _));
            Assert.Throws<EndOfStreamException>(() => codec.TryReadTag(doc.AsSpan(0, doc.Length - 3), out _, out _));

            Assert.IsFalse(codec.TryReadTag(ReadOnlySpan<byte>.Empty, out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(0, bytesConsumed);
        }


        [TestMethod]
        public void SpanTryReadHandlesAbsentDocuments() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);
            byte[] absent = { 0x00, 0xFF };
            Assert.IsFalse(codec.TryReadTag(absent, out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(1, bytesConsumed);

            byte[] present = codec.WriteTag(new NbtString("hi"));
            Assert.IsTrue(codec.TryReadTag(present, out tag, out bytesConsumed));
            Assert.AreEqual("hi", tag.StringValue);
            Assert.AreEqual(present.Length, bytesConsumed);
        }


        [TestMethod]
        public void SpanReadAppliesLimits() {
            // A 12-byte document declaring a 512 MiB byte array
            byte[] hostile = {
                0x0A, 0x00, 0x00,
                0x07, 0x00, 0x01, (byte)'a', 0x20, 0x00, 0x00, 0x00,
                0x00
            };
            var capped = new NbtCodec(new NbtOptions { MaxAllocation = 1024 });
            Assert.Throws<NbtFormatException>(() => capped.ReadTag(hostile, out _));
            // Without the cap, the length is checked against the span's own bounds instead
            Assert.Throws<EndOfStreamException>(() => NbtCodec.For(NbtFlavor.Java).ReadTag(hostile, out _));
        }


        [TestMethod]
        public void BufferWriterOutputMatchesStreamOutput() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Bedrock);
            NbtCompound root = MakeRoot();
            byte[] expected = codec.WriteTag(root);

            var output = new ArrayBufferWriter<byte>();
            codec.WriteTag(root, output);
            CollectionAssert.AreEqual(expected, output.WrittenSpan.ToArray());

            // Concatenated documents append back to back
            output.Clear();
            codec.WriteConcatenatedTags(new NbtTag[] { root, root }, output);
            Assert.AreEqual(expected.Length * 2, output.WrittenCount);
            using (var ms = new MemoryStream(output.WrittenSpan.ToArray())) {
                Assert.AreEqual(2, codec.ReadConcatenatedTags(ms).Count());
            }

            // A pipeline-style round trip: written to a buffer writer, read from its span
            output.Clear();
            codec.WriteTag(root, output);
            NbtTag read = codec.ReadTag(output.WrittenSpan, out int bytesConsumed);
            Assert.AreEqual(output.WrittenCount, bytesConsumed);
            NbtAssert.AreEqual(root, read);
        }


        [TestMethod]
        public void BufferWriterWritesAbsentDocuments() {
            var output = new ArrayBufferWriter<byte>();
            NbtCodec.For(NbtFlavor.JavaNetwork).WriteTag(null, output);
            CollectionAssert.AreEqual(new byte[] { 0x00 }, output.WrittenSpan.ToArray());
        }


        [TestMethod]
        public void BufferWriterNullArgumentsThrow() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            Assert.Throws<ArgumentNullException>(() => codec.WriteTag(MakeRoot(), (IBufferWriter<byte>)null));
            Assert.Throws<ArgumentNullException>(() => codec.WriteConcatenatedTags(null, new ArrayBufferWriter<byte>()));
            Assert.Throws<ArgumentNullException>(
                () => codec.WriteConcatenatedTags(new NbtTag[0], (IBufferWriter<byte>)null));
        }
    }
}
#endif
