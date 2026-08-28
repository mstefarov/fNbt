using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Checksum validation and stream-consumption contracts for compressed loads.
    // Compressed loads read the container to its end, so the trailer checksum is always
    // validated where the target supports it, and byte counts are deterministic.
    [TestClass]
    public class CompressedBoundaryTests {
        static byte[] MakeDoc(NbtCompression compression) {
            return new NbtFile(TestFiles.MakeSmallFile().RootTag).SaveToBuffer(compression);
        }


        [TestMethod]
        public void ZLibChecksumMismatchThrows() {
            // The last four bytes of a zlib document are its big-endian Adler-32.
            // A corrupt trailer must be rejected on every target, netstandard2.0 included.
            byte[] doc = MakeDoc(NbtCompression.ZLib);
            doc[doc.Length - 1] ^= 0xFF;

            var file = new NbtFile();
            Assert.Throws<InvalidDataException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.ZLib));
        }


        [TestMethod]
        public void ZLibCorruptBodyThrows() {
            // A bit-flip in the deflate body either breaks the deflate framing or changes the
            // output; the Adler check catches whatever the inflater does not.
            byte[] doc = MakeDoc(NbtCompression.ZLib);
            for (int i = 3; i < doc.Length - 4; i += 7) {
                byte[] corrupt = (byte[])doc.Clone();
                corrupt[i] ^= 0x10;
                var file = new NbtFile();
                try {
                    file.LoadFromBuffer(corrupt, 0, corrupt.Length, NbtCompression.ZLib);
                    Assert.Fail("Corrupt byte at offset " + i + " loaded without an exception.");
                } catch (InvalidDataException) {
                } catch (NbtFormatException) {
                    // Corruption can also surface as malformed NBT before the checksum is reached
                } catch (EndOfStreamException) {
                }
            }
        }


        [TestMethod]
        public void GZipCrcMismatchThrows() {
            // GZip trailer: CRC32 then ISIZE, 4 bytes each. Corrupt the CRC.
            byte[] doc = MakeDoc(NbtCompression.GZip);
            doc[doc.Length - 8] ^= 0xFF;

            var file = new NbtFile();
            Assert.Throws<InvalidDataException>(
                () => file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.GZip));
        }


        [TestMethod]
        public void CompressedLoadConsumesStreamToEnd() {
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                using (var ms = new MemoryStream(doc)) {
                    var file = new NbtFile();
                    long bytesRead = file.LoadFromStream(ms, compression);
                    Assert.AreEqual(ms.Length, ms.Position, compression.ToString());
                    Assert.AreEqual(ms.Length, bytesRead, compression.ToString());
                }
            }
        }


        [TestMethod]
        public void CompressedLoadDoesNotOverreadNonSeekableStreams() {
            // Non-seekable sources are left wherever decompression stopped, so a load cannot
            // block on a stream that never ends. The count never passes the document, and it
            // reaches at least the end of the deflate data; the few trailer bytes may stay
            // unpulled on the .NET Framework build.
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                using (var ms = new MemoryStream(doc)) {
                    var awkward = new PartialReadStream(new NonSeekableStream(ms), 3);
                    var file = new NbtFile();
                    long bytesRead = file.LoadFromStream(awkward, compression);
                    Assert.IsTrue(bytesRead <= doc.Length, compression + ": read past the document");
                    Assert.IsTrue(bytesRead >= doc.Length - 8, compression + ": stopped before the trailer region");
                }
            }
        }


        [TestMethod]
        public void CompressedLoadFromMidStreamOffsetWorks() {
            byte[] doc = MakeDoc(NbtCompression.ZLib);
            byte[] padded = new byte[10].Concat(doc).ToArray();
            using (var ms = new MemoryStream(padded)) {
                ms.Position = 10;
                var file = new NbtFile();
                long bytesRead = file.LoadFromStream(ms, NbtCompression.ZLib);
                Assert.AreEqual(doc.Length, bytesRead);
                Assert.AreEqual(ms.Length, ms.Position);
            }
        }


        [TestMethod]
        public void UncompressedLoadStillStopsExactlyAtDocumentEnd() {
            // Draining is a compressed-path behavior only. Uncompressed loads keep exact
            // consumption; NbtCodec and concatenated reads depend on it.
            byte[] doc = MakeDoc(NbtCompression.None);
            byte[] padded = doc.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
            using (var ms = new MemoryStream(padded)) {
                var file = new NbtFile();
                long bytesRead = file.LoadFromStream(ms, NbtCompression.None);
                Assert.AreEqual(doc.Length, bytesRead);
                Assert.AreEqual(doc.Length, ms.Position);
            }
        }


        [TestMethod]
        public void TrailingGarbageAfterCompressedDocument() {
            // Data after a compressed document is not meaningful (the decompressor's read-ahead
            // makes the document's extent unknowable), but a load should still succeed where the
            // decompressor tolerates it, and the byte count covers the whole stream.
            byte[] garbage = { 0x01, 0x02, 0x03, 0x04, 0x05 }; // must not start with the GZip magic

            byte[] gz = MakeDoc(NbtCompression.GZip).Concat(garbage).ToArray();
            var gzFile = new NbtFile();
            long gzRead = gzFile.LoadFromBuffer(gz, 0, gz.Length, NbtCompression.GZip);
            Assert.AreEqual(gz.Length, gzRead);
            TestFiles.AssertNbtSmallFile(gzFile);

            byte[] z = MakeDoc(NbtCompression.ZLib).Concat(garbage).ToArray();
#if NETFRAMEWORK
            // netstandard2.0 finds the Adler trailer as the stream's last four bytes, so trailing
            // garbage is indistinguishable from a corrupt checksum
            Assert.Throws<InvalidDataException>(
                () => new NbtFile().LoadFromBuffer(z, 0, z.Length, NbtCompression.ZLib));
#else
            var zFile = new NbtFile();
            long zRead = zFile.LoadFromBuffer(z, 0, z.Length, NbtCompression.ZLib);
            Assert.AreEqual(z.Length, zRead);
            TestFiles.AssertNbtSmallFile(zFile);
#endif
        }


        [TestMethod]
        public void CompressedRoundTripsStillLoadClean() {
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                var file = new NbtFile();
                long bytesRead = file.LoadFromBuffer(doc, 0, doc.Length, compression);
                Assert.AreEqual(doc.Length, bytesRead, compression.ToString());
                TestFiles.AssertNbtSmallFile(file);
            }
        }
    }
}
