using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Checksum validation and stream-consumption contracts for compressed loads.
    // Seekable sources are read to the container's end, so the trailer checksum is validated
    // where the target supports it and byte counts are deterministic. Non-seekable sources stop
    // wherever decompression did, since draining them could block.
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
                    TestFiles.AssertSmallFile(file);
                }
            }
        }


        [TestMethod]
        public void CompressedLoadCountsBytesOnAwkwardNonSeekableStreams() {
            // Decompression must cope with a source that dribbles three bytes per read, and
            // the reported byte count must not exceed the document
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                using (var ms = new MemoryStream(doc)) {
                    var awkward = new PartialReadStream(new NonSeekableStream(ms), 3);
                    var file = new NbtFile();
                    long bytesRead = file.LoadFromStream(awkward, compression);
                    Assert.IsTrue(bytesRead <= doc.Length, compression + ": counted past the document");
                    TestFiles.AssertSmallFile(file);
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
            TestFiles.AssertSmallFile(gzFile);

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
            TestFiles.AssertSmallFile(zFile);
#endif
        }


#if NETFRAMEWORK
        // The internal ZLibStream only exists in the netstandard2.0 build; net6+ uses the framework's
        [TestMethod]
        public void ZLibStreamChecksumCountsSingleByteAccess() {
            // The internal stream's ReadByte/WriteByte must feed the Adler-32 exactly once per
            // byte on every target. DeflateStream's own single-byte fast path would skip the
            // checksum; the Stream fallback would count bytes twice.
            byte[] payload = new byte[1000];
            for (int i = 0; i < payload.Length; i++) {
                payload[i] = (byte)(i * 31);
            }

            int bulkChecksum;
            byte[] compressed;
            using (var ms = new MemoryStream()) {
                using (var z = new ZLibStream(ms, System.IO.Compression.CompressionMode.Compress, true)) {
                    z.Write(payload, 0, payload.Length);
                    bulkChecksum = z.Checksum;
                }
                compressed = ms.ToArray();
            }

            using (var ms = new MemoryStream()) {
                using (var z = new ZLibStream(ms, System.IO.Compression.CompressionMode.Compress, true)) {
                    foreach (byte b in payload) {
                        z.WriteByte(b);
                    }
                    Assert.AreEqual(bulkChecksum, z.Checksum, "byte-wise write checksum");
                }
            }

            using (var ms = new MemoryStream(compressed)) {
                var z = new ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress, true);
                int b;
                int index = 0;
                while ((b = z.ReadByte()) >= 0) {
                    Assert.AreEqual(payload[index++], (byte)b);
                }
                Assert.AreEqual(payload.Length, index);
                Assert.AreEqual(bulkChecksum, z.Checksum, "byte-wise read checksum");
            }

            // With tracking off, the checksum stays at its initial value
            using (var ms = new MemoryStream(compressed)) {
                var z = new ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress, true, trackChecksum: false);
                var sink = new byte[payload.Length];
                while (z.Read(sink, 0, sink.Length) > 0) { }
                Assert.AreEqual(1, z.Checksum);
            }
        }
#endif


        [TestMethod]
        public void CompressedLoadDoesNotReadPastANonSeekableSource() {
            // GZipStream supports concatenated members, so draining it reads on looking for
            // another header. On a source that stays open, a socket for example, that read
            // blocks forever instead of returning end-of-stream. Non-seekable sources are
            // therefore not drained, and nothing may be read past the compressed data.
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                var file = new NbtFile();
                file.LoadFromStream(new ThrowOnOverreadStream(doc), compression);
                TestFiles.AssertSmallFile(file);
            }
        }


        [TestMethod]
        public void DrainingCatchesTrailerCorruptionAtAwkwardSizes() {
            // Whether the parse alone pulls the trailer depends on how the document aligns with
            // the decompressor's buffers. These payload sizes are ones where it does not, so an
            // undrained load accepts a corrupt checksum. Draining is what closes it, which is
            // why seekable sources are read to the end; non-seekable ones keep the gap.
            foreach ((NbtCompression compression, int payloadSize, int trailer) in
                     new[] { (NbtCompression.GZip, 8148, 8), (NbtCompression.ZLib, 24541, 4) }) {
                var payload = new byte[payloadSize];
                new Random(payloadSize).NextBytes(payload);
                var root = new NbtCompound("root") { new NbtByteArray("data", payload) };
                byte[] doc = new NbtFile(root).SaveToBuffer(compression);
                byte[] bad = (byte[])doc.Clone();
                bad[bad.Length - trailer] ^= 0xFF;

                Assert.Throws<InvalidDataException>(
                    () => new NbtFile().LoadFromBuffer(bad, 0, bad.Length, compression),
                    compression + ": seekable load accepted a corrupt trailer");

            }
        }


        // Stands in for a source that would block rather than report end-of-stream.
        sealed class ThrowOnOverreadStream : Stream {
            readonly byte[] data;
            int position;


            public ThrowOnOverreadStream(byte[] data) {
                this.data = data;
            }


            public override int Read(byte[] buffer, int offset, int count) {
                if (position >= data.Length) {
                    throw new IOException("Read past the compressed data; a live source would block here.");
                }
                int bytesRead = Math.Min(count, data.Length - position);
                Buffer.BlockCopy(data, position, buffer, offset, bytesRead);
                position += bytesRead;
                return bytesRead;
            }


            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }

            public override long Position {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
