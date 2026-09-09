using System;

namespace fNbt.Test {
    // NbtOptions on NbtCodec: MaxAllocation, invalid construction, and the snapshot taken
    // at construction.
    [TestClass]
    public class NbtCodecOptionsTests {
        [TestMethod]
        public void MaxAllocationCapsArrayAllocations() {
            var root = new NbtCompound("r") { new NbtByteArray("blob", new byte[200_000]) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            // Without the cap, the same document loads fine
            var openCodec = new NbtCodec(NbtFlavor.Java);
            NbtAssert.AreEqual(root, openCodec.ReadTag(doc, 0, doc.Length, out _));

            var cappedCodec = new NbtCodec(new NbtOptions { MaxAllocation = 65536 });
            Assert.Throws<NbtFormatException>(() => cappedCodec.ReadTag(doc, 0, doc.Length, out _));

            // Int arrays count element size: 20k elements = 80 KB > 64 KB
            var intRoot = new NbtCompound("r") { new NbtIntArray("ints", new int[20_000]) };
            byte[] intDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(intRoot);
            Assert.Throws<NbtFormatException>(() => cappedCodec.ReadTag(intDoc, 0, intDoc.Length, out _));
        }


        [TestMethod]
        public void MaxAllocationCapsListElementArrays() {
            // A list's element references are one allocation driven by the declared length, so
            // the cap covers them the way it covers an array payload. The element objects are
            // not counted, so the cap only has to fit the pointers.
            var list = new NbtList("l", NbtTagType.Byte);
            for (int i = 0; i < 20_000; i++) list.Add(new NbtByte(1));
            var root = new NbtCompound("r") { list };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);
            long pointers = 20_000L * IntPtr.Size;

            var fitting = new NbtCodec(new NbtOptions { MaxAllocation = pointers });
            NbtAssert.AreEqual(root, fitting.ReadTag(doc, 0, doc.Length, out _));
            var tooSmall = new NbtCodec(new NbtOptions { MaxAllocation = pointers - 1 });
            Assert.Throws<NbtFormatException>(() => tooSmall.ReadTag(doc, 0, doc.Length, out _));

            // Lists of containers hold references the same way
            var compounds = new NbtList("c", NbtTagType.Compound);
            for (int i = 0; i < 100; i++) compounds.Add(new NbtCompound());
            byte[] compoundDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(new NbtCompound("r") { compounds });
            var tooSmallForCompounds = new NbtCodec(new NbtOptions { MaxAllocation = 100L * IntPtr.Size - 1 });
            Assert.Throws<NbtFormatException>(() => tooSmallForCompounds.ReadTag(compoundDoc, 0, compoundDoc.Length, out _));
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
            NbtAssert.AreEqual(smallRoot, cappedCodec.ReadTag(smallDoc, 0, smallDoc.Length, out _));
        }


        [TestMethod]
        public void InvalidConstructionThrows() {
            Assert.Throws<ArgumentNullException>(() => new NbtCodec((NbtFlavor)null));
            Assert.Throws<ArgumentNullException>(() => new NbtCodec((NbtOptions)null));
            Assert.Throws<ArgumentNullException>(() => new NbtCodec(new NbtOptions { Flavor = null }));
        }


        [TestMethod]
        public void OptionsAreSnapshottedAtConstruction() {
            var options = new NbtOptions(NbtFlavor.ClassiCube);
            var codec = new NbtCodec(options);
            options.Flavor = NbtFlavor.Java;
            options.ValidateOnWrite = false;

            // The codec keeps ClassiCube rules despite the later mutation
            var over = new NbtCompound("r") { new NbtString("s", new string('x', 300)) };
            Assert.Throws<NbtFormatException>(() => codec.WriteTag(over));
        }
    }
}
