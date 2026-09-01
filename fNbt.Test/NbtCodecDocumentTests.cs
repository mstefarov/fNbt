using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Document-level semantics of NbtCodec: root rules per flavor, absent documents,
    // exact consumption, and concatenated reads.
    [TestClass]
    public class NbtCodecDocumentTests {
        static NbtCompound MakeSampleRoot(string name) {
            return new NbtCompound(name) {
                new NbtInt("id", 42),
                new NbtString("motd", "Hello, world!"),
                new NbtList("vals") {
                    new NbtShort(1),
                    new NbtShort(2)
                },
                new NbtCompound("nested") {
                    new NbtLong("big", 1234567890123L)
                },
                new NbtByteArray("blob", new byte[] { 1, 2, 3, 4 })
            };
        }


        [TestMethod]
        public void ForReturnsCachedEquivalentCodec() {
            Assert.AreSame(NbtCodec.For(NbtFlavor.Java), NbtCodec.For(NbtFlavor.Java));

            NbtCompound root = MakeSampleRoot("hello");
            byte[] cached = NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root);
            byte[] fresh = new NbtCodec(NbtFlavor.Bedrock).WriteTag(root);
            CollectionAssert.AreEqual(fresh, cached);
        }


        [TestMethod]
        public void JavaWriteMatchesNbtFileOutput() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] codecBytes = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            var file = new NbtFile(root, NbtFlavor.Java);
            byte[] fileBytes = file.SaveToBuffer(NbtCompression.None);
            CollectionAssert.AreEqual(fileBytes, codecBytes);
        }


        [TestMethod]
        public void BedrockWriteMatchesLittleEndianNbtFileOutput() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] codecBytes = NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root);

            var file = new NbtFile(root, NbtFlavor.Bedrock);
            byte[] fileBytes = file.SaveToBuffer(NbtCompression.None);
            CollectionAssert.AreEqual(fileBytes, codecBytes);
        }


        [TestMethod]
        public void EndiannessProducesExpectedBytes() {
            var root = new NbtCompound("") { new NbtInt("i", 1) };

            byte[] javaDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);
            CollectionAssert.AreEqual(new byte[] {
                0x0A, 0x00, 0x00, // TAG_Compound, name ""
                0x03, 0x00, 0x01, (byte)'i', // TAG_Int "i"
                0x00, 0x00, 0x00, 0x01, // value 1, big-endian
                0x00 // TAG_End
            }, javaDoc);

            byte[] bedrockDoc = NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root);
            CollectionAssert.AreEqual(new byte[] {
                0x0A, 0x00, 0x00,
                0x03, 0x01, 0x00, (byte)'i',
                0x01, 0x00, 0x00, 0x00, // value 1, little-endian
                0x00
            }, bedrockDoc);
        }


        [TestMethod]
        public void JavaRoundTripPreservesTree() {
            NbtCompound root = MakeSampleRoot("hello");
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(root);

            NbtTag read = codec.ReadTag(doc, 0, doc.Length, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("hello", read.Name);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void JavaNetworkOmitsRootName() {
            var root = new NbtCompound("ignored") { new NbtByte("b", 7) };
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);
            byte[] doc = codec.WriteTag(root);
            CollectionAssert.AreEqual(new byte[] {
                0x0A, // TAG_Compound, no name
                0x01, 0x00, 0x01, (byte)'b', 0x07, // TAG_Byte "b" = 7
                0x00 // TAG_End
            }, doc);

            NbtTag read = codec.ReadTag(doc, 0, doc.Length, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsNull(read.Name);
            Assert.AreEqual(7, read["b"].ByteValue);
        }


        [TestMethod]
        public void JavaNetworkAllowsNonCompoundRoots() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);

            // TAG_Int root, e.g. a numeric payload
            byte[] intDoc = codec.WriteTag(new NbtInt(42));
            CollectionAssert.AreEqual(new byte[] { 0x03, 0x00, 0x00, 0x00, 42 }, intDoc);
            Assert.AreEqual(42, codec.ReadTag(intDoc, 0, intDoc.Length, out _).IntValue);

            // TAG_String root, e.g. a text component
            byte[] strDoc = codec.WriteTag(new NbtString("hi"));
            CollectionAssert.AreEqual(new byte[] { 0x08, 0x00, 0x02, (byte)'h', (byte)'i' }, strDoc);
            Assert.AreEqual("hi", codec.ReadTag(strDoc, 0, strDoc.Length, out _).StringValue);
        }


        [TestMethod]
        public void JavaNetworkAbsentDocument() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);

            // Writing a null tag produces a lone TAG_End byte
            byte[] doc = codec.WriteTag(null);
            CollectionAssert.AreEqual(new byte[] { 0x00 }, doc);

            // TryReadTag reports it as absent
            Assert.IsFalse(codec.TryReadTag(doc, 0, doc.Length, out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(1, bytesConsumed);

            // Plain ReadTag refuses it
            Assert.Throws<NbtFormatException>(() => codec.ReadTag(doc, 0, doc.Length, out _));

            // Flavors with mandatory compound roots can express neither the write...
            Assert.Throws<ArgumentNullException>(() => NbtCodec.For(NbtFlavor.Java).WriteTag(null));
            // ...nor the read
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.Java).TryReadTag(doc, 0, doc.Length, out _, out _));
        }


        [TestMethod]
        public void TryReadTagAtCleanEndOfStreamReturnsFalse() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            Assert.IsFalse(codec.TryReadTag(new byte[0], 0, 0, out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(0, bytesConsumed);

            // With actual data present, TryReadTag reads it
            byte[] doc = codec.WriteTag(MakeSampleRoot("r"));
            Assert.IsTrue(codec.TryReadTag(doc, 0, doc.Length, out tag, out bytesConsumed));
            Assert.AreEqual("r", tag.Name);
            Assert.AreEqual(doc.Length, bytesConsumed);
        }


        [TestMethod]
        public void TrailingBytesAreLeftAlone() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(MakeSampleRoot("r"));
            byte[] padded = doc.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();

            NbtTag read = codec.ReadTag(padded, 0, padded.Length, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("r", read.Name);

            // The stream overload stops at the same exact position
            using (var ms = new MemoryStream(padded)) {
                codec.ReadTag(ms);
                Assert.AreEqual(doc.Length, ms.Position);
            }
        }


        [TestMethod]
        public void ConcatenatedTagsRoundTrip() {
            // LevelDB values are back-to-back little-endian roots
            NbtCodec codec = NbtCodec.For(NbtFlavor.Bedrock);
            using (var ms = new MemoryStream()) {
                for (int i = 0; i < 3; i++) {
                    codec.WriteTag(new NbtCompound("root" + i) { new NbtInt("i", i) }, ms);
                }
                ms.Position = 0;

                List<NbtTag> tags = codec.ReadConcatenatedTags(ms).ToList();
                Assert.AreEqual(3, tags.Count);
                for (int i = 0; i < 3; i++) {
                    Assert.AreEqual("root" + i, tags[i].Name);
                    Assert.AreEqual(i, tags[i]["i"].IntValue);
                }
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }


        [TestMethod]
        public void ConcatenatedTagsOnEmptyStreamYieldsNothing() {
            using (var ms = new MemoryStream()) {
                Assert.AreEqual(0, NbtCodec.For(NbtFlavor.Bedrock).ReadConcatenatedTags(ms).Count());
            }
        }


        [TestMethod]
        public void ConcatenatedTagsOnNonSeekableStreamWorks() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Bedrock);
            using (var ms = new MemoryStream()) {
                codec.WriteTag(new NbtCompound("a") { new NbtInt("i", 1) }, ms);
                codec.WriteTag(new NbtCompound("b") { new NbtInt("i", 2) }, ms);
                ms.Position = 0;

                var nss = new NonSeekableStream(ms);
                List<NbtTag> tags = codec.ReadConcatenatedTags(nss).ToList();
                Assert.AreEqual(2, tags.Count);
                Assert.AreEqual("a", tags[0].Name);
                Assert.AreEqual("b", tags[1].Name);
            }
        }


        [TestMethod]
        public void WriteConcatenatedTagsMatchesPerDocumentWrites() {
            foreach (NbtFlavor flavor in new[] { NbtFlavor.Java, NbtFlavor.BedrockNetwork }) {
                NbtCodec codec = NbtCodec.For(flavor);
                var roots = new NbtTag[] {
                    new NbtCompound("a") { new NbtInt("i", 300) },
                    new NbtCompound("b") { new NbtString("s", "hi") },
                    new NbtCompound("c") { new NbtLongArray("la", new[] { 1L, -2L }) }
                };
                if (flavor == NbtFlavor.BedrockNetwork) {
                    // LongArray fails this flavor's write validation
                    roots[2] = new NbtCompound("c") { new NbtIntArray("ia", new[] { 1, -2 }) };
                }

                using (var perDoc = new MemoryStream())
                using (var concatenated = new MemoryStream()) {
                    foreach (NbtTag root in roots) {
                        codec.WriteTag(root, perDoc);
                    }
                    codec.WriteConcatenatedTags(roots, concatenated);
                    CollectionAssert.AreEqual(perDoc.ToArray(), concatenated.ToArray(), flavor.Name);

                    concatenated.Position = 0;
                    List<NbtTag> readBack = codec.ReadConcatenatedTags(concatenated).ToList();
                    Assert.AreEqual(roots.Length, readBack.Count);
                    for (int i = 0; i < roots.Length; i++) {
                        Assert.IsTrue(NbtComparer.Instance.Equals(roots[i], readBack[i]), flavor.Name);
                    }
                }
            }
        }


        [TestMethod]
        public void WriteConcatenatedTagsRejectsBadArguments() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            var root = new NbtCompound("r");
            using (var ms = new MemoryStream()) {
                Assert.Throws<ArgumentNullException>(() => codec.WriteConcatenatedTags(null, ms));
                Assert.Throws<ArgumentNullException>(() => codec.WriteConcatenatedTags(new[] { root }, (Stream)null));
                // Absent documents cannot appear in a concatenated stream, so null elements are refused
                Assert.Throws<ArgumentException>(
                    () => codec.WriteConcatenatedTags(new NbtTag[] { root, null }, ms));
                // Root rules and validation apply per document
                Assert.Throws<NbtFormatException>(
                    () => codec.WriteConcatenatedTags(new NbtTag[] { new NbtInt("i", 1) }, ms));
                var over = new NbtCompound("r") { new NbtString("s", new string('x', 300)) };
                Assert.Throws<NbtFormatException>(
                    () => NbtCodec.For(NbtFlavor.ClassiCube).WriteConcatenatedTags(new NbtTag[] { root, over }, ms));
            }
        }


        [TestMethod]
        public void ConcatenatedTagsThrowOnTruncatedDocument() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Bedrock);
            using (var ms = new MemoryStream()) {
                codec.WriteTag(new NbtCompound("a") { new NbtInt("i", 1) }, ms);
                codec.WriteTag(new NbtCompound("b") { new NbtInt("i", 2) }, ms);
                byte[] truncated = ms.ToArray().Take((int)ms.Length - 3).ToArray();

                using (var tms = new MemoryStream(truncated)) {
                    Assert.Throws<EndOfStreamException>(
                        () => codec.ReadConcatenatedTags(tms).ToList());
                }
            }
        }


        [TestMethod]
        public void FileFlavorsRequireCompoundRootOnWrite() {
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.Java).WriteTag(new NbtInt(1)));
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.Bedrock).WriteTag(new NbtString("s", "v")));
            Assert.Throws<NbtFormatException>(
                () => NbtCodec.For(NbtFlavor.ClassiCube).WriteTag(new NbtList("l", NbtTagType.Int)));
        }


        [TestMethod]
        public void ReadingNonCompoundRootIsGenerous() {
            // TAG_String root named "s" with value "hi": not something Minecraft writes,
            // but the reader accepts what it can parse.
            byte[] doc = {
                0x08, 0x00, 0x01, (byte)'s', // TAG_String "s"
                0x00, 0x02, (byte)'h', (byte)'i'
            };
            NbtTag read = NbtCodec.For(NbtFlavor.Java).ReadTag(doc, 0, doc.Length, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("s", read.Name);
            Assert.AreEqual("hi", read.StringValue);
        }


        // TAG_String root named "s" with value "hi": parseable, but not a compound
        static readonly byte[] StringRootDoc = {
            0x08, 0x00, 0x01, (byte)'s',
            0x00, 0x02, (byte)'h', (byte)'i'
        };


        static NbtCodec StrictCodec(NbtFlavor flavor) {
            return new NbtCodec(new NbtOptions(flavor) { ValidateOnRead = true });
        }


        [TestMethod]
        public void ReadValidationRequiresCompoundRoot() {
            // Java has no tag-type or string restrictions, so the root rule is the only thing
            // its strict reads enforce. Every entry point must apply it.
            NbtCodec strict = StrictCodec(NbtFlavor.Java);
            byte[] doc = StringRootDoc;
            Assert.Throws<NbtFormatException>(() => strict.ReadTag(doc, 0, doc.Length, out _));
            Assert.Throws<NbtFormatException>(() => strict.ReadTag(doc, 0, doc.Length, NbtTagType.String, out _));
            Assert.Throws<NbtFormatException>(() => strict.TryReadTag(doc, 0, doc.Length, out _, out _));
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(() => strict.ReadTag(ms));
            }
            using (var ms = new MemoryStream(doc)) {
                // Asking for that root type does not override the flavor's rule
                Assert.Throws<NbtFormatException>(() => strict.ReadTag(ms, NbtTagType.String));
            }
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(() => strict.TryReadTag(ms, out _));
            }
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(() => strict.ReadConcatenatedTags(ms).ToList());
            }
#if NETCOREAPP
            Assert.Throws<NbtFormatException>(() => strict.ReadTag((ReadOnlySpan<byte>)doc, out _));
#endif

            // Compound roots still pass, and reads stay generous without the option
            byte[] compoundDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(new NbtCompound("r") { new NbtInt("i", 1) });
            Assert.AreEqual(1, strict.ReadTag(compoundDoc, 0, compoundDoc.Length, out _)["i"].IntValue);
            Assert.AreEqual("hi", NbtCodec.For(NbtFlavor.Java).ReadTag(doc, 0, doc.Length, out _).StringValue);
        }


        [TestMethod]
        public void ReadValidationRootRuleFollowsEachFlavor() {
            NbtFlavor[] compoundRootFlavors = {
                NbtFlavor.Java, NbtFlavor.JavaAnvil, NbtFlavor.JavaLegacy,
                NbtFlavor.Bedrock, NbtFlavor.BedrockNetwork, NbtFlavor.ClassiCube
            };
            foreach (NbtFlavor flavor in compoundRootFlavors) {
                // TAG_Byte root with an empty name and value 7; the name prefix is one varint
                // byte under BedrockNetwork and two bytes elsewhere
                byte[] doc = flavor.UsesVarInts
                    ? new byte[] { 0x01, 0x00, 0x07 }
                    : new byte[] { 0x01, 0x00, 0x00, 0x07 };
                Assert.AreEqual(7, new NbtCodec(flavor).ReadTag(doc, 0, doc.Length, out _).ByteValue, flavor.Name);
                NbtCodec strict = StrictCodec(flavor);
                Assert.Throws<NbtFormatException>(() => strict.ReadTag(doc, 0, doc.Length, out _));
            }

            // JavaNetwork allows any root, so its strict reads keep accepting one
            byte[] unnamed = { 0x01, 0x07 };
            Assert.AreEqual(7, StrictCodec(NbtFlavor.JavaNetwork).ReadTag(unnamed, 0, unnamed.Length, out _).ByteValue);
        }


        [TestMethod]
        public void ExpectedRootTypeIsEnforced() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);
            byte[] doc = codec.WriteTag(new NbtInt(42));

            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(() => codec.ReadTag(ms, NbtTagType.Compound));
            }
            using (var ms = new MemoryStream(doc)) {
                NbtTag read = codec.ReadTag(ms, NbtTagType.Int);
                Assert.AreEqual(42, read.IntValue);
            }

            // The buffer overload takes the same argument
            Assert.Throws<NbtFormatException>(() => codec.ReadTag(doc, 0, doc.Length, NbtTagType.Compound, out _));
            Assert.AreEqual(42, codec.ReadTag(doc, 0, doc.Length, NbtTagType.Int, out int consumed).IntValue);
            Assert.AreEqual(doc.Length, consumed);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => codec.ReadTag(doc, 0, doc.Length, NbtTagType.End, out _));
        }


[TestMethod]
        public void TruncatedDocumentThrows() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(MakeSampleRoot("r"));
            byte[] truncated = doc.Take(doc.Length - 3).ToArray();

            Assert.Throws<EndOfStreamException>(
                () => codec.ReadTag(truncated, 0, truncated.Length, out _));

            // A partial document is a hard error even for TryReadTag: only a clean
            // end-of-stream before the first byte reads as "no tag".
            Assert.Throws<EndOfStreamException>(
                () => codec.TryReadTag(truncated, 0, truncated.Length, out _, out _));

            Assert.Throws<EndOfStreamException>(() => codec.ReadTag(new byte[0], 0, 0, out _));
        }


        // Builds an uncompressed Java doc of compounds nested totalLevels deep (including root)
        static byte[] MakeNestedCompoundDoc(int totalLevels) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 0); // root name: ""
                for (int i = 1; i < totalLevels; i++) {
                    ms.WriteByte(0x0A);
                    TestFiles.WriteBEShort(ms, 1);
                    ms.WriteByte((byte)'c');
                }
                for (int i = 0; i < totalLevels; i++) {
                    ms.WriteByte(0x00);
                }
                return ms.ToArray();
            }
        }


        [TestMethod]
        public void DepthLimitIsEnforced() {
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);

            byte[] okDoc = MakeNestedCompoundDoc(512);
            NbtTag read = codec.ReadTag(okDoc, 0, okDoc.Length, out _);
            Assert.IsNotNull(((NbtCompound)read).Get<NbtCompound>("c"));

            byte[] deepDoc = MakeNestedCompoundDoc(513);
            Assert.Throws<NbtFormatException>(() => codec.ReadTag(deepDoc, 0, deepDoc.Length, out _));
        }


        [TestMethod]
        public void NullRootNameIsWrittenAsEmpty() {
            var root = new NbtCompound { new NbtInt("i", 1) }; // unnamed
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(root);
            NbtTag read = codec.ReadTag(doc, 0, doc.Length, out _);
            Assert.AreEqual("", read.Name);
            Assert.AreEqual(1, read["i"].IntValue);
        }


        [TestMethod]
        public void ReadingRealUncompressedFileMatchesNbtFile() {
            var file = new NbtFile();
            file.LoadFromFile(TestFiles.Small, NbtCompression.None, null);

            using (FileStream fs = File.OpenRead(TestFiles.Small)) {
                NbtTag codecRoot = NbtCodec.For(NbtFlavor.Java).ReadTag(fs);
                Assert.AreEqual(fs.Length, fs.Position);
                Assert.IsTrue(NbtComparer.Instance.Equals(file.RootTag, codecRoot));
            }
        }


        [TestMethod]
        public void ReadingThroughAwkwardStreamsWorks() {
            NbtCompound root = MakeSampleRoot("r");
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(root);
            using (var ms = new MemoryStream(doc)) {
                var awkward = new PartialReadStream(new NonSeekableStream(ms), 1);
                NbtTag read = codec.ReadTag(awkward);
                Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
            }
        }


        [TestMethod]
        public void BufferOverloadsHonorIndex() {
            NbtCompound root = MakeSampleRoot("r");
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            byte[] doc = codec.WriteTag(root);
            byte[] padded = new byte[] { 0xAA, 0xBB, 0xCC }.Concat(doc).Concat(new byte[] { 0xDD }).ToArray();

            NbtTag read = codec.ReadTag(padded, 3, doc.Length + 1, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));

            Assert.IsTrue(codec.TryReadTag(padded, 3, doc.Length, out NbtTag tag, out bytesConsumed));
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, tag));
        }


        [TestMethod]
        public void NullArgumentsThrow() {
            var root = new NbtCompound("r");
            NbtCodec codec = NbtCodec.For(NbtFlavor.Java);
            Assert.Throws<ArgumentNullException>(() => NbtCodec.For(null));
            Assert.Throws<ArgumentNullException>(() => codec.ReadTag((Stream)null));
            Assert.Throws<ArgumentNullException>(() => codec.ReadTag(null, 0, 0, out _));
            Assert.Throws<ArgumentNullException>(() => codec.TryReadTag((Stream)null, out _));
            Assert.Throws<ArgumentNullException>(() => codec.ReadConcatenatedTags(null));
            Assert.Throws<ArgumentNullException>(() => codec.WriteTag(root, (Stream)null));
        }
    }
}
