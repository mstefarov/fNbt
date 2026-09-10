using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public class NbtFlavorTests {
        static NbtFlavor[] AllFlavors() {
            return new[] {
                NbtFlavor.Java, NbtFlavor.JavaAnvil, NbtFlavor.JavaLegacy, NbtFlavor.JavaNetwork,
                NbtFlavor.Bedrock, NbtFlavor.BedrockNetwork, NbtFlavor.ClassiCube
            };
        }


        [TestMethod]
        public void SingletonsAreDistinctAndNamed() {
            NbtFlavor[] all = AllFlavors();
            Assert.AreEqual(all.Length, all.Distinct().Count());
            foreach (NbtFlavor flavor in all) {
                Assert.AreEqual(flavor.Name, flavor.ToString());
            }
            Assert.AreEqual("Java", NbtFlavor.Java.Name);
            Assert.AreEqual("JavaAnvil", NbtFlavor.JavaAnvil.Name);
            Assert.AreEqual("JavaLegacy", NbtFlavor.JavaLegacy.Name);
            Assert.AreEqual("JavaNetwork", NbtFlavor.JavaNetwork.Name);
            Assert.AreEqual("Bedrock", NbtFlavor.Bedrock.Name);
            Assert.AreEqual("BedrockNetwork", NbtFlavor.BedrockNetwork.Name);
            Assert.AreEqual("ClassiCube", NbtFlavor.ClassiCube.Name);
        }


        [TestMethod]
        public void FlavorsCarryFormatData() {
            (NbtFlavor Flavor, bool BigEndian, bool ModifiedUtf8, NbtTagType MaxTagType, int MaxStringBytes)[] cases = {
                (NbtFlavor.Java, true, true, NbtTagType.LongArray, 65535),
                (NbtFlavor.JavaAnvil, true, true, NbtTagType.IntArray, 65535),
                (NbtFlavor.JavaLegacy, true, true, NbtTagType.Compound, 65535),
                (NbtFlavor.JavaNetwork, true, true, NbtTagType.LongArray, 65535),
                (NbtFlavor.Bedrock, false, false, NbtTagType.IntArray, 32767),
                (NbtFlavor.BedrockNetwork, false, false, NbtTagType.IntArray, int.MaxValue),
                (NbtFlavor.ClassiCube, true, true, NbtTagType.Compound, 256)
            };
            foreach ((NbtFlavor flavor, bool bigEndian, bool modifiedUtf8, NbtTagType maxTagType, int maxStringBytes) in cases) {
                Assert.AreEqual(bigEndian, flavor.BigEndian, flavor.Name);
                Assert.AreEqual(modifiedUtf8, flavor.UsesModifiedUtf8, flavor.Name);
                Assert.AreEqual(maxTagType, flavor.MaxTagType, flavor.Name);
                Assert.AreEqual(maxStringBytes, flavor.MaxStringBytes, flavor.Name);

                bool isJavaNetwork = ReferenceEquals(flavor, NbtFlavor.JavaNetwork);
                Assert.AreEqual(!isJavaNetwork, flavor.HasRootName, flavor.Name);
                Assert.AreEqual(isJavaNetwork, flavor.AllowsNonCompoundRoot, flavor.Name);
                Assert.AreEqual(ReferenceEquals(flavor, NbtFlavor.BedrockNetwork), flavor.UsesVarInts, flavor.Name);
            }
        }
    }
}
