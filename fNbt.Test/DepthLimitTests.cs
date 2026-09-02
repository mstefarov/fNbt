using System;
using System.IO;

namespace fNbt.Test {
    [TestClass]
    public class DepthLimitTests {
        const int MaxDepth = NbtTag.MaxDepth;


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
        public void ValidationDepthMatchesTheWriteWalk() {
            // A maximally deep tree the write walk accepts must also pass write validation,
            // leaves included: depth counts open containers, not every tag
            NbtCompound okTree = MakeNestedCompoundTree(MaxDepth);
            NbtCompound deepest = okTree;
            while (deepest.Get<NbtCompound>("c") != null) {
                deepest = deepest.Get<NbtCompound>("c")!;
            }
            deepest.Add(new NbtByte("leaf", 1));

            var codec = new NbtCodec(NbtFlavor.ClassiCube);
            byte[] doc = codec.WriteTag(okTree);
            Assert.IsTrue(doc.Length > 0);

            NbtCompound deepTree = MakeNestedCompoundTree(MaxDepth + 1);
            Assert.Throws<NbtFormatException>(() => codec.WriteTag(deepTree));
        }


        [TestMethod]
        public void ComparerDepthMatchesTheOtherTreeWalks() {
            // A scalar below 512 containers is one tag level deeper, but consumes no container budget.
            NbtCompound left = MakeNestedCompoundTree(MaxDepth);
            NbtCompound right = MakeNestedCompoundTree(MaxDepth);
            NbtCompound leftDeepest = left;
            NbtCompound rightDeepest = right;
            while (leftDeepest.Get<NbtCompound>("c") != null) {
                leftDeepest = leftDeepest.Get<NbtCompound>("c")!;
                rightDeepest = rightDeepest.Get<NbtCompound>("c")!;
            }
            leftDeepest.Add(new NbtByte("leaf", 1));
            rightDeepest.Add(new NbtByte("leaf", 1));
            Assert.IsTrue(NbtComparer.Instance.Equals(left, right));

            NbtCompound tooDeepLeft = MakeNestedCompoundTree(MaxDepth + 1);
            NbtCompound tooDeepRight = MakeNestedCompoundTree(MaxDepth + 1);
            Assert.Throws<ArgumentException>(() => NbtComparer.Instance.Equals(tooDeepLeft, tooDeepRight));
        }


        [TestMethod]
        public void NamedContainerDepthFailureWritesNoHeader() {
            using (var ms = new MemoryStream()) {
                var binaryWriter = new NbtBinaryWriter(ms, NbtFlavor.Java);
                Assert.Throws<NbtFormatException>(
                    () => new NbtCompound("c").WriteTag(binaryWriter, 0));
                Assert.AreEqual(0, ms.Length);

                Assert.Throws<NbtFormatException>(
                    () => new NbtList("l", NbtTagType.End).WriteTag(binaryWriter, 0));
                Assert.AreEqual(0, ms.Length);
            }
        }


        [TestMethod]
        public void LoadingDocAtDepthLimitSucceeds() {
            byte[] listDoc = MakeNestedListDoc(MaxDepth - 1);
            var file = new NbtFile();
            file.LoadFromBuffer(listDoc, 0, listDoc.Length, NbtCompression.None);
            Assert.IsNotNull(file.RootTag.Get<NbtList>("l"));

            byte[] compoundDoc = TestFiles.MakeNestedCompoundDoc(MaxDepth);
            file.LoadFromBuffer(compoundDoc, 0, compoundDoc.Length, NbtCompression.None);
            Assert.IsNotNull(file.RootTag.Get<NbtCompound>("c"));
        }


        [TestMethod]
        public void LoadingDocOverDepthLimitThrows() {
            byte[] listDoc = MakeNestedListDoc(MaxDepth);
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(listDoc, 0, listDoc.Length, NbtCompression.None));

            byte[] compoundDoc = TestFiles.MakeNestedCompoundDoc(MaxDepth + 1);
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
                // The budget counts containers, so a scalar payload at this point is still valid.
                writer.WriteByte("leaf", 1);

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
        public void NbtWriterCountsOpenContainersAgainstWrittenSubtree() {
            // The root is already open, so a 512-container subtree would put the document
            // one level over the cap. Restricted flavors preflight it without output.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.ClassiCube);
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(
                    () => writer.WriteTag(MakeNestedCompoundTree(MaxDepth)));
                Assert.AreEqual(before, ms.Length);

                writer.WriteTag(MakeNestedCompoundTree(MaxDepth - 1));
                writer.EndCompound();
                writer.Finish();
                byte[] doc = ms.ToArray();
                new NbtFile().LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
            }

            // The same accounting must include an arbitrary streamed prefix, not just the root.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.ClassiCube);
                for (int i = 0; i < 200; i++) {
                    writer.BeginCompound("c");
                }
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(
                    () => writer.WriteTag(MakeNestedCompoundTree(MaxDepth - 200)));
                Assert.AreEqual(before, ms.Length);

                writer.WriteTag(MakeNestedCompoundTree(MaxDepth - 201));
                for (int i = 0; i <= 200; i++) {
                    writer.EndCompound();
                }
                writer.Finish();
                byte[] doc = ms.ToArray();
                new NbtFile().LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
            }

            // Java avoids a second whole-tree walk. Its recursive write detects the same limit,
            // and the partial output leaves the writer permanently failed.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(
                    () => writer.WriteTag(MakeNestedCompoundTree(MaxDepth)));
                Assert.IsTrue(ms.Length > before);
                Assert.Throws<NbtFormatException>(writer.EndCompound);
                Assert.Throws<NbtFormatException>(writer.Finish);
            }
        }


        [TestMethod]
        public void NbtWriterDepthRefusalDoesNotConsumeListSlot() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                for (int i = 2; i < MaxDepth; i++) {
                    writer.BeginCompound("c");
                }
                // The list is the 512th open container, leaving no room for its compound element
                writer.BeginList("l", NbtTagType.Compound, 1);
                Assert.Throws<NbtFormatException>(() => writer.BeginCompound());
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtCompound()));
                // The refusals must not poison the writer; the open slot is what blocks EndList
                Assert.IsFalse(writer.IsInErrorState);
                Assert.Throws<NbtFormatException>(() => writer.EndList());
            }
        }


        [TestMethod]
        public void NbtWriterContainerRefusedAtLimitLeavesWriterUsable() {
            // A container tag handed to WriteTag at the limit is refused like BeginCompound is:
            // before the emission window, with nothing written and the writer still usable
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                for (int i = 1; i < MaxDepth; i++) {
                    writer.BeginCompound("c");
                }
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtCompound("x")));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtList("x", NbtTagType.Int)));
                Assert.AreEqual(before, ms.Length);
                writer.WriteByte("leaf", 1);
                for (int i = 0; i < MaxDepth; i++) {
                    writer.EndCompound();
                }
                writer.Finish();
                byte[] doc = ms.ToArray();
                new NbtFile().LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
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
