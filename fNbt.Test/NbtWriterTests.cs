using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace fNbt.Test {
    // NbtWriter output: every value type, lists and compounds in every combination, and round
    // trips back through NbtFile.
    [TestClass]
    public class NbtWriterTests {
        static string GenRandomUnicodeString(Random rand) {
            // The wire limit is in UTF-8 bytes, up to 4 per char, so lengths stay under short.MaxValue/4
            int len = rand.Next(8, short.MaxValue / 4);
            StringBuilder sb = new StringBuilder();

            while (sb.Length < len) {
                char ch = (char)rand.Next(0, 0xFFFF);
                if (Char.IsControl(ch) || Char.IsSurrogate(ch) || ch >= 0xE000 && ch <= 0xF8FF) {
                    // exclude control characters, surrogates, and private range
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }


        [TestMethod]
        public void WritesEveryValueType() {
            // write one named tag for every value type, and read it back
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.AreEqual(ms, writer.BaseStream);
                Assert.AreSame(NbtFlavor.Java, writer.Flavor);
                {
                    writer.WriteByte("byte", 1);
                    writer.WriteShort("short", 2);
                    writer.WriteInt("int", 3);
                    writer.WriteLong("long", 4L);
                    writer.WriteFloat("float", 5f);
                    writer.WriteDouble("double", 6d);
                    writer.WriteByteArray("byteArray", new byte[] { 10, 11, 12 });
                    writer.WriteIntArray("intArray", new[] { 20, 21, 22 });
                    writer.WriteLongArray("longArray", new long[] { 200, 210, 220 });
                    writer.WriteString("string", "123");
                }
                Assert.IsFalse(writer.IsDone);
                Assert.IsFalse(writer.IsInErrorState);
                writer.EndCompound();
                Assert.IsTrue(writer.IsDone);
                Assert.IsFalse(writer.IsInErrorState);
                NbtFile file = TestFiles.FinishAndReload(writer, ms);

                TestFiles.AssertAllValues(file);
            }
        }


        [TestMethod]
        public void ByteArrayFromStream() {
            var data = new byte[64 * 1024];
            for (int i = 0; i < data.Length; i++) {
                data[i] = unchecked((byte)i);
            }

            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                {
                    byte[] buffer = new byte[1024];
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray("byteArray1", dataStream, data.Length);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray("byteArray2", dataStream, data.Length, buffer);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray("byteArray3", dataStream, 1);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray("byteArray4", dataStream, 1, buffer);
                    }

                    writer.BeginList("innerLists", NbtTagType.ByteArray, 4);
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray(dataStream, data.Length);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray(dataStream, data.Length, buffer);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray(dataStream, 1);
                    }
                    using (var dataStream = new NonSeekableStream(new MemoryStream(data))) {
                        writer.WriteByteArray(dataStream, 1, buffer);
                    }
                    writer.EndList();
                }
                writer.EndCompound();
                NbtFile file = TestFiles.FinishAndReload(writer, ms);
                CollectionAssert.AreEqual(data, file.RootTag["byteArray1"].ByteArrayValue);
                CollectionAssert.AreEqual(data, file.RootTag["byteArray2"].ByteArrayValue);
                Assert.AreEqual(1, file.RootTag["byteArray3"].ByteArrayValue.Length);
                Assert.AreEqual(data[0], file.RootTag["byteArray3"].ByteArrayValue[0]);
                Assert.AreEqual(1, file.RootTag["byteArray4"].ByteArrayValue.Length);
                Assert.AreEqual(data[0], file.RootTag["byteArray4"].ByteArrayValue[0]);

                CollectionAssert.AreEqual(data, file.RootTag["innerLists"][0].ByteArrayValue);
                CollectionAssert.AreEqual(data, file.RootTag["innerLists"][1].ByteArrayValue);
                Assert.AreEqual(1, file.RootTag["innerLists"][2].ByteArrayValue.Length);
                Assert.AreEqual(data[0], file.RootTag["innerLists"][2].ByteArrayValue[0]);
                Assert.AreEqual(1, file.RootTag["innerLists"][3].ByteArrayValue.Length);
                Assert.AreEqual(data[0], file.RootTag["innerLists"][3].ByteArrayValue[0]);
            }
        }


        [TestMethod]
        public void WritesCompoundAndListCombinations() {
            // test writing various combinations of compound tags and list tags
            const string testString = "Come on and slam, and welcome to the jam.";
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "Test");
                {
                    writer.BeginCompound("EmptyCompy");
                    { }
                    writer.EndCompound();

                    writer.BeginCompound("OuterNestedCompy");
                    {
                        writer.BeginCompound("InnerNestedCompy");
                        {
                            writer.WriteInt("IntTest", 123);
                            writer.WriteString("StringTest", testString);
                        }
                        writer.EndCompound();
                    }
                    writer.EndCompound();

                    writer.BeginList("ListOfInts", NbtTagType.Int, 3);
                    {
                        writer.WriteInt(1);
                        writer.WriteInt(2);
                        writer.WriteInt(3);
                    }
                    writer.EndList();

                    writer.BeginCompound("CompoundOfListsOfCompounds");
                    {
                        writer.BeginList("ListOfCompounds", NbtTagType.Compound, 1);
                        {
                            writer.BeginCompound();
                            {
                                writer.WriteInt("TestInt", 123);
                            }
                            writer.EndCompound();
                        }
                        writer.EndList();
                    }
                    writer.EndCompound();


                    writer.BeginList("ListOfEmptyLists", NbtTagType.List, 3);
                    {
                        writer.BeginList(NbtTagType.List, 0);
                        { }
                        writer.EndList();
                        writer.BeginList(NbtTagType.List, 0);
                        { }
                        writer.EndList();
                        writer.BeginList(NbtTagType.List, 0);
                        { }
                        writer.EndList();
                    }
                    writer.EndList();
                }
                writer.EndCompound();
                writer.Finish();
                Assert.IsFalse(writer.IsInErrorState);

                // The tree writer must produce the same bytes
                var expected = new NbtCompound("Test") {
                    new NbtCompound("EmptyCompy"),
                    new NbtCompound("OuterNestedCompy") {
                        new NbtCompound("InnerNestedCompy") {
                            new NbtInt("IntTest", 123),
                            new NbtString("StringTest", testString)
                        }
                    },
                    new NbtList("ListOfInts", NbtTagType.Int) {
                        new NbtInt(1), new NbtInt(2), new NbtInt(3)
                    },
                    new NbtCompound("CompoundOfListsOfCompounds") {
                        new NbtList("ListOfCompounds", NbtTagType.Compound) {
                            new NbtCompound { new NbtInt("TestInt", 123) }
                        }
                    },
                    new NbtList("ListOfEmptyLists", NbtTagType.List) {
                        new NbtList(NbtTagType.List),
                        new NbtList(NbtTagType.List),
                        new NbtList(NbtTagType.List)
                    }
                };
                CollectionAssert.AreEqual(new NbtFile(expected).SaveToBuffer(NbtCompression.None),
                                          ms.ToArray());
            }
        }


        [TestMethod]
        public void WritesListsOfEveryType() {
            // write short (1-element) lists of every possible kind
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "Test");
                writer.BeginList("LotsOfLists", NbtTagType.List, 12);
                {
                    writer.BeginList(NbtTagType.Byte, 1);
                    writer.WriteByte(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.ByteArray, 1);
                    writer.WriteByteArray(new byte[] {
                        1
                    });
                    writer.EndList();

                    writer.BeginList(NbtTagType.Compound, 1);
                    writer.BeginCompound();
                    writer.EndCompound();
                    writer.EndList();

                    writer.BeginList(NbtTagType.Double, 1);
                    writer.WriteDouble(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.Float, 1);
                    writer.WriteFloat(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.Int, 1);
                    writer.WriteInt(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.IntArray, 1);
                    writer.WriteIntArray(new[] {
                        1
                    });
                    writer.EndList();

                    writer.BeginList(NbtTagType.List, 1);
                    writer.BeginList(NbtTagType.List, 0);
                    writer.EndList();
                    writer.EndList();

                    writer.BeginList(NbtTagType.Long, 1);
                    writer.WriteLong(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.Short, 1);
                    writer.WriteShort(1);
                    writer.EndList();

                    writer.BeginList(NbtTagType.String, 1);
                    writer.WriteString("ponies");
                    writer.EndList();

                    writer.BeginList(NbtTagType.LongArray, 1);
                    writer.WriteLongArray(new[] {
                        1L
                    });
                    writer.EndList();
                }
                writer.EndList();
                writer.EndCompound();
                writer.Finish();

                // The tree writer must produce the same bytes
                var expected = new NbtCompound("Test") {
                    new NbtList("LotsOfLists", NbtTagType.List) {
                        new NbtList(NbtTagType.Byte) { new NbtByte(1) },
                        new NbtList(NbtTagType.ByteArray) { new NbtByteArray(new byte[] { 1 }) },
                        new NbtList(NbtTagType.Compound) { new NbtCompound() },
                        new NbtList(NbtTagType.Double) { new NbtDouble(1) },
                        new NbtList(NbtTagType.Float) { new NbtFloat(1) },
                        new NbtList(NbtTagType.Int) { new NbtInt(1) },
                        new NbtList(NbtTagType.IntArray) { new NbtIntArray(new[] { 1 }) },
                        new NbtList(NbtTagType.List) { new NbtList(NbtTagType.List) },
                        new NbtList(NbtTagType.Long) { new NbtLong(1) },
                        new NbtList(NbtTagType.Short) { new NbtShort(1) },
                        new NbtList(NbtTagType.String) { new NbtString("ponies") },
                        new NbtList(NbtTagType.LongArray) { new NbtLongArray(new[] { 1L }) }
                    }
                };
                CollectionAssert.AreEqual(new NbtFile(expected).SaveToBuffer(NbtCompression.None),
                                          ms.ToArray());
            }
        }


        [TestMethod]
        public void WriteTagWritesEveryValueType() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                {
                    foreach (NbtTag tag in TestFiles.MakeAllValuesRoot().Tags) {
                        writer.WriteTag(tag);
                    }
                    writer.EndCompound();
                    Assert.IsTrue(writer.IsDone);
                    writer.Finish();
                }
                ms.Position = 0;
                var file = new NbtFile();
                long bytesRead = file.LoadFromBuffer(ms.ToArray(), 0, (int)ms.Length, NbtCompression.None);
                Assert.AreEqual(ms.Length, bytesRead);
                TestFiles.AssertAllValues(file);
            }
        }


        // Ensure that Unicode strings of arbitrary size and content are written/read properly
        [TestMethod]
        public void RandomUnicodeStringsRoundTrip() {
            // Use a fixed seed for repeatability of this test
            Random rand = new Random(0);

            const int numStrings = 1024;
            List<string> writtenStrings = new List<string>();
            for (int i = 0; i < numStrings; i++) {
                writtenStrings.Add(GenRandomUnicodeString(rand));
            }

            using (var ms = new MemoryStream()) {
                NbtWriter writer = new NbtWriter(ms, "test");
                writer.BeginList("stringList", NbtTagType.String, numStrings);
                foreach (string s in writtenStrings) {
                    writer.WriteString(s);
                }
                writer.EndList();
                writer.EndCompound();

                NbtFile file = TestFiles.FinishAndReload(writer, ms);
                var readStrings =
                    file.RootTag.Get<NbtList>("stringList")
                        .ToArray<NbtString>()
                        .Select(tag => tag.StringValue);

                CollectionAssert.AreEqual(writtenStrings, readStrings.ToList());
            }
        }


        [TestMethod]
        public void EmptyEndTypedListRoundTrips() {
            byte[] granular;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                writer.BeginList("emptyList", NbtTagType.End, 0);
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
                granular = ms.ToArray();
            }

            // Byte-identical to what NbtFile emits for the same document
            var objectModel = new NbtFile(new NbtCompound("root") {
                new NbtList("emptyList", NbtTagType.End)
            });
            byte[] tree = objectModel.SaveToBuffer(NbtCompression.None);
            CollectionAssert.AreEqual(tree, granular);

            NbtFile reloaded = TestFiles.Load(granular);
            NbtList list = reloaded.RootTag.Get<NbtList>("emptyList");
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(NbtTagType.End, list.ListType);
        }


        [TestMethod]
        public void UnnamedEmptyEndTypedListRoundTrips() {
            // The unnamed BeginList overload must also accept an empty End-typed list nested
            // in a list of lists, matching what NbtFile writes.
            byte[] granular;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                writer.BeginList("listOfLists", NbtTagType.List, 1);
                writer.BeginList(NbtTagType.End, 0);
                writer.EndList();
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
                granular = ms.ToArray();
            }

            NbtFile reloaded = TestFiles.Load(granular);
            NbtList outer = reloaded.RootTag.Get<NbtList>("listOfLists");
            Assert.AreEqual(1, outer.Count);
            Assert.AreEqual(0, outer.Get<NbtList>(0).Count);
            Assert.AreEqual(NbtTagType.End, outer.Get<NbtList>(0).ListType);
        }


        [TestMethod]
        public void ArraySegmentsHonorOffset() {
            int[] ints = { 10, 11, 12, 13, 14 };
            long[] longs = { 20, 21, 22, 23, 24 };
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                writer.WriteIntArray("ia", ints, 2, 3);
                writer.WriteIntArray("iaTail", ints, 3, 2);
                writer.WriteLongArray("la", longs, 2, 3);
                writer.WriteLongArray("laTail", longs, 3, 2);
                writer.BeginList("list", NbtTagType.IntArray, 1);
                writer.WriteIntArray(ints, 1, 4);
                writer.EndList();
                writer.EndCompound();
                NbtFile file = TestFiles.FinishAndReload(writer, ms);
                CollectionAssert.AreEqual(new[] { 12, 13, 14 }, file.RootTag["ia"].IntArrayValue);
                CollectionAssert.AreEqual(new[] { 13, 14 }, file.RootTag["iaTail"].IntArrayValue);
                CollectionAssert.AreEqual(new long[] { 22, 23, 24 }, file.RootTag["la"].LongArrayValue);
                CollectionAssert.AreEqual(new long[] { 23, 24 }, file.RootTag["laTail"].LongArrayValue);
                CollectionAssert.AreEqual(new[] { 11, 12, 13, 14 }, file.RootTag["list"][0].IntArrayValue);
            }
        }
    }
}
