using NUnit.Framework;

namespace fNbt.Test {
    [TestFixture]
    internal class ComparerTests {
        private readonly NbtComparer comparer = NbtComparer.Instance;

        [Test]
        public void InstanceIsSingleton() {
            Assert.That(NbtComparer.Instance, Is.SameAs(NbtComparer.Instance));
        }

        [Test]
        public void BasicTagsEqualAndHashCode() {
            var a = new NbtInt("foo", 123);
            var b = new NbtInt("foo", 123);
            var c = new NbtInt("foo", 456);    // different value
            var d = new NbtInt("bar", 123);    // different name

            Assert.IsTrue(comparer.Equals(a, b), "Same type/name/value should be equal");
            Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b), "Equal tags must have equal hash codes");

            Assert.IsFalse(comparer.Equals(a, c), "Different values => not equal");
            Assert.IsFalse(comparer.Equals(a, d), "Different names => not equal");
        }

        [Test]
        public void StringCaseSensitivity() {
            var lower = new NbtString("same", "test");
            var upper = new NbtString("same", "TEST");
            Assert.IsFalse(comparer.Equals(lower, upper), "String comparison is case-sensitive");

            // same for tag names
            var nameLower = new NbtString("test", "same");
            var nameUpper = new NbtString("TEST", "same");
            Assert.IsFalse(comparer.Equals(nameLower, nameUpper), "Tag name comparison is case-sensitive");
        }

        [Test]
        public void StringUnicode() {
            // make sure unicode strings that are loosely comparable (in some locales) but not identical (ordinal) are not equal.
            var uVal1 = new NbtString("same", "\u00E9"); // e with accent (precomposed)
            var uVal2 = new NbtString("same", "\u0065\u0301"); // e + combining acute accent (decomposed)
            Assert.IsFalse(comparer.Equals(uVal1, uVal2), "Unicode strings with different codepoints should not match");

            // test the same for tag NAMES
            var uName1 = new NbtString("\u00E9", "same");
            var uName2 = new NbtString("\u0065\u0301", "same");
            Assert.IsFalse(comparer.Equals(uName1, uName2), "Tag names with different codepoints should not match");
        }

        [Test]
        public void FloatAndDoubleNaN() {
            var f1 = new NbtFloat("f", float.NaN);
            var f2 = new NbtFloat("f", float.NaN);
            Assert.IsTrue(comparer.Equals(f1, f2), "NaN floats should compare equal using Equals()");
            Assert.AreEqual(comparer.GetHashCode(f1), comparer.GetHashCode(f2), "Hash codes for NaN floats must match");

            var d1 = new NbtDouble("d", double.NaN);
            var d2 = new NbtDouble("d", double.NaN);
            Assert.IsTrue(comparer.Equals(d1, d2), "NaN doubles should compare equal");
            Assert.AreEqual(comparer.GetHashCode(d1), comparer.GetHashCode(d2), "Hash codes for NaN doubles must match");
        }

        [Test]
        public void ArrayTagEquality() {
            var b1 = new NbtByteArray("arr", new byte[] { 1, 2, 3 });
            var b2 = new NbtByteArray("arr", new byte[] { 1, 2, 3 });
            var b3 = new NbtByteArray("arr", new byte[] { 3, 2, 1 });
            Assert.IsTrue(comparer.Equals(b1, b2), "Same byte arrays should be equal");
            Assert.IsFalse(comparer.Equals(b1, b3));

            var i1 = new NbtIntArray("arr", new[] { 1, -2, 3 });
            var i2 = new NbtIntArray("arr", new[] { 1, -2, 3 });
            Assert.IsTrue(comparer.Equals(i1, i2), "Same int arrays should be equal");

            var l1 = new NbtLongArray("arr", new long[] { 10, -20, 30 });
            var l2 = new NbtLongArray("arr", new long[] { 10, -20, 30 });
            Assert.IsTrue(comparer.Equals(l1, l2), "Same long arrays should be equal");
        }

        [Test]
        public void ListTagsOrderMatters() {
            var list1 = new NbtList("l") { new NbtInt(1), new NbtInt(2), new NbtInt(3) };
            var list2 = new NbtList("l") { new NbtInt(1), new NbtInt(2), new NbtInt(3) };
            var list3 = new NbtList("l") { new NbtInt(3), new NbtInt(2), new NbtInt(1) };

            Assert.IsTrue(comparer.Equals(list1, list2), "Same order => equal");
            Assert.IsFalse(comparer.Equals(list1, list3), "Different order => not equal");
        }

        [Test]
        public void CompoundTagsIgnoresOrder() {
            var compA = new NbtCompound("c")
            {
                new NbtByte("a", 1),
                new NbtByte("b", 2)
            };
            var compB = new NbtCompound("c")
            {
                new NbtByte("b", 2),
                new NbtByte("a", 1)
            };

            Assert.IsTrue(comparer.Equals(compA, compB), "Compounds compare as sets regardless of insertion order");
            Assert.AreEqual(comparer.GetHashCode(compA), comparer.GetHashCode(compB), "HashCode only considers count for compounds");
        }

        [Test]
        public void ListTestEquals() {
            // Stress-test: compare lists with every single tag type
            var x = TestFiles.MakeListTest();
            var y = TestFiles.MakeListTest();
            Assert.IsTrue(comparer.Equals(x, y), "Two runs of MakeListTest should be deeply equal");
            Assert.AreEqual(comparer.GetHashCode(x), comparer.GetHashCode(y), "And their hash codes should match");
        }

        [Test]
        public void NullNameNotEqualEmptyName() {
            var nullName = new NbtInt(null, 42);
            var emptyName = new NbtInt("", 42);
            Assert.IsFalse(comparer.Equals(nullName, emptyName), "Null name and empty name should not be equal");
            Assert.AreNotEqual(comparer.GetHashCode(nullName), comparer.GetHashCode(emptyName), "Hash codes should not match");
        }
    }
}
