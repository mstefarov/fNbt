using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace fNbt.Test {
    [TestClass]
    public class NbtWriterTest {
        [TestMethod]
        public void ValueTest() {
            // write one named tag for every value type, and read it back
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.AreEqual(ms, writer.BaseStream);
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

                TestFiles.AssertValueTest(file);
            }
        }


        [TestMethod]
        public void HugeNbtWriterTest() {
            // Tests writing byte arrays that exceed the max NbtBinaryWriter chunk size
            using (BufferedStream bs = new BufferedStream(Stream.Null)) {
                NbtWriter writer = new NbtWriter(bs, "root");
                writer.WriteByteArray("payload4", new byte[5 * 1024 * 1024]);
                writer.EndCompound();
                writer.Finish();
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
        public void CompoundListTest() {
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
        public void ListTest() {
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
        public void WriteTagTest() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                {
                    foreach (NbtTag tag in TestFiles.MakeValueTest().Tags) {
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
                TestFiles.AssertValueTest(file);
            }
        }


        [TestMethod]
        public void ErrorTest() {
            byte[] dummyByteArray = { 1, 2, 3, 4, 5 };
            int[] dummyIntArray = { 1, 2, 3, 4, 5 };
            long[] dummyLongArray = { 1, 2, 3, 4, 5 };
            MemoryStream dummyStream = new MemoryStream(dummyByteArray);

            using (var ms = new MemoryStream()) {
                // null constructor parameters, or a non-writable stream
                Assert.Throws<ArgumentNullException>(() => new NbtWriter(null, "root"));
                Assert.Throws<ArgumentNullException>(() => new NbtWriter(ms, null));
                Assert.Throws<ArgumentException>(() => new NbtWriter(new NonWritableStream(), "root"));

                var writer = new NbtWriter(ms, "root");
                {
                    // use negative list size
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("list", NbtTagType.Int, -1));
                    writer.BeginList("listOfLists", NbtTagType.List, 1);
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList(NbtTagType.Int, -1));
                    writer.BeginList(NbtTagType.Int, 0);
                    writer.EndList();
                    writer.EndList();

                    writer.BeginList("list", NbtTagType.Int, 1);

                    // call EndCompound when not in a compound
                    Assert.Throws<NbtFormatException>(writer.EndCompound);

                    // end list before all elements have been written
                    Assert.Throws<NbtFormatException>(writer.EndList);

                    // write the wrong kind of tag inside a list
                    Assert.Throws<NbtFormatException>(() => writer.WriteShort(0));

                    // write a named tag where an unnamed tag is expected
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt("NamedInt", 0));

                    // write too many list elements
                    writer.WriteTag(new NbtInt());
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt(0));
                    writer.EndList();

                    // write a null tag
                    Assert.Throws<ArgumentNullException>(() => writer.WriteTag(null));

                    // write an unnamed tag where a named tag is expected
                    Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt()));
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt(0));

                    // end a list when not in a list
                    Assert.Throws<NbtFormatException>(writer.EndList);

                    // unacceptable nulls: WriteString
                    Assert.Throws<ArgumentNullException>(() => writer.WriteString(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteString("NullString", null));

                    // unacceptable nulls: WriteByteArray from array
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullByteArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullByteArray", null, 0, 5));

                    // unacceptable nulls: WriteByteArray from stream
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(dummyStream, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullBuffer", dummyStream, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullStream", null, 5));
                    Assert.Throws<ArgumentNullException>(
                        () => writer.WriteByteArray("NullStream", null, 5, dummyByteArray));

                    // unacceptable nulls: WriteIntArray
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray("NullIntArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray("NullIntArray", null, 0, 5));

                    // unacceptable nulls: WriteLongArray
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray("NullLongArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray("NullLongArray", null, 0, 5));

                    // non-readable streams are unacceptable
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(new NonReadableStream(), 0));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray(new NonReadableStream(), 0, new byte[10]));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("NonReadableStream", new NonReadableStream(), 0));

                    // trying to write array with out-of-range offset/count
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyByteArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyByteArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyByteArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyByteArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 1, 5));

                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteIntArray(dummyIntArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteIntArray(dummyIntArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteIntArray(dummyIntArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteIntArray(dummyIntArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 1, 5));

                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteLongArray(dummyLongArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteLongArray(dummyLongArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteLongArray(dummyLongArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteLongArray(dummyLongArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 1, 5));

                    // out-of-range values for stream-reading overloads of WriteByteArray
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyStream, -1));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray("BadLength", dummyStream, -1));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray(dummyStream, -1, dummyByteArray));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("BadLength", dummyStream, -1, dummyByteArray));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyStream, 5, new byte[0]));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("BadLength", dummyStream, 5, new byte[0]));

                    // trying to read from non-readable stream
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("ByteStream", new NonReadableStream(), 0));

                    // finish too early
                    Assert.Throws<NbtFormatException>(writer.Finish);

                    writer.EndCompound();
                    writer.Finish();

                    // write tag after finishing
                    Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt()));
                }
            }
        }


        // Ensure that Unicode strings of arbitrary size and content are written/read properly
        [TestMethod]
        public void ComplexStringsTest() {
            // Use a fixed seed for repeatability of this test
            Random rand = new Random(0);

            // Generate random Unicode strings
            const int numStrings = 1024;
            List<string> writtenStrings = new List<string>();
            for (int i = 0; i < numStrings; i++) {
                writtenStrings.Add(GenRandomUnicodeString(rand));
            }

            using (var ms = new MemoryStream()) {
                // Write a list of strings
                NbtWriter writer = new NbtWriter(ms, "test");
                writer.BeginList("stringList", NbtTagType.String, numStrings);
                foreach (string s in writtenStrings) {
                    writer.WriteString(s);
                }
                writer.EndList();
                writer.EndCompound();

                // Let's read what we have written, and check contents
                NbtFile file = TestFiles.FinishAndReload(writer, ms);
                var readStrings =
                    file.RootTag.Get<NbtList>("stringList")
                        .ToArray<NbtString>()
                        .Select(tag => tag.StringValue);

                // Make sure that all read/written strings match exactly
                CollectionAssert.AreEqual(writtenStrings, readStrings.ToList());
            }
        }


        [TestMethod]
        public void MissingNameTest() {
            using (var ms = new MemoryStream()) {
                NbtWriter writer = new NbtWriter(ms, "test");
                // All tags (aside from list elements) must be named. The check is
                // type-independent, so one value, one array, and two containers cover it.
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt(123)));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtByteArray(new byte[0])));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtList(NbtTagType.Byte)));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtCompound()));
                // Refusals must not poison the writer
                Assert.IsFalse(writer.IsInErrorState);
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void WriteByteArrayFromShortStreamThrows() {
            // A source shorter than count used to spin forever returning 0. It must throw instead.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.Throws<EndOfStreamException>(
                    () => writer.WriteByteArray("arr", new MemoryStream(new byte[5]), 10));
                Assert.Throws<NbtFormatException>(() => writer.WriteInt("afterFailure", 1));
            }
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                writer.BeginList("list", NbtTagType.ByteArray, 1);
                Assert.Throws<EndOfStreamException>(
                    () => writer.WriteByteArray(new MemoryStream(new byte[5]), 10));
                Assert.Throws<NbtFormatException>(() => writer.WriteByteArray(new byte[0]));
            }
        }


        [TestMethod]
        public void StandardUtf8StringsAreValidatedBeforeOutput() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.Bedrock);
                writer.BeginList("strings", NbtTagType.String, 1);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString("\uD800"));
                writer.WriteString("ok");
                writer.EndList();

                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteInt("\uD800", 1));
                NbtAssert.WriterStillUsable(writer);
            }

            using (var ms = new MemoryStream()) {
                Assert.Throws<NbtFormatException>(() => new NbtWriter(ms, "\uD800", NbtFlavor.Bedrock));
                Assert.AreEqual(0, ms.Length);
            }
        }


        [TestMethod]
        public void StandardUtf8TreeIsValidatedBeforeOutput() {
            const string loneSurrogate = "\uD800";

            // With tree validation disabled, a direct name is still measured before its type
            // byte is emitted, so rejection does not damage the writer.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", new NbtOptions(NbtFlavor.Bedrock) {
                    ValidateOnWrite = false
                });
                NbtAssert.WritesNothing<NbtFormatException>(ms,
                    () => writer.WriteTag(new NbtInt(loneSurrogate, 1)));
                NbtAssert.WriterStillUsable(writer);
            }

            // With validation on, the pre-walk counts the exact bytes under standard UTF-8, so
            // a nested lone surrogate is refused before anything is written
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.Bedrock);
                NbtAssert.WritesNothing<NbtFormatException>(ms,
                    () => writer.WriteTag(new NbtCompound("nested") {
                        new NbtString("value", loneSurrogate)
                    }));
                NbtAssert.WriterStillUsable(writer);
            }

            // NbtFile and NbtCodec share the validation walk
            var doc = new NbtCompound("root") { new NbtString("value", loneSurrogate) };
            using (var ms = new MemoryStream()) {
                var file = new NbtFile(doc, NbtFlavor.Bedrock);
                Assert.Throws<NbtFormatException>(() => file.SaveToStream(ms, NbtCompression.None));
                Assert.AreEqual(0, ms.Length);
            }
            Assert.Throws<NbtFormatException>(() => NbtCodec.For(NbtFlavor.Bedrock).WriteTag(doc));
        }


        [TestMethod]
        public void WriteTagRefusedUpFrontLeavesWriterUsable() {
            // A list with no element type is refused before the emission window opens, like
            // the tag layer would refuse it, so nothing is written and the writer goes on
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteTag(new NbtList("l")));
                NbtAssert.WriterStillUsable(writer);
            }
        }


        [TestMethod]
        public void PartialTreeFailurePoisonsWriter() {
            var partialTree = new NbtCompound("partial") {
                new NbtString("tooLong", new string('x', ushort.MaxValue + 1))
            };
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(partialTree));
                Assert.IsTrue(ms.Length > before);
                NbtAssert.WriterIsPoisoned(writer);
            }
        }


        [TestMethod]
        public void DirectOutputFailurePoisonsWriter() {
            using (var stream = new FailAfterBytesStream()) {
                var writer = new NbtWriter(stream, "root");
                writer.BeginList("ints", NbtTagType.Int, 1);
                stream.FailAfter(2);

                // The int payload is four bytes, so the stream writes a prefix before throwing.
                Assert.Throws<IOException>(() => writer.WriteInt(123));
                // Observable without catching, and distinct from being done
                Assert.IsTrue(writer.IsInErrorState);
                Assert.IsFalse(writer.IsDone);
                Assert.Throws<NbtFormatException>(() => writer.WriteInt(456));
                Assert.Throws<NbtFormatException>(writer.EndList);
                Assert.Throws<NbtFormatException>(writer.EndCompound);
                Assert.Throws<NbtFormatException>(writer.Finish);
                // The stream itself stays reachable, so the caller can inspect or discard it
                Assert.IsNotNull(writer.BaseStream);
            }

            // A constructor failure cannot leak a usable writer instance.
            using (var stream = new FailAfterBytesStream()) {
                stream.FailAfter(0);
                Assert.Throws<IOException>(() => new NbtWriter(stream, "root"));
                Assert.AreEqual(0, stream.Length);
            }
        }


        [TestMethod]
        public void BaseStreamFlushFailureLeavesWriterUsable() {
            using (var stream = new FlushFailingStream()) {
                var writer = new NbtWriter(stream, "root");
                writer.WriteInt("before", 1);
                stream.FailFlush();

                // The getter flushes, so the failure surfaces there, but reading the property
                // is not a write: the writer stays usable, and a monitoring read cannot fail it
                Assert.Throws<IOException>(() => { _ = writer.BaseStream; });
                Assert.IsFalse(writer.IsInErrorState);
                writer.WriteInt("after", 2);
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void BaseStreamReadFromInsideAWriteIsHarmless() {
            // A stream that logs the writer's position from its own Write must not fail the writer
            using (var stream = new PositionLoggingStream()) {
                var writer = new NbtWriter(stream, "root");
                stream.Writer = writer;
                writer.WriteInt("a", 1);
                writer.WriteString("b", "two");
                writer.EndCompound();
                writer.Finish();
                Assert.IsTrue(stream.Logged > 0);
                // In-flight writes share the error-state sentinel, so a stream that asks from
                // inside its own Write sees true. Reads from outside a write are exact.
                Assert.IsTrue(stream.SawInFlightErrorState);
                Assert.IsFalse(writer.IsInErrorState);
            }
        }


        sealed class PositionLoggingStream : MemoryStream {
            public NbtWriter Writer;
            public int Logged;
            public bool SawInFlightErrorState;


            public override void Write(byte[] buffer, int offset, int count) {
                base.Write(buffer, offset, count);
                if (Writer != null) {
                    _ = Writer.BaseStream.Position;
                    if (Writer.IsInErrorState) SawInFlightErrorState = true;
                    Logged++;
                }
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
        public void BadListTypesRejected() {
            // "End" is only valid for empty lists, and "Unknown" is never valid.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("bad", NbtTagType.End, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("bad", NbtTagType.Unknown, 0));
            }
        }


        static string GenRandomUnicodeString(Random rand) {
            // String length is limited by number of bytes, not characters.
            // Most bytes per char in UTF8 is 4, so max string length is therefore short.MaxValue/4
            int len = rand.Next(8, short.MaxValue / 4);
            StringBuilder sb = new StringBuilder();

            // Generate one char at a time until we filled up the StringBuilder
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


        sealed class FailAfterBytesStream : MemoryStream {
            int bytesBeforeFailure = int.MaxValue;


            public void FailAfter(int byteCount) {
                bytesBeforeFailure = byteCount;
            }


            public override void WriteByte(byte value) {
                if (bytesBeforeFailure == 0) throw new IOException("Injected write failure.");
                base.WriteByte(value);
                bytesBeforeFailure--;
            }


            public override void Write(byte[] buffer, int offset, int count) {
                if (count <= bytesBeforeFailure) {
                    base.Write(buffer, offset, count);
                    bytesBeforeFailure -= count;
                    return;
                }
                if (bytesBeforeFailure > 0) {
                    base.Write(buffer, offset, bytesBeforeFailure);
                    bytesBeforeFailure = 0;
                }
                throw new IOException("Injected write failure.");
            }
        }


        sealed class FlushFailingStream : MemoryStream {
            bool failFlush;


            public void FailFlush() {
                failFlush = true;
            }


            public override void Flush() {
                if (failFlush) throw new IOException("Injected flush failure.");
                base.Flush();
            }
        }


        [TestMethod]
        public void RejectedWriteTagDoesNotConsumeAListSlot() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                writer.BeginList("l", NbtTagType.String, 2);
                // Fails the flavor's 256-byte string ceiling; the list must still have both slots
                var over = new NbtString(new string('x', 300));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(over));
                writer.WriteString("a");
                writer.WriteString("b");
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void RejectedStringDoesNotConsumeAListSlot() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                writer.BeginList("l", NbtTagType.String, 1);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString(new string('x', 300)));
                // The slot is still open, so the list cannot close short
                Assert.Throws<NbtFormatException>(() => writer.EndList());
                writer.WriteString("ok");
                writer.EndList();
                writer.EndCompound();
                NbtFile file = TestFiles.FinishAndReload(writer, ms, new NbtOptions(NbtFlavor.ClassiCube));
                Assert.AreEqual("ok", file.RootTag["l"][0].StringValue);
            }

            // The Java wire limit gets the same treatment
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r");
                writer.BeginList("l", NbtTagType.String, 1);
                Assert.Throws<NbtFormatException>(() => writer.WriteString(new string('x', 65_536)));
                Assert.Throws<NbtFormatException>(() => writer.EndList());
            }
        }


        [TestMethod]
        public void RejectedStringsAndNamesWriteNothing() {
            string over = new string('x', 300);
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString("s", over));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteInt(over, 1));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.BeginCompound(over));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.BeginList(over, NbtTagType.Int, 0));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteTag(new NbtInt(over, 1)));
                // No type byte or name reached the stream, no container was left open, and
                // nothing that wrote nothing counts as an error
                NbtAssert.WriterStillUsable(writer);

                NbtFile file = TestFiles.Load(ms.ToArray(), NbtFlavor.ClassiCube);
                Assert.AreEqual(1, file.RootTag["afterRefusal"].IntValue);
            }

            // A root name over the limit writes nothing at all
            using (var ms = new MemoryStream()) {
                Assert.Throws<NbtFormatException>(() => new NbtWriter(ms, over, NbtFlavor.ClassiCube));
                Assert.AreEqual(0, ms.Length);
            }

            // A null name on a named overload is an argument error, not an unnamed element
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r");
                writer.BeginList("l", NbtTagType.Int, 1);
                NbtAssert.WritesNothing<ArgumentNullException>(ms, () => writer.WriteInt(null, 5));
                Assert.Throws<NbtFormatException>(() => writer.EndList());
                writer.WriteInt(5);
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
            }
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
