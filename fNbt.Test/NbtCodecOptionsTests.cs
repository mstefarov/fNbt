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
            Assert.Throws<ArgumentOutOfRangeException>(() => new NbtCodec(new NbtOptions { MaxAllocation = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NbtCodec(new NbtOptions { MaxAllocation = -5 }));
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
