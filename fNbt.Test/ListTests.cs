using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace fNbt.Test {
    [TestClass]
    public sealed class ListTests {
        [TestMethod]
        public void InterfaceImplementation() {
            // prepare our test lists
            var referenceList = new List<NbtTag> {
                new NbtInt(1),
                new NbtInt(2),
                new NbtInt(3)
            };
            var testTag = new NbtInt(4);
            var originalList = new NbtList(referenceList);

            // check IList implementation
            IList iList = originalList;
            CollectionAssert.AreEqual(referenceList, iList);

            // check IList<NbtTag> implementation
            IList<NbtTag> iGenericList = originalList;
            ListAssert.AreEqual(referenceList, iGenericList, NbtComparer.Instance);
            Assert.IsFalse(iGenericList.IsReadOnly);

            // check IList.Add
            referenceList.Add(testTag);
            iList.Add(testTag);
            CollectionAssert.AreEqual(referenceList, iList);

            // check IList.IndexOf
            Assert.AreEqual(referenceList.IndexOf(testTag), iList.IndexOf(testTag));
            Assert.IsTrue(iList.IndexOf(null) < 0);

            // check IList<NbtTag>.IndexOf
            Assert.AreEqual(referenceList.IndexOf(testTag), iGenericList.IndexOf(testTag));
            Assert.IsTrue(iGenericList.IndexOf(null) < 0);

            // check IList.Contains
            Assert.IsTrue(iList.Contains(testTag));
            Assert.IsFalse(iList.Contains(null));

            // check IList.Remove
            iList.Remove(testTag);
            Assert.IsFalse(iList.Contains(testTag));

            // check IList.Insert
            iList.Insert(0, testTag);
            Assert.AreEqual(0, iList.IndexOf(testTag));

            // check IList.RemoveAt
            iList.RemoveAt(0);
            Assert.IsFalse(iList.Contains(testTag));

            // check misc IList properties
            Assert.IsFalse(iList.IsFixedSize);
            Assert.IsFalse(iList.IsReadOnly);
            Assert.IsFalse(iList.IsSynchronized);
            Assert.IsNotNull(iList.SyncRoot);

            // check IList.CopyTo
            var exportTest = new NbtInt[iList.Count];
            iList.CopyTo(exportTest, 0);
            CollectionAssert.AreEqual(iList, exportTest);

            // check IList.this[int]
            for (int i = 0; i < iList.Count; i++) {
                iList[i] = new NbtInt(i);
                Assert.AreEqual(i, ((NbtInt)iList[i]).Value);
            }

            // check IList.Clear
            iList.Clear();
            Assert.AreEqual(0, iList.Count);
            Assert.AreEqual(-1, iList.IndexOf(testTag));
        }


        [TestMethod]
        public void IndexerRejectsBadElements() {
            NbtByte ourTag = new NbtByte(1);
            var secondList = new NbtList {
                new NbtByte()
            };

            var testList = new NbtList();
            // Trying to set an out-of-range element
            Assert.Throws<ArgumentOutOfRangeException>(() => testList[0] = new NbtByte(1));

            // Make sure that setting did not affect ListType
            Assert.AreEqual(NbtTagType.Unknown, testList.ListType);
            Assert.AreEqual(0, testList.Count);
            testList.Add(ourTag);

            // set a tag to null
            Assert.Throws<ArgumentNullException>(() => testList[0] = null);

            // set a tag to itself
            Assert.Throws<ArgumentException>(() => testList[0] = testList);

            // give a named tag where an unnamed tag was expected
            Assert.Throws<ArgumentException>(() => testList[0] = new NbtByte("NamedTag"));

            // give a tag of wrong type
            Assert.Throws<ArgumentException>(() => testList[0] = new NbtInt(0));

            // give an unnamed tag that already has a parent
            Assert.Throws<ArgumentException>(() => testList[0] = secondList[0]);

            // Make sure that none of the failed insertions went through
            Assert.AreEqual(ourTag, testList[0]);
        }


        [TestMethod]
        public void InitializingListFromCollection() {
            // auto-detecting list type
            var test1 = new NbtList("Test1", new NbtTag[] {
                new NbtInt(1),
                new NbtInt(2),
                new NbtInt(3)
            });
            Assert.AreEqual(NbtTagType.Int, test1.ListType);

            // check pre-conditions
            Assert.Throws<ArgumentNullException>(() => new NbtList((NbtTag[])null));
            Assert.Throws<ArgumentNullException>(() => new NbtList(null, null));
            _ = new NbtList((string)null, NbtTagType.Unknown); // does not throw, but creates an empty list
            Assert.Throws<ArgumentNullException>(() => new NbtList((NbtTag[])null, NbtTagType.Unknown));

            // correct explicitly-given list type
            var typed = new NbtList("Test2", new NbtTag[] {
                new NbtInt(1),
                new NbtInt(2),
                new NbtInt(3)
            }, NbtTagType.Int);
            Assert.AreEqual(NbtTagType.Int, typed.ListType);
            Assert.AreEqual(3, typed.Count);

            // wrong explicitly-given list type
            Assert.Throws<ArgumentException>(() => new NbtList("Test3", new NbtTag[] {
                new NbtInt(1),
                new NbtInt(2),
                new NbtInt(3)
            }, NbtTagType.Float));

            // auto-detecting mixed list given
            Assert.Throws<ArgumentException>(() => new NbtList("Test4", new NbtTag[] {
                new NbtFloat(1),
                new NbtByte(2),
                new NbtInt(3)
            }));

            // using AddRange
            var ranged = new NbtList();
            ranged.AddRange(new NbtTag[] {
                new NbtInt(1),
                new NbtInt(2),
                new NbtInt(3)
            });
            Assert.AreEqual(NbtTagType.Int, ranged.ListType);
            Assert.AreEqual(3, ranged.Count);
            Assert.Throws<ArgumentNullException>(() => new NbtList().AddRange(null));
        }


        [TestMethod]
        public void ManipulatingList() {
            var sameTags = new NbtTag[] {
                new NbtInt(0),
                new NbtInt(1),
                new NbtInt(2)
            };

            var list = new NbtList("Test1", sameTags);

            // testing enumerator, indexer, Contains, and IndexOf
            int j = 0;
            foreach (NbtTag tag in list) {
                Assert.IsTrue(list.Contains(sameTags[j]));
                Assert.AreEqual(sameTags[j], tag);
                Assert.AreEqual(j, list.IndexOf(tag));
                j++;
            }

            // adding an item of correct type
            list.Add(new NbtInt(3));
            list.Insert(3, new NbtInt(4));

            // adding an item of wrong type
            Assert.Throws<ArgumentException>(() => list.Add(new NbtString()));
            Assert.Throws<ArgumentException>(() => list.Insert(3, new NbtString()));
            Assert.Throws<ArgumentNullException>(() => list.Insert(3, null));

            // testing array contents: the insert landed at its index, ahead of the appended tag
            for (int i = 0; i < sameTags.Length; i++) {
                Assert.AreSame(sameTags[i], list[i]);
                Assert.AreEqual(i, ((NbtInt)list[i]).Value);
            }
            Assert.AreEqual(4, ((NbtInt)list[3]).Value);
            Assert.AreEqual(3, ((NbtInt)list[4]).Value);

            // test removal
            Assert.IsFalse(list.Remove(new NbtInt(5)));
            Assert.IsTrue(list.Remove(sameTags[0]));
            Assert.Throws<ArgumentNullException>(() => list.Remove(null));
            list.RemoveAt(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(10));

            // Test some failure scenarios for Add:
            // adding a list to itself
            var loopList = new NbtList();
            Assert.AreEqual(NbtTagType.Unknown, loopList.ListType);
            Assert.Throws<ArgumentException>(() => loopList.Add(loopList));

            // adding same tag to multiple lists
            Assert.Throws<ArgumentException>(() => loopList.Add(list[0]));
            Assert.Throws<ArgumentException>(() => loopList.Insert(0, list[0]));

            // adding null tag
            Assert.Throws<ArgumentNullException>(() => loopList.Add(null));

            // make sure that all those failed adds didn't affect the tag
            Assert.AreEqual(0, loopList.Count);
            Assert.AreEqual(NbtTagType.Unknown, loopList.ListType);

            // try creating a list with invalid tag type
            Assert.Throws<ArgumentOutOfRangeException>(() => new NbtList((NbtTagType)200));
        }


        [TestMethod]
        public void ChangingListTagType() {
            var list = new NbtList();

            // changing list type to an out-of-range type
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ListType = (NbtTagType)200);

            // failing to add or insert a tag should not change ListType
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(-1, new NbtInt()));
            Assert.Throws<ArgumentException>(() => list.Add(new NbtInt("namedTagWhereUnnamedIsExpected")));
            Assert.AreEqual(NbtTagType.Unknown, list.ListType);

            // changing the type of an empty list to "End" is allowed, see https://github.com/fragmer/fNbt/issues/12
            list.ListType = NbtTagType.End;
            Assert.AreEqual(list.ListType, NbtTagType.End);

            // changing the type of an empty list back to "Unknown" is allowed too!
            list.ListType = NbtTagType.Unknown;
            Assert.AreEqual(list.ListType, NbtTagType.Unknown);

            // adding the first element should set the tag type
            list.Add(new NbtInt());
            Assert.AreEqual(list.ListType, NbtTagType.Int);

            // setting correct type for a non-empty list
            list.ListType = NbtTagType.Int;

            // changing list type to an incorrect type
            Assert.Throws<ArgumentException>(() => list.ListType = NbtTagType.Short);

            // after the list is cleared, we should once again be allowed to change its TagType
            list.Clear();
            list.ListType = NbtTagType.Short;
        }


        [TestMethod]
        public void ListWithoutTypeSavesAsEnd() {
            // A list that never committed to a type writes the element type Minecraft writes for
            // every empty list, and comes back End-typed like any other loaded empty list
            var root = new NbtCompound("root") {
                new NbtList("list")
            };
            NbtCompound reloaded = TestFiles.Reload(root);
            Assert.AreEqual(NbtTagType.End, reloaded.Get<NbtList>("list").ListType);
            NbtAssert.AreEqual(root, reloaded);
        }


        [TestMethod]
        public void ListTypeAndCountSurviveRoundTrip() {
            // check the basics of saving/loading
            const NbtTagType expectedListType = NbtTagType.Int;
            const int elements = 10;

            // construct nbt file
            var writtenFile = new NbtFile(new NbtCompound("ListTypeTest"));
            var writtenList = new NbtList("Entities", null, expectedListType);
            for (int i = 0; i < elements; i++) {
                writtenList.Add(new NbtInt(i));
            }
            writtenFile.RootTag.Add(writtenList);

            // test saving
            byte[] data = writtenFile.SaveToBuffer(NbtCompression.None);

            // test loading
            var readFile = new NbtFile();
            long bytesRead = readFile.LoadFromBuffer(data, 0, data.Length, NbtCompression.None);
            Assert.AreEqual(bytesRead, data.Length);

            // check contents of loaded file
            Assert.IsInstanceOfType<NbtList>(readFile.RootTag["Entities"]);
            var readList = (NbtList)readFile.RootTag["Entities"];
            Assert.AreEqual(writtenList.ListType, readList.ListType);
            Assert.AreEqual(writtenList.Count, readList.Count);

            // check .ToArray, in order
            CollectionAssert.AreEqual(readList, readList.ToArray());
            CollectionAssert.AreEqual(readList, readList.ToArray<NbtInt>());

            // check contents of loaded list
            for (int i = 0; i < elements; i++) {
                Assert.AreEqual(readList.Get<NbtInt>(i).Value, writtenList.Get<NbtInt>(i).Value);
            }
        }


        [TestMethod]
        public void ListsOfEveryTypeRoundTrip() {
            // check saving/loading lists of all possible value types
            var testFile = new NbtFile(TestFiles.MakeAllListsRoot());
            byte[] buffer = testFile.SaveToBuffer(NbtCompression.None);
            long bytesRead = testFile.LoadFromBuffer(buffer, 0, buffer.Length, NbtCompression.None);
            Assert.AreEqual(buffer.Length, bytesRead);
            NbtAssert.AreEqual(TestFiles.MakeAllListsRoot(), testFile.RootTag);
        }


        [TestMethod]
        public void SerializingEmpty() {
            // check saving/loading an empty list and a list holding one empty list
            NbtCompound root = TestFiles.Reload(new NbtCompound("root") {
                new NbtList("emptyList", NbtTagType.End),
                new NbtList("listyList", NbtTagType.List) {
                    new NbtList(NbtTagType.End)
                }
            });

            NbtList list1 = root.Get<NbtList>("emptyList");
            Assert.AreEqual(list1.Count, 0);
            Assert.AreEqual(list1.ListType, NbtTagType.End);

            NbtList list2 = root.Get<NbtList>("listyList");
            Assert.AreEqual(list2.Count, 1);
            Assert.AreEqual(list2.ListType, NbtTagType.List);
            Assert.AreEqual(list2.Get<NbtList>(0).Count, 0);
            Assert.AreEqual(list2.Get<NbtList>(0).ListType, NbtTagType.End);
        }


        [TestMethod]
        public void NestedListsAndCompoundsRoundTrip() {
            byte[] data;
            {
                var root = new NbtCompound("Root");
                var outerList = new NbtList("OuterList", NbtTagType.Compound);
                var outerCompound = new NbtCompound();
                var innerList = new NbtList("InnerList", NbtTagType.Compound);
                var innerCompound = new NbtCompound();

                innerList.Add(innerCompound);
                outerCompound.Add(innerList);
                outerList.Add(outerCompound);
                root.Add(outerList);

                var file = new NbtFile(root);
                data = file.SaveToBuffer(NbtCompression.None);
            }
            {
                var file = new NbtFile();
                long bytesRead = file.LoadFromBuffer(data, 0, data.Length, NbtCompression.None);
                Assert.AreEqual(bytesRead, data.Length);
                Assert.AreEqual(1, file.RootTag.Get<NbtList>("OuterList").Count);
                Assert.AreEqual(null, file.RootTag.Get<NbtList>("OuterList").Get<NbtCompound>(0).Name);
                Assert.AreEqual(1,
                                file.RootTag.Get<NbtList>("OuterList")
                                    .Get<NbtCompound>(0)
                                    .Get<NbtList>("InnerList")
                                    .Count);
                Assert.AreEqual(null,
                                file.RootTag.Get<NbtList>("OuterList")
                                    .Get<NbtCompound>(0)
                                    .Get<NbtList>("InnerList")
                                    .Get<NbtCompound>(0)
                                    .Name);
            }
        }


        [TestMethod]
        public void FirstInsertSetsListType() {
            NbtList list = new NbtList();
            Assert.AreEqual(NbtTagType.Unknown, list.ListType);
            list.Insert(0, new NbtInt(123));
            // Inserting a tag should set ListType
            Assert.AreEqual(NbtTagType.Int, list.ListType);
        }

        [TestMethod]
        public void ParsedListCapacityFollowsPlausibleDeclaredCount() {
            // Each element is nine bytes on the wire, more than the reference it costs up front
            var list = new NbtList("items", NbtTagType.Compound);
            for (int i = 0; i < 100; i++) list.Add(new NbtCompound { new NbtInt("v", i) });
            byte[] doc = new NbtFile(new NbtCompound("root") { list }).SaveToBuffer(NbtCompression.None);

            // A complete seekable input vouches for the count, so storage is exact
            NbtList fromBuffer = TestFiles.Load(doc).RootTag.Get<NbtList>("items");
            Assert.AreEqual(100, fromBuffer.Count);
            Assert.AreEqual(100, fromBuffer.tags.Capacity);

            // A non-seekable input cannot, so the list grows past the small preset
            var streamed = new NbtFile();
            streamed.LoadFromStream(new NonSeekableStream(new MemoryStream(doc)), NbtCompression.None);
            NbtList fromStream = streamed.RootTag.Get<NbtList>("items");
            Assert.AreEqual(100, fromStream.Count);
            Assert.AreEqual(128, fromStream.tags.Capacity);
        }


        [TestMethod]
        public void EmptyListTakesTypeOfFirstTagWhateverItsListType() {
            // A loaded empty list carries End, the wire's spelling of "no elements"
            NbtList loaded = TestFiles.Reload(new NbtCompound("root") {
                new NbtList("empty", NbtTagType.End)
            }).Get<NbtList>("empty");
            Assert.AreEqual(NbtTagType.End, loaded.ListType);
            loaded.Add(new NbtInt(1));
            Assert.AreEqual(NbtTagType.Int, loaded.ListType);
            Assert.AreEqual(1, loaded.Count);

            NbtList inserted = new NbtList(NbtTagType.End);
            inserted.Insert(0, new NbtString("a"));
            Assert.AreEqual(NbtTagType.String, inserted.ListType);

            NbtList ranged = new NbtList(NbtTagType.End);
            ranged.AddRange(new NbtTag[] { new NbtByte(1), new NbtByte(2) });
            Assert.AreEqual(NbtTagType.Byte, ranged.ListType);

            // An empty batch leaves End in place, and a mixed batch is still refused whole
            NbtList untouched = new NbtList(NbtTagType.End);
            untouched.AddRange(new NbtTag[0]);
            Assert.AreEqual(NbtTagType.End, untouched.ListType);
            Assert.AreEqual(NbtTagType.End, new NbtList(new NbtTag[0], NbtTagType.End).ListType);
            Assert.AreEqual(NbtTagType.End, new NbtList("copy", new NbtTag[0], NbtTagType.End).ListType);
            TestFiles.Reload(new NbtCompound("root") { new NbtList("copy", new NbtTag[0], NbtTagType.End) });
            Assert.Throws<ArgumentException>(
                () => untouched.AddRange(new NbtTag[] { new NbtByte(1), new NbtInt(2) }));
            Assert.AreEqual(NbtTagType.End, untouched.ListType);
            Assert.AreEqual(0, untouched.Count);

            // Once the list has elements, its type is fixed and End can no longer be assigned
            Assert.Throws<ArgumentException>(() => loaded.ListType = NbtTagType.End);
            Assert.Throws<ArgumentException>(() => loaded.Add(new NbtByte(1)));
        }


        [TestMethod]
        public void CreateMixedStoresMixedTypesTheWayMinecraftDoes() {
            // Tags of one type make an ordinary list
            NbtList ints = NbtList.CreateMixed(new NbtInt(1), new NbtInt(2));
            Assert.AreEqual(NbtTagType.Int, ints.ListType);
            Assert.AreEqual(2, ints.Count);
            Assert.AreEqual(0, NbtList.CreateMixed().Count);

            // Mixed types become compounds with each tag under an empty key; plain compounds stay
            NbtCompound plain = new NbtCompound { new NbtInt("k", 1) };
            NbtCompound wrapperShaped = new NbtCompound { new NbtInt("", 5) };
            NbtList mixed = NbtList.CreateMixed(new NbtInt(1), new NbtString("a"), plain, wrapperShaped, new NbtList());
            Assert.AreEqual(NbtTagType.Compound, mixed.ListType);
            Assert.AreEqual(1, mixed.Get<NbtCompound>(0)[""].IntValue);
            Assert.AreEqual("a", mixed.Get<NbtCompound>(1)[""].StringValue);
            Assert.AreSame(plain, mixed[2]);
            // A wrapper-shaped compound is wrapped again, so unwrapping gives it back
            Assert.AreSame(wrapperShaped, mixed.Get<NbtCompound>(3)[""]);
            Assert.AreEqual(NbtTagType.List, mixed.Get<NbtCompound>(4)[""].TagType);
            Assert.AreEqual("[1,\"a\",{k:1},{\"\":5},[]]", mixed.ToSnbt());

            // A list of compounds gets the same treatment, since that is what the wire holds
            NbtList compounds = NbtList.CreateMixed(new NbtCompound { new NbtInt("", 5) }, new NbtCompound());
            Assert.AreEqual(NbtTagType.Compound, compounds.ListType);
            Assert.AreEqual(5, compounds.Get<NbtCompound>(0)[""][""].IntValue);
            Assert.AreEqual(0, compounds.Get<NbtCompound>(1).Count);
        }


        [TestMethod]
        public void CreateMixedRefusesWhatAddRefusesAndLeavesTheTagsAlone() {
            Assert.Throws<ArgumentNullException>(() => NbtList.CreateMixed((NbtTag[])null));
            Assert.Throws<ArgumentNullException>(() => NbtList.CreateMixed(new NbtInt(1), null));
            Assert.Throws<ArgumentNullException>(() => NbtList.CreateMixed(new NbtInt(1), new NbtString("a"), null));
            NbtInt named = new NbtInt("named", 1);
            Assert.Throws<ArgumentException>(() => NbtList.CreateMixed(named, new NbtString("a")));
            NbtInt twice = new NbtInt(1);
            Assert.Throws<ArgumentException>(() => NbtList.CreateMixed(twice, new NbtString("a"), twice));
            NbtInt parented = new NbtInt(1);
            NbtList owner = new NbtList { parented };
            Assert.Throws<ArgumentException>(() => NbtList.CreateMixed(parented, new NbtString("a")));
            Assert.IsNull(twice.Parent);
            Assert.IsNull(twice.Name);
            Assert.AreSame(owner, parented.Parent);
        }


        [TestMethod]
        public void UnwrapMixedReadsTheWayMinecraftLoads() {
            NbtCompound plain = new NbtCompound { new NbtInt("k", 1) };
            NbtList mixed = NbtList.CreateMixed(new NbtInt(1), new NbtString("a"), plain,
                                                new NbtCompound { new NbtInt("", 5) });
            NbtTag[] parts = mixed.UnwrapMixed();
            Assert.AreEqual(4, parts.Length);
            Assert.AreEqual(1, parts[0].IntValue);
            Assert.AreEqual("a", parts[1].StringValue);
            Assert.AreSame(plain, parts[2]);
            // One level comes off, so the wrapper-shaped compound is itself again
            Assert.AreEqual(5, parts[3][""].IntValue);
            // The tags are the list's own, not copies
            Assert.AreSame(mixed.Get<NbtCompound>(0)[""], parts[0]);
            Assert.IsNotNull(parts[0].Parent);

            // A loaded list of compounds under empty keys reads as its values, as in the game;
            // other lists come back as they are
            NbtList loaded = new NbtList(new NbtTag[] {
                new NbtCompound { new NbtInt("", 1) },
                new NbtCompound { new NbtInt("", 2) }
            });
            Assert.AreEqual(1, loaded.UnwrapMixed()[0].IntValue);
            Assert.AreEqual(2, loaded.UnwrapMixed()[1].IntValue);
            NbtList ints = new NbtList(new NbtTag[] { new NbtInt(1) });
            Assert.AreSame(ints[0], ints.UnwrapMixed()[0]);
            Assert.AreEqual(0, new NbtList().UnwrapMixed().Length);
        }
    }
}
