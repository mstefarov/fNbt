using System;
using System.IO;

namespace fNbt.Test {
    // The process-wide NbtOptions defaults: setter validation, entry points and new options
    // copying the current values, and the policy-keyed NbtCodec.For cache.
    // The defaults are process-wide, so a cleanup restores them after every test and the
    // class never runs in parallel.
    [TestClass]
    [DoNotParallelize]
    public class OptionsDefaultsTests {
        [TestCleanup]
        public void RestoreDefaults() {
            NbtOptions.DefaultFlavor = NbtFlavor.Java;
            NbtOptions.DefaultValidateOnRead = false;
            NbtOptions.DefaultValidateOnWrite = true;
            NbtOptions.DefaultMaxAllocation = long.MaxValue;
        }


        [TestMethod]
        public void StaticDefaultsInitialValues() {
            Assert.AreSame(NbtFlavor.Java, NbtOptions.DefaultFlavor);
            Assert.IsFalse(NbtOptions.DefaultValidateOnRead);
            Assert.IsTrue(NbtOptions.DefaultValidateOnWrite);
            Assert.AreEqual(long.MaxValue, NbtOptions.DefaultMaxAllocation);
        }


        [TestMethod]
        public void DefaultFlavorSetterValidatesEagerly() {
            Assert.Throws<ArgumentNullException>(() => NbtOptions.DefaultFlavor = null);
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => NbtOptions.DefaultFlavor = NbtFlavor.JavaNetwork);
            // The message must hand the user the migration
            StringAssert.Contains(ex.Message, "NbtCodec");
            StringAssert.Contains(ex.Message, "JavaNetwork");
            Assert.AreSame(NbtFlavor.Java, NbtOptions.DefaultFlavor);
        }


        [TestMethod]
        public void DefaultMaxAllocationSetterValidatesEagerly() {
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtOptions.DefaultMaxAllocation = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtOptions.DefaultMaxAllocation = -1);
            Assert.AreEqual(long.MaxValue, NbtOptions.DefaultMaxAllocation);
        }


        [TestMethod]
        public void NewOptionsCopyCurrentDefaults() {
            NbtOptions.DefaultFlavor = NbtFlavor.Bedrock;
            NbtOptions.DefaultValidateOnRead = true;
            NbtOptions.DefaultValidateOnWrite = false;
            NbtOptions.DefaultMaxAllocation = 12345;

            var options = new NbtOptions();
            Assert.AreSame(NbtFlavor.Bedrock, options.Flavor);
            Assert.IsTrue(options.ValidateOnRead);
            Assert.IsFalse(options.ValidateOnWrite);
            Assert.AreEqual(12345, options.MaxAllocation);

            // The flavor constructor copies only the policy defaults
            var flavored = new NbtOptions(NbtFlavor.ClassiCube);
            Assert.AreSame(NbtFlavor.ClassiCube, flavored.Flavor);
            Assert.IsTrue(flavored.ValidateOnRead);
            Assert.IsFalse(flavored.ValidateOnWrite);
            Assert.AreEqual(12345, flavored.MaxAllocation);

            // Later default changes do not reach existing instances
            RestoreDefaults();
            Assert.AreSame(NbtFlavor.Bedrock, options.Flavor);
            Assert.IsTrue(options.ValidateOnRead);
            Assert.IsFalse(options.ValidateOnWrite);
            Assert.AreEqual(12345, options.MaxAllocation);
        }


        [TestMethod]
        public void FlavorlessEntryPointsUseDefaultFlavor() {
            NbtOptions.DefaultFlavor = NbtFlavor.Bedrock;
            Assert.AreSame(NbtFlavor.Bedrock, new NbtFile().Flavor);
            Assert.AreSame(NbtFlavor.Bedrock, TestFiles.OpenReader(new byte[] { 0x0A }).Flavor);
            using (var ms = new MemoryStream()) {
                Assert.AreSame(NbtFlavor.Bedrock, new NbtWriter(ms, "r").Flavor);
            }
        }


        [TestMethod]
        public void EntryPointsSnapshotDefaultsAtConstruction() {
            var file = new NbtFile();
            NbtOptions.DefaultFlavor = NbtFlavor.Bedrock;
            Assert.AreSame(NbtFlavor.Java, file.Flavor);
        }


        [TestMethod]
        public void FlavorEntryPointsUseCurrentPolicyDefaults() {
            // Write validation: ClassiCube normally refuses TAG_Int_Array
            NbtOptions.DefaultValidateOnWrite = false;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                writer.WriteIntArray("ints", new int[] { 1 });
                writer.EndCompound();
                writer.Finish();
            }

            // Allocation cap through the flavor overload
            var bigRoot = new NbtCompound("r") { new NbtByteArray("blob", new byte[200_000]) };
            byte[] bigDoc = new NbtFile(bigRoot, NbtFlavor.Java).SaveToBuffer(NbtCompression.None);
            NbtOptions.DefaultMaxAllocation = 65536;
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(bigDoc, NbtFlavor.Java));
        }


        [TestMethod]
        public void ObsoleteBoolCtorsUseCurrentPolicyDefaults() {
            // The bool ctors chain through the flavor ctors, so ambient policy reaches them
#pragma warning disable 618
            NbtOptions.DefaultValidateOnWrite = false;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", false);
                // Bedrock normally refuses TAG_Long_Array
                writer.WriteLongArray("longs", new long[] { 1 });
                writer.EndCompound();
                writer.Finish();
            }
#pragma warning restore 618
        }


        [TestMethod]
        public void CodecForIsKeyedByPolicyDefaultsOnly() {
            NbtCodec before = NbtCodec.For(NbtFlavor.Bedrock);

            // A flavor-default change must not invalidate an explicit-flavor codec
            NbtOptions.DefaultFlavor = NbtFlavor.ClassiCube;
            Assert.AreSame(before, NbtCodec.For(NbtFlavor.Bedrock));

            // A policy change makes later calls return a fresh instance
            NbtOptions.DefaultValidateOnWrite = false;
            NbtCodec after = NbtCodec.For(NbtFlavor.Bedrock);
            Assert.AreNotSame(before, after);

            // The returned codecs themselves never change
            var tree = new NbtCompound("c") { new NbtLongArray("longs", new long[] { 1 }) };
            Assert.Throws<NbtFormatException>(() => before.WriteTag(tree));
            Assert.IsTrue(after.WriteTag(tree).Length > 0);

            // A same-value set keeps the cached instance
            NbtOptions.DefaultValidateOnWrite = false;
            Assert.AreSame(after, NbtCodec.For(NbtFlavor.Bedrock));
        }


        [TestMethod]
        public void ReadRootTagNameNoFlavorOverloadUsesDefaultFlavor() {
            byte[] doc = new NbtFile(new NbtCompound("rootName"), NbtFlavor.Bedrock)
                .SaveToBuffer(NbtCompression.None);
            string path = Path.Combine(Path.GetTempPath(), "fNbt-defaults-test.nbt");
            try {
                File.WriteAllBytes(path, doc);
                NbtOptions.DefaultFlavor = NbtFlavor.Bedrock;
                Assert.AreEqual("rootName", NbtFile.ReadRootTagName(path));
            } finally {
                File.Delete(path);
            }
        }


        [TestMethod]
        public void NewOptionsCarryInitialDefaults() {
            var options = new NbtOptions();
            Assert.AreSame(NbtFlavor.Java, options.Flavor);
            Assert.IsFalse(options.ValidateOnRead);
            Assert.IsTrue(options.ValidateOnWrite);
            Assert.AreEqual(long.MaxValue, options.MaxAllocation);

            // The flavor constructor keeps every other default
            var flavored = new NbtOptions(NbtFlavor.Bedrock);
            Assert.AreSame(NbtFlavor.Bedrock, flavored.Flavor);
            Assert.IsFalse(flavored.ValidateOnRead);
            Assert.IsTrue(flavored.ValidateOnWrite);
            Assert.AreEqual(long.MaxValue, flavored.MaxAllocation);
            Assert.Throws<ArgumentNullException>(() => new NbtOptions(null));
        }


        [TestMethod]
        public void ObsoleteBigEndianByDefaultReflectsDefaultFlavor() {
#pragma warning disable 618
            NbtOptions.DefaultFlavor = NbtFlavor.Bedrock;
            Assert.IsFalse(NbtFile.BigEndianByDefault);
            NbtOptions.DefaultFlavor = NbtFlavor.Java;
            Assert.IsTrue(NbtFile.BigEndianByDefault);
#pragma warning restore 618
        }


        [TestMethod]
        public void InstanceMaxAllocationValidatedAtUse() {
            // Instance setters are unchecked; the entry point's resolve rejects the value
            using (var ms = new MemoryStream()) {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new NbtWriter(ms, "r", new NbtOptions { MaxAllocation = 0 }));
            }
        }
    }
}
