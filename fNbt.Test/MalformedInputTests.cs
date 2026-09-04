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


        static void TryReadIncompleteRootTagName(byte[] partialData) {

            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(partialData), NbtCompression.None, NbtFlavor.Java), "Length=" + partialData.Length);
            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(partialData), NbtCompression.AutoDetect, NbtFlavor.Java), "Length=" + partialData.Length);
        }


        static void TryReadIncompleteFile(byte[] partialData) {

            Assert.Throws<EndOfStreamException>(() => TryReadBadFile(partialData));
            Assert.Throws<EndOfStreamException>(() => TestFiles.Load(partialData));
            Assert.Throws<EndOfStreamException>(
                () => new NbtFile().LoadFromBuffer(partialData, 0, partialData.Length, NbtCompression.AutoDetect));
        }


        void AssertBadFileFromBuffer(byte[] input) {
            // Corrupt input fails as a format error, or as a premature end of stream when a
            // tolerated length, such as a negative array size read as empty, leaves the rest of
            // the document truncated
            AssertThrowsParseError(() => TryReadBadFile(input));
            AssertThrowsParseError(() => TestFiles.Load(input));
            AssertThrowsParseError(() => TestFiles.Load(input, selector: tag => false));
        }


        static void AssertThrowsParseError(Action action) {
            try {
                action();
            } catch (NbtFormatException) {
                return;
            } catch (EndOfStreamException) {
                return;
            }
            Assert.Fail("Expected NbtFormatException or EndOfStreamException.");
        }


        static void TryReadBadFile(byte[] data) {
            using (MemoryStream ms = new MemoryStream(data)) {
                NbtReader reader = new NbtReader(ms);
                try {
                    while (reader.ReadToFollowing()) { }
                } catch (Exception) {
                    Assert.IsTrue(reader.IsInErrorState);
                    throw;
                }
            }
        }


        // Duplicate compound names must throw NbtFormatException.
        [TestMethod]
        public void DuplicateCompoundNamesThrowFormatException() {
            byte[] doc = { 0x0A, 0x00, 0x01, (byte)'r', 0x01, 0x00, 0x01, (byte)'x', 0x01,
                           0x01, 0x00, 0x01, (byte)'x', 0x02, 0x00 };
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(doc));
        }


        [TestMethod]
        public void DuplicateCompoundNamesPutReaderIntoErrorState() {
            byte[] doc = { 0x0A, 0x00, 0x01, (byte)'r', 0x01, 0x00, 0x01, (byte)'x', 0x01,
                           0x01, 0x00, 0x01, (byte)'x', 0x02, 0x00 };
            NbtReader reader = TestFiles.OpenReader(doc);
            Assert.Throws<NbtFormatException>(() => reader.ReadAsTag());
            Assert.IsTrue(reader.IsInErrorState);
        }


        // Array tags used to allocate straight from the caller-controlled length prefix.
        // A 13-byte document could commit 512 MiB before noticing EOF.
        [TestMethod]
        public void ByteArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x07, 0x08000000); // 128 MiB claimed, 13-byte doc
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(() => TestFiles.Load(doc));
            // The up-front bound must reject it, not the read after a huge allocation
            StringAssert.Contains(ex.Message, "Declared");
        }


        [TestMethod]
        public void IntArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x08000000);
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(() => TestFiles.Load(doc));
            // The up-front bound must reject it, not the read after a huge allocation
            StringAssert.Contains(ex.Message, "Declared");
        }


        [TestMethod]
        public void LongArrayOversizedLengthSeekableThrows() {
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x08000000);
            EndOfStreamException ex = Assert.Throws<EndOfStreamException>(() => TestFiles.Load(doc));
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

            NbtFile seekable = TestFiles.Load(doc);
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
            Assert.Throws<EndOfStreamException>(
                () => TestFiles.Load(doc, selector: tag => tag.Name != "a"));
        }


        [TestMethod]
        public void LongArraySkipDoesNotWrapToZero() {
            // LongArray of 0x20000000 => 0x100000000 bytes, wraps to 0 in 32-bit
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x20000000);
            Assert.Throws<EndOfStreamException>(
                () => TestFiles.Load(doc, selector: tag => tag.Name != "a"));
        }


        [TestMethod]
        public void ListOfLongSkipDoesNotWrapToZero() {
            // List of Long with count 0x20000000 => 0x100000000 bytes, wraps to 0
            byte[] doc = MakeListHeaderDoc(0x04, 0x20000000);
            Assert.Throws<EndOfStreamException>(
                () => TestFiles.Load(doc, selector: tag => tag.Name != "a"));
        }


        // NbtReader skips lists and arrays itself. The same wrap must not let it walk clean.
        [TestMethod]
        public void NbtReaderIntArrayDoesNotWrapToZero() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x40000000);
            NbtReader reader = TestFiles.OpenReader(doc);
            Assert.Throws<EndOfStreamException>(() => {
                while (reader.ReadToFollowing()) { }
            });
        }


        [TestMethod]
        public void NbtReaderLongArrayDoesNotWrapToZero() {
            byte[] doc = MakeArrayHeaderDoc(0x0C, 0x20000000);
            NbtReader reader = TestFiles.OpenReader(doc);
            Assert.Throws<EndOfStreamException>(() => {
                while (reader.ReadToFollowing()) { }
            });
        }


        // A huge length that stays positive must also throw a stream error.
        [TestMethod]
        public void HugeIntArraySkipThrowsStreamError() {
            byte[] doc = MakeArrayHeaderDoc(0x0B, 0x0FFFFFFF);
            Assert.Throws<EndOfStreamException>(
                () => TestFiles.Load(doc, selector: tag => tag.Name != "a"));
        }


        [TestMethod]
        public void EndOfStreamFileRead() {
            byte[] data = {
                0x0A, // Compound tag
                0x00, 0x02, 0x66, 0x4E, // Root name 'fN'
                0x00 // end tag
            };

            for (int i = 0; i < data.Length; i++) {
                var partialData = new byte[i];
                Array.Copy(data,partialData,i);
                TryReadIncompleteFile(partialData);
                if (i < 5)
                    TryReadIncompleteRootTagName(partialData);
            }
        }


        [TestMethod]
        public void CorruptFileRead() {
            byte[] badHeader = {
                0x02, // TAG_Short ID (instead of TAG_Compound ID)
                0x00, 0x01, 0x66, // Root name: 'f'
                0x00 // end tag
            };
            Assert.Throws<NbtFormatException>(() => TryReadBadFile(badHeader));
            Assert.Throws<NbtFormatException>(() => TestFiles.Load(badHeader));
            Assert.Throws<NbtFormatException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(badHeader), NbtCompression.None, NbtFlavor.Java));

            byte[] badStringLength = {
                0x0A, // Compound tag
                0xFF, 0xFF, 0x66, // Root name 'f' (string length prefix 0xFFFF = 65535 unsigned bytes)
                0x00 // end tag
            };
            // The prefix is unsigned, so 0xFFFF is a valid 65535-byte length. The string is
            // truncated, so it fails with EndOfStreamException rather than a negative-length error.
            Assert.Throws<EndOfStreamException>(() => TryReadBadFile(badStringLength));
            Assert.Throws<EndOfStreamException>(() => TestFiles.Load(badStringLength));
            Assert.Throws<EndOfStreamException>(
                () => NbtFile.ReadRootTagName(new MemoryStream(badStringLength), NbtCompression.None, NbtFlavor.Java));

            byte[] badSecondTag = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0xFF, 0x01, 0x4E, 0x7F, 0xFF, // Short tag named 'N' with invalid tag ID (0xFF instead of 0x02)
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badSecondTag);

            // The list's element type must be valid when elements follow it. The document is
            // otherwise complete, so the type byte is the only defect.
            byte[] badListType = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x09, // List tag
                0x00, 0x01, 0x67, // List tag name: 'g'
                0xFF, // invalid list tag type
                0x00, 0x00, 0x00, 0x01, // List size: 1
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badListType);

            // Negative sizes read as empty, so an impossibly large size is the bad case
            byte[] badListSize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x09, // List tag
                0x00, 0x01, 0x67, // List tag name: 'g'
                0x01, // List type: Byte
                0x7F, 0x00, 0x00, 0x00, // List size: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badListSize);
        }


        [TestMethod]
        public void BadArraySize() {
            // Negative sizes read as empty, so impossibly large sizes are the bad case
            byte[] badByteArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x07, // ByteArray tag
                0x00, 0x01, 0x67, // ByteArray tag name: 'g'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badByteArraySize);


            byte[] badIntArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0b, // IntArray tag
                0x00, 0x01, 0x66, // IntArray tag name: 'f'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badIntArraySize);

            byte[] badLongArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0c, // LongArray tag
                0x00, 0x01, 0x66, // LongArray tag name: 'f'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badLongArraySize);
        }


        [TestMethod]
        public void BadNestedArraySize() {
            // Negative sizes read as empty, so impossibly large sizes are the bad case
            byte[] badNestedByteArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x07, // ByteArray tag
                0x00, 0x01, 0x68, // ByteArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedByteArraySize);


            byte[] badNestedIntArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x0b, // IntArray tag
                0x00, 0x01, 0x68, // IntArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedIntArraySize);

            byte[] badNestedLongArraySize = {
                0x0A, // Compound tag
                0x00, 0x01, 0x66, // Root name: 'f'
                0x0A, // Child compound tag
                0x00, 0x01, 0x67, // Child name: 'g'
                0x0c, // LongArray tag
                0x00, 0x01, 0x68, // LongArray tag name: 'h'
                0x7F, 0x00, 0x00, 0x00, // array length: ~2 billion, cannot fit
                0x00, // child end tag
                0x00 // end tag
            };
            AssertBadFileFromBuffer(badNestedLongArraySize);
        }
    }
}
