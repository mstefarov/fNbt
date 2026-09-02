using System;
using System.IO;

namespace fNbt.Test {
    // Flavors and NbtOptions handed to the three named-root entry points (NbtFile, NbtReader,
    // NbtWriter), and the legacy BigEndian getters reflecting them.
    [TestClass]
    public class EntryPointOptionsTests {
        static NbtCompound MakeSampleRoot(string name) {
            return new NbtCompound(name) {
                new NbtInt("id", 42),
                new NbtString("motd", "Hello, world!")
            };
        }


        [TestMethod]
        public void NbtFileFlavorRoundTrip() {
            NbtCompound root = MakeSampleRoot("hello");
            var file = new NbtFile(root, NbtFlavor.Bedrock);
            byte[] doc = file.SaveToBuffer(NbtCompression.None);

            // Bytes match the codec's little-endian output exactly
            CollectionAssert.AreEqual(NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root), doc);

            NbtFile reloaded = TestFiles.Load(doc, NbtFlavor.Bedrock);
            NbtAssert.AreEqual(root, reloaded.RootTag);
        }


        [TestMethod]
        public void ObsoleteBigEndianReflectsFlavor() {
#pragma warning disable 618 // testing the obsolete surface on purpose
            Assert.IsTrue(new NbtFile().BigEndian);
            Assert.IsFalse(new NbtFile(NbtFlavor.Bedrock).BigEndian);
            Assert.IsTrue(new NbtFile(NbtFlavor.ClassiCube).BigEndian);
#pragma warning restore 618
        }


        [TestMethod]
        public void FileApisRejectUnnamedRootFlavors() {
            // JavaNetwork has no root name, which the named-root APIs cannot express
            Assert.Throws<ArgumentException>(() => new NbtFile(NbtFlavor.JavaNetwork));
            Assert.Throws<ArgumentException>(
                () => new NbtFile(new NbtOptions { Flavor = NbtFlavor.JavaNetwork }));

            using (var ms = new MemoryStream(new byte[] { 0x0A })) {
                Assert.Throws<ArgumentException>(() => new NbtReader(ms, NbtFlavor.JavaNetwork));
            }
            using (var ms = new MemoryStream()) {
                Assert.Throws<ArgumentException>(() => new NbtWriter(ms, "r", NbtFlavor.JavaNetwork));
            }
        }


        [TestMethod]
        public void EntryPointsExposeTheirFlavor() {
            Assert.AreSame(NbtFlavor.Bedrock, NbtCodec.For(NbtFlavor.Bedrock).Flavor);
            Assert.AreSame(NbtFlavor.Bedrock, TestFiles.OpenReader(new byte[] { 0x0A }, NbtFlavor.Bedrock).Flavor);
            using (var ms = new MemoryStream()) {
                Assert.AreSame(NbtFlavor.Bedrock, new NbtWriter(ms, "r", NbtFlavor.Bedrock).Flavor);
                Assert.AreSame(NbtFlavor.Java, new NbtWriter(ms, "r").Flavor);
            }
        }


        [TestMethod]
        public void ReadRootTagNameWorksOnEveryFileFlavor() {
            NbtFlavor[] flavors = {
                NbtFlavor.Java, NbtFlavor.JavaAnvil, NbtFlavor.JavaLegacy,
                NbtFlavor.Bedrock, NbtFlavor.BedrockNetwork, NbtFlavor.ClassiCube
            };
            foreach (NbtFlavor flavor in flavors) {
                byte[] doc = new NbtFile(new NbtCompound("rootName"), flavor)
                    .SaveToBuffer(NbtCompression.None);
                using (var ms = new MemoryStream(doc)) {
                    Assert.AreEqual("rootName",
                                    NbtFile.ReadRootTagName(ms, NbtCompression.None, flavor),
                                    flavor.Name);
                }
            }
        }


        [TestMethod]
        public void ReadRootTagNameBoundsHostileNameLengths() {
            // A 5-byte document declaring a 256 MB root name must fail with a format error,
            // not attempt the allocation
            byte[] doc = { 0x0A, 0xFF, 0xFF, 0xFF, 0x7F };
            using (var ms = new MemoryStream(doc)) {
                Assert.Throws<NbtFormatException>(
                    () => NbtFile.ReadRootTagName(ms, NbtCompression.None, NbtFlavor.BedrockNetwork));
            }

            // Same for a length past int.MaxValue, which must not surface as an overflow
            byte[] overflow = { 0x0A, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };
            using (var ms = new MemoryStream(overflow)) {
                Assert.Throws<NbtFormatException>(
                    () => NbtFile.ReadRootTagName(ms, NbtCompression.None, NbtFlavor.BedrockNetwork));
            }
        }


        [TestMethod]
        public void ReadRootTagNameTakesFlavor() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] doc = new NbtFile(root, NbtFlavor.Bedrock).SaveToBuffer(NbtCompression.None);
            using (var ms = new MemoryStream(doc)) {
                Assert.AreEqual("hello", NbtFile.ReadRootTagName(ms, NbtCompression.None, NbtFlavor.Bedrock));
            }
            Assert.Throws<ArgumentNullException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(doc), NbtCompression.None, (NbtFlavor)null));
            Assert.Throws<ArgumentException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(doc), NbtCompression.None, NbtFlavor.JavaNetwork));

            // The obsolete bool overload still maps correctly
#pragma warning disable 618
            using (var ms = new MemoryStream(doc)) {
                Assert.AreEqual("hello", NbtFile.ReadRootTagName(ms, NbtCompression.None, false, 0));
            }
#pragma warning restore 618
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
        public void NbtFileReadOptionsEnforceLimits() {
            // MaxAllocation on load
            var bigRoot = new NbtCompound("r") { new NbtByteArray("blob", new byte[200_000]) };
            byte[] bigDoc = new NbtFile(bigRoot).SaveToBuffer(NbtCompression.None);

            Assert.Throws<NbtFormatException>(
                () => TestFiles.Load(bigDoc, new NbtOptions { MaxAllocation = 65536 }));

            // Read validation on load
            var longArrayRoot = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };
            byte[] laDoc = new NbtFile(longArrayRoot).SaveToBuffer(NbtCompression.None);

            TestFiles.Load(laDoc, new NbtOptions { Flavor = NbtFlavor.JavaLegacy });

            Assert.Throws<NbtFormatException>(
                () => TestFiles.Load(laDoc, new NbtOptions {
                    Flavor = NbtFlavor.JavaLegacy,
                    ValidateOnRead = true
                }));
        }


        [TestMethod]
        public void NbtReaderFlavorParsesLittleEndian() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] doc = NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root);

            NbtTag read = TestFiles.OpenReader(doc, NbtFlavor.Bedrock).ReadAsTag();
            NbtAssert.AreEqual(root, read);

            // The obsolete bool ctor still maps correctly
#pragma warning disable 618
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms, false);
                NbtAssert.AreEqual(root, reader.ReadAsTag());
            }
#pragma warning restore 618
        }


        [TestMethod]
        public void NbtReaderOptionsEnforceLimits() {
            var bigRoot = new NbtCompound("r") { new NbtByteArray("blob", new byte[200_000]) };
            byte[] doc = NbtCodec.For(NbtFlavor.Java).WriteTag(bigRoot);

            NbtReader capped = TestFiles.OpenReader(doc, new NbtOptions { MaxAllocation = 65536 });
            Assert.Throws<NbtFormatException>(() => capped.ReadAsTag());

            var laRoot = new NbtCompound("r") { new NbtLongArray("longs", new long[] { 1 }) };
            byte[] laDoc = NbtCodec.For(NbtFlavor.Java).WriteTag(laRoot);
            NbtReader strict = TestFiles.OpenReader(laDoc, new NbtOptions {
                Flavor = NbtFlavor.JavaLegacy,
                ValidateOnRead = true
            });
            Assert.Throws<NbtFormatException>(() => strict.ReadAsTag());
        }


        [TestMethod]
        public void NbtWriterFlavorWritesLittleEndian() {
            NbtCompound root = MakeSampleRoot("hello");
            byte[] expected = NbtCodec.For(NbtFlavor.Bedrock).WriteTag(root);

            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "hello", NbtFlavor.Bedrock);
                writer.WriteInt("id", 42);
                writer.WriteString("motd", "Hello, world!");
                writer.EndCompound();
                writer.Finish();
                CollectionAssert.AreEqual(expected, ms.ToArray());
            }

            // The obsolete bool ctor still maps correctly
#pragma warning disable 618
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "hello", false);
                writer.WriteInt("id", 42);
                writer.WriteString("motd", "Hello, world!");
                writer.EndCompound();
                writer.Finish();
                CollectionAssert.AreEqual(expected, ms.ToArray());
            }
#pragma warning restore 618
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
