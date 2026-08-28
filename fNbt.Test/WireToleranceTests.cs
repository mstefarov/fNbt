using System;
using System.IO;

namespace fNbt.Test {
    // Wire tolerances that exceed vanilla on purpose: negative list/array lengths read as
    // empty, and an empty list accepts any element-type byte. Always on, not a toggle.
    [TestClass]
    public class WireToleranceTests {
        // TAG_Compound "" { TAG_List "l": <elementType> <length> } TAG_End
        static byte[] MakeListDoc(byte elementType, int length) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 0);
                ms.WriteByte(0x09);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'l');
                ms.WriteByte(elementType);
                TestFiles.WriteBEInt(ms, length);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }


        // TAG_Compound "" { <arrayTagType> "a": <length> } TAG_End
        static byte[] MakeArrayDoc(byte arrayTagType, int length) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 0);
                ms.WriteByte(arrayTagType);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'a');
                TestFiles.WriteBEInt(ms, length);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }


        static NbtCompound Load(byte[] doc) {
            var file = new NbtFile();
            file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
            return file.RootTag;
        }


        [TestMethod]
        public void NegativeListLengthReadsAsEmpty() {
            NbtCompound root = Load(MakeListDoc(0x01, -1));
            var list = root.Get<NbtList>("l");
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);

            // Extreme value must not overflow anything
            root = Load(MakeListDoc(0x01, int.MinValue));
            Assert.AreEqual(0, root.Get<NbtList>("l").Count);
        }


        [TestMethod]
        public void NegativeArrayLengthReadsAsEmpty() {
            NbtCompound root = Load(MakeArrayDoc(0x07, -5));
            Assert.AreEqual(0, root.Get<NbtByteArray>("a").Value.Length);

            root = Load(MakeArrayDoc(0x0B, -5));
            Assert.AreEqual(0, root.Get<NbtIntArray>("a").Value.Length);

            root = Load(MakeArrayDoc(0x0C, int.MinValue));
            Assert.AreEqual(0, root.Get<NbtLongArray>("a").Value.Length);
        }


        [TestMethod]
        public void EmptyListAcceptsAnyElementTypeByte() {
            // Type byte 0x63 is garbage, but the declared length is zero, so nothing needs it
            NbtCompound root = Load(MakeListDoc(0x63, 0));
            var list = root.Get<NbtList>("l");
            Assert.AreEqual(0, list.Count);

            // A negative length with a garbage type byte is doubly tolerated
            root = Load(MakeListDoc(0xFF, -3));
            Assert.AreEqual(0, root.Get<NbtList>("l").Count);

            // The tolerated list saves cleanly as an ordinary empty End-typed list
            byte[] resaved = new NbtFile(root).SaveToBuffer(NbtCompression.None);
            Assert.AreEqual(0, Load(resaved).Get<NbtList>("l").Count);
        }


        [TestMethod]
        public void NonEmptyListWithInvalidTypeByteStillThrows() {
            byte[] doc = MakeListDoc(0x63, 1);
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));

            // TAG_End elements with a positive count are also still malformed
            byte[] endDoc = MakeListDoc(0x00, 2);
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(endDoc, 0, endDoc.Length, NbtCompression.None));
        }


        [TestMethod]
        public void NbtReaderAppliesTheSameTolerances() {
            using (var ms = new MemoryStream(MakeListDoc(0x63, -1))) {
                var root = (NbtCompound)new NbtReader(ms).ReadAsTag();
                Assert.AreEqual(0, root.Get<NbtList>("l").Count);
            }
            using (var ms = new MemoryStream(MakeArrayDoc(0x07, -5))) {
                var root = (NbtCompound)new NbtReader(ms).ReadAsTag();
                Assert.AreEqual(0, root.Get<NbtByteArray>("a").Value.Length);
            }
            using (var ms = new MemoryStream(MakeArrayDoc(0x0C, -5))) {
                var root = (NbtCompound)new NbtReader(ms).ReadAsTag();
                Assert.AreEqual(0, root.Get<NbtLongArray>("a").Value.Length);
            }
        }


        [TestMethod]
        public void SkippingToleratedTagsWorks() {
            // A selector that skips the tolerated list must not desync the stream
            byte[] doc = MakeListDoc(0x63, -1);
            var file = new NbtFile();
            file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "l");
            Assert.IsNull(file.RootTag.Get<NbtList>("l"));
        }
    }
}
