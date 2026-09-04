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
        public void EndiannessMatchesFormatMatrix() {
            Assert.IsTrue(NbtFlavor.Java.BigEndian);
            Assert.IsTrue(NbtFlavor.JavaAnvil.BigEndian);
            Assert.IsTrue(NbtFlavor.JavaLegacy.BigEndian);
            Assert.IsTrue(NbtFlavor.JavaNetwork.BigEndian);
            Assert.IsTrue(NbtFlavor.ClassiCube.BigEndian);
            Assert.IsFalse(NbtFlavor.Bedrock.BigEndian);
            Assert.IsFalse(NbtFlavor.BedrockNetwork.BigEndian);
        }


        [TestMethod]
        public void OnlyJavaNetworkHasUnnamedAnyTypeRoot() {
            foreach (NbtFlavor flavor in AllFlavors()) {
                bool isJavaNetwork = ReferenceEquals(flavor, NbtFlavor.JavaNetwork);
                Assert.AreEqual(!isJavaNetwork, flavor.HasRootName, flavor.Name);
                Assert.AreEqual(isJavaNetwork, flavor.AllowsNonCompoundRoot, flavor.Name);
            }
        }


        [TestMethod]
        public void OnlyBedrockNetworkUsesVarInts() {
            foreach (NbtFlavor flavor in AllFlavors()) {
                Assert.AreEqual(ReferenceEquals(flavor, NbtFlavor.BedrockNetwork), flavor.UsesVarInts, flavor.Name);
            }
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
            Assert.AreEqual(NbtTagType.LongArray, NbtFlavor.Java.MaxTagType);
            Assert.AreEqual(NbtTagType.LongArray, NbtFlavor.JavaNetwork.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.JavaAnvil.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.Bedrock.MaxTagType);
            Assert.AreEqual(NbtTagType.IntArray, NbtFlavor.BedrockNetwork.MaxTagType);
            Assert.AreEqual(NbtTagType.Compound, NbtFlavor.JavaLegacy.MaxTagType);
            Assert.AreEqual(NbtTagType.Compound, NbtFlavor.ClassiCube.MaxTagType);

            Assert.AreEqual(65535, NbtFlavor.Java.MaxStringBytes);
            Assert.AreEqual(32767, NbtFlavor.Bedrock.MaxStringBytes);
            Assert.AreEqual(int.MaxValue, NbtFlavor.BedrockNetwork.MaxStringBytes);
            Assert.AreEqual(256, NbtFlavor.ClassiCube.MaxStringBytes);
        }


        [TestMethod]
        public void FlavorsDeclareStringEncoding() {
            Assert.IsTrue(NbtFlavor.Java.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaAnvil.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaLegacy.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaNetwork.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.ClassiCube.UsesModifiedUtf8);
            Assert.IsFalse(NbtFlavor.Bedrock.UsesModifiedUtf8);
            Assert.IsFalse(NbtFlavor.BedrockNetwork.UsesModifiedUtf8);
        }
    }
}
