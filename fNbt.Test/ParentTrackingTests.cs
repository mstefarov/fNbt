using System;
using System.Linq;

namespace fNbt.Test {
    [TestClass]
    public class ParentTrackingTests {
        // Indexer setters used to leave the displaced tag with Parent still pointing at the container.
        [TestMethod]
        public void CompoundIndexerClearsDisplacedParent() {
            var root = new NbtCompound("root");
            var first = new NbtInt("x", 1);
            root.Add(first);

            root["x"] = new NbtInt("x", 2);
            Assert.IsNull(first.Parent);
            Assert.AreEqual(2, root["x"].IntValue);
        }


        [TestMethod]
        public void ListIndexerClearsDisplacedParent() {
            var list = new NbtList("l", NbtTagType.Int);
            var first = new NbtInt(1);
            list.Add(first);

            list[0] = new NbtInt(2);
            Assert.IsNull(first.Parent);
            Assert.AreEqual(2, list[0].IntValue);
        }


        [TestMethod]
        public void SelfAssignmentThroughIndexerThrowsAndKeepsTag() {
            // Re-assigning a tag to its own slot throws, since the already-parented check runs first.
            var root = new NbtCompound("root");
            var x = new NbtInt("x", 1);
            root.Add(x);
            Assert.Throws<ArgumentException>(() => root["x"] = x);
            Assert.AreSame(root, x.Parent);
            Assert.AreEqual(1, root["x"].IntValue);

            var list = new NbtList("l", NbtTagType.Int);
            var y = new NbtInt(1);
            list.Add(y);
            Assert.Throws<ArgumentException>(() => list[0] = y);
            Assert.AreSame(list, y.Parent);  // The tag must not get orphaned.
        }


        [TestMethod]
        public void RenamingDisplacedTagLeavesOldParentAlone() {
            // After a tag is displaced, its stale Parent link must not let a rename re-key the live tag
            var root = new NbtCompound("root");
            var first = new NbtInt("x", 1);
            root.Add(first);
            root["x"] = new NbtInt("x", 2); // displaces 'first' and clears its Parent

            // first is detached, so renaming it just relabels it
            first.Name = "y";
            Assert.AreEqual("y", first.Name);
            Assert.AreEqual(2, root["x"].IntValue);
            Assert.IsFalse(root.Contains("y"));
        }


        // Reference cycles must be rejected at every entry point that can create one.
        [TestMethod]
        public void TwoCompoundCycleRejected() {
            var a = new NbtCompound("a");
            var b = new NbtCompound("b");
            a.Add(b);
            Assert.Throws<ArgumentException>(() => b.Add(a));
            // and the tree must remain saveable afterwards
            new NbtFile(a).SaveToBuffer(NbtCompression.None);
        }


        [TestMethod]
        public void ThreeLevelListCycleRejected() {
            var x = new NbtList();
            var y = new NbtList();
            var z = new NbtList();
            x.Add(y);
            y.Add(z);
            Assert.Throws<ArgumentException>(() => z.Add(x));
        }


        [TestMethod]
        public void MixedCycleThroughListRejected() {
            // Cycle across three levels and both container kinds, past the old one-level guards
            var comp = new NbtCompound(); // unnamed, so the list's name check won't reject it first
            var list = new NbtList("list", NbtTagType.Compound);
            var inner = new NbtCompound();
            var innerList = new NbtList("il", NbtTagType.Compound);
            comp.Add(list);
            list.Add(inner);
            inner.Add(innerList);
            Assert.Throws<ArgumentException>(() => innerList.Add(comp));
            // adding an unrelated tag must still work
            innerList.Add(new NbtCompound());
        }


        [TestMethod]
        public void CompoundIndexerCycleRejected() {
            var a = new NbtCompound("a");
            var b = new NbtCompound("b");
            a.Add(b);
            Assert.Throws<ArgumentException>(() => b["a"] = a);
        }


        [TestMethod]
        public void ListIndexerCycleRejected() {
            var x = new NbtList();
            var y = new NbtList();
            var z = new NbtList();
            x.Add(y);
            y.Add(z);
            z.Add(new NbtList());
            Assert.Throws<ArgumentException>(() => z[0] = x);
        }


        [TestMethod]
        public void ListInsertCycleRejected() {
            var x = new NbtList();
            var y = new NbtList();
            var z = new NbtList();
            x.Add(y);
            y.Add(z);
            Assert.Throws<ArgumentException>(() => z.Insert(0, x));
            // one-call self-cycle on a detached list
            var solo = new NbtList();
            Assert.Throws<ArgumentException>(() => solo.Insert(0, solo));
        }


        [TestMethod]
        public void ListInsertRejectsNamedTag() {
            // Insert used to skip the named-tag guard that Add applies
            var list = new NbtList("l", NbtTagType.Int);
            list.Add(new NbtInt(1));
            Assert.Throws<ArgumentException>(() => list.Insert(0, new NbtInt("named", 2)));
            Assert.HasCount(1, list);
        }


        // AddRange and the collection constructors must be atomic.
        [TestMethod]
        public void CompoundAddRangeIsAtomic() {
            foreach (bool conflictsWithDestination in new[] { false, true }) {
                NbtTag[] original = conflictsWithDestination
                    ? new NbtTag[] { new NbtInt("first", 99), new NbtInt("b", 100) }
                    : Array.Empty<NbtTag>();
                NbtCompound dst = new NbtCompound("dst", original);
                NbtInt a = new NbtInt("a", 1);
                NbtInt b = new NbtInt("b", 2);
                NbtInt dup = new NbtInt(conflictsWithDestination ? "c" : "a", 3);
                NbtInt d = new NbtInt("d", 4);
                NbtTag[] batch = { a, b, dup, d };

                Assert.Throws<ArgumentException>(() => dst.AddRange(batch));
                CollectionAssert.AreEqual(original, dst.ToArray());
                foreach (NbtTag tag in original) {
                    Assert.AreSame(tag, dst[tag.Name]);
                    Assert.AreSame(dst, tag.Parent);
                }
                foreach (NbtTag tag in batch) Assert.IsNull(tag.Parent);

                dup.Name = "c";
                NbtCompound retry = new NbtCompound("retry", batch);
                foreach (NbtTag tag in batch) Assert.AreSame(retry, tag.Parent);
            }
        }


        [TestMethod]
        public void CompoundConstructorIsAtomic() {
            var a = new NbtInt("a", 1);
            var dup = new NbtInt("a", 2);
            Assert.Throws<ArgumentException>(() => new NbtCompound("c", new NbtTag[] { a, dup }));
            // The caller's tags must remain usable, not stranded on a half-built compound
            Assert.IsNull(a.Parent);
            var ok = new NbtCompound("ok");
            ok.Add(a); // would throw if 'a' still had a Parent
            Assert.AreSame(ok, a.Parent);
        }


        [TestMethod]
        public void ListAddRangeRollsBackListType() {
            var list = new NbtList(); // Unknown
            var ok = new NbtInt(1);
            var wrong = new NbtString("s");
            Assert.Throws<ArgumentException>(() => list.AddRange(new NbtTag[] { ok, wrong }));
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(NbtTagType.Unknown, list.ListType);
            Assert.IsNull(ok.Parent);
            // ListType wasn't pinned, so a string list is still allowed afterward
            list.Add(new NbtString("later"));
            Assert.AreEqual(NbtTagType.String, list.ListType);
        }


        [TestMethod]
        public void ListConstructorIsAtomic() {
            var ok = new NbtInt(1);
            var wrong = new NbtString("s");
            Assert.Throws<ArgumentException>(() => new NbtList(new NbtTag[] { ok, wrong }));
            Assert.IsNull(ok.Parent);
        }


        [TestMethod]
        public void ListBatchRejectsDuplicateInstance() {
            // The same instance twice in one batch would alias one tag at two indices
            var t = new NbtInt(1);
            var list = new NbtList("l", NbtTagType.Int);
            Assert.Throws<ArgumentException>(() => list.AddRange(new NbtTag[] { t, t }));
            Assert.AreEqual(0, list.Count);
            Assert.IsNull(t.Parent);

            Assert.Throws<ArgumentException>(() => new NbtList("l2", new NbtTag[] { t, t }, NbtTagType.Int));
            Assert.IsNull(t.Parent);

            // Compounds catch the same case through the duplicate-name check
            var named = new NbtInt("x", 1);
            Assert.Throws<ArgumentException>(() => new NbtCompound("c", new NbtTag[] { named, named }));
            Assert.IsNull(named.Parent);
        }


        [TestMethod]
        public void AddRangeStillWorksForValidBatch() {
            var dst = new NbtCompound("dst");
            dst.AddRange(new NbtTag[] { new NbtInt("a", 1), new NbtInt("b", 2) });
            Assert.AreEqual(2, dst.Count);
            Assert.AreEqual(1, dst["a"].IntValue);

            var list = new NbtList("l", NbtTagType.Int);
            list.AddRange(new NbtTag[] { new NbtInt(1), new NbtInt(2), new NbtInt(3) });
            Assert.AreEqual(3, list.Count);
        }


        [TestMethod]
        public void CopyConstructorSetsParents() {
            // Cloned children must belong to the clone, not to the original or to nothing
            var original = new NbtList("original", NbtTagType.Int) {
                new NbtInt(1),
                new NbtInt(2)
            };
            var root = new NbtCompound("root") { original };

            var clone = (NbtList)original.Clone();
            Assert.AreSame(clone, clone[0].Parent);
            Assert.AreSame(clone, clone[1].Parent);
            Assert.AreEqual("original[0]", clone[0].Path);

            var thief = new NbtList("thief", NbtTagType.Int);
            Assert.Throws<ArgumentException>(() => thief.Add(clone[0]));
        }
    }
}
