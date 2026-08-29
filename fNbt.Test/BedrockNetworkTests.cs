using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Wire-format coverage for the BedrockNetwork flavor: TAG_Int/TAG_Long values and container
    // lengths are zigzag varints, string lengths are plain unsigned varints, everything else is
    // fixed-width little-endian. The golden bytes are hand-derived from those rules, which
    // gophertunnel and CloudburstMC also implement.
    [TestClass]
    public class BedrockNetworkTests {
        // Root compound named "", eight children covering every varint-affected shape.
        // zigzag(300) = 600 = 0xD8 0x04; zigzag(-2) = 3; zigzag(64) = 128 = 0x80 0x01.
        static readonly byte[] GoldenDoc = {
            0x0A, 0x00,                                                 // TAG_Compound, root name ""
            0x03, 0x01, (byte)'i', 0xD8, 0x04,                          // TAG_Int "i" = 300
            0x04, 0x01, (byte)'L', 0x03,                                // TAG_Long "L" = -2
            0x08, 0x01, (byte)'s', 0x02, (byte)'h', (byte)'i',          // TAG_String "s" = "hi"
            0x09, 0x04, (byte)'l', (byte)'i', (byte)'s', (byte)'t',
            0x03, 0x06, 0x02, 0x01, 0x80, 0x01,                         // TAG_List "list" of Int: 1, -1, 64
            0x07, 0x02, (byte)'b', (byte)'a', 0x06, 0x01, 0x02, 0x03,   // TAG_Byte_Array "ba" = 1, 2, 3
            0x0B, 0x02, (byte)'i', (byte)'a', 0x06, 0x00, 0x01, 0xD8, 0x04, // TAG_Int_Array "ia" = 0, -1, 300
            0x02, 0x02, (byte)'s', (byte)'h', 0xFE, 0xFF,               // TAG_Short "sh" = -2, fixed-width LE
            0x0A, 0x03, (byte)'e', (byte)'n', (byte)'d', 0x00,          // empty TAG_Compound "end"
            0x00                                                        // root TAG_End
        };


        static NbtCompound MakeGoldenTree() {
            return new NbtCompound("") {
                new NbtInt("i", 300),
                new NbtLong("L", -2),
                new NbtString("s", "hi"),
                new NbtList("list", NbtTagType.Int) {
                    new NbtInt(1), new NbtInt(-1), new NbtInt(64)
                },
                new NbtByteArray("ba", new byte[] { 1, 2, 3 }),
                new NbtIntArray("ia", new[] { 0, -1, 300 }),
                new NbtShort("sh", -2),
                new NbtCompound("end")
            };
        }


        [TestMethod]
        public void NbtWriterProducesGoldenBytes() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "", NbtFlavor.BedrockNetwork);
                writer.WriteInt("i", 300);
                writer.WriteLong("L", -2);
                writer.WriteString("s", "hi");
                writer.BeginList("list", NbtTagType.Int, 3);
                writer.WriteInt(1);
                writer.WriteInt(-1);
                writer.WriteInt(64);
                writer.EndList();
                writer.WriteByteArray("ba", new byte[] { 1, 2, 3 });
                writer.WriteIntArray("ia", new[] { 0, -1, 300 });
                writer.WriteShort("sh", -2);
                writer.BeginCompound("end");
                writer.EndCompound();
                writer.EndCompound();
                writer.Finish();

                CollectionAssert.AreEqual(GoldenDoc, ms.ToArray());
            }
        }


        [TestMethod]
        public void CodecReadsGoldenBytes() {
            NbtTag root = NbtCodec.For(NbtFlavor.BedrockNetwork)
                                  .ReadTag(GoldenDoc, 0, GoldenDoc.Length, out int bytesConsumed);
            Assert.AreEqual(GoldenDoc.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(MakeGoldenTree(), root));
        }


        [TestMethod]
        public void CodecWritesGoldenBytes() {
            // A single-child compound sidesteps NbtCompound's unspecified iteration order
            var root = new NbtCompound("") { new NbtIntArray("ia", new[] { 0, -1, 300 }) };
            byte[] expected = {
                0x0A, 0x00,
                0x0B, 0x02, (byte)'i', (byte)'a', 0x06, 0x00, 0x01, 0xD8, 0x04,
                0x00
            };
            CollectionAssert.AreEqual(expected, NbtCodec.For(NbtFlavor.BedrockNetwork).WriteTag(root));
        }


        [TestMethod]
        public void CodecRoundTripsTree() {
            var root = MakeGoldenTree();
            root.Add(new NbtFloat("f", 1.5f));
            root.Add(new NbtDouble("d", -2.5));
            root.Add(new NbtByte("b", 200));
            root.Add(new NbtList("nested", NbtTagType.List) {
                new NbtList(NbtTagType.Long) { new NbtLong(long.MaxValue), new NbtLong(long.MinValue) },
                new NbtList(NbtTagType.String) { new NbtString(new string('x', 200)) }
            });
            root.Add(new NbtIntArray("extremes", new[] { int.MinValue, int.MaxValue }));

            NbtCodec codec = NbtCodec.For(NbtFlavor.BedrockNetwork);
            byte[] doc = codec.WriteTag(root);
            NbtTag read = codec.ReadTag(doc, 0, doc.Length, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void NbtReaderSkipsVarIntValues() {
            // Reaching "sh" skips an int, a long, a string, a list of ints, a byte array,
            // and an int array, all with varint-encoded values or lengths
            using (var ms = new MemoryStream(GoldenDoc)) {
                var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                Assert.IsTrue(reader.ReadToFollowing("sh"));
                Assert.AreEqual((short)-2, reader.ReadValueAs<short>());
            }
        }


        [TestMethod]
        public void NbtReaderReadsListAsArray() {
            using (var ms = new MemoryStream(GoldenDoc)) {
                var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                Assert.IsTrue(reader.ReadToFollowing("list"));
                CollectionAssert.AreEqual(new[] { 1, -1, 64 }, reader.ReadListAsArray<int>());
            }
        }


        [TestMethod]
        public void NbtFileRoundTripsUncompressed() {
            var file = new NbtFile(MakeGoldenTree()) { Flavor = NbtFlavor.BedrockNetwork };
            byte[] saved = file.SaveToBuffer(NbtCompression.None);

            var reloaded = new NbtFile { Flavor = NbtFlavor.BedrockNetwork };
            long bytesRead = reloaded.LoadFromBuffer(saved, 0, saved.Length, NbtCompression.None);
            Assert.AreEqual(saved.Length, bytesRead);
            Assert.IsTrue(NbtComparer.Instance.Equals(file.RootTag, reloaded.RootTag));

            // The golden doc holds the same tags, so it must load to an equal tree even if
            // the dictionary happened to write them in another order
            var golden = new NbtFile { Flavor = NbtFlavor.BedrockNetwork };
            golden.LoadFromBuffer(GoldenDoc, 0, GoldenDoc.Length, NbtCompression.None);
            Assert.IsTrue(NbtComparer.Instance.Equals(file.RootTag, golden.RootTag));
        }


        [TestMethod]
        public void SelectorSkipsVarIntArrays() {
            // The tree-loading skip path must walk varint elements one at a time
            var file = new NbtFile { Flavor = NbtFlavor.BedrockNetwork };
            file.LoadFromBuffer(GoldenDoc, 0, GoldenDoc.Length, NbtCompression.None,
                                tag => tag.Name != "ia" && tag.Name != "list" && tag.Name != "L");
            Assert.IsFalse(file.RootTag.Contains("ia"));
            Assert.IsFalse(file.RootTag.Contains("list"));
            Assert.IsFalse(file.RootTag.Contains("L"));
            Assert.AreEqual(300, file.RootTag["i"].IntValue);
            Assert.AreEqual((short)-2, file.RootTag["sh"].ShortValue);
        }


        [TestMethod]
        public void LongArrayIsRejectedOnWriteByDefault() {
            // Bedrock's own reader hard-rejects tag id 12, so validation refuses it
            var root = new NbtCompound("") { new NbtLongArray("la", new[] { 1L }) };
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.BedrockNetwork).WriteTag(root));
        }


        [TestMethod]
        public void LongArrayRoundTripsWithValidationOff() {
            // Third-party libraries read and write TAG_Long_Array on the varint encoding,
            // with zigzag varint64 elements. 2^40 encodes as 6 bytes.
            byte[] expected = {
                0x0A, 0x00,
                0x0C, 0x02, (byte)'l', (byte)'a', 0x02, 0x80, 0x80, 0x80, 0x80, 0x80, 0x40,
                0x00
            };
            var options = new NbtOptions { Flavor = NbtFlavor.BedrockNetwork, ValidateOnWrite = false };
            var codec = new NbtCodec(options);
            var root = new NbtCompound("") { new NbtLongArray("la", new[] { 1L << 40 }) };

            byte[] doc = codec.WriteTag(root);
            CollectionAssert.AreEqual(expected, doc);

            // Reads are generous and accept it without any opt-out
            NbtTag read = NbtCodec.For(NbtFlavor.BedrockNetwork).ReadTag(doc, 0, doc.Length, out _);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void SkippingOverlongVarIntElementsThrows() {
            // An int-array element of six continuation bytes exceeds VarInt32's maximum width.
            // The skip path must reject it just like the read path does.
            byte[] doc = {
                0x0A, 0x00,
                0x0B, 0x01, (byte)'x', 0x02, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                0x00
            };
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                reader.ReadToFollowing(); // root
                reader.ReadToFollowing(); // "x"
                Assert.Throws<NbtFormatException>(() => reader.ReadToFollowing());
            }
            var file = new NbtFile { Flavor = NbtFlavor.BedrockNetwork };
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => false));
        }


        [TestMethod]
        public void OverflowingVarIntLengthIsRejected() {
            // Declares a root-name length of 2^32. The bit that does not fit in the fifth byte
            // used to shift away, leaving a length of zero and a document that parsed cleanly.
            byte[] doc = { 0x0A, 0x80, 0x80, 0x80, 0x80, 0x10, 0x00 };
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.BedrockNetwork).ReadTag(doc, 0, doc.Length, out _));
            var file = new NbtFile { Flavor = NbtFlavor.BedrockNetwork };
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(
                    () => new NbtReader(ms, NbtFlavor.BedrockNetwork).ReadToFollowing());
            }
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(
                    () => NbtFile.ReadRootTagName(ms, NbtCompression.None, NbtFlavor.BedrockNetwork));
            }

            // An int-array element with the same overflow is rejected when skipped, too
            byte[] arrayDoc = {
                0x0A, 0x00,
                0x0B, 0x01, (byte)'x', 0x02, 0x80, 0x80, 0x80, 0x80, 0x10,
                0x00
            };
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(arrayDoc, 0, arrayDoc.Length, NbtCompression.None, tag => false));
            using (var ms = new MemoryStream(arrayDoc)) {
                var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                reader.ReadToFollowing(); // root
                reader.ReadToFollowing(); // "x"
                Assert.Throws<NbtFormatException>(() => reader.ReadToFollowing());
            }
        }


        [TestMethod]
        public void SkippingFloatsAndDoublesStaysAligned() {
            // Floats and doubles stay fixed-width in the varint encoding; skipping them
            // through a varint-decoding path would desync the stream
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "", NbtFlavor.BedrockNetwork);
                writer.WriteFloat("f", 1.5f);
                writer.WriteDouble("d", -2.5);
                writer.WriteShort("marker", 42);
                writer.EndCompound();
                writer.Finish();

                ms.Position = 0;
                var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                Assert.IsTrue(reader.ReadToFollowing("marker"));
                Assert.AreEqual((short)42, reader.ReadValueAs<short>());
                while (reader.ReadToFollowing()) { }
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }


        [TestMethod]
        public void HugeDeclaredStringLengthFailsBeforeAllocating() {
            // TAG_String "s" declaring a 256 MB varint length in a 10-byte document.
            // The plausibility check must reject it against the bytes actually available.
            byte[] doc = {
                0x0A, 0x00,
                0x08, 0x01, (byte)'s', 0xFF, 0xFF, 0xFF, 0x7F,
                0x00
            };
            Assert.Throws<EndOfStreamException>(
                () => NbtCodec.For(NbtFlavor.BedrockNetwork).ReadTag(doc, 0, doc.Length, out _));

            // A length past int.MaxValue is a format error, not an OverflowException
            byte[] overflow = {
                0x0A, 0x00,
                0x08, 0x01, (byte)'s', 0xFF, 0xFF, 0xFF, 0xFF, 0x0F,
                0x00
            };
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.BedrockNetwork).ReadTag(overflow, 0, overflow.Length, out _));
        }


        [TestMethod]
        public void ReadRootTagNameDecodesVarIntPrefix() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, new string('n', 200), NbtFlavor.BedrockNetwork);
                writer.EndCompound();
                writer.Finish();
                ms.Position = 0;
                Assert.AreEqual(new string('n', 200),
                                NbtFile.ReadRootTagName(ms, NbtCompression.None, NbtFlavor.BedrockNetwork));
            }
        }
    }
}
