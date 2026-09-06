using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public sealed class NameCachingTests {
        // Each warm-up compound supplies two short names.
        const int WarmupCompoundCount = (NbtBinaryReader.NameCacheActivation + 1) / 2;

        static NbtCompound MakeSchemaDoc(int compoundCount, Func<int, int, string> namer) {
            var list = new NbtList("Items", NbtTagType.Compound);
            for (int i = 0; i < compoundCount; i++) {
                var compound = new NbtCompound();
                for (int f = 0; f < 3; f++) {
                    compound.Add(new NbtInt(namer(i, f), i * 3 + f));
                }
                list.Add(compound);
            }
            return new NbtCompound("Root") { list };
        }


        [TestMethod]
        public void TreeAndStreamingReadersCacheShortNamesAlongsideLongNames() {
            int firstCachedIndex = WarmupCompoundCount;
            int secondCachedIndex = firstCachedIndex + 1;
            int compoundCount = secondCachedIndex + 1;
            string longName = new string('x', 100);
            byte[] doc = new NbtFile(MakeSchemaDoc(compoundCount, (i, f) => f == 2 ? longName : "field" + f))
                .SaveToBuffer(NbtCompression.None);
            NbtCompound root = TestFiles.Load(doc).RootTag;
            var items = (NbtList)root["Items"];
            Assert.AreEqual(compoundCount, items.Count);
            Assert.AreEqual(secondCachedIndex * 3 + 2, items[secondCachedIndex][longName].IntValue);
            Assert.AreEqual("field0", ((NbtCompound)items[firstCachedIndex]).Tags.First().Name);
            Assert.AreSame(items[firstCachedIndex]["field1"].Name, items[secondCachedIndex]["field1"].Name);

            NbtReader reader = TestFiles.OpenReader(doc);
            var occurrences = new List<string>();
            while (reader.ReadToFollowing()) {
                if (reader.TagName == "field1") {
                    occurrences.Add(reader.TagName);
                }
            }
            Assert.AreEqual(compoundCount, occurrences.Count);
            Assert.AreSame(occurrences[firstCachedIndex], occurrences[secondCachedIndex]);
        }


        [TestMethod]
        public void ManyUniqueNamesStillParse() {
            // Far more unique names than the cache retains
            NbtCompound root = TestFiles.Reload(MakeSchemaDoc(2000, (i, f) => "n" + i + "_" + f));
            var items = (NbtList)root["Items"];
            Assert.AreEqual(2000, items.Count);
            Assert.AreEqual(1500 * 3 + 2, ((NbtCompound)items[1500])["n1500_2"].IntValue);
        }


        [TestMethod]
        public void AlternateEncodingsOfOneNameDecodeEqual() {
            // The name "a\0b" written two ways: standard NUL (0x00) and Java's overlong C0 80.
            // Different bytes, same decoded string; both must decode and compare equal.
            byte[] doc;
            using (var ms = new MemoryStream()) {
                void WriteNamedByte(byte[] nameBytes, byte value) {
                    ms.WriteByte(0x01); // TAG_Byte
                    ms.WriteByte(0);
                    ms.WriteByte((byte)nameBytes.Length);
                    ms.Write(nameBytes, 0, nameBytes.Length);
                    ms.WriteByte(value);
                }
                void BeginCompound(string name) {
                    ms.WriteByte(0x0A);
                    ms.WriteByte(0);
                    ms.WriteByte((byte)name.Length);
                    foreach (char c in name) ms.WriteByte((byte)c);
                }
                BeginCompound(""); // root
                // Cross the cache's activation threshold before testing alternate encodings.
                for (int i = 0; i < WarmupCompoundCount; i++) {
                    BeginCompound("warm" + i);
                    WriteNamedByte(new byte[] { (byte)'w' }, 0);
                    ms.WriteByte(0x00);
                }
                BeginCompound("first");
                WriteNamedByte(new byte[] { (byte)'a', 0x00, (byte)'b' }, 1);
                ms.WriteByte(0x00); // end "first"
                BeginCompound("second");
                WriteNamedByte(new byte[] { (byte)'a', 0xC0, 0x80, (byte)'b' }, 2);
                ms.WriteByte(0x00); // end "second"
                BeginCompound("third");
                WriteNamedByte(new byte[] { (byte)'a', 0x00, (byte)'b' }, 3);
                ms.WriteByte(0x00);
                BeginCompound("fourth");
                WriteNamedByte(new byte[] { (byte)'a', 0xC0, 0x80, (byte)'b' }, 4);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00); // end root
                doc = ms.ToArray();
            }
            NbtFile file = TestFiles.Load(doc);
            string nameA = ((NbtCompound)file.RootTag["first"]).Tags.First().Name;
            string nameB = ((NbtCompound)file.RootTag["second"]).Tags.First().Name;
            Assert.AreEqual(nameA, nameB);
            Assert.AreEqual("a\0b", nameA);
            Assert.AreSame(nameA, ((NbtCompound)file.RootTag["third"]).Tags.First().Name);
            Assert.AreSame(nameB, ((NbtCompound)file.RootTag["fourth"]).Tags.First().Name);
            string[] children = { "first", "second", "third", "fourth" };
            for (int i = 0; i < children.Length; i++) {
                Assert.AreEqual(i + 1, file.RootTag[children[i]]["a\0b"].ByteValue);
            }
        }


        [TestMethod]
        public void DuplicateNamesInOneCompoundStillRejected() {
            // Cached names must not bypass decoded-name duplicate detection
            byte[] doc;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                for (int i = 0; i < WarmupCompoundCount; i++) {
                    writer.BeginCompound("warm" + i);
                    writer.WriteByte("dup", 0);
                    writer.EndCompound();
                }
                // NbtWriter permits duplicate names; the tree reader must reject them.
                writer.BeginCompound("duplicates");
                writer.WriteByte("dup", 1);
                writer.WriteByte("dup", 2);
                writer.EndCompound();
                writer.EndCompound();
                writer.Finish();
                doc = ms.ToArray();
            }
            NbtReader reader = TestFiles.OpenReader(doc);
            Assert.IsTrue(reader.ReadToFollowing("duplicates"));
            Assert.IsTrue(reader.ReadToFollowing());
            string firstName = reader.TagName;
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreSame(firstName, reader.TagName);
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(doc));
        }
    }
}
