using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public class NbtCodecTests {
        static NbtCompound MakeSampleRoot(string name) {
            return new NbtCompound(name) {
                new NbtInt("id", 42),
                new NbtString("motd", "Hello, world!"),
                new NbtCompound("nested") {
                    new NbtLong("big", 1234567890123L)
                }
            };
        }


        [TestMethod]
        public void OptionsDefaultsAreCorrect() {
            NbtOptions options = NbtOptions.Default;
            Assert.AreSame(NbtFlavor.Java, options.Flavor);
            Assert.IsFalse(options.ValidateOnRead);
            Assert.IsTrue(options.ValidateOnWrite);
            Assert.IsNull(options.MaxAllocation);

            // A fresh instance matches the Default singleton
            var custom = new NbtOptions();
            Assert.AreSame(NbtFlavor.Java, custom.Flavor);
            Assert.IsTrue(custom.ValidateOnWrite);
        }


        [TestMethod]
        public void FlavorsCarryFormatData() {
            Assert.AreEqual(NbtTagType.LongArray, NbtFlavor.Java.MaxTagType);
            Assert.AreEqual(NbtTagType.LongArray, NbtFlavor.JavaNetwork.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.JavaAnvil.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.Bedrock.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.BedrockNetwork.MaxTagType);
            Assert.AreEqual(NbtTagType.Compound, NbtFlavor.JavaLegacy.MaxTagType);
            Assert.AreEqual(NbtTagType.Compound, NbtFlavor.ClassiCube.MaxTagType);

            Assert.AreEqual(65535, NbtFlavor.Java.MaxStringBytes);
            Assert.AreEqual(32767, NbtFlavor.Bedrock.MaxStringBytes);
            Assert.AreEqual(int.MaxValue, NbtFlavor.BedrockNetwork.MaxStringBytes);
            Assert.AreEqual(256, NbtFlavor.ClassiCube.MaxStringBytes);

            Assert.AreEqual(NbtCompression.GZip, NbtFlavor.Java.DefaultCompression);
            Assert.AreEqual(NbtCompression.GZip, NbtFlavor.ClassiCube.DefaultCompression);
            Assert.AreEqual(NbtCompression.None, NbtFlavor.Bedrock.DefaultCompression);
            Assert.AreEqual(NbtCompression.None, NbtFlavor.JavaNetwork.DefaultCompression);
        }


        [TestMethod]
        public void CodecRoundTripPreservesTree() {
            NbtCompound root = MakeSampleRoot("hello");
            var codec = new NbtCodec(NbtFlavor.Bedrock);

            byte[] codecBytes = codec.WriteTag(root);
            NbtTag read = codec.ReadTag(codecBytes, 0, codecBytes.Length, out int bytesConsumed);
            Assert.AreEqual(codecBytes.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void MaxAllocationCapsArrayAllocations() {
            var root = new NbtCompound("r") { new NbtByteArray("blob", new byte[200_000]) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            // Without the cap, the same document loads fine
            var openCodec = new NbtCodec(NbtFlavor.Java);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, openCodec.ReadTag(doc, 0, doc.Length, out _)));

            var cappedCodec = new NbtCodec(new NbtOptions { MaxAllocation = 65536 });
            Assert.Throws<NbtFormatException>(() => cappedCodec.ReadTag(doc, 0, doc.Length, out _));

            // Int arrays count element size: 20k elements = 80 KB > 64 KB
            var intRoot = new NbtCompound("r") { new NbtIntArray("ints", new int[20_000]) };
            byte[] intDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(intRoot);
            Assert.Throws<NbtFormatException>(() => cappedCodec.ReadTag(intDoc, 0, intDoc.Length, out _));
        }


        [TestMethod]
        public void MaxAllocationCapsStringAllocations() {
            var root = new NbtCompound("r") { new NbtString("s", new string('x', 100)) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            var cappedCodec = new NbtCodec(new NbtOptions { MaxAllocation = 64 });
            Assert.Throws<NbtFormatException>(() => cappedCodec.ReadTag(doc, 0, doc.Length, out _));

            // A small document loads fine under the same cap
            var smallRoot = new NbtCompound("r") { new NbtString("s", "short") };
            byte[] smallDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(smallRoot);
            Assert.IsTrue(NbtComparer.Instance.Equals(
                smallRoot, cappedCodec.ReadTag(smallDoc, 0, smallDoc.Length, out _)));
        }


        [TestMethod]
        public void WriteValidationRejectsDisallowedTagTypes() {
            // ClassiCube's reader stops at tag 10, Bedrock's at 11
            var intArrayRoot = new NbtCompound("r") { new NbtIntArray("ints", new int[] { 1 }) };
            var longArrayRoot = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };

            Assert.Throws<NbtFormatException>(
                () => new NbtCodec(NbtFlavor.ClassiCube).WriteTag(intArrayRoot));
            Assert.Throws<NbtFormatException>(
                () => new NbtCodec(NbtFlavor.Bedrock).WriteTag(longArrayRoot));

            // Java permits everything; Bedrock permits int arrays
            new NbtCodec(NbtFlavor.Java).WriteTag(longArrayRoot);
            new NbtCodec(NbtFlavor.Bedrock).WriteTag(intArrayRoot);
        }


        [TestMethod]
        public void WriteValidationRejectsOverlongStrings() {
            var longValue = new NbtCompound("r") { new NbtString("s", new string('x', 300)) };
            var longName = new NbtCompound("r") { new NbtInt(new string('n', 300), 1) };

            var ccCodec = new NbtCodec(NbtFlavor.ClassiCube);
            Assert.Throws<NbtFormatException>(() => ccCodec.WriteTag(longValue));
            Assert.Throws<NbtFormatException>(() => ccCodec.WriteTag(longName));

            // 40,000 bytes: over Bedrock's 32,767, under Java's 65,535
            var overBedrock = new NbtCompound("r") { new NbtString("s", new string('x', 40_000)) };
            Assert.Throws<NbtFormatException>(() => new NbtCodec(NbtFlavor.Bedrock).WriteTag(overBedrock));
            new NbtCodec(NbtFlavor.Java).WriteTag(overBedrock);
        }


        [TestMethod]
        public void WriteValidationCanBeDisabled() {
            var root = new NbtCompound("r") { new NbtIntArray("ints", new int[] { 1, 2 }) };
            var codec = new NbtCodec(new NbtOptions {
                Flavor = NbtFlavor.ClassiCube,
                ValidateOnWrite = false
            });
            byte[] doc = codec.WriteTag(root);

            // Reads are generous regardless, so the same flavor loads it back
            Assert.IsTrue(NbtComparer.Instance.Equals(root, codec.ReadTag(doc, 0, doc.Length, out _)));
        }


        [TestMethod]
        public void ReadValidationRejectsDisallowedContent() {
            var root = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            // Default: generous
            new NbtCodec(NbtFlavor.JavaLegacy).ReadTag(doc, 0, doc.Length, out _);

            // Opted in: the flavor's tag ceiling is enforced
            var strict = new NbtCodec(new NbtOptions { Flavor = NbtFlavor.JavaLegacy, ValidateOnRead = true });
            Assert.Throws<NbtFormatException>(() => strict.ReadTag(doc, 0, doc.Length, out _));

            // String ceilings too: 33,000 bytes is over Bedrock's 32,767
            var longString = new NbtCompound("r") { new NbtString("s", new string('x', 33_000)) };
            byte[] leDoc = new NbtCodec(new NbtOptions {
                Flavor = NbtFlavor.Bedrock, ValidateOnWrite = false
            }).WriteTag(longString);

            new NbtCodec(NbtFlavor.Bedrock).ReadTag(leDoc, 0, leDoc.Length, out _);
            var strictBedrock = new NbtCodec(new NbtOptions { Flavor = NbtFlavor.Bedrock, ValidateOnRead = true });
            Assert.Throws<NbtFormatException>(() => strictBedrock.ReadTag(leDoc, 0, leDoc.Length, out _));
        }


        [TestMethod]
        public void InvalidConstructionThrows() {
            Assert.Throws<ArgumentNullException>(() => new NbtCodec((NbtFlavor)null));
            Assert.Throws<ArgumentNullException>(() => new NbtCodec((NbtOptions)null));
            Assert.Throws<ArgumentNullException>(() => new NbtCodec(new NbtOptions { Flavor = null }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NbtCodec(new NbtOptions { MaxAllocation = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NbtCodec(new NbtOptions { MaxAllocation = -5 }));
            Assert.Throws<NotSupportedException>(() => new NbtCodec(NbtFlavor.BedrockNetwork));
        }


        [TestMethod]
        public void OptionsAreSnapshottedAtConstruction() {
            var options = new NbtOptions { Flavor = NbtFlavor.ClassiCube };
            var codec = new NbtCodec(options);
            options.Flavor = NbtFlavor.Java;
            options.ValidateOnWrite = false;

            // The codec keeps ClassiCube rules and reports the snapshot
            var over = new NbtCompound("r") { new NbtString("s", new string('x', 300)) };
            Assert.Throws<NbtFormatException>(() => codec.WriteTag(over));
            Assert.AreSame(NbtFlavor.ClassiCube, codec.Options.Flavor);
            Assert.IsTrue(codec.Options.ValidateOnWrite);

            // Default returns a fresh instance every time
            Assert.AreNotSame(NbtOptions.Default, NbtOptions.Default);
        }


        [TestMethod]
        public void WriteValidationChecksEmptyListElementTypes() {
            // The element type is written even when no elements follow it
            var root = new NbtCompound("r") { new NbtList("l", NbtTagType.LongArray) };
            Assert.Throws<NbtFormatException>(() => new NbtCodec(NbtFlavor.JavaLegacy).WriteTag(root));
            new NbtCodec(NbtFlavor.Java).WriteTag(root);
            new NbtCodec(new NbtOptions { Flavor = NbtFlavor.JavaLegacy, ValidateOnWrite = false }).WriteTag(root);

            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.Bedrock);
                Assert.Throws<NbtFormatException>(() => writer.BeginList("l", NbtTagType.LongArray, 0));
            }
        }


        [TestMethod]
        public void MaxAllocationGuardsHostileDeclaredLengths() {
            // A tiny document declaring a 64 MB array. On compressed and non-seekable streams
            // the declared length cannot be checked against the bytes actually available, which
            // is the scenario MaxAllocation exists for.
            byte[] hostile = {
                0x0A, 0x00, 0x00, // TAG_Compound ""
                0x07, 0x00, 0x01, (byte)'a', // TAG_Byte_Array "a"
                0x04, 0x00, 0x00, 0x00 // declared length: 64 MB
            };
            var capped = new NbtFile(new NbtOptions { MaxAllocation = 1_048_576 });

            byte[] compressed;
            using (var ms = new MemoryStream()) {
                using (var gzs = new GZipStream(ms, CompressionMode.Compress, true)) {
                    gzs.Write(hostile, 0, hostile.Length);
                }
                compressed = ms.ToArray();
            }
            Assert.Throws<NbtFormatException>(
                () => capped.LoadFromBuffer(compressed, 0, compressed.Length, NbtCompression.GZip));

            using (var ms = new MemoryStream(hostile)) {
                Assert.Throws<NbtFormatException>(
                    () => capped.LoadFromStream(new NonSeekableStream(ms), NbtCompression.None));
            }

            using (var ms = new MemoryStream(hostile)) {
                var reader = new NbtReader(new NonSeekableStream(ms),
                                           new NbtOptions { MaxAllocation = 1_048_576 });
                Assert.Throws<NbtFormatException>(() => reader.ReadAsTag());
            }
        }


        [TestMethod]
        public void CodecStreamMethodsWork() {
            NbtCompound root = MakeSampleRoot("hello");
            var codec = new NbtCodec(NbtFlavor.Java);

            using (var ms = new MemoryStream()) {
                codec.WriteTag(root, ms);
                codec.WriteTag(root, ms);
                ms.Position = 0;

                var tags = codec.ReadConcatenatedTags(ms).ToList();
                Assert.AreEqual(2, tags.Count);
                Assert.IsTrue(NbtComparer.Instance.Equals(root, tags[0]));
            }

            using (var ms = new MemoryStream(codec.WriteTag(root))) {
                Assert.IsTrue(codec.TryReadTag(ms, out NbtTag tag));
                Assert.IsTrue(NbtComparer.Instance.Equals(root, tag));
            }
        }
    }
}
