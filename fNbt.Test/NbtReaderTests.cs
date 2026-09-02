using System;
using System.Collections.Generic;
using System.IO;

namespace fNbt.Test {
    [TestClass]
    public sealed class NbtReaderTests {
        [TestMethod]
        public void WalkBigFile() {
            // ReadToFollowing must visit every tag; a walk that stops early fails the count
            using (FileStream fs = File.OpenRead(TestFiles.Big)) {
                var reader = new NbtReader(fs);
                Assert.AreSame(fs, reader.BaseStream);
                while (reader.ReadToFollowing()) {
                    _ = reader.ToString(); // must not throw mid-walk
                }
                Assert.AreEqual("Level", reader.RootName);
                Assert.AreEqual(31, reader.TagsRead);
            }
        }


        [TestMethod]
        public void WalkBigFileNoSkip() {
            // With SkipEndTags off, End markers count as tags, so the walk gets longer
            using (FileStream fs = File.OpenRead(TestFiles.Big)) {
                var reader = new NbtReader(fs) {
                    SkipEndTags = false
                };
                while (reader.ReadToFollowing()) {
                }
                Assert.AreEqual(37, reader.TagsRead);
            }
        }


        [TestMethod]
        public void CacheTagValuesTest() {
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeValueTest());
            Assert.IsFalse(reader.CacheTagValues);
            reader.CacheTagValues = true;
            Assert.IsTrue(reader.ReadToFollowing()); // root

            Assert.IsTrue(reader.ReadToFollowing()); // byte
            Assert.AreEqual((byte)1, reader.ReadValue());
            Assert.AreEqual((byte)1, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // short
            Assert.AreEqual((short)2, reader.ReadValue());
            Assert.AreEqual((short)2, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // int
            Assert.AreEqual(3, reader.ReadValue());
            Assert.AreEqual(3, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // long
            Assert.AreEqual(4L, reader.ReadValue());
            Assert.AreEqual(4L, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // float
            Assert.AreEqual(5f, reader.ReadValue());
            Assert.AreEqual(5f, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // double
            Assert.AreEqual(6d, reader.ReadValue());
            Assert.AreEqual(6d, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // byteArray
            CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, (byte[])reader.ReadValue());
            CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, (byte[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // intArray
            CollectionAssert.AreEqual(new[] { 20, 21, 22 }, (int[])reader.ReadValue());
            CollectionAssert.AreEqual(new[] { 20, 21, 22 }, (int[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing());
            CollectionAssert.AreEqual(new long[] { 200, 210, 220 }, (long[])reader.ReadValue());
            CollectionAssert.AreEqual(new long[] { 200, 210, 220 }, (long[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // string
            Assert.AreEqual("123", reader.ReadValue());
            Assert.AreEqual("123", reader.ReadValue());
        }


        [TestMethod]
        public void NestedListTest() {
            var root = new NbtCompound("root") {
                new NbtList("OuterList") {
                    new NbtList {
                        new NbtByte()
                    },
                    new NbtList {
                        new NbtShort()
                    },
                    new NbtList {
                        new NbtInt()
                    }
                }
            };
            NbtReader reader = TestFiles.OpenReader(root);
            while (reader.ReadToFollowing()) {
                _ = reader.ToString(true); // must not throw mid-walk
            }
            // root, OuterList, three sublists, three elements
            Assert.AreEqual(8, reader.TagsRead);
        }


        [TestMethod]
        public void PropertiesTest() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            Assert.AreEqual(0, reader.Depth);
            Assert.AreEqual(0, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("root", reader.TagName);
            Assert.AreEqual(NbtTagType.Compound, reader.TagType);
            Assert.AreEqual(NbtTagType.Unknown, reader.ListType);
            Assert.IsFalse(reader.HasValue);
            Assert.IsTrue(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsFalse(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(1, reader.Depth);
            Assert.AreEqual(null, reader.ParentName);
            Assert.AreEqual(NbtTagType.Unknown, reader.ParentTagType);
            Assert.AreEqual(0, reader.ParentTagLength);
            Assert.AreEqual(0, reader.TagLength);
            Assert.AreEqual(1, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("first", reader.TagName);
            Assert.AreEqual(NbtTagType.Int, reader.TagType);
            Assert.AreEqual(NbtTagType.Unknown, reader.ListType);
            Assert.IsTrue(reader.HasValue);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsFalse(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(2, reader.Depth);
            Assert.AreEqual("root", reader.ParentName);
            Assert.AreEqual(NbtTagType.Compound, reader.ParentTagType);
            Assert.AreEqual(0, reader.ParentTagLength);
            Assert.AreEqual(0, reader.TagLength);
            Assert.AreEqual(2, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing("fourth-list"));
            Assert.AreEqual("fourth-list", reader.TagName);
            Assert.AreEqual(NbtTagType.List, reader.TagType);
            Assert.AreEqual(NbtTagType.List, reader.ListType);
            Assert.IsFalse(reader.HasValue);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsTrue(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsTrue(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(2, reader.Depth);
            Assert.AreEqual("root", reader.ParentName);
            Assert.AreEqual(NbtTagType.Compound, reader.ParentTagType);
            Assert.AreEqual(0, reader.ParentTagLength);
            Assert.AreEqual(3, reader.TagLength);
            Assert.AreEqual(8, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing()); // first list element, itself a list
            Assert.AreEqual(null, reader.TagName);
            Assert.AreEqual(NbtTagType.List, reader.TagType);
            Assert.AreEqual(NbtTagType.Compound, reader.ListType);
            Assert.IsFalse(reader.HasValue);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsTrue(reader.IsList);
            Assert.IsTrue(reader.IsListElement);
            Assert.IsTrue(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(3, reader.Depth);
            Assert.AreEqual("fourth-list", reader.ParentName);
            Assert.AreEqual(NbtTagType.List, reader.ParentTagType);
            Assert.AreEqual(3, reader.ParentTagLength);
            Assert.AreEqual(1, reader.TagLength);
            Assert.AreEqual(9, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing()); // first nested list element, compound
            Assert.AreEqual(null, reader.TagName);
            Assert.AreEqual(NbtTagType.Compound, reader.TagType);
            Assert.AreEqual(NbtTagType.Unknown, reader.ListType);
            Assert.IsFalse(reader.HasValue);
            Assert.IsTrue(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsTrue(reader.IsListElement);
            Assert.IsFalse(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(4, reader.Depth);
            Assert.AreEqual(null, reader.ParentName);
            Assert.AreEqual(NbtTagType.List, reader.ParentTagType);
            Assert.AreEqual(1, reader.ParentTagLength);
            Assert.AreEqual(0, reader.TagLength);
            Assert.AreEqual(10, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing("fifth"));
            Assert.AreEqual("fifth", reader.TagName);
            Assert.AreEqual(NbtTagType.Int, reader.TagType);
            Assert.AreEqual(NbtTagType.Unknown, reader.ListType);
            Assert.IsTrue(reader.HasValue);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsFalse(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(2, reader.Depth);
            Assert.AreEqual("root", reader.ParentName);
            Assert.AreEqual(NbtTagType.Compound, reader.ParentTagType);
            Assert.AreEqual(0, reader.ParentTagLength);
            Assert.AreEqual(0, reader.TagLength);
            Assert.AreEqual(18, reader.TagsRead);

            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("hugeArray", reader.TagName);
            Assert.AreEqual(NbtTagType.ByteArray, reader.TagType);
            Assert.AreEqual(NbtTagType.Unknown, reader.ListType);
            Assert.IsTrue(reader.HasValue);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsTrue(reader.HasLength);
            Assert.AreEqual(0, reader.ListIndex);
            Assert.AreEqual(2, reader.Depth);
            Assert.AreEqual("root", reader.ParentName);
            Assert.AreEqual(NbtTagType.Compound, reader.ParentTagType);
            Assert.AreEqual(0, reader.ParentTagLength);
            Assert.AreEqual(1024 * 1024, reader.TagLength);
            Assert.AreEqual(19, reader.TagsRead);
        }


        [TestMethod]
        public void ReadToSiblingTest() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("root", reader.TagName);
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("first", reader.TagName);
            Assert.IsTrue(reader.ReadToNextSibling("third-comp"));
            Assert.AreEqual("third-comp", reader.TagName);
            Assert.IsTrue(reader.ReadToNextSibling());
            Assert.AreEqual("fourth-list", reader.TagName);
            Assert.IsTrue(reader.ReadToNextSibling());
            Assert.AreEqual("fifth", reader.TagName);
            Assert.IsTrue(reader.ReadToNextSibling());
            Assert.AreEqual("hugeArray", reader.TagName);
            Assert.IsFalse(reader.ReadToNextSibling());
            // Test twice, since we hit different paths through the code
            Assert.IsFalse(reader.ReadToNextSibling());
        }


        [TestMethod]
        public void ReadToSiblingTest2() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            Assert.IsTrue(reader.ReadToFollowing("inComp1"));
            // Expect all siblings to be read while we search for a non-existent one
            Assert.IsFalse(reader.ReadToNextSibling("no such tag"));
            // Expect to pop out of "third-comp" by now
            Assert.AreEqual("fourth-list", reader.TagName);
        }


        [TestMethod]
        public void ReadToFollowingNotFound() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            Assert.IsTrue(reader.ReadToFollowing()); // at "root"
            Assert.IsFalse(reader.ReadToFollowing("no such tag"));
            Assert.IsFalse(reader.ReadToFollowing("not this one either"));
            Assert.IsTrue(reader.IsAtStreamEnd);
        }


        [TestMethod]
        public void ReadToDescendantTest() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            Assert.IsTrue(reader.ReadToDescendant("third-comp"));
            Assert.AreEqual("third-comp", reader.TagName);
            Assert.IsTrue(reader.ReadToDescendant("inComp2"));
            Assert.AreEqual("inComp2", reader.TagName);
            Assert.IsFalse(reader.ReadToDescendant("derp"));
            Assert.AreEqual("inComp3", reader.TagName);
            reader.ReadToFollowing(); // at fourth-list
            Assert.IsTrue(reader.ReadToDescendant("inList2"));
            Assert.AreEqual("inList2", reader.TagName);

            // Read through the rest of the file until we run out of tags in a compound
            Assert.IsFalse(reader.ReadToDescendant("*"));

            // Ensure ReadToDescendant returns false when at end-of-stream
            while (reader.ReadToFollowing()) { }
            Assert.IsFalse(reader.ReadToDescendant("*"));

            // Ensure that this works even on the root
            Assert.IsFalse(new NbtReader(TestFiles.MakeReaderTest()).ReadToDescendant("*"));
        }


        [TestMethod]
        public void SkipTest() {
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            reader.ReadToFollowing(); // at root
            reader.ReadToFollowing(); // at first
            reader.ReadToFollowing(); // at second
            reader.ReadToFollowing(); // at third-comp
            reader.ReadToFollowing(); // at inComp1
            Assert.AreEqual("inComp1", reader.TagName);
            Assert.AreEqual(2, reader.Skip());
            Assert.AreEqual("fourth-list", reader.TagName);
            Assert.AreEqual(11, reader.Skip());
            Assert.IsFalse(reader.ReadToFollowing());
            Assert.AreEqual(0, reader.Skip());
        }


        [TestMethod]
        public void ReadAsTagTest1() {
            // read various lists/compounds as tags
            var reader = new NbtReader(TestFiles.MakeReaderTest());
            reader.ReadToFollowing(); // skip root
            while (!reader.IsAtStreamEnd) {
                reader.ReadAsTag();
            }
            Assert.Throws<EndOfStreamException>(() => reader.ReadAsTag());
        }


        [TestMethod]
        public void ReadAsTagTest2() {
            // read the whole thing as one tag
            byte[] testData = new NbtFile(TestFiles.MakeValueTest()).SaveToBuffer(NbtCompression.None);
            {
                NbtReader reader = TestFiles.OpenReader(testData);
                var root = (NbtCompound)reader.ReadAsTag();
                TestFiles.AssertValueTest(new NbtFile(root));
            }
            {
                // Try the same thing but with end tag skipping disabled
                NbtReader reader = TestFiles.OpenReader(testData);
                reader.SkipEndTags = false;
                var root = (NbtCompound)reader.ReadAsTag();
                TestFiles.AssertValueTest(new NbtFile(root));
            }
        }


        [TestMethod]
        public void ReadAsTagTest3() {
            // read values as tags
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeValueTest());
            var root = new NbtCompound("root");

            // skip root
            reader.ReadToFollowing();
            reader.ReadToFollowing();

            while (!reader.IsAtStreamEnd) {
                root.Add(reader.ReadAsTag());
            }

            TestFiles.AssertValueTest(new NbtFile(root));
        }


        [TestMethod]
        public void ReadAsTagTest4() {
            // read a bunch of lists as tags
            NbtCompound expected = TestFiles.MakeListTest();
            byte[] testData = new NbtFile(expected).SaveToBuffer(NbtCompression.None);

            // first, read the whole document at once
            {
                NbtReader reader = TestFiles.OpenReader(testData);
                NbtAssert.AreEqual(expected, reader.ReadAsTag());
                Assert.IsTrue(reader.IsAtStreamEnd);
            }

            // next, read each list individually
            {
                var expectedLists = new List<NbtTag>(expected);
                NbtReader reader = TestFiles.OpenReader(testData);
                reader.ReadToFollowing(); // read to root
                reader.ReadToFollowing(); // read to first list tag
                int index = 0;
                while (!reader.IsAtStreamEnd) {
                    NbtAssert.AreEqual(expectedLists[index], reader.ReadAsTag());
                    index++;
                }
                Assert.AreEqual(expectedLists.Count, index);
            }
        }


        [TestMethod]
        public void ReadListAsArray() {
            NbtCompound intList = TestFiles.MakeListTest();

            NbtReader reader = TestFiles.OpenReader(intList);

            // attempt to read value before we're in a list
            Assert.Throws<InvalidOperationException>(() => reader.ReadListAsArray<int>());

            // test byte values
            reader.ReadToFollowing("ByteList");
            byte[] bytes = reader.ReadListAsArray<byte>();
            CollectionAssert.AreEqual(new byte[] { 100, 20, 3 }, bytes);

            // test double values
            reader.ReadToFollowing("DoubleList");
            double[] doubles = reader.ReadListAsArray<double>();
            CollectionAssert.AreEqual(new[] { 1d, 2000d, -3000000d }, doubles);

            // test float values
            reader.ReadToFollowing("FloatList");
            float[] floats = reader.ReadListAsArray<float>();
            CollectionAssert.AreEqual(new[] { 1f, 2000f, -3000000f }, floats);

            // test int values
            reader.ReadToFollowing("IntList");
            int[] ints = reader.ReadListAsArray<int>();
            CollectionAssert.AreEqual(new[] { 1, 2000, -3000000 }, ints);

            // test long values
            reader.ReadToFollowing("LongList");
            long[] longs = reader.ReadListAsArray<long>();
            CollectionAssert.AreEqual(new[] { 1L, 2000L, -3000000L }, longs);

            // test short values
            reader.ReadToFollowing("ShortList");
            short[] shorts = reader.ReadListAsArray<short>();
            CollectionAssert.AreEqual(new short[] { 1, 200, -30000 }, shorts);

            // test string values
            reader.ReadToFollowing("StringList");
            string[] strings = reader.ReadListAsArray<string>();
            CollectionAssert.AreEqual(new[] { "one", "two thousand", "negative three million" }, strings);

            // try reading list of compounds (should fail)
            reader.ReadToFollowing("CompoundList");
            Assert.Throws<InvalidOperationException>(() => reader.ReadListAsArray<NbtCompound>());
            // That failed call should have been recoverable. Assert that we didn't desync or enter bad state.
            Assert.IsFalse(reader.IsInErrorState);
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual(NbtTagType.Compound, reader.TagType);

            // skip to the end of the stream
            while (reader.ReadToFollowing()) { }
            Assert.Throws<EndOfStreamException>(() => reader.ReadListAsArray<int>());
        }


        [TestMethod]
        public void ReadListAsArrayOversizedCountThrows() {
            // An oversized list count must not allocate a huge array before reading.
            // On a seekable stream the bound catches it up front.
            byte[] doc;
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A); // root compound ""
                ms.WriteByte(0);
                ms.WriteByte(0);
                ms.WriteByte(0x09); // TAG_List "l"
                ms.WriteByte(0);
                ms.WriteByte(1);
                ms.WriteByte((byte)'l');
                ms.WriteByte(0x03); // element type Int
                ms.WriteByte(0x7F); // count 0x7FFFFFFF
                ms.WriteByte(0xFF);
                ms.WriteByte(0xFF);
                ms.WriteByte(0xFF);
                ms.WriteByte(0x00); // root End
                doc = ms.ToArray();
            }
            NbtReader reader = TestFiles.OpenReader(doc);
            reader.ReadToFollowing("l");
            Assert.Throws<EndOfStreamException>(() => reader.ReadListAsArray<int>());
            // Partial reads due to bad count are not recoverable.
            Assert.IsTrue(reader.IsInErrorState);
        }


        [TestMethod]
        public void ReadListAsArrayTwiceReturnsEmpty() {
            // Reading a value list to completion, then calling again, must return an empty array
            // rather than reading past the list end.
            NbtCompound intList = TestFiles.MakeListTest();
            NbtReader reader = TestFiles.OpenReader(intList);

            reader.ReadToFollowing("IntList");
            int[] first = reader.ReadListAsArray<int>();
            Assert.AreEqual(3, first.Length);
            int[] second = reader.ReadListAsArray<int>();
            Assert.AreEqual(0, second.Length);
            Assert.IsFalse(reader.IsInErrorState);
        }


        [TestMethod]
        public void ReadListAsArrayIncludesPublishedUnreadElement() {
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeListTest());

            Assert.IsTrue(reader.ReadToFollowing("IntList"));
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual(NbtTagType.Int, reader.TagType);
            Assert.AreEqual(0, reader.ListIndex);
            int tagsReadBefore = reader.TagsRead;

            int[] remaining = reader.ReadListAsArray<int>();

            CollectionAssert.AreEqual(new[] { 1, 2000, -3000000 }, remaining);
            Assert.AreEqual(tagsReadBefore + 2, reader.TagsRead);
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("LongList", reader.TagName);
        }


        [TestMethod]
        public void ReadListAsArrayExcludesPublishedConsumedElement() {
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeListTest());

            Assert.IsTrue(reader.ReadToFollowing("IntList"));
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual(1, reader.ReadValue());
            int tagsReadBefore = reader.TagsRead;

            int[] remaining = reader.ReadListAsArray<int>();

            CollectionAssert.AreEqual(new[] { 2000, -3000000 }, remaining);
            Assert.AreEqual(tagsReadBefore + 2, reader.TagsRead);
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("LongList", reader.TagName);

            // Nothing left after the last element's value was consumed
            reader = TestFiles.OpenReader(TestFiles.MakeListTest());
            Assert.IsTrue(reader.ReadToFollowing("IntList"));
            for (int i = 0; i < 3; i++) {
                Assert.IsTrue(reader.ReadToFollowing());
            }
            Assert.AreEqual(-3000000, reader.ReadValue());
            Assert.AreEqual(0, reader.ReadListAsArray<int>().Length);
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual("LongList", reader.TagName);
        }


        [TestMethod]
        public void ReadEmptyListAsArrayKeepsCursorOnTheList() {
            var root = new NbtCompound("root") {
                new NbtList("empty", NbtTagType.Int),
                new NbtByte("after", 1)
            };
            NbtReader reader = TestFiles.OpenReader(root);

            Assert.IsTrue(reader.ReadToFollowing("empty"));
            int depth = reader.Depth;
            Assert.AreEqual(0, reader.ReadListAsArray<int>().Length);
            Assert.AreEqual("empty", reader.TagName);
            Assert.AreEqual(NbtTagType.List, reader.TagType);
            Assert.AreEqual(depth, reader.Depth);

            // Still a list tag as far as navigation is concerned
            Assert.IsTrue(reader.ReadToNextSibling());
            Assert.AreEqual("after", reader.TagName);
        }


        [TestMethod]
        public void ReadListAsArrayRecast() {
            NbtCompound intList = TestFiles.MakeListTest();

            NbtReader reader = TestFiles.OpenReader(intList);

            // test bytes as shorts
            reader.ReadToFollowing("ByteList");
            short[] bytes = reader.ReadListAsArray<short>();
            CollectionAssert.AreEqual(new short[] { 100, 20, 3 }, bytes);
        }


        [TestMethod]
        public void ReadValueTest() {
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeValueTest());

            Assert.IsTrue(reader.ReadToFollowing()); // root

            Assert.IsTrue(reader.ReadToFollowing()); // byte
            Assert.AreEqual((byte)1, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // short
            Assert.AreEqual((short)2, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // int
            Assert.AreEqual(3, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // long
            Assert.AreEqual(4L, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // float
            Assert.AreEqual(5f, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // double
            Assert.AreEqual(6d, reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // byteArray
            CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, (byte[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // intArray
            CollectionAssert.AreEqual(new[] { 20, 21, 22 }, (int[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // longArray
            CollectionAssert.AreEqual(new long[] { 200, 210, 220 }, (long[])reader.ReadValue());
            Assert.IsTrue(reader.ReadToFollowing()); // string
            Assert.AreEqual("123", reader.ReadValue());

            // Skip to the very end and make sure that we can't read any more values
            reader.ReadToFollowing();
            Assert.Throws<EndOfStreamException>(() => reader.ReadValue());
        }


        [TestMethod]
        public void ReadValueAsTest() {
            NbtReader reader = TestFiles.OpenReader(TestFiles.MakeValueTest());

            Assert.IsTrue(reader.ReadToFollowing()); // root

            Assert.IsTrue(reader.ReadToFollowing()); // byte
            Assert.AreEqual(1, reader.ReadValueAs<byte>());
            Assert.IsTrue(reader.ReadToFollowing()); // short
            Assert.AreEqual(2, reader.ReadValueAs<short>());
            Assert.IsTrue(reader.ReadToFollowing()); // int
            Assert.AreEqual(3, reader.ReadValueAs<int>());
            Assert.IsTrue(reader.ReadToFollowing()); // long
            Assert.AreEqual(4L, reader.ReadValueAs<long>());
            Assert.IsTrue(reader.ReadToFollowing()); // float
            Assert.AreEqual(5f, reader.ReadValueAs<float>());
            Assert.IsTrue(reader.ReadToFollowing()); // double
            Assert.AreEqual(6d, reader.ReadValueAs<double>());
            Assert.IsTrue(reader.ReadToFollowing()); // byteArray
            CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, reader.ReadValueAs<byte[]>());
            Assert.IsTrue(reader.ReadToFollowing()); // intArray
            CollectionAssert.AreEqual(new[] { 20, 21, 22 }, reader.ReadValueAs<int[]>());
            Assert.IsTrue(reader.ReadToFollowing()); // longArray
            CollectionAssert.AreEqual(new long[] { 200, 210, 220 }, reader.ReadValueAs<long[]>());
            Assert.IsTrue(reader.ReadToFollowing()); // string
            Assert.AreEqual("123", reader.ReadValueAs<string>());
        }


        [TestMethod]
        public void ErrorTest() {
            var root = new NbtCompound("root");
            byte[] testData = new NbtFile(root).SaveToBuffer(NbtCompression.None);

            // creating NbtReader without a stream, or with a non-readable stream
            Assert.Throws<ArgumentNullException>(() => new NbtReader(null));
            Assert.Throws<ArgumentException>(() => new NbtReader(new NonReadableStream()));

            // corrupt the data
            testData[0] = 123;
            NbtReader reader = TestFiles.OpenReader(testData);

            // attempt to use ReadValue when not at value
            Assert.Throws<InvalidOperationException>(() => reader.ReadValue());
            reader.CacheTagValues = true;
            Assert.Throws<InvalidOperationException>(() => reader.ReadValue());

            // attempt to read a corrupt stream
            Assert.Throws<NbtFormatException>(() => reader.ReadToFollowing());

            // make sure we've properly entered the error state
            NbtAssert.ReaderIsPoisoned(reader);
            Assert.IsFalse(reader.HasName);
        }


        [TestMethod]
        public void FailedValueReadEntersErrorState() {
            // A read that fails partway through a payload leaves the stream desynchronised,
            // so the reader must poison itself instead of parsing from mid-payload
            byte[] doc = new NbtFile(new NbtCompound("") {
                new NbtByteArray("a", new byte[65536])
            }).SaveToBuffer(NbtCompression.None);
            var options = new NbtOptions { MaxAllocation = 1024 };

            NbtReader reader = TestFiles.OpenReader(doc, options);
            reader.ReadToFollowing();
            reader.ReadToFollowing();
            Assert.Throws<NbtFormatException>(() => reader.ReadValue());
            NbtAssert.ReaderIsPoisoned(reader);

            // Same through the ReadAsTag path
            reader = TestFiles.OpenReader(doc, options);
            reader.ReadToFollowing();
            reader.ReadToFollowing();
            Assert.Throws<NbtFormatException>(() => reader.ReadAsTag());
            NbtAssert.ReaderIsPoisoned(reader);
        }


        [TestMethod]
        public void FailedListArrayReadPoisonsReadValue() {
            byte[] doc = new NbtFile(new NbtCompound("r") {
                new NbtList("i") { new NbtInt(1), new NbtInt(2), new NbtInt(3) }
            }).SaveToBuffer(NbtCompression.None);
            var options = new NbtOptions { MaxAllocation = 5 };

            // The current payload is still unread when the bulk allocation is refused
            NbtReader reader = TestFiles.OpenReader(doc, options);
            Assert.IsTrue(reader.ReadToFollowing("i"));
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.Throws<NbtFormatException>(() => reader.ReadListAsArray<int>());
            NbtAssert.ReaderIsPoisoned(reader);

            // A cached value must not leak back out after the reader is poisoned either
            reader = TestFiles.OpenReader(doc, options);
            reader.CacheTagValues = true;
            Assert.IsTrue(reader.ReadToFollowing("i"));
            Assert.IsTrue(reader.ReadToFollowing());
            Assert.AreEqual(1, reader.ReadValue());
            Assert.Throws<NbtFormatException>(() => reader.ReadListAsArray<int>());
            NbtAssert.ReaderIsPoisoned(reader);
        }


        [TestMethod]
        public void MaxAllocationCapsListAsArrayReads() {
            byte[] doc;
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r");
                writer.BeginList("longs", NbtTagType.Long, 1000);
                for (int i = 0; i < 1000; i++) {
                    writer.WriteLong(i);
                }
                writer.EndList();
                writer.BeginList("strings", NbtTagType.String, 2000);
                for (int i = 0; i < 2000; i++) {
                    writer.WriteString("");
                }
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
                doc = ms.ToArray();
            }

            // 1,000 longs need an 8,000-byte array
            NbtReader reader = TestFiles.OpenReader(doc, new NbtOptions { MaxAllocation = 1024 });
            Assert.IsTrue(reader.ReadToFollowing("longs"));
            Assert.Throws<NbtFormatException>(() => reader.ReadListAsArray<long>());

            // 2,000 empty strings are only 4,000 payload bytes, but the result array alone
            // holds 2,000 references, which the estimate must count at pointer size
            reader = TestFiles.OpenReader(doc, new NbtOptions { MaxAllocation = 4096 });
            Assert.IsTrue(reader.ReadToFollowing("strings"));
            Assert.Throws<NbtFormatException>(() => reader.ReadListAsArray<string>());

            // With room to spare, both read fine
            reader = TestFiles.OpenReader(doc, new NbtOptions { MaxAllocation = 64 * 1024 });
            Assert.IsTrue(reader.ReadToFollowing("longs"));
            Assert.AreEqual(1000, reader.ReadListAsArray<long>().Length);
            Assert.IsTrue(reader.ReadToFollowing("strings"));
            Assert.AreEqual(2000, reader.ReadListAsArray<string>().Length);
        }


        [TestMethod]
        public void NonSeekableStreamSkip() {
            // The buffered skip fallback must count the same tags the seekable path does
            byte[] fileBytes = File.ReadAllBytes(TestFiles.Big);
            using (var nss = new NonSeekableStream(new MemoryStream(fileBytes))) {
                var reader = new NbtReader(nss);
                reader.ReadToFollowing();
                Assert.AreEqual(30, reader.Skip());
            }
            using (var nss = new NonSeekableStream(TestFiles.MakeReaderTest())) {
                var reader = new NbtReader(nss);
                reader.ReadToFollowing();
                Assert.AreEqual(18, reader.Skip());
            }
        }


        [TestMethod]
        public void EndOfStreamFileRead() {
            byte[] data = {
                0x0A, // Compound tag
                0x00, 0x02, 0x66, 0x4E, // Root name 'fN'
                0x00 // end tag
            };

            for (int i = 0; i < data.Length; i++) {
                var partialData = new byte[i];
                Array.Copy(data,partialData,i);
                TryReadIncompleteFile(partialData);
                if (i < 5)
                    TryReadIncompleteRootTagName(partialData);
            }
        }


        static void TryReadIncompleteRootTagName(byte[] partialData) {

            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(partialData), NbtCompression.None, NbtFlavor.Java), "Length=" + partialData.Length);
            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(partialData), NbtCompression.AutoDetect, NbtFlavor.Java), "Length=" + partialData.Length);
        }


        static void TryReadIncompleteFile(byte[] partialData) {

            Assert.Throws<EndOfStreamException>(() => TryReadBadFile(partialData));
            Assert.Throws<EndOfStreamException>(() => TestFiles.Load(partialData));
            Assert.Throws<EndOfStreamException>(
                () => new NbtFile().LoadFromBuffer(partialData, 0, partialData.Length, NbtCompression.AutoDetect));
        }


        [TestMethod]
        public void CorruptFileRead() {
            byte[] badHeader = {
                0x02, // TAG_Short ID (instead of TAG_Compound ID)
                0x00, 0x01, 0x66, // Root name: 'f'
                0x00 // end tag
            };
            Assert.Throws<NbtFormatException>(() => TryReadBadFile(badHeader));
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(badHeader));
            Assert.Throws<NbtFormatException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(badHeader), NbtCompression.None, NbtFlavor.Java));

            byte[] badStringLength = {
                0x0A, // Compound tag
                0xFF, 0xFF, 0x66, // Root name 'f' (string length prefix 0xFFFF = 65535 unsigned bytes)
                0x00 // end tag
            };
            // The prefix is unsigned, so 0xFFFF is a valid 65535-byte length. The string is
            // truncated, so it fails with EndOfStreamException rather than a negative-length error.
            Assert.Throws<EndOfStreamException>(() => TryReadBadFile(badStringLength));
            Assert.Throws<EndOfStreamException>(() => TestFiles.Load(badStringLength));
            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(badStringLength), NbtCompression.None, NbtFlavor.Java));

            byte[] badSecondTag = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0xFF, 0x01, 0x4E, 0x7F, 0xFF, // Short tag named 'N' with invalid tag ID (0xFF instead of 0x02)
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badSecondTag);

            // The list's element type must be valid when elements follow it. The document is
            // otherwise complete, so the type byte is the only defect.
            byte[] badListType = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x09, // List tag
                0x00, 0x01, 0x67, // List tag name: 'g'
                0xFF, // invalid list tag type
                0x00, 0x00, 0x00, 0x01, // List size: 1
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badListType);

            // Negative sizes read as empty since 2.0, so an impossibly large size is the bad case
            byte[] badListSize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x09, // List tag
                0x00, 0x01, 0x67, // List tag name: 'g'
                0x01, // List type: Byte
                0x7F, 0x00, 0x00, 0x00, // List size: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badListSize);
        }


        [TestMethod]
        public void BadArraySize() {
            // Negative sizes read as empty since 2.0, so impossibly large sizes are the bad case
            byte[] badByteArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x07, // ByteArray tag
                0x00, 0x01, 0x67, // ByteArray tag name: 'g'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badByteArraySize);


            byte[] badIntArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0b, // IntArray tag
                0x00, 0x01, 0x66, // IntArray tag name: 'f'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badIntArraySize);

            byte[] badLongArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0c, // LongArray tag
                0x00, 0x01, 0x66, // LongArray tag name: 'f'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badLongArraySize);
        }


        [TestMethod]
        public void BadNestedArraySize() {
            // Negative sizes read as empty since 2.0, so impossibly large sizes are the bad case
            byte[] badNestedByteArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x07, // ByteArray tag
                0x00, 0x01, 0x68, // ByteArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedByteArraySize);


            byte[] badNestedIntArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x0b, // IntArray tag
                0x00, 0x01, 0x68, // IntArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedIntArraySize);

            byte[] badNestedLongArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x0c, // LongArray tag
                0x00, 0x01, 0x68, // LongArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedLongArraySize);
        }


        [TestMethod]
        public void PartialReadTest() {
            // read the whole thing as one tag, one byte at a time
            TestFiles.AssertValueTest(PartialReadTestInternal(new NbtFile(TestFiles.MakeValueTest())));
            TestFiles.AssertNbtSmallFile(PartialReadTestInternal(TestFiles.MakeSmallFile()));
            TestFiles.AssertNbtBigFile(PartialReadTestInternal(new NbtFile(TestFiles.Big)));
        }


        [TestMethod]
        public void PartialBatchReadTest() {
            // read the whole thing as one tag, in batches of 4 bytes
            // Verifies fix for https://github.com/fragmer/fNbt/issues/26
            TestFiles.AssertValueTest(PartialReadTestInternal(new NbtFile(TestFiles.MakeValueTest()), 4));
            TestFiles.AssertNbtSmallFile(PartialReadTestInternal(TestFiles.MakeSmallFile(), 4));
            TestFiles.AssertNbtBigFile(PartialReadTestInternal(new NbtFile(TestFiles.Big), 4));
        }


        [TestMethod]
        public void EndTagTest() {
            var root = new NbtCompound("root") {
                new NbtInt("test", 0)
            };

            NbtReader reader = TestFiles.OpenReader(root);
            reader.SkipEndTags = false;
            reader.ReadToDescendant("test");
            Assert.AreEqual(NbtTagType.Int, reader.TagType);
            Assert.IsTrue(reader.ReadToNextSibling());

            // should be at root's End tag now
            Assert.AreEqual(NbtTagType.End, reader.TagType);
            Assert.IsFalse(reader.IsInErrorState);
            Assert.IsFalse(reader.IsAtStreamEnd);
            Assert.IsFalse(reader.IsCompound);
            Assert.IsFalse(reader.IsList);
            Assert.IsFalse(reader.IsListElement);
            Assert.IsFalse(reader.HasValue);
            Assert.IsFalse(reader.HasName);
            Assert.IsFalse(reader.HasLength);
            Assert.Throws<InvalidOperationException>(() => reader.ReadAsTag()); // Cannot create NbtTag from TAG_END

            // We done now
            Assert.IsFalse(reader.ReadToFollowing());
            Assert.IsTrue(reader.IsAtStreamEnd);
        }


        static NbtFile PartialReadTestInternal(NbtFile comp, int increment = 1) {
            byte[] testData = comp.SaveToBuffer(NbtCompression.None);
            var reader = new NbtReader(new PartialReadStream(new MemoryStream(testData), increment));
            var root = (NbtCompound)reader.ReadAsTag();
            return new NbtFile(root);
        }


        void AssertBadFileFromBuffer(byte[] input) {
            // Corrupt input fails as a format error, or as a premature end of stream when a
            // tolerated length (e.g. a negative array size read as empty) leaves the rest of
            // the document truncated
            AssertThrowsParseError(() => TryReadBadFile(input));
            AssertThrowsParseError(() => TestFiles.Load(input));
            AssertThrowsParseError(() => TestFiles.Load(input, selector: tag => false));
        }


        static void AssertThrowsParseError(Action action) {
            try {
                action();
            } catch (NbtFormatException) {
                return;
            } catch (EndOfStreamException) {
                return;
            }
            Assert.Fail("Expected NbtFormatException or EndOfStreamException.");
        }


        static void TryReadBadFile(byte[] data) {
            using (MemoryStream ms = new MemoryStream(data)) {
                NbtReader reader = new NbtReader(ms);
                try {
                    while (reader.ReadToFollowing()) { }
                } catch (Exception) {
                    Assert.IsTrue(reader.IsInErrorState);
                    throw;
                }
            }
        }
    }
}
