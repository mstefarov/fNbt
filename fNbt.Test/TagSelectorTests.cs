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
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None,
                                          tag => tag.Name != "skipme"));
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
            {
                var file = new NbtFile(TestFiles.MakeListTest());
                byte[] savedFile = file.SaveToBuffer(NbtCompression.None);
                file.LoadFromBuffer(savedFile, 0, savedFile.Length, NbtCompression.None,
                                    tag => tag.TagType != NbtTagType.List);
                Assert.AreEqual(0, file.RootTag.Count);
            }
            {
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
                var file = new NbtFile(comp);
                byte[] savedFile = file.SaveToBuffer(NbtCompression.None);
                file.LoadFromBuffer(savedFile, 0, savedFile.Length, NbtCompression.None,
                                    tag => tag.TagType != NbtTagType.List);
                Assert.AreEqual(1, file.RootTag.Count);
            }
        }


        [TestMethod]
        public void SkippingValuesInCompoundTest() {
            NbtCompound root = TestFiles.MakeValueTest();
            NbtCompound nestedComp = TestFiles.MakeValueTest();
            nestedComp.Name = "NestedComp";
            root.Add(nestedComp);

            var file = new NbtFile(root);
            byte[] savedFile = file.SaveToBuffer(NbtCompression.None);
            file.LoadFromBuffer(savedFile, 0, savedFile.Length, NbtCompression.None, tag => false);
            Assert.AreEqual(0, file.RootTag.Count);
        }
    }
}
