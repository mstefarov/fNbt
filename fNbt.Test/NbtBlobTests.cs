using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public class NbtBlobTests {
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
        public void JavaWriteMatchesNbtFileOutput() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] blobBytes = NbtBlob.WriteTag(root, NbtFlavor.Java);

            var file = new NbtFile(root) { BigEndian = true };
            byte[] fileBytes = file.SaveToBuffer(NbtCompression.None);
            CollectionAssert.AreEqual(fileBytes, blobBytes);
        }


        [TestMethod]
        public void BedrockWriteMatchesLittleEndianNbtFileOutput() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] blobBytes = NbtBlob.WriteTag(root, NbtFlavor.Bedrock);

            var file = new NbtFile(root) { BigEndian = false };
            byte[] fileBytes = file.SaveToBuffer(NbtCompression.None);
            CollectionAssert.AreEqual(fileBytes, blobBytes);
        }


        [TestMethod]
        public void EndiannessProducesExpectedBytes() {
            var root = new NbtCompound("") { new NbtInt("i", 1) };

            byte[] javaDoc = NbtBlob.WriteTag(root, NbtFlavor.Java);
            CollectionAssert.AreEqual(new byte[] {
                0x0A, 0x00, 0x00, // TAG_Compound, name ""
                0x03, 0x00, 0x01, (byte)'i', // TAG_Int "i"
                0x00, 0x00, 0x00, 0x01, // value 1, big-endian
                0x00 // TAG_End
            }, javaDoc);

            byte[] bedrockDoc = NbtBlob.WriteTag(root, NbtFlavor.Bedrock);
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
            byte[] doc = NbtBlob.WriteTag(root, NbtFlavor.Java);

            NbtTag read = NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.Java, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("hello", read.Name);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void BedrockRoundTripPreservesTree() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] doc = NbtBlob.WriteTag(root, NbtFlavor.Bedrock);

            NbtTag read = NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.Bedrock, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
        }


        [TestMethod]
        public void JavaNetworkOmitsRootName() {
            var root = new NbtCompound("ignored") { new NbtByte("b", 7) };
            byte[] doc = NbtBlob.WriteTag(root, NbtFlavor.JavaNetwork);
            CollectionAssert.AreEqual(new byte[] {
                0x0A, // TAG_Compound, no name
                0x01, 0x00, 0x01, (byte)'b', 0x07, // TAG_Byte "b" = 7
                0x00 // TAG_End
            }, doc);

            NbtTag read = NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.JavaNetwork, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.IsNull(read.Name);
            Assert.AreEqual(7, read["b"].ByteValue);
        }


        [TestMethod]
        public void JavaNetworkAllowsNonCompoundRoots() {
            // TAG_Int root, e.g. a numeric payload
            byte[] intDoc = NbtBlob.WriteTag(new NbtInt(42), NbtFlavor.JavaNetwork);
            CollectionAssert.AreEqual(new byte[] { 0x03, 0x00, 0x00, 0x00, 42 }, intDoc);
            NbtTag intRead = NbtBlob.ReadTag(intDoc, 0, intDoc.Length, NbtFlavor.JavaNetwork, out _);
            Assert.AreEqual(42, intRead.IntValue);

            // TAG_String root, e.g. a text component
            byte[] strDoc = NbtBlob.WriteTag(new NbtString("hi"), NbtFlavor.JavaNetwork);
            CollectionAssert.AreEqual(new byte[] { 0x08, 0x00, 0x02, (byte)'h', (byte)'i' }, strDoc);
            NbtTag strRead = NbtBlob.ReadTag(strDoc, 0, strDoc.Length, NbtFlavor.JavaNetwork, out _);
            Assert.AreEqual("hi", strRead.StringValue);
        }


        [TestMethod]
        public void JavaNetworkAbsentDocument() {
            // Writing a null tag produces a lone TAG_End byte
            byte[] doc = NbtBlob.WriteTag(null, NbtFlavor.JavaNetwork);
            CollectionAssert.AreEqual(new byte[] { 0x00 }, doc);

            // TryReadTag reports it as absent
            Assert.IsFalse(NbtBlob.TryReadTag(doc, 0, doc.Length, NbtFlavor.JavaNetwork,
                                              out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(1, bytesConsumed);

            // Plain ReadTag refuses it
            Assert.Throws<NbtFormatException>(
                () => NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.JavaNetwork, out _));

            // Flavors with mandatory compound roots can express neither the write...
            Assert.Throws<ArgumentNullException>(() => NbtBlob.WriteTag(null, NbtFlavor.Java));
            // ...nor the read
            Assert.Throws<NbtFormatException>(
                () => NbtBlob.TryReadTag(doc, 0, doc.Length, NbtFlavor.Java, out _, out _));
        }


        [TestMethod]
        public void TryReadTagAtCleanEndOfStreamReturnsFalse() {
            Assert.IsFalse(NbtBlob.TryReadTag(new byte[0], 0, 0, NbtFlavor.Java,
                                              out NbtTag tag, out int bytesConsumed));
            Assert.IsNull(tag);
            Assert.AreEqual(0, bytesConsumed);

            // With actual data present, TryReadTag reads it
            byte[] doc = NbtBlob.WriteTag(MakeSampleRoot("r"), NbtFlavor.Java);
            Assert.IsTrue(NbtBlob.TryReadTag(doc, 0, doc.Length, NbtFlavor.Java, out tag, out bytesConsumed));
            Assert.AreEqual("r", tag.Name);
            Assert.AreEqual(doc.Length, bytesConsumed);
        }


        [TestMethod]
        public void TrailingBytesAreLeftAlone() {
            byte[] doc = NbtBlob.WriteTag(MakeSampleRoot("r"), NbtFlavor.Java);
            byte[] padded = doc.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();

            NbtTag read = NbtBlob.ReadTag(padded, 0, padded.Length, NbtFlavor.Java, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("r", read.Name);

            // The stream overload stops at the same exact position
            using (var ms = new MemoryStream(padded)) {
                NbtBlob.ReadTag(ms, NbtFlavor.Java);
                Assert.AreEqual(doc.Length, ms.Position);
            }
        }


        [TestMethod]
        public void ConcatenatedTagsRoundTrip() {
            // LevelDB values are back-to-back little-endian roots
            using (var ms = new MemoryStream()) {
                for (int i = 0; i < 3; i++) {
                    NbtBlob.WriteTag(new NbtCompound("root" + i) { new NbtInt("i", i) }, ms, NbtFlavor.Bedrock);
                }
                ms.Position = 0;

                List<NbtTag> tags = NbtBlob.ReadConcatenatedTags(ms, NbtFlavor.Bedrock).ToList();
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
                Assert.AreEqual(0, NbtBlob.ReadConcatenatedTags(ms, NbtFlavor.Bedrock).Count());
            }
        }


        [TestMethod]
        public void ConcatenatedTagsOnNonSeekableStreamWorks() {
            using (var ms = new MemoryStream()) {
                NbtBlob.WriteTag(new NbtCompound("a") { new NbtInt("i", 1) }, ms, NbtFlavor.Bedrock);
                NbtBlob.WriteTag(new NbtCompound("b") { new NbtInt("i", 2) }, ms, NbtFlavor.Bedrock);
                ms.Position = 0;

                var nss = new NonSeekableStream(ms);
                List<NbtTag> tags = NbtBlob.ReadConcatenatedTags(nss, NbtFlavor.Bedrock).ToList();
                Assert.AreEqual(2, tags.Count);
                Assert.AreEqual("a", tags[0].Name);
                Assert.AreEqual("b", tags[1].Name);
            }
        }


        [TestMethod]
        public void ConcatenatedTagsThrowOnTruncatedDocument() {
            using (var ms = new MemoryStream()) {
                NbtBlob.WriteTag(new NbtCompound("a") { new NbtInt("i", 1) }, ms, NbtFlavor.Bedrock);
                NbtBlob.WriteTag(new NbtCompound("b") { new NbtInt("i", 2) }, ms, NbtFlavor.Bedrock);
                byte[] truncated = ms.ToArray().Take((int)ms.Length - 3).ToArray();

                using (var tms = new MemoryStream(truncated)) {
                    Assert.Throws<EndOfStreamException>(
                        () => NbtBlob.ReadConcatenatedTags(tms, NbtFlavor.Bedrock).ToList());
                }
            }
        }


        [TestMethod]
        public void FileFlavorsRequireCompoundRootOnWrite() {
            Assert.Throws<NbtFormatException>(() => NbtBlob.WriteTag(new NbtInt(1), NbtFlavor.Java));
            Assert.Throws<NbtFormatException>(() => NbtBlob.WriteTag(new NbtString("s", "v"), NbtFlavor.Bedrock));
            Assert.Throws<NbtFormatException>(
                () => NbtBlob.WriteTag(new NbtList("l", NbtTagType.Int), NbtFlavor.ClassicWorld));
        }


        [TestMethod]
        public void ReadingNonCompoundRootIsGenerous() {
            // TAG_String root named "s" with value "hi": not something vanilla writes,
            // but the reader accepts what it can parse.
            byte[] doc = {
                0x08, 0x00, 0x01, (byte)'s', // TAG_String "s"
                0x00, 0x02, (byte)'h', (byte)'i'
            };
            NbtTag read = NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.Java, out int bytesConsumed);
            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual("s", read.Name);
            Assert.AreEqual("hi", read.StringValue);
        }


        [TestMethod]
        public void ExpectedRootTypeIsEnforced() {
            byte[] doc = NbtBlob.WriteTag(new NbtInt(42), NbtFlavor.JavaNetwork);

            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(
                    () => NbtBlob.ReadTag(ms, NbtFlavor.JavaNetwork, NbtTagType.Compound));
            }
            using (var ms = new MemoryStream(doc)) {
                NbtTag read = NbtBlob.ReadTag(ms, NbtFlavor.JavaNetwork, NbtTagType.Int);
                Assert.AreEqual(42, read.IntValue);
            }
        }


        [TestMethod]
        public void BedrockNetworkIsNotYetSupported() {
            var root = new NbtCompound("") { new NbtInt("i", 1) };
            Assert.Throws<NotSupportedException>(() => NbtBlob.WriteTag(root, NbtFlavor.BedrockNetwork));
            Assert.Throws<NotSupportedException>(
                () => NbtBlob.ReadTag(new byte[] { 0x0A }, 0, 1, NbtFlavor.BedrockNetwork, out _));
        }


        [TestMethod]
        public void TruncatedDocumentThrows() {
            byte[] doc = NbtBlob.WriteTag(MakeSampleRoot("r"), NbtFlavor.Java);
            byte[] truncated = doc.Take(doc.Length - 3).ToArray();

            Assert.Throws<EndOfStreamException>(
                () => NbtBlob.ReadTag(truncated, 0, truncated.Length, NbtFlavor.Java, out _));

            // A partial document is a hard error even for TryReadTag: only a clean
            // end-of-stream before the first byte reads as "no tag".
            Assert.Throws<EndOfStreamException>(
                () => NbtBlob.TryReadTag(truncated, 0, truncated.Length, NbtFlavor.Java, out _, out _));

            Assert.Throws<EndOfStreamException>(
                () => NbtBlob.ReadTag(new byte[0], 0, 0, NbtFlavor.Java, out _));
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
            byte[] okDoc = MakeNestedCompoundDoc(512);
            NbtTag read = NbtBlob.ReadTag(okDoc, 0, okDoc.Length, NbtFlavor.Java, out _);
            Assert.IsNotNull(((NbtCompound)read).Get<NbtCompound>("c"));

            byte[] deepDoc = MakeNestedCompoundDoc(513);
            Assert.Throws<NbtFormatException>(
                () => NbtBlob.ReadTag(deepDoc, 0, deepDoc.Length, NbtFlavor.Java, out _));
        }


        [TestMethod]
        public void NullRootNameIsWrittenAsEmpty() {
            var root = new NbtCompound { new NbtInt("i", 1) }; // unnamed
            byte[] doc = NbtBlob.WriteTag(root, NbtFlavor.Java);
            NbtTag read = NbtBlob.ReadTag(doc, 0, doc.Length, NbtFlavor.Java, out _);
            Assert.AreEqual("", read.Name);
            Assert.AreEqual(1, read["i"].IntValue);
        }


        [TestMethod]
        public void ReadingRealUncompressedFileMatchesNbtFile() {
            var file = new NbtFile();
            file.LoadFromFile(TestFiles.Small, NbtCompression.None, null);

            using (FileStream fs = File.OpenRead(TestFiles.Small)) {
                NbtTag blobRoot = NbtBlob.ReadTag(fs, NbtFlavor.Java);
                Assert.AreEqual(fs.Length, fs.Position);
                Assert.IsTrue(NbtComparer.Instance.Equals(file.RootTag, blobRoot));
            }
        }


        [TestMethod]
        public void ReadingThroughAwkwardStreamsWorks() {
            NbtCompound root = MakeSampleRoot("r");
            byte[] doc = NbtBlob.WriteTag(root, NbtFlavor.Java);
            using (var ms = new MemoryStream(doc)) {
                var awkward = new PartialReadStream(new NonSeekableStream(ms), 1);
                NbtTag read = NbtBlob.ReadTag(awkward, NbtFlavor.Java);
                Assert.IsTrue(NbtComparer.Instance.Equals(root, read));
            }
        }


        [TestMethod]
        public void NullArgumentsThrow() {
            var root = new NbtCompound("r");
            Assert.Throws<ArgumentNullException>(() => NbtBlob.ReadTag((Stream)null, NbtFlavor.Java));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.ReadTag(new MemoryStream(), null));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.ReadTag(null, 0, 0, NbtFlavor.Java, out _));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.TryReadTag((Stream)null, NbtFlavor.Java, out _));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.ReadConcatenatedTags(null, NbtFlavor.Java));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.ReadConcatenatedTags(new MemoryStream(), null));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.WriteTag(root, null, NbtFlavor.Java));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.WriteTag(root, new MemoryStream(), null));
            Assert.Throws<ArgumentNullException>(() => NbtBlob.WriteTag(root, (NbtFlavor)null));
        }
    }
}
