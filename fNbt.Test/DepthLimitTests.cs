using System;
using System.IO;

namespace fNbt.Test {
    [TestClass]
    public class DepthLimitTests {
        // Same as the internal cap shared by all recursive walks.
        const int MaxDepth = 512;


        // Builds an uncompressed doc: root compound "" holding recursively nested TAG_List "l".
        // Total container depth, root included, comes out to listLevels + 1.
        static byte[] MakeNestedListDoc(int listLevels) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A); // TAG_Compound
                TestFiles.WriteBEShort(ms, 0); // root name: ""
                ms.WriteByte(0x09); // TAG_List
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'l');
                for (int i = 1; i < listLevels; i++) {
                    ms.WriteByte(0x09); // element type: List
                    TestFiles.WriteBEInt(ms, 1); // one element
                }
                ms.WriteByte(0x00); // innermost list's element type: End
                TestFiles.WriteBEInt(ms, 0); // zero elements
                ms.WriteByte(0x00); // root's TAG_End
                return ms.ToArray();
            }
        }


        // Builds an uncompressed doc of compounds nested totalLevels deep (including root).
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


        // Builds an in-memory chain of compounds nested totalLevels deep (including root).
        static NbtCompound MakeNestedCompoundTree(int totalLevels) {
            var root = new NbtCompound("root");
            NbtCompound current = root;
            for (int i = 1; i < totalLevels; i++) {
                var child = new NbtCompound("c");
                current.Add(child);
                current = child;
            }
            return root;
        }


        [TestMethod]
        public void LoadingDocAtDepthLimitSucceeds() {
            byte[] listDoc = MakeNestedListDoc(MaxDepth - 1);
            var file = new NbtFile();
            file.LoadFromBuffer(listDoc, 0, listDoc.Length, NbtCompression.None);
            Assert.IsNotNull(file.RootTag.Get<NbtList>("l"));

            byte[] compoundDoc = MakeNestedCompoundDoc(MaxDepth);
            file.LoadFromBuffer(compoundDoc, 0, compoundDoc.Length, NbtCompression.None);
            Assert.IsNotNull(file.RootTag.Get<NbtCompound>("c"));
        }


        [TestMethod]
        public void LoadingDocOverDepthLimitThrows() {
            byte[] listDoc = MakeNestedListDoc(MaxDepth);
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(listDoc, 0, listDoc.Length, NbtCompression.None));

            byte[] compoundDoc = MakeNestedCompoundDoc(MaxDepth + 1);
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(compoundDoc, 0, compoundDoc.Length, NbtCompression.None));
        }


        [TestMethod]
        public void LoadingVeryDeepDocThrowsInsteadOfCrashing() {
            // Before the depth cap, this depth was an uncatchable StackOverflowException
            byte[] doc = MakeNestedListDoc(10000);
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(() => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
        }


        [TestMethod]
        public void SkippingDeepDocThrows() {
            // Selector rejection routes through SkipTag, which recurses on its own
            byte[] doc = MakeNestedListDoc(10000);
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "l"));
        }


        [TestMethod]
        public void NbtReaderDeepDocThrows() {
            byte[] doc = MakeNestedListDoc(10000);
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms);
                Assert.Throws<NbtFormatException>(() => {
                    while (reader.ReadToFollowing()) { }
                });
                Assert.IsTrue(reader.IsInErrorState);
            }
        }


        [TestMethod]
        public void NbtReaderDocAtDepthLimitSucceeds() {
            byte[] doc = MakeNestedListDoc(MaxDepth - 1);
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms);
                while (reader.ReadToFollowing()) { }
                Assert.IsTrue(reader.IsAtStreamEnd);
            }
        }


        [TestMethod]
        public void SavingTreeAtDepthLimitSucceeds() {
            var file = new NbtFile(MakeNestedCompoundTree(MaxDepth));
            byte[] saved = file.SaveToBuffer(NbtCompression.None);
            file.LoadFromBuffer(saved, 0, saved.Length, NbtCompression.None);
            Assert.IsNotNull(file.RootTag.Get<NbtCompound>("c"));
        }


        [TestMethod]
        public void SavingTreeOverDepthLimitThrows() {
            var file = new NbtFile(MakeNestedCompoundTree(MaxDepth + 1));
            Assert.Throws<NbtFormatException>(() => file.SaveToBuffer(NbtCompression.None));
        }


        [TestMethod]
        public void SavingVeryDeepTreeThrowsInsteadOfCrashing() {
            // Before the depth cap, saving this tree was an uncatchable StackOverflowException
            var file = new NbtFile(MakeNestedCompoundTree(20000));
            Assert.Throws<NbtFormatException>(() => file.SaveToBuffer(NbtCompression.None));
        }


        [TestMethod]
        public void NbtWriterDeepTreeThrows() {
            NbtCompound deepTree = MakeNestedCompoundTree(MaxDepth + 1);
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(deepTree));
            }
        }


        [TestMethod]
        public void NbtWriterStreamingDepthCapped() {
            // The streaming writer must not allow deeper nesting than our reader (or Minecraft) can read.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                for (int i = 1; i < MaxDepth; i++) {
                    writer.BeginCompound("c");
                }
                Assert.Throws<NbtFormatException>(() => writer.BeginCompound("d"));
                Assert.Throws<NbtFormatException>(() => writer.BeginList("l", NbtTagType.Int, 0));

                // The refused calls wrote nothing, so a document at the cap still closes and reloads
                for (int i = 0; i < MaxDepth; i++) {
                    writer.EndCompound();
                }
                writer.Finish();
                byte[] doc = ms.ToArray();
                var file = new NbtFile();
                file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
                Assert.IsNotNull(file.RootTag.Get<NbtCompound>("c"));
            }
        }


        [TestMethod]
        public void CloningDeepTreeThrows() {
            NbtCompound okTree = MakeNestedCompoundTree(MaxDepth);
            var clone = (NbtCompound)okTree.Clone();
            Assert.IsNotNull(clone.Get<NbtCompound>("c"));

            NbtCompound deepTree = MakeNestedCompoundTree(MaxDepth + 1);
            Assert.Throws<NbtFormatException>(() => deepTree.Clone());
            Assert.Throws<NbtFormatException>(() => new NbtCompound(deepTree));
        }


        [TestMethod]
        public void CloningDeepListThrows() {
            // Same limit through the NbtList copy constructor
            var root = new NbtList("l", NbtTagType.List);
            NbtList current = root;
            for (int i = 1; i < MaxDepth; i++) {
                var child = new NbtList(NbtTagType.List);
                current.Add(child);
                current = child;
            }
            Assert.IsNotNull(root.Clone());

            current.Add(new NbtList(NbtTagType.List));
            Assert.Throws<NbtFormatException>(() => root.Clone());
            Assert.Throws<NbtFormatException>(() => new NbtList(root));
        }


        [TestMethod]
        public void PrintingDeepTreeThrows() {
            NbtCompound okTree = MakeNestedCompoundTree(MaxDepth);
            Assert.IsNotNull(okTree.ToString());

            NbtCompound deepTree = MakeNestedCompoundTree(MaxDepth + 1);
            Assert.Throws<NbtFormatException>(() => deepTree.ToString());
        }


        [TestMethod]
        public void PathOfDeepTagWorks() {
            // Path used to recurse through parents. It should handle any depth.
            var root = new NbtCompound("root");
            NbtCompound current = root;
            for (int i = 1; i < 6000; i++) {
                var child = new NbtCompound("c" + i);
                current.Add(child);
                current = child;
            }
            string path = current.Path;
            Assert.IsTrue(path.StartsWith("root.c1.c2.", StringComparison.Ordinal));
            Assert.IsTrue(path.EndsWith(".c5999", StringComparison.Ordinal));
        }
    }
}
