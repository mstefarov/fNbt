using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // NbtOptions.DisallowTrailingData: opt-in strict mode for NbtFile loads. Uncompressed
    // documents must end exactly where the stream does; compressed loads consume the stream
    // to its end regardless, so there is nothing left to detect.
    [TestClass]
    public class TrailingDataTests {
        static byte[] MakeDoc(NbtCompression compression) {
            return new NbtFile(TestFiles.MakeSmallFile().RootTag).SaveToBuffer(compression);
        }


        [TestMethod]
        public void DefaultLoadsIgnoreTrailingData() {
            byte[] doc = MakeDoc(NbtCompression.None);
            byte[] padded = doc.Concat(new byte[] { 1, 2, 3 }).ToArray();

            var file = new NbtFile();
            long bytesRead = file.LoadFromBuffer(padded, 0, padded.Length, NbtCompression.None);
            Assert.AreEqual(doc.Length, bytesRead);
        }


        [TestMethod]
        public void StrictLoadRejectsTrailingData() {
            byte[] doc = MakeDoc(NbtCompression.None);
            byte[] padded = doc.Concat(new byte[] { 1, 2, 3 }).ToArray();

            var strict = new NbtFile(new NbtOptions { DisallowTrailingData = true });
            Assert.Throws<NbtFormatException>(
                () => strict.LoadFromBuffer(padded, 0, padded.Length, NbtCompression.None));

            // An exact-length document still loads
            long bytesRead = strict.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
            Assert.AreEqual(doc.Length, bytesRead);
            TestFiles.AssertNbtSmallFile(strict);
        }


        [TestMethod]
        public void StrictLoadWorksOnNonSeekableStreams() {
            byte[] doc = MakeDoc(NbtCompression.None);
            byte[] padded = doc.Concat(new byte[] { 1, 2, 3 }).ToArray();

            var strict = new NbtFile(new NbtOptions { DisallowTrailingData = true });
            using (var ms = new MemoryStream(padded)) {
                Assert.Throws<NbtFormatException>(
                    () => strict.LoadFromStream(new NonSeekableStream(ms), NbtCompression.None));
            }
            using (var ms = new MemoryStream(doc)) {
                strict.LoadFromStream(new NonSeekableStream(ms), NbtCompression.None);
                TestFiles.AssertNbtSmallFile(strict);
            }
        }


        [TestMethod]
        public void StrictModeCannotDetectTrailingDataUnderCompression() {
            // Compressed loads consume the stream to its end; the document's extent is
            // unknowable behind decompressor read-ahead, so strict mode has nothing to check.
            byte[] doc = MakeDoc(NbtCompression.GZip);
            byte[] padded = doc.Concat(new byte[] { 1, 2, 3 }).ToArray();

            var strict = new NbtFile(new NbtOptions { DisallowTrailingData = true });
            long bytesRead = strict.LoadFromBuffer(padded, 0, padded.Length, NbtCompression.GZip);
            Assert.AreEqual(padded.Length, bytesRead);
            TestFiles.AssertNbtSmallFile(strict);
        }
    }
}
