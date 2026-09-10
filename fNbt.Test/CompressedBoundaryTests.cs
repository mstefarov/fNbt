using System;
using System.IO;
using System.IO.Compression;
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
        public void GZipLoadDrainsPayloadAfterTheRoot() {
            byte[] root = MakeDoc(NbtCompression.None);
            byte[] tail = new byte[64 * 1024];
            new Random(64000).NextBytes(tail);
            byte[] doc;
            using (MemoryStream output = new MemoryStream()) {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true)) {
                    gzip.Write(root, 0, root.Length);
                    gzip.Write(tail, 0, tail.Length);
                }
                doc = output.ToArray();
            }
            // The trailer must remain beyond the inflater's first input buffer.
            Assert.IsTrue(doc.Length > 32 * 1024);
            byte[] corrupt = Flip(doc, doc.Length - 8);

            foreach (int bufferSize in new[] { 0, 8192 }) {
                foreach (bool seekable in new[] { true, false }) {
                    foreach (bool badTrailer in new[] { false, true }) {
                        string context = "buffer " + bufferSize + ", seekable " + seekable + ", corrupt " + badTrailer;
                        using (MemoryStream input = new MemoryStream(badTrailer ? corrupt : doc))
                        using (Stream source = seekable ? input : new PartialReadStream(new NonSeekableStream(input), 7)) {
                            NbtFile file = new NbtFile { BufferSize = bufferSize };
                            if (badTrailer) {
                                Assert.Throws<InvalidDataException>(
                                    () => file.LoadFromStream(source, NbtCompression.GZip), context);
                            } else {
                                long bytesRead = file.LoadFromStream(source, NbtCompression.GZip);
                                Assert.AreEqual((long)doc.Length, bytesRead, context);
                                Assert.AreEqual(input.Length, input.Position, context);
                                TestFiles.AssertSmallFile(file);
                            }
                        }
                    }
                }
            }
        }


        [TestMethod]
        public void CompressedLoadCountsBytesOnAwkwardNonSeekableStreams() {
            // Decompression must cope with a source that dribbles three bytes per read, and the
            // reported count must be exactly what the source handed out. Non-seekable sources
            // are not drained, so the decompressor may leave the trailer unread.
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                using (var ms = new MemoryStream(doc)) {
                    var awkward = new PartialReadStream(new NonSeekableStream(ms), 3);
                    var file = new NbtFile();
                    long bytesRead = file.LoadFromStream(awkward, compression);
                    Assert.AreEqual(ms.Position, bytesRead, compression.ToString());
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
            var zFile = new NbtFile();
            long zRead = zFile.LoadFromBuffer(z, 0, z.Length, NbtCompression.ZLib);
            Assert.AreEqual(z.Length, zRead);
            TestFiles.AssertSmallFile(zFile);
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
        }
#endif


        [TestMethod]
        public void CompressedLoadDoesNotReadPastANonSeekableSource() {
            // GZipStream reads on past a finished member looking for another one. On a source
            // that stays open, a socket for example, that read blocks forever instead of
            // returning end-of-stream, so no load path may read past the compressed data.
            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] doc = MakeDoc(compression);
                var file = new NbtFile();
                file.LoadFromStream(new ThrowOnOverreadStream(doc), compression);
                TestFiles.AssertSmallFile(file);
            }
        }


        [TestMethod]
        public void TrailerCorruptionIsCaughtAtAwkwardSizes() {
            // At these payload sizes the trailer lands in a different decompressor read than the
            // last of the data, so a load that stopped at the root tag never saw it. Seekable
            // sources are drained; non-seekable ones either drain a ZLibStream, which stops at the
            // member end, or locate the GZip trailer themselves.
            foreach ((NbtCompression compression, int payloadSize, int trailer) in
                     new[] { (NbtCompression.GZip, 8148, 8), (NbtCompression.ZLib, 24530, 4) }) {
                var payload = new byte[payloadSize];
                new Random(payloadSize).NextBytes(payload);
                var root = new NbtCompound("root") { new NbtByteArray("data", payload) };
                byte[] doc = new NbtFile(root).SaveToBuffer(compression);
                byte[] bad = Flip(doc, doc.Length - trailer);

                Assert.Throws<InvalidDataException>(
                    () => new NbtFile().LoadFromBuffer(bad, 0, bad.Length, compression),
                    compression + ": seekable load accepted a corrupt trailer");
                Assert.Throws<InvalidDataException>(
                    () => new NbtFile().LoadFromStream(new NonSeekableStream(new MemoryStream(bad)), compression),
                    compression + ": non-seekable load accepted a corrupt trailer");
            }
        }


        [TestMethod]
        public void NonSeekableZLibLoadVerifiesChecksum() {
            // ZLib decompressors stop at the end of the member, so the load can finish it and
            // check the trailer on any source, however the source chunks its reads
            byte[] doc = MakeDoc(NbtCompression.ZLib);
            byte[] bad = Flip(doc, doc.Length - 1);
            foreach (int increment in new[] { 1, 3, 4096 }) {
                foreach (int bufferSize in new[] { 0, 8192 }) {
                    var good = new NbtFile { BufferSize = bufferSize };
                    good.LoadFromStream(Dribble(doc, increment), NbtCompression.ZLib);
                    TestFiles.AssertSmallFile(good);

                    var file = new NbtFile { BufferSize = bufferSize };
                    Assert.Throws<InvalidDataException>(
                        () => file.LoadFromStream(Dribble(bad, increment), NbtCompression.ZLib),
                        "increment " + increment + ", buffer " + bufferSize);
                }
            }
        }


        [TestMethod]
        public void NonSeekableGZipLoadVerifiesTrailer() {
            // GZipStream cannot be drained on a non-seekable source, so the load inflates the
            // raw deflate data itself, computes the trailer values, and finds the trailer
            // wherever a read boundary left it, including a missing or partial one
            byte[] doc = MakeDoc(NbtCompression.GZip);
            var corruptions = new[] {
                ("crc", Flip(doc, doc.Length - 8)),
                ("size", Flip(doc, doc.Length - 1)),
                ("missing trailer", doc.Take(doc.Length - 8).ToArray()),
                ("partial trailer", doc.Take(doc.Length - 3).ToArray())
            };
            foreach (int increment in new[] { 1, 3, 4096 }) {
                var good = new NbtFile();
                good.LoadFromStream(Dribble(doc, increment), NbtCompression.GZip);
                TestFiles.AssertSmallFile(good);

                foreach ((string name, byte[] bad) in corruptions) {
                    var file = new NbtFile();
                    Assert.Throws<InvalidDataException>(
                        () => file.LoadFromStream(Dribble(bad, increment), NbtCompression.GZip),
                        name + " at increment " + increment);
                }
            }
        }


        [TestMethod]
        public void TrailerIsFoundAtEveryChunkSplit() {
            // The located trailer can start anywhere in the last chunk the inflater took, or
            // straddle its end by any number of bytes. One-byte reads over a range of document
            // sizes walk through every split, for the 8-byte GZip and 4-byte ZLib trailers.
            for (int payloadSize = 0; payloadSize < 40; payloadSize++) {
                var payload = new byte[payloadSize];
                new Random(payloadSize).NextBytes(payload);
                var root = new NbtCompound("root") { new NbtByteArray("data", payload) };
                foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                    byte[] doc = new NbtFile(root).SaveToBuffer(compression);
                    var file = new NbtFile();
                    file.LoadFromStream(Dribble(doc, 1), compression);
                    CollectionAssert.AreEqual(payload, file.RootTag["data"].ByteArrayValue);

                    byte[] bad = Flip(doc, doc.Length - (compression == NbtCompression.GZip ? 8 : 4));
                    var corrupt = new NbtFile();
                    Assert.Throws<InvalidDataException>(
                        () => corrupt.LoadFromStream(Dribble(bad, 1), compression),
                        compression + " with payload " + payloadSize);
                }
            }
        }


        [TestMethod]
        public void NonSeekableGZipLoadSkipsOptionalHeaderFields() {
            // RFC 1952 allows an extra field, a file name, a comment and a header CRC, none of
            // which Minecraft writes. The non-seekable path parses the header itself, so it has
            // to step over all of them; the seekable path leaves that to GZipStream. The header
            // CRC is the low 16 bits of the CRC-32 of the header bytes, which GZipStream checks.
            byte[] doc = MakeDoc(NbtCompression.GZip);
            byte[] extra = { 0x41, 0x42, 3, 0, 1, 2, 3 }; // one subfield: id "AB", three bytes
            byte[] name = System.Text.Encoding.ASCII.GetBytes("level.dat\0");
            byte[] comment = System.Text.Encoding.ASCII.GetBytes("made by a test\0");
            var header = new MemoryStream();
            header.Write(new byte[] { 0x1F, 0x8B, 8, 0x02 | 0x04 | 0x08 | 0x10, 0, 0, 0, 0, 0, 0xFF }, 0, 10);
            header.Write(new byte[] { (byte)extra.Length, 0 }, 0, 2);
            header.Write(extra, 0, extra.Length);
            header.Write(name, 0, name.Length);
            header.Write(comment, 0, comment.Length);
            byte[] headerBytes = header.ToArray();
            var crc = new Crc32Stream(new MemoryStream(headerBytes));
            while (crc.ReadByte() >= 0) { }
            byte[] fancy = headerBytes
                .Concat(new[] { (byte)crc.Crc, (byte)(crc.Crc >> 8) })
                .Concat(doc.Skip(10))
                .ToArray();

            // Truncate at every header byte, including within optional fields and their terminators.
            for (int length = 0; length < headerBytes.Length + 2; length++) {
                using (Stream truncated = new NonSeekableStream(new MemoryStream(fancy, 0, length))) {
                    Assert.Throws<EndOfStreamException>(
                        () => new NbtFile().LoadFromStream(truncated, NbtCompression.GZip),
                        "Header length " + length);
                }
            }

            // A wrong header CRC is rejected on both paths
            byte[] badCrc = (byte[])fancy.Clone();
            badCrc[headerBytes.Length] ^= 0xFF;

            foreach (bool seekable in new[] { false, true }) {
                var file = new NbtFile();
                Stream source = seekable ? new MemoryStream(fancy) : new NonSeekableStream(new MemoryStream(fancy));
                file.LoadFromStream(source, NbtCompression.GZip);
                TestFiles.AssertSmallFile(file);

                Stream badSource = seekable ? new MemoryStream(badCrc) : new NonSeekableStream(new MemoryStream(badCrc));
                Assert.Throws<InvalidDataException>(
                    () => new NbtFile().LoadFromStream(badSource, NbtCompression.GZip),
                    seekable ? "seekable" : "non-seekable");
            }
        }


        [TestMethod]
        public void NonSeekableGZipLoadRejectsInvalidHeaders() {
            byte[] doc = MakeDoc(NbtCompression.GZip);
            // Bad magic bytes, unsupported compression method, and reserved flag bits.
            foreach (int index in new[] { 0, 1, 2, 3 }) {
                byte[] malformed = (byte[])doc.Clone();
                malformed[index] = 0xE0;
                using (Stream source = new NonSeekableStream(new MemoryStream(malformed))) {
                    Assert.Throws<InvalidDataException>(
                        () => new NbtFile().LoadFromStream(source, NbtCompression.GZip),
                        "Header byte " + index);
                }
            }
        }


        [TestMethod]
        public void Crc32StreamMatchesTheFrameworkTrailer() {
            // The framework's GZipStream writes the real CRC-32 into its trailer, an oracle for
            // the internal checksum across the fold and tail boundaries and across read sizes,
            // since the register carries over between Read calls.
            var empty = new Crc32Stream(new MemoryStream());
            Assert.AreEqual(-1, empty.ReadByte());
            Assert.AreEqual(0u, empty.Crc);

            // .NET Framework's GZipStream writes nothing at all for an empty payload
            var rnd = new Random(32);
            foreach (int length in Enumerable.Range(1, 199).Concat(new[] { 255, 256, 1000, 4095, 4096, 65537 })) {
                var payload = new byte[length];
                rnd.NextBytes(payload);
                byte[] gz;
                using (var ms = new MemoryStream()) {
                    using (var g = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true)) {
                        g.Write(payload, 0, payload.Length);
                    }
                    gz = ms.ToArray();
                }
                uint expected = BitConverter.ToUInt32(gz, gz.Length - 8);
                foreach (int increment in new[] { 1, 7, 63, 64, 65, 8192 }) {
                    var crc = new Crc32Stream(new PartialReadStream(new MemoryStream(payload), increment));
                    var sink = new byte[8192];
                    while (crc.Read(sink, 0, sink.Length) > 0) { }
                    Assert.AreEqual(expected, crc.Crc, "length " + length + ", increment " + increment);
                    Assert.AreEqual(length, crc.BytesRead);
                }
            }
        }


        static byte[] Flip(byte[] doc, int index) {
            byte[] copy = (byte[])doc.Clone();
            copy[index] ^= 0xFF;
            return copy;
        }


        static Stream Dribble(byte[] doc, int increment) {
            return new PartialReadStream(new NonSeekableStream(new MemoryStream(doc)), increment);
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
