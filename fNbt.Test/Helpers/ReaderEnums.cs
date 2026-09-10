using System;

namespace fNbt.Test {
    enum ReaderSByteEnum : sbyte {
    }

    enum ReaderShortEnum : short {
        Negative = -1,
        Positive = 2
    }

    enum ReaderLongEnum : long {
        Negative = -1,
        Large = 1L << 40
    }

    enum ReaderUShortEnum : ushort {
    }

    enum ReaderUIntEnum : uint {
    }

    enum ReaderULongEnum : ulong {
    }

    static class ReaderEnumAssert {
        public static void AcceptsRange<T>(long min, long max, params long[] outsideRange) {
            T[] expected = { (T)Enum.ToObject(typeof(T), min), (T)Enum.ToObject(typeof(T), max) };
            NbtCompound root = new NbtCompound("r") {
                new NbtList("l") { new NbtLong(min), new NbtLong(max) }
            };
            NbtReader reader = TestFiles.OpenReader(root);
            Assert.IsTrue(reader.ReadToFollowing("l"));
            CollectionAssert.AreEqual(expected, reader.ReadListAsArray<T>());

            reader = TestFiles.OpenReader(root);
            Assert.IsTrue(reader.ReadToFollowing("l"));
            foreach (T value in expected) {
                Assert.IsTrue(reader.ReadToFollowing());
                Assert.AreEqual(value, reader.ReadValueAs<T>());
            }

            foreach (long value in outsideRange) {
                root = new NbtCompound("r") { new NbtList("l") { new NbtLong(value) } };
                reader = TestFiles.OpenReader(root);
                Assert.IsTrue(reader.ReadToFollowing("l"));
                Assert.Throws<OverflowException>(() => reader.ReadListAsArray<T>());

                reader = TestFiles.OpenReader(root);
                Assert.IsTrue(reader.ReadToFollowing("l"));
                Assert.IsTrue(reader.ReadToFollowing());
                Assert.Throws<OverflowException>(() => reader.ReadValueAs<T>());
            }
        }
    }
}
