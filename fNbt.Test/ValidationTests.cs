using System;
using System.IO;

namespace fNbt.Test {
    // Flavor conformance behind ValidateOnWrite and ValidateOnRead, checked on each entry
    // point that takes the toggles: NbtCodec, NbtFile and NbtWriter.
    [TestClass]
    public class ValidationTests {
        [TestMethod]
        public void WriteValidationRejectsDisallowedTagTypes() {
            // ClassiCube's reader stops at tag 10, Bedrock's at 11
            var intArrayRoot = new NbtCompound("r") { new NbtIntArray("ints", new int[] { 1 }) };
            var longArrayRoot = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };

            Assert.Throws<NbtFormatException>(
                () => new NbtCodec(NbtFlavor.ClassiCube).WriteTag(intArrayRoot));
            Assert.Throws<NbtFormatException>(
                () => new NbtCodec(NbtFlavor.Bedrock).WriteTag(longArrayRoot));

            // Java permits everything; Bedrock permits int arrays
            new NbtCodec(NbtFlavor.Java).WriteTag(longArrayRoot);
            new NbtCodec(NbtFlavor.Bedrock).WriteTag(intArrayRoot);
        }


        [TestMethod]
        public void WriteValidationRejectsOverlongStrings() {
            var longValue = new NbtCompound("r") { new NbtString("s", new string('x', 300)) };
            var longName = new NbtCompound("r") { new NbtInt(new string('n', 300), 1) };

            var ccCodec = new NbtCodec(NbtFlavor.ClassiCube);
            Assert.Throws<NbtFormatException>(() => ccCodec.WriteTag(longValue));
            Assert.Throws<NbtFormatException>(() => ccCodec.WriteTag(longName));

            // 40,000 bytes: over Bedrock's 32,767, under Java's 65,535
            var overBedrock = new NbtCompound("r") { new NbtString("s", new string('x', 40_000)) };
            Assert.Throws<NbtFormatException>(() => new NbtCodec(NbtFlavor.Bedrock).WriteTag(overBedrock));
            new NbtCodec(NbtFlavor.Java).WriteTag(overBedrock);
        }


        [TestMethod]
        public void WriteValidationCanBeDisabled() {
            var root = new NbtCompound("r") { new NbtIntArray("ints", new int[] { 1, 2 }) };
            var codec = new NbtCodec(new NbtOptions {
                Flavor = NbtFlavor.ClassiCube,
                ValidateOnWrite = false
            });
            byte[] doc = codec.WriteTag(root);

            // Reads are generous regardless, so the same flavor loads it back
            NbtAssert.AreEqual(root, codec.ReadTag(doc, 0, doc.Length, out _));
        }


        [TestMethod]
        public void ReadValidationRejectsDisallowedContent() {
            var root = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(root);

            // Default: generous
            new NbtCodec(NbtFlavor.JavaLegacy).ReadTag(doc, 0, doc.Length, out _);

            // Opted in: the flavor's tag ceiling is enforced
            var strict = new NbtCodec(new NbtOptions { Flavor = NbtFlavor.JavaLegacy, ValidateOnRead = true });
            Assert.Throws<NbtFormatException>(() => strict.ReadTag(doc, 0, doc.Length, out _));

            // String ceilings too: 33,000 bytes is over Bedrock's 32,767
            var longString = new NbtCompound("r") { new NbtString("s", new string('x', 33_000)) };
            byte[] leDoc = new NbtCodec(new NbtOptions {
                Flavor = NbtFlavor.Bedrock, ValidateOnWrite = false
            }).WriteTag(longString);

            new NbtCodec(NbtFlavor.Bedrock).ReadTag(leDoc, 0, leDoc.Length, out _);
            var strictBedrock = new NbtCodec(new NbtOptions { Flavor = NbtFlavor.Bedrock, ValidateOnRead = true });
            Assert.Throws<NbtFormatException>(() => strictBedrock.ReadTag(leDoc, 0, leDoc.Length, out _));
        }


        [TestMethod]
        public void WriteValidationChecksEmptyListElementTypes() {
            // The element type is written even when no elements follow it
            var root = new NbtCompound("r") { new NbtList("l", NbtTagType.LongArray) };
            Assert.Throws<NbtFormatException>(() => new NbtCodec(NbtFlavor.JavaLegacy).WriteTag(root));

            // An unset element type gets the list's own message, not an empty type name
            var unset = new NbtCompound("r") { new NbtList("l") };
            NbtFormatException ex = Assert.Throws<NbtFormatException>(
                () => new NbtCodec(NbtFlavor.JavaLegacy).WriteTag(unset));
            StringAssert.Contains(ex.Message, "Unknown ListType");
            new NbtCodec(NbtFlavor.Java).WriteTag(root);
            new NbtCodec(new NbtOptions { Flavor = NbtFlavor.JavaLegacy, ValidateOnWrite = false }).WriteTag(root);

            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.Bedrock);
                Assert.Throws<NbtFormatException>(() => writer.BeginList("l", NbtTagType.LongArray, 0));
            }
        }


        [TestMethod]
        public void NbtFileWriteValidationEnforcesFlavor() {
            var root = new NbtCompound("r") { new NbtIntArray("ints", new int[] { 1 }) };

            var strict = new NbtFile(root, new NbtOptions { Flavor = NbtFlavor.ClassiCube });
            Assert.Throws<NbtFormatException>(() => strict.SaveToBuffer(NbtCompression.None));

            var lenient = new NbtFile(root, new NbtOptions {
                Flavor = NbtFlavor.ClassiCube,
                ValidateOnWrite = false
            });
            byte[] doc = lenient.SaveToBuffer(NbtCompression.None);
            Assert.IsTrue(doc.Length > 0);
        }


        [TestMethod]
        public void NbtWriterValidationEnforcesFlavor() {
            // Tag types, per call
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                Assert.Throws<NbtFormatException>(() => writer.WriteIntArray("ints", new int[] { 1 }));
            }

            // Strings, through the binary writer's ceiling
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                Assert.Throws<NbtFormatException>(() => writer.WriteString("s", new string('x', 300)));
            }

            // Whole subtrees written via WriteTag get the same pre-walk as NbtFile
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.Bedrock);
                var tree = new NbtCompound("c") { new NbtLongArray("longs", new long[] { 1 }) };
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(tree));
            }

            // Validation off: same writes succeed
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", new NbtOptions {
                    Flavor = NbtFlavor.ClassiCube,
                    ValidateOnWrite = false
                });
                writer.WriteIntArray("ints", new int[] { 1 });
                writer.WriteString("s", new string('x', 300));
                writer.EndCompound();
                writer.Finish();
            }
        }
    }
}
