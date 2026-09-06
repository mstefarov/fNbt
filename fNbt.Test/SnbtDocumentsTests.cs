using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Whole real-world SNBT documents (TestFiles/snbt, see SOURCES.md there): each parses to the
    // same tree as Minecraft 26.2's own printout of it (the .game.snbt twin), survives a round trip
    // through every layout, and a document without a twin is one every game version refuses.
    // The large binary fixtures round-trip through text as well; the game accepted fNbt's text
    // for all of them in the research run, but that verdict is too large to ship.
    [TestClass]
    public class SnbtDocumentsTests {
        // Text carries no root name, so the parsed tree takes the original's before comparing
        static NbtTag RoundTrip(NbtTag tag) {
            foreach (SnbtLayout layout in new[] { SnbtLayout.Compact, SnbtLayout.Spaced, SnbtLayout.Indented }) {
                NbtTag back = NbtTag.ParseSnbt(tag.ToSnbt(new SnbtOptions { WriteLayout = layout }));
                back.Name = tag.Name;
                NbtAssert.AreEqual(tag, back, layout.ToString());
            }
            return tag;
        }


        [TestMethod]
        public void RealDocumentsReadAsTheGameReadsThem() {
            string[] documents = Directory.GetFiles(TestFiles.SnbtDocumentsDir, "*.snbt")
                                          .Where(f => !f.EndsWith(".game.snbt"))
                                          .ToArray();
            Assert.IsTrue(documents.Length >= 12, "documents found: " + documents.Length);
            foreach (string document in documents) {
                string text = File.ReadAllText(document);
                string twin = document.Substring(0, document.Length - 5) + ".game.snbt";
                if (!File.Exists(twin)) {
                    Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt(text), Path.GetFileName(document));
                    continue;
                }
                NbtTag actual = RoundTrip(NbtTag.ParseSnbt(text));
                NbtTag expected = NbtTag.ParseSnbt(File.ReadAllText(twin));
                NbtAssert.AreEqual(expected, actual, Path.GetFileName(document));
            }
        }


        [TestMethod]
        public void BinaryFixturesRoundTripThroughText() {
            NbtFile bigtest = new NbtFile(TestFiles.Big);
            RoundTrip(bigtest.RootTag);

            NbtCodec codec = NbtCodec.For(NbtFlavor.BedrockNetwork);
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(TestFiles.EntityIdentifiers))) {
                RoundTrip(codec.ReadTag(ms));
            }

            // 16,913 palette roots, 2.5 MB of text
            int roots = 0;
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(TestFiles.CanonicalBlockStates))) {
                foreach (NbtTag root in codec.ReadConcatenatedTags(ms)) {
                    NbtTag back = NbtTag.ParseSnbt(root.ToSnbt());
                    back.Name = root.Name;
                    Assert.IsTrue(NbtComparer.Instance.Equals(root, back), "palette root " + roots);
                    roots++;
                }
            }
            Assert.AreEqual(16913, roots);
        }
    }
}
