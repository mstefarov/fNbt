using System;

namespace fNbt.Test {
    [TestClass]
    public class ComparerTests {
        private readonly NbtComparer comparer = NbtComparer.Instance;

        [TestMethod]
        public void ValueTagsWithEqualValuesHaveEqualHashes() {
            NbtCompound original = TestFiles.MakeAllValuesRoot();
            NbtCompound copy = TestFiles.MakeAllValuesRoot();
            foreach (NbtTag tag in original) {
                NbtTag equal = copy[tag.Name];
                Assert.IsTrue(comparer.Equals(tag, equal), tag.Name);
                Assert.AreEqual(comparer.GetHashCode(tag), comparer.GetHashCode(equal), tag.Name);
            }

            Assert.IsFalse(comparer.Equals(new NbtInt("foo", 123), new NbtInt("foo", 456)));
            Assert.IsFalse(comparer.Equals(new NbtInt("foo", 123), new NbtInt("bar", 123)));
        }

        [TestMethod]
        public void EqualityDistinguishesNullAndTagTypes() {
            NbtInt tag = new NbtInt("value", 1);
            Assert.IsTrue(comparer.Equals(tag, tag));
            Assert.IsTrue(comparer.Equals(null, null));
            Assert.IsFalse(comparer.Equals(null, tag));
            Assert.IsFalse(comparer.Equals(tag, null));
            Assert.IsFalse(comparer.Equals(tag, new NbtByte("value", 1)));
            Assert.Throws<ArgumentNullException>(() => comparer.GetHashCode(null));
        }

        [TestMethod]
        public void NullNameNotEqualEmptyName() {
            var nullName = new NbtInt(null, 42);
            var emptyName = new NbtInt("", 42);
            Assert.IsFalse(comparer.Equals(nullName, emptyName), "Null and empty names differ");
        }

        [TestMethod]
        public void NamesAndStringValuesUseOrdinalComparison() {
            foreach ((string left, string right) in new[] {
                ("test", "TEST"),
                ("\u00E9", "\u0065\u0301") // precomposed versus decomposed acute accent
            }) {
                Assert.IsFalse(comparer.Equals(new NbtString("same", left), new NbtString("same", right)),
                               "Values: " + left + " / " + right);
                Assert.IsFalse(comparer.Equals(new NbtString(left, "same"), new NbtString(right, "same")),
                               "Names: " + left + " / " + right);
            }
        }

        [TestMethod]
        public void NaNPayloadsCompareEqualAndHaveEqualHashes() {
            var f1 = new NbtFloat("f", float.NaN);
            NbtFloat f2 = new NbtFloat("f", BitConverter.ToSingle(BitConverter.GetBytes(0x7fc00001), 0));
            Assert.IsTrue(comparer.Equals(f1, f2), "NaN floats should compare equal using Equals()");
            Assert.AreEqual(comparer.GetHashCode(f1), comparer.GetHashCode(f2), "Hash codes for NaN floats must match");

            var d1 = new NbtDouble("d", double.NaN);
            NbtDouble d2 = new NbtDouble("d", BitConverter.Int64BitsToDouble(0x7ff8000000000001));
            Assert.IsTrue(comparer.Equals(d1, d2), "NaN doubles should compare equal");
            Assert.AreEqual(comparer.GetHashCode(d1), comparer.GetHashCode(d2), "Hash codes for NaN doubles must match");
        }

        [TestMethod]
        public void ArrayEqualityIncludesLengthAndEveryElement() {
            NbtByteArray bytes = new NbtByteArray("arr", new byte[] { 1, 2, 3 });
            Assert.IsTrue(comparer.Equals(bytes, new NbtByteArray("arr", new byte[] { 1, 2, 3 })));
            Assert.IsFalse(comparer.Equals(bytes, new NbtByteArray("arr", new byte[] { 3, 2, 1 })));
            Assert.IsFalse(comparer.Equals(bytes, new NbtByteArray("arr", new byte[] { 1, 2 })));
            Assert.IsFalse(comparer.Equals(bytes, new NbtByteArray("arr", new byte[] { 1, 2, 4 })));

            NbtIntArray ints = new NbtIntArray("arr", new[] { 1, -2, 3 });
            Assert.IsTrue(comparer.Equals(ints, new NbtIntArray("arr", new[] { 1, -2, 3 })));
            Assert.IsFalse(comparer.Equals(ints, new NbtIntArray("arr", new[] { 1, -2 })));
            Assert.IsFalse(comparer.Equals(ints, new NbtIntArray("arr", new[] { 1, -2, 4 })));

            NbtLongArray longs = new NbtLongArray("arr", new long[] { 10, -20, 30 });
            Assert.IsTrue(comparer.Equals(longs, new NbtLongArray("arr", new long[] { 10, -20, 30 })));
            Assert.IsFalse(comparer.Equals(longs, new NbtLongArray("arr", new long[] { 10, -20 })));
            Assert.IsFalse(comparer.Equals(longs, new NbtLongArray("arr", new long[] { 10, -20, 40 })));
        }

        [TestMethod]
        public void ListTagsOrderMatters() {
            var list1 = new NbtList("l") { new NbtInt(1), new NbtInt(2), new NbtInt(3) };
            var list2 = new NbtList("l") { new NbtInt(1), new NbtInt(2), new NbtInt(3) };
            var list3 = new NbtList("l") { new NbtInt(3), new NbtInt(2), new NbtInt(1) };

            Assert.IsTrue(comparer.Equals(list1, list2), "Same order => equal");
            Assert.AreEqual(comparer.GetHashCode(list1), comparer.GetHashCode(list2));
            Assert.IsFalse(comparer.Equals(list1, list3), "Different order => not equal");
            Assert.IsFalse(comparer.Equals(list1, new NbtList("l") { new NbtInt(1), new NbtInt(2) }));
            Assert.IsFalse(comparer.Equals(list1, new NbtList("l") { new NbtByte(1), new NbtByte(2), new NbtByte(3) }));
        }

        [TestMethod]
        public void CompoundEqualityIgnoresOrderButIncludesEveryChild() {
            // A shared prefix followed by a swapped pair exercises both the positional and the lookup paths
            var compA = new NbtCompound("c")
            {
                new NbtByte("p", 0),
                new NbtByte("a", 1),
                new NbtByte("b", 2)
            };
            var compB = new NbtCompound("c")
            {
                new NbtByte("p", 0),
                new NbtByte("b", 2),
                new NbtByte("a", 1)
            };

            Assert.IsTrue(comparer.Equals(compA, compB), "Compounds compare as sets regardless of insertion order");
            Assert.AreEqual(comparer.GetHashCode(compA), comparer.GetHashCode(compB));

            compB["a"] = new NbtByte("a", 9);
            Assert.IsFalse(comparer.Equals(compA, compB), "A reordered child has a different value");
            compB["a"] = new NbtShort("a", 1);
            Assert.IsFalse(comparer.Equals(compA, compB), "A reordered child has a different type");
            compB["a"].Name = "missing";
            Assert.IsFalse(comparer.Equals(compA, compB), "Equal counts do not imply equal child names");
            compB.Remove("missing");
            Assert.IsFalse(comparer.Equals(compA, compB), "A child is missing");

            NbtCompound sameOrder = new NbtCompound(compA);
            sameOrder["p"] = new NbtByte("p", 1);
            Assert.IsFalse(comparer.Equals(compA, sameOrder), "The first child has a different value");
            sameOrder["p"] = new NbtInt("p", 0);
            Assert.IsFalse(comparer.Equals(compA, sameOrder), "The first child has a different type");
        }

        [TestMethod]
        public void ListsOfEveryTypeCompareEqual() {
            // Stress-test: compare lists with every single tag type
            var x = TestFiles.MakeAllListsRoot();
            var y = TestFiles.MakeAllListsRoot();
            Assert.IsTrue(comparer.Equals(x, y), "Two runs of MakeAllListsRoot should be deeply equal");
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y), "And their hash codes should match");
        }
    }
}
