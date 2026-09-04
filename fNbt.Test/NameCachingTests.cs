using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public sealed class NameCachingTests {
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
        public void RepeatedNamesShareOneInstance() {
            // Names repeated across compounds may come back as one string instance. That is
            // intended, so pin it.
            string[] names = { "alpha", "beta", "gamma" };
            NbtCompound root = TestFiles.Reload(MakeSchemaDoc(60, (i, f) => names[f]));
            var items = (NbtList)root["Items"];
            string name30 = ((NbtCompound)items[30]).Tags.First().Name;
            string name50 = ((NbtCompound)items[50]).Tags.First().Name;
            Assert.AreEqual("alpha", name30);
            Assert.AreSame(name30, name50);
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
        public void LongNamesStillParse() {
            string longName = new string('x', 100);
            NbtCompound root = TestFiles.Reload(MakeSchemaDoc(20, (i, f) => longName + f));
            var items = (NbtList)root["Items"];
            Assert.AreEqual(15 * 3 + 1, ((NbtCompound)items[15])[longName + "1"].IntValue);
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
                BeginCompound("first");
                WriteNamedByte(new byte[] { (byte)'a', 0x00, (byte)'b' }, 1);
                ms.WriteByte(0x00); // end "first"
                BeginCompound("second");
                WriteNamedByte(new byte[] { (byte)'a', 0xC0, 0x80, (byte)'b' }, 2);
                ms.WriteByte(0x00); // end "second"
                ms.WriteByte(0x00); // end root
                doc = ms.ToArray();
            }
            NbtFile file = TestFiles.Load(doc);
            string nameA = ((NbtCompound)file.RootTag["first"]).Tags.First().Name;
            string nameB = ((NbtCompound)file.RootTag["second"]).Tags.First().Name;
            Assert.AreEqual(nameA, nameB);
            Assert.AreEqual("a\0b", nameA);
        }


        [TestMethod]
        public void DuplicateNamesInOneCompoundStillRejected() {
            // Cached names must not bypass decoded-name duplicate detection
            byte[] doc;
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                ms.WriteByte(0);
                ms.WriteByte(0);
                for (int i = 0; i < 12; i++) {
                    ms.WriteByte(0x01); // TAG_Byte "dup"
                    ms.WriteByte(0);
                    ms.WriteByte(3);
                    foreach (char c in "dup") ms.WriteByte((byte)c);
                    ms.WriteByte((byte)i);
                }
                ms.WriteByte(0x00);
                doc = ms.ToArray();
            }
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(doc));
        }


        [TestMethod]
        public void NbtReaderNamesAreCachedToo() {
            byte[] doc = new NbtFile(MakeSchemaDoc(60, (i, f) => "field" + f))
                .SaveToBuffer(NbtCompression.None);
            NbtReader reader = TestFiles.OpenReader(doc);
            var occurrences = new List<string>();
            while (reader.ReadToFollowing()) {
                if (reader.TagName == "field1") {
                    occurrences.Add(reader.TagName);
                }
            }
            Assert.AreEqual(60, occurrences.Count);
            // Early occurrences precede cache activation; late ones must share one instance
            Assert.AreSame(occurrences[30], occurrences[50]);
        }
    }
}
