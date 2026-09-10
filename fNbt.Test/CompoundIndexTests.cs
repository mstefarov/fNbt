using System;
using System.Collections.Generic;
using System.Linq;

namespace fNbt.Test {
    // Exercises the hashed index large compounds build over their ordered storage:
    // tombstones, position fix-ups after removals, renames, growth, and rebuilds.
    [TestClass]
    public sealed class CompoundIndexTests {
        static NbtCompound MakeIndexed(int childCount, string prefix = "t") {
            var compound = new NbtCompound("root");
            for (int i = 0; i < childCount; i++) {
                compound.Add(new NbtInt(prefix + i, i));
            }
            return compound;
        }


        [TestMethod]
        public void RemoveEveryOtherThenVerifyAll() {
            // The smaller table reaches its cleanup threshold during these removals.
            foreach (int childCount in new[] { 31, 100 }) {
                NbtCompound compound = MakeIndexed(childCount);
                for (int i = 0; i < childCount; i += 2) {
                    Assert.IsTrue(compound.Remove("t" + i));
                }
                Assert.AreEqual(childCount / 2, compound.Count);
                for (int i = 0; i < childCount; i++) {
                    if (i % 2 == 0) {
                        Assert.IsNull(compound["t" + i]);
                    } else {
                        Assert.AreEqual(i, compound["t" + i].IntValue);
                    }
                }
                // Survivors keep insertion order
                CollectionAssert.AreEqual(
                    Enumerable.Range(0, childCount).Where(i => i % 2 == 1).Select(i => "t" + i).ToList(),
                    compound.Names.ToList());
            }
        }


        [TestMethod]
        public void RemoveFromFrontRepeatedly() {
            NbtCompound compound = MakeIndexed(60);
            for (int i = 0; i < 40; i++) {
                Assert.IsTrue(compound.Remove("t" + i));
                // Every survivor stays reachable after each position shift
                Assert.AreEqual(i + 1, compound["t" + (i + 1)]?.IntValue ?? -1);
                Assert.AreEqual(59, compound["t59"].IntValue);
            }
            Assert.AreEqual(20, compound.Count);
        }


        [TestMethod]
        public void ReAddingRemovedNamesReusesTombstones() {
            NbtCompound compound = MakeIndexed(40);
            for (int i = 5; i < 35; i++) {
                Assert.IsTrue(compound.Remove("t" + i));
            }
            for (int i = 5; i < 35; i++) {
                compound.Add(new NbtInt("t" + i, i + 1000));
            }
            Assert.AreEqual(40, compound.Count);
            for (int i = 0; i < 40; i++) {
                int expected = (i >= 5 && i < 35) ? i + 1000 : i;
                Assert.AreEqual(expected, compound["t" + i].IntValue);
            }
        }


        [TestMethod]
        public void DuplicateStillRejectedAfterRemovalCycles() {
            NbtCompound compound = MakeIndexed(30);
            Assert.IsTrue(compound.Remove("t7"));
            compound.Add(new NbtInt("t7", 7));
            Assert.Throws<ArgumentException>(() => compound.Add(new NbtInt("t7", 77)));
            Assert.AreEqual(30, compound.Count);
        }


        [TestMethod]
        public void RenameStormOnIndexedCompound() {
            foreach (int childCount in new[] { 31, 50 }) {
                NbtCompound compound = MakeIndexed(childCount);
                List<string> orderBefore = compound.Names.ToList();
                for (int i = 0; i < childCount; i++) {
                    compound["t" + i].Name = "renamed" + i;
                }
                for (int i = 0; i < childCount; i++) {
                    Assert.IsNull(compound["t" + i]);
                    Assert.AreEqual(i, compound["renamed" + i].IntValue);
                }
                // Renames must not move children
                CollectionAssert.AreEqual(
                    orderBefore.Select(n => "renamed" + n.Substring(1)).ToList(),
                    compound.Names.ToList());
            }
        }


        [TestMethod]
        public void ReplaceViaIndexerKeepsPosition() {
            NbtCompound compound = MakeIndexed(30);
            compound["t12"] = new NbtString("t12", "replaced");
            Assert.AreEqual("replaced", compound["t12"].StringValue);
            Assert.AreEqual(12, compound.Names.ToList().IndexOf("t12"));
            Assert.AreEqual(30, compound.Count);
        }


        [TestMethod]
        public void ManyChildrenGrowAndStayReachable() {
            foreach ((int childCount, bool useAddRange) in new[] { (5000, false), (100, true) }) {
                NbtTag[] children = Enumerable.Range(0, childCount)
                    .Select(i => (NbtTag)new NbtInt("t" + i, i)).ToArray();
                NbtCompound compound = new NbtCompound("root");
                if (useAddRange) {
                    compound.AddRange(children);
                } else {
                    foreach (NbtTag child in children) compound.Add(child);
                }
                CollectionAssert.AreEqual(children, compound.Tags.ToArray());
                for (int i = 0; i < childCount; i++) {
                    NbtTag child = compound["t" + i];
                    Assert.AreSame(children[i], child);
                    Assert.AreEqual(i, child.IntValue);
                    Assert.AreSame(compound, child.Parent);
                }
                Assert.IsNull(compound["t" + childCount]);
                Assert.IsNull(compound["missing"]);
            }
        }


        [TestMethod]
        public void CloneOfIndexedCompoundIsIndependent() {
            NbtCompound original = MakeIndexed(520);
            NbtCompound clone = (NbtCompound)original.Clone();

            CollectionAssert.AreEqual(original.Names.ToList(), clone.Names.ToList());
            for (int i = 0; i < original.Count; i++) {
                NbtTag clonedChild = clone["t" + i];
                Assert.AreNotSame(original["t" + i], clonedChild);
                Assert.AreEqual(i, clonedChild.IntValue);
                Assert.AreSame(clone, clonedChild.Parent);
            }

            // Renames and removals on either side must not leak through a shared index
            clone["t10"].Name = "clone10";
            original["t20"].Name = "original20";
            Assert.AreEqual(10, original["t10"].IntValue);
            Assert.IsNull(original["clone10"]);
            Assert.AreEqual(20, clone["t20"].IntValue);
            Assert.IsNull(clone["original20"]);
            Assert.IsTrue(clone.Remove("t30"));
            Assert.AreEqual(30, original["t30"].IntValue);
        }


        [TestMethod]
        public void CloneOfEditedIndexedCompoundRebuildsIndex() {
            NbtCompound original = MakeIndexed(128);
            for (int i = 0; i < 88; i++) {
                Assert.IsTrue(original.Remove("t" + i));
            }
            original["t100"].Name = "renamed100";

            NbtCompound clone = new NbtCompound(original);
            CollectionAssert.AreEqual(original.Names.ToList(), clone.Names.ToList());
            for (int i = 88; i < 128; i++) {
                string name = i == 100 ? "renamed100" : "t" + i;
                Assert.AreEqual(i, clone[name].IntValue);
            }
            Assert.IsTrue(clone.Remove("t90"));
            Assert.AreEqual(90, original["t90"].IntValue);
            Assert.Throws<ArgumentException>(() => clone.Add(new NbtInt("t92", -1)));
        }


        [TestMethod]
        public void CloneOfShrunkIndexedCompoundGrowsAgain() {
            NbtCompound original = MakeIndexed(128);
            for (int i = 0; i < 115; i++) {
                Assert.IsTrue(original.Remove("t" + i));
            }

            NbtCompound clone = (NbtCompound)original.Clone();
            Assert.AreEqual(13, clone.Count);
            for (int i = 128; i < 140; i++) {
                clone.Add(new NbtInt("t" + i, i));
            }
            for (int i = 115; i < 140; i++) {
                Assert.AreEqual(i, clone["t" + i].IntValue);
            }
            Assert.IsNull(original["t139"]);
        }


        [TestMethod]
        public void RemoveByInstanceOnIndexedCompound() {
            NbtCompound compound = MakeIndexed(40);
            NbtTag inside = compound["t20"];
            var impostor = new NbtInt("t20", 999);
            Assert.IsFalse(compound.Remove(impostor));
            Assert.IsTrue(compound.Contains(inside));
            Assert.IsFalse(compound.Contains(impostor));
            Assert.IsTrue(compound.Remove(inside));
            Assert.IsFalse(compound.Contains(inside));
            Assert.IsNull(compound["t20"]);
        }


        [TestMethod]
        public void OrderSurvivesRemovalsThroughRoundTrip() {
            NbtCompound compound = MakeIndexed(25);
            compound.Remove("t3");
            compound.Remove("t17");
            compound.Add(new NbtInt("t3", 333));
            NbtCompound root = TestFiles.Reload(compound);
            CollectionAssert.AreEqual(
                compound.Names.ToList(), root.Names.ToList());
            Assert.AreEqual("t3", root.Names.Last());
        }


        [TestMethod]
        public void EnumerationInvalidatedByRemoveOnIndexedCompound() {
            NbtCompound compound = MakeIndexed(30);
            Assert.Throws<InvalidOperationException>(() => {
                foreach (NbtTag tag in compound) {
                    compound.Remove("t29");
                }
            });
        }


        [TestMethod]
        public void ClearThenRebuildFromScratch() {
            NbtCompound compound = MakeIndexed(30);
            compound.Clear();
            Assert.AreEqual(0, compound.Count);
            for (int i = 0; i < 30; i++) {
                compound.Add(new NbtInt("fresh" + i, i));
            }
            Assert.AreEqual(30, compound.Count);
            Assert.AreEqual(29, compound["fresh29"].IntValue);
        }
    }
}
