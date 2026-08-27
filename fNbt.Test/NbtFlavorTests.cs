using System;
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
                Assert.IsFalse(string.IsNullOrEmpty(flavor.Name));
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
    }
}
