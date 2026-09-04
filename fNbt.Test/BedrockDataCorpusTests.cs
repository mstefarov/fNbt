using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Real BedrockNetwork-encoded data generated from Minecraft: Bedrock Edition, via
    // pmmp/BedrockData (CC0-1.0), commit bdb44a48fb6beffb6e9f6864f06d2232eb62b6a3.
    // canonical_block_states.nbt is the full block-state palette as 16,913 back-to-back
    // compound roots; entity_identifiers.nbt is a single document holding a list of
    // 136 compounds.
    [TestClass]
    public class BedrockDataCorpusTests {
        const int PaletteRootCount = 16913;

        static readonly NbtCodec Codec = NbtCodec.For(NbtFlavor.BedrockNetwork);


        [TestMethod]
        public void BlockPaletteParsesCompletely() {
            byte[] doc = File.ReadAllBytes(TestFiles.CanonicalBlockStates);
            List<NbtTag> roots;
            using (var ms = new MemoryStream(doc)) {
                roots = Codec.ReadConcatenatedTags(ms).ToList();
                Assert.AreEqual(ms.Length, ms.Position, "stream not fully consumed");
            }

            Assert.AreEqual(PaletteRootCount, roots.Count);
            Assert.AreEqual("minecraft:cyan_terracotta", roots[0]["name"].StringValue);
            Assert.AreEqual("minecraft:dandelion", roots[roots.Count - 1]["name"].StringValue);
            foreach (NbtTag root in roots) {
                var compound = (NbtCompound)root;
                Assert.AreEqual("", compound.Name);
                Assert.IsTrue(compound.Contains("name"), "block state without a name");
                Assert.AreEqual(NbtTagType.Compound, compound["states"].TagType);
                Assert.AreEqual(NbtTagType.Int, compound["version"].TagType);
            }
        }


        [TestMethod]
        public void BlockPaletteRoundTripsByteForByte() {
            byte[] doc = File.ReadAllBytes(TestFiles.CanonicalBlockStates);
            using (var input = new MemoryStream(doc))
            using (var output = new MemoryStream(doc.Length)) {
                Codec.WriteConcatenatedTags(Codec.ReadConcatenatedTags(input), output);
                CollectionAssert.AreEqual(doc, output.ToArray());
            }
        }


        [TestMethod]
        public void BlockPaletteStreamsThroughNbtReader() {
            // One NbtReader per concatenated document, exercising the streaming header
            // and skip paths over real varint data
            byte[] doc = File.ReadAllBytes(TestFiles.CanonicalBlockStates);
            using (var ms = new MemoryStream(doc)) {
                int documents = 0;
                long tags = 0;
                while (ms.Position < ms.Length) {
                    var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
                    while (reader.ReadToFollowing()) {
                        tags++;
                    }
                    documents++;
                }
                Assert.AreEqual(PaletteRootCount, documents);
                Assert.IsTrue(tags > PaletteRootCount * 3, "palette should hold several tags per root");
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }


        [TestMethod]
        public void EntityIdentifiersRoundTripByteForByte() {
            byte[] doc = File.ReadAllBytes(TestFiles.EntityIdentifiers);
            NbtTag root = Codec.ReadTag(doc, 0, doc.Length, out int bytesConsumed);

            Assert.AreEqual(doc.Length, bytesConsumed);
            Assert.AreEqual(136, ((NbtList)root["idlist"]).Count);
            CollectionAssert.AreEqual(doc, Codec.WriteTag(root));
        }
    }
}
