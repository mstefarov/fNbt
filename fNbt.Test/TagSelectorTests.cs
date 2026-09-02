using System.IO;

namespace fNbt.Test {
    [TestClass]
    public sealed class TagSelectorTests {
        [TestMethod]
        public void SkippingListOfLongArrays() {
            // Bugfix regression test: NbtList.SkipTag had no LongArray case.
            // Each skipped element consumed zero bytes, desyncing the reader.
            var root = new NbtCompound("root") {
                new NbtList("skipme", NbtTagType.LongArray) {
                    new NbtLongArray(new long[] { 1, 2 }),
                    new NbtLongArray(new long[] { 3, 4, 5 })
                },
                new NbtInt("after", 0x41424344)
            };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.None);

            var file = new NbtFile();
            long consumed = file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None,
                                                tag => tag.Name != "skipme");
            Assert.AreEqual(doc.Length, consumed);
            Assert.AreEqual(1, file.RootTag.Count);
            Assert.AreEqual(0x41424344, file.RootTag["after"].IntValue);
        }


        [TestMethod]
        public void SkippingListOfInvalidTypeThrows() {
            // A skipped List<End> with a nonzero count must be rejected the way a normal load
            // rejects it, instead of quietly skipping zero bytes per element
            byte[] doc;
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A); // root compound, named ""
                ms.WriteByte(0);
                ms.WriteByte(0);
                ms.WriteByte(0x09); // TAG_List "skipme"
                ms.WriteByte(0);
                ms.WriteByte(6);
                foreach (char c in "skipme") ms.WriteByte((byte)c);
                ms.WriteByte(0x00); // element type: End
                ms.WriteByte(0); // count: 3
                ms.WriteByte(0);
                ms.WriteByte(0);
                ms.WriteByte(3);
                ms.WriteByte(0x00); // root's TAG_End
                doc = ms.ToArray();
            }
            Assert.Throws<NbtFormatException>(
                () => TestFiles.Load(doc, selector: tag => tag.Name != "skipme"));
        }


        [TestMethod]
        public void SkippingTagsOnFileLoad() {
            var loadedFile = new NbtFile();
            loadedFile.LoadFromFile(TestFiles.Big,
                                    NbtCompression.None,
                                    tag => tag.Name != "nested compound test");
            Assert.IsFalse(loadedFile.RootTag.Contains("nested compound test"));
            Assert.IsTrue(loadedFile.RootTag.Contains("listTest (long)"));

            loadedFile.LoadFromFile(TestFiles.Big,
                                    NbtCompression.None,
                                    tag => tag.TagType != NbtTagType.Float || tag.Parent.Name != "Level");
            Assert.IsFalse(loadedFile.RootTag.Contains("floatTest"));
            Assert.AreEqual(0.75f, loadedFile.RootTag["nested compound test"]["ham"]["value"].FloatValue);

            loadedFile.LoadFromFile(TestFiles.Big,
                                    NbtCompression.None,
                                    tag => tag.Name != "listTest (long)");
            Assert.IsFalse(loadedFile.RootTag.Contains("listTest (long)"));
            Assert.IsTrue(loadedFile.RootTag.Contains("byteTest"));

            loadedFile.LoadFromFile(TestFiles.Big,
                                    NbtCompression.None,
                                    tag => false);
            Assert.AreEqual(0, loadedFile.RootTag.Count);
        }


        [TestMethod]
        public void SkippingLists() {
            NbtCompound root = TestFiles.Reload(TestFiles.MakeListTest(),
                                                selector: tag => tag.TagType != NbtTagType.List);
            Assert.AreEqual(0, root.Count);

            // Check list-compound interaction
            NbtCompound comp = new NbtCompound("root") {
                new NbtCompound("compOfLists") {
                    new NbtList("listOfComps") {
                        new NbtCompound {
                            new NbtList("emptyList", NbtTagType.Compound)
                        }
                    }
                }
            };
            root = TestFiles.Reload(comp, selector: tag => tag.TagType != NbtTagType.List);
            Assert.AreEqual(1, root.Count);
        }



        [TestMethod]
        public void SkippedStringsRespectFlavorCeilingButNotMaxAllocation() {
            // A 300-byte string, valid under Java rules
            var root = new NbtCompound("r") {
                new NbtString("s", new string('x', 300)),
                new NbtShort("k", 5)
            };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            // Read validation enforces the flavor's own ceiling on skipped strings too:
            // conformance is about the document, not about whether the value was kept
            Assert.Throws<NbtFormatException>(
                () => TestFiles.Load(doc, new NbtOptions { Flavor = NbtFlavor.ClassiCube, ValidateOnRead = true },
                                     tag => tag.Name != "s"));

            // MaxAllocation does not apply to skips, which allocate nothing
            NbtFile capped = TestFiles.Load(doc, new NbtOptions { MaxAllocation = 100 }, tag => tag.Name != "s");
            Assert.IsFalse(capped.RootTag.Contains("s"));
            Assert.AreEqual((short)5, capped.RootTag["k"].ShortValue);

            // Reading the same string with that cap still throws
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(doc, new NbtOptions { MaxAllocation = 100 }));
        }
    }
}
