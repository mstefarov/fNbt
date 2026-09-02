using System;
using System.IO;
using System.IO.Compression;

namespace fNbt.Test {
    // Corrupt and hostile input must fail cleanly.
    // Helpful exceptions are fine, but crashes/silent corruption/confusing exceptions are not.
    [TestClass]
    public class MalformedInputTests {
        // root compound "r" | <arrayType> "a" length=<length> | (no payload) | End
        static byte[] MakeArrayHeaderDoc(byte arrayType, int length) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'r');
                ms.WriteByte(arrayType);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'a');
                TestFiles.WriteBEInt(ms, length);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }


        // root "r" | TAG_List "a" of <elemType> count=<count> | (no payload) | End
        static byte[] MakeListHeaderDoc(byte elemType, int count) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'r');
                ms.WriteByte(0x09);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'a');
                ms.WriteByte(elemType);
                TestFiles.WriteBEInt(ms, count);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }


        // A string over 65,535 bytes can't fit the unsigned prefix. Writing must throw instead
        // of wrapping the length and forging a shorter tag.
        [TestMethod]
        public void OverlongStringThrowsOnWrite() {
            var value = new string('a', 70000);
            var root = new NbtCompound("root") { new NbtString("s", value) };
            Assert.Throws<NbtFormatException>(() => new NbtFile(root).SaveToBuffer(NbtCompression.None));
        }


        [TestMethod]
        public void OverlongTagNameThrowsOnWrite() {
            var longName = new string('n', 70000);
            var root = new NbtCompound("root") { new NbtInt(longName, 1) };
            Assert.Throws<NbtFormatException>(() => new NbtFile(root).SaveToBuffer(NbtCompression.None));
        }


        // Duplicate compound names must throw NbtFormatException.
        [TestMethod]
        public void DuplicateCompoundNamesThrowFormatException() {
            byte[] doc = { 0x0A, 0x00, 0x01, (byte)'r', 0x01, 0x00, 0x01, (byte)'x', 0x01,
                           0x01, 0x00, 0x01, (byte)'x', 0x02, 0x00 };
            var file = new NbtFile();
            Assert.Throws<NbtFormatException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
        }


        [TestMethod]
        public void DuplicateCompoundNamesPutReaderIntoErrorState() {
            byte[] doc = { 0x0A, 0x00, 0x01, (byte)'r', 0x01, 0x00, 0x01, (byte)'x', 0x01,
                           0x01, 0x00, 0x01, (byte)'x', 0x02, 0x00 };
            var reader = new NbtReader(new MemoryStream(doc));
            Assert.Throws<NbtFormatException>(() => reader.ReadAsTag());
            Assert.IsTrue(reader.IsInErrorState);
        }


        [TestMethod]
        public void BadZLibHeaderThrows() {
            // A header with bad check bits must fail up front on every target
            var root = new NbtCompound("root") { new NbtInt("v", 1) };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.ZLib);
            doc[1] ^= 0x01;

            var file = new NbtFile();
            Assert.Throws<InvalidDataException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.ZLib));
        }


        // A corrupt ZLib checksum must be caught and reported as InvalidDataException.
        // The netstandard2.0 build validates the Adler-32 trailer itself on seekable streams.
        [TestMethod]
        public void CorruptZLibChecksumThrows() {
            var root = new NbtCompound("root") { new NbtInt("v", 1) };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.ZLib);
            // Corrupt the 4-byte Adler-32 trailer
            doc[doc.Length - 1] ^= 0xFF;

            var file = new NbtFile();
            Assert.Throws<InvalidDataException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.ZLib));
        }


        // Array tags used to allocate straight from the caller-controlled length prefix.
        // A 13-byte document could commit 512 MiB before noticing EOF.
        [TestMethod]
        public void ByteArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x07, 0x08000000); // 128 MiB claimed, 13-byte doc
            var file = new NbtFile();
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
            // The up-front bound must reject it, not the read after a huge allocation
            StringAssert.Contains(ex.Message, "Declared");
        }


        [TestMethod]
        public void IntArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x08000000);
            var file = new NbtFile();
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
            // The up-front bound must reject it, not the read after a huge allocation
            StringAssert.Contains(ex.Message, "Declared");
        }


        [TestMethod]
        public void LongArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x08000000);
            var file = new NbtFile();
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None));
            // The up-front bound must reject it, not the read after a huge allocation
            StringAssert.Contains(ex.Message, "Declared");
        }


        // Non-seekable streams give no length up front, so an oversized length can't be rejected
        // before reading. Loading must still fail with EndOfStreamException.
        [TestMethod]
        public void ByteArrayOversizedLengthNonSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x07, 100000);
            var file = new NbtFile();
            using (var ms = new MemoryStream(doc))
            using (var nss = new NonSeekableStream(ms)) {
                Assert.Throws<EndOfStreamException>(
                    () => file.LoadFromStream(nss, NbtCompression.None));
            }
        }


        [TestMethod]
        public void IntArrayOversizedLengthNonSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 100000);
            var file = new NbtFile();
            using (var ms = new MemoryStream(doc))
            using (var nss = new NonSeekableStream(ms)) {
                Assert.Throws<EndOfStreamException>(
                    () => file.LoadFromStream(nss, NbtCompression.None));
            }
        }


        [TestMethod]
        public void OversizedLengthInGZipThrows() {
            // Compressed input decompresses through a non-seekable stream.
            // An oversized length must still throw rather than load silently corrupt.
            byte[] doc = MakeArrayHeaderDoc(0x07, 100000);
            byte[] gz;
            using (var outMs = new MemoryStream()) {
                using (var gzip = new GZipStream(outMs, CompressionMode.Compress, true)) {
                    gzip.Write(doc, 0, doc.Length);
                }
                gz = outMs.ToArray();
            }
            var file = new NbtFile();
            Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(gz, 0, gz.Length, NbtCompression.GZip));
        }


        [TestMethod]
        public void HonestArraysStillRoundTrip() {
            // The guard must not break honestly-sized arrays, seekable or not
            var root = new NbtCompound("root") {
                new NbtByteArray("bytes", new byte[5000]),
                new NbtIntArray("ints", new int[1000]),
                new NbtLongArray("longs", new long[500])
            };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.None);

            var seekable = new NbtFile();
            seekable.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None);
            Assert.AreEqual(5000, seekable.RootTag.Get<NbtByteArray>("bytes").Value.Length);
            Assert.AreEqual(1000, seekable.RootTag.Get<NbtIntArray>("ints").Value.Length);
            Assert.AreEqual(500, seekable.RootTag.Get<NbtLongArray>("longs").Value.Length);

            var nonSeekable = new NbtFile();
            using (var ms = new MemoryStream(doc))
            using (var nss = new NonSeekableStream(ms)) {
                nonSeekable.LoadFromStream(nss, NbtCompression.None);
            }
            Assert.AreEqual(5000, nonSeekable.RootTag.Get<NbtByteArray>("bytes").Value.Length);
            Assert.AreEqual(1000, nonSeekable.RootTag.Get<NbtIntArray>("ints").Value.Length);
            Assert.AreEqual(500, nonSeekable.RootTag.Get<NbtLongArray>("longs").Value.Length);
        }


        // A length whose byte count is a multiple of 2^32 once wrapped to a zero-byte skip,
        // letting the parser "cleanly" walk a document that was never read.
        [TestMethod]
        public void IntArraySkipDoesNotWrapToZero() {
            // IntArray of 0x40000000 => 0x100000000 bytes, wraps to 0 in 32-bit
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x40000000);
            var file = new NbtFile();
            Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "a"));
        }


        [TestMethod]
        public void LongArraySkipDoesNotWrapToZero() {
            // LongArray of 0x20000000 => 0x100000000 bytes, wraps to 0 in 32-bit
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x20000000);
            var file = new NbtFile();
            Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "a"));
        }


        [TestMethod]
        public void ListOfLongSkipDoesNotWrapToZero() {
            // List of Long with count 0x20000000 => 0x100000000 bytes, wraps to 0
            byte[] doc = MakeListHeaderDoc(0x04, 0x20000000);
            var file = new NbtFile();
            Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "a"));
        }


        // NbtReader skips lists and arrays itself. The same wrap must not let it walk clean.
        [TestMethod]
        public void NbtReaderIntArrayDoesNotWrapToZero() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x40000000);
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms);
                Assert.Throws<EndOfStreamException>(() => {
                    while (reader.ReadToFollowing()) { }
                });
            }
        }


        [TestMethod]
        public void NbtReaderLongArrayDoesNotWrapToZero() {
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x20000000);
            using (var ms = new MemoryStream(doc)) {
                var reader = new NbtReader(ms);
                Assert.Throws<EndOfStreamException>(() => {
                    while (reader.ReadToFollowing()) { }
                });
            }
        }


        // A huge length that stays positive must also throw a stream error.
        [TestMethod]
        public void HugeIntArraySkipThrowsStreamError() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x0FFFFFFF);
            var file = new NbtFile();
            Assert.Throws<EndOfStreamException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.None, tag => tag.Name != "a"));
        }
    }
}
