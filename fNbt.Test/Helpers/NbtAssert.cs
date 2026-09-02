using System;
using System.IO;

namespace fNbt.Test {
    // Assertions about NBT trees and about the streaming types after a refused call
    static class NbtAssert {
        // Structural equality, with both trees in the failure message
        public static void AreEqual(NbtTag expected, NbtTag actual, string message = null) {
            if (NbtComparer.Instance.Equals(expected, actual)) return;
            string prefix = message == null ? "" : message + ": ";
            Assert.Fail(prefix + "NBT trees differ.\nExpected:\n" + expected + "\nActual:\n" + actual);
        }


        // The call is refused and the stream is left exactly as long as it was
        public static TException WritesNothing<TException>(MemoryStream ms, Action write)
            where TException : Exception {
            long before = ms.Length;
            TException ex = Assert.Throws<TException>(write);
            Assert.AreEqual(before, ms.Length);
            return ex;
        }


        // A refusal left the writer usable: one more tag goes out, then the root closes and
        // the document finishes
        public static void WriterStillUsable(NbtWriter writer) {
            Assert.IsFalse(writer.IsInErrorState);
            writer.WriteInt("afterRefusal", 1);
            writer.EndCompound();
            writer.Finish();
        }


        // The writer is in its error state, so every further call throws
        public static void WriterIsPoisoned(NbtWriter writer) {
            Assert.IsTrue(writer.IsInErrorState);
            Assert.Throws<NbtFormatException>(() => writer.WriteInt("afterFailure", 1));
            Assert.Throws<NbtFormatException>(writer.EndCompound);
            Assert.Throws<NbtFormatException>(writer.Finish);
        }


        // The reader is in its error state, so every read throws
        public static void ReaderIsPoisoned(NbtReader reader) {
            Assert.IsTrue(reader.IsInErrorState);
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadToFollowing());
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadToNextSibling());
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadToDescendant("x"));
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadAsTag());
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadValue());
            Assert.Throws<InvalidReaderStateException>(() => reader.ReadListAsArray<int>());
            Assert.Throws<InvalidReaderStateException>(() => reader.Skip());
        }
    }
}
