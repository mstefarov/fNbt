using System.IO;

namespace fNbt.Test {
    // Wire tolerances that exceed Minecraft's own readers on purpose: negative list/array lengths read as
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


        [TestMethod]
        public void NegativeListLengthReadsAsEmpty() {
            NbtCompound root = TestFiles.Load(MakeListDoc(0x01, -1)).RootTag;
            var list = root.Get<NbtList>("l");
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);

            // Extreme value must not overflow anything
            root = TestFiles.Load(MakeListDoc(0x01, int.MinValue)).RootTag;
            Assert.AreEqual(0, root.Get<NbtList>("l").Count);
        }


        [TestMethod]
        public void NegativeArrayLengthReadsAsEmpty() {
            NbtCompound root = TestFiles.Load(MakeArrayDoc(0x07, -5)).RootTag;
            Assert.AreEqual(0, root.Get<NbtByteArray>("a").Value.Length);

            root = TestFiles.Load(MakeArrayDoc(0x0B, -5)).RootTag;
            Assert.AreEqual(0, root.Get<NbtIntArray>("a").Value.Length);

            root = TestFiles.Load(MakeArrayDoc(0x0C, int.MinValue)).RootTag;
            Assert.AreEqual(0, root.Get<NbtLongArray>("a").Value.Length);
        }


        [TestMethod]
        public void EmptyListAcceptsAnyElementTypeByte() {
            // Type byte 0x63 is garbage, but the declared length is zero, so nothing needs it.
            // The garbage byte normalizes to End on load, so a resave is an ordinary document.
            NbtCompound root = TestFiles.Load(MakeListDoc(0x63, 0)).RootTag;
            var list = root.Get<NbtList>("l");
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(NbtTagType.End, list.ListType);

            // A negative length with a garbage type byte is doubly tolerated
            root = TestFiles.Load(MakeListDoc(0xFF, -3)).RootTag;
            list = root.Get<NbtList>("l");
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(NbtTagType.End, list.ListType);
        }


        [TestMethod]
        public void NonEmptyListWithInvalidTypeByteStillThrows() {
            byte[] doc = MakeListDoc(0x63, 1);
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(doc));

            // TAG_End elements with a positive count are also still malformed
            byte[] endDoc = MakeListDoc(0x00, 2);
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(endDoc));
        }


        [TestMethod]
        public void NbtReaderAppliesTheSameTolerances() {
            NbtCompound listRoot = (NbtCompound)TestFiles.OpenReader(MakeListDoc(0x63, -1)).ReadAsTag();
            Assert.AreEqual(0, listRoot.Get<NbtList>("l").Count);
            NbtCompound byteArrayRoot = (NbtCompound)TestFiles.OpenReader(MakeArrayDoc(0x07, -5)).ReadAsTag();
            Assert.AreEqual(0, byteArrayRoot.Get<NbtByteArray>("a").Value.Length);
            NbtCompound longArrayRoot = (NbtCompound)TestFiles.OpenReader(MakeArrayDoc(0x0C, -5)).ReadAsTag();
            Assert.AreEqual(0, longArrayRoot.Get<NbtLongArray>("a").Value.Length);
        }


        [TestMethod]
        public void SkippingToleratedTagsWorks() {
            // A selector that skips the tolerated list must not desync the stream
            byte[] doc = MakeListDoc(0x63, -1);
            NbtFile file = TestFiles.Load(doc, selector: tag => tag.Name != "l");
            Assert.IsNull(file.RootTag.Get<NbtList>("l"));
        }
    }
}
