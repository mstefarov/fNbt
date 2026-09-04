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
            NbtCompound compound = MakeIndexed(100);
            for (int i = 0; i < 100; i += 2) {
                Assert.IsTrue(compound.Remove("t" + i));
            }
            Assert.AreEqual(50, compound.Count);
            for (int i = 0; i < 100; i++) {
                if (i % 2 == 0) {
                    Assert.IsNull(compound["t" + i]);
                } else {
                    Assert.AreEqual(i, compound["t" + i].IntValue);
                }
            }
            // Survivors keep insertion order
            CollectionAssert.AreEqual(
                Enumerable.Range(0, 100).Where(i => i % 2 == 1).Select(i => "t" + i).ToList(),
                compound.Names.ToList());
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
            NbtCompound compound = MakeIndexed(50);
            List<string> orderBefore = compound.Names.ToList();
            for (int i = 0; i < 50; i++) {
                compound["t" + i].Name = "renamed" + i;
            }
            for (int i = 0; i < 50; i++) {
                Assert.IsNull(compound["t" + i]);
                Assert.AreEqual(i, compound["renamed" + i].IntValue);
            }
            // Renames must not move children
            CollectionAssert.AreEqual(
                orderBefore.Select(n => "renamed" + n.Substring(1)).ToList(),
                compound.Names.ToList());
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
            NbtCompound compound = MakeIndexed(5000);
            Assert.AreEqual(5000, compound.Count);
            var rng = new Random(7);
            for (int i = 0; i < 500; i++) {
                int pick = rng.Next(5000);
                Assert.AreEqual(pick, compound["t" + pick].IntValue);
            }
            Assert.IsNull(compound["t5000"]);
            Assert.IsNull(compound["missing"]);
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
