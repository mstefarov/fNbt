using System;
using System.IO;

namespace fNbt.Test {
    // Arrays past the large-object threshold take the uninitialized-allocation and staged
    // endian-swap paths, so every byte must come out written and the caller's data untouched.
    [TestClass]
    public class LargeArrayTests {
        const int LargeByteCount = ArrayAllocator.LargeObjectThreshold + 257;


        [TestMethod]
        public void LargeExactBuffersMatchStreamOutput() {
            byte[] payload = new byte[NbtBinaryWriter.MaxWriteChunk + 257];
            // Vary the data so repeating a chunk changes the result.
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31 + 7 + i / NbtBinaryWriter.MaxWriteChunk);
            NbtCompound root = new NbtCompound("root") {
                new NbtByteArray("bytes", payload),
                new NbtString("tail", "done")
            };

            using (MemoryStream expectedStream = new MemoryStream())
            using (MemoryStream writerStream = new MemoryStream()) {
                new NbtFile(root).SaveToStream(expectedStream, NbtCompression.None);
                byte[] expected = expectedStream.ToArray();

                CollectionAssert.AreEqual(expected, new NbtFile(root).SaveToBuffer(NbtCompression.None));
                CollectionAssert.AreEqual(expected, NbtCodec.For(NbtFlavor.Java).WriteTag(root));

                using (BufferedStream buffered = new BufferedStream(writerStream)) {
                    NbtWriter writer = new NbtWriter(buffered, "root");
                    writer.WriteByteArray("bytes", payload);
                    writer.WriteString("tail", "done");
                    writer.EndCompound();
                    buffered.Flush();
                    NbtFile reloaded = TestFiles.FinishAndReload(writer, writerStream);
                    CollectionAssert.AreEqual(expected, writerStream.ToArray());
                    NbtAssert.AreEqual(root, reloaded.RootTag);
                }
            }
        }


        [TestMethod]
        public void LargeCompressedBufferRoundTrips() {
            byte[] payload = new byte[LargeByteCount * 4];
            new Random(12345).NextBytes(payload);
            NbtFile file = new NbtFile(new NbtCompound("root") { new NbtByteArray("bytes", payload) });

            foreach (NbtCompression compression in new[] { NbtCompression.GZip, NbtCompression.ZLib }) {
                byte[] buffer = file.SaveToBuffer(compression);
                Assert.IsTrue(buffer.Length > 3 * 64 * 1024, compression.ToString());
                NbtFile reloaded = new NbtFile();
                reloaded.LoadFromBuffer(buffer, 0, buffer.Length, NbtCompression.AutoDetect);
                CollectionAssert.AreEqual(payload, reloaded.RootTag["bytes"].ByteArrayValue);
            }
        }


        [TestMethod]
        public void LargeArrayPayloadsRoundTripUnderBothEndiannesses() {
            byte[] bytes = new byte[LargeByteCount];
            int[] ints = new int[LargeByteCount / sizeof(int) + 37];
            long[] longs = new long[LargeByteCount / sizeof(long) + 37];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 29 + 11);
            for (int i = 0; i < ints.Length; i++) ints[i] = unchecked(i * 1_000_003 + 17);
            for (int i = 0; i < longs.Length; i++) longs[i] = unchecked((long)i * 1_099_511_627_791L + 23);
            int[] intsBefore = (int[])ints.Clone();
            long[] longsBefore = (long[])longs.Clone();

            foreach (NbtFlavor flavor in new[] { NbtFlavor.Java, NbtFlavor.Bedrock }) {
                var stream = new MemoryStream();
                var writer = new NbtBinaryWriter(stream, flavor);
                writer.Write(bytes, 0, bytes.Length);
                writer.Write(ints, 0, ints.Length);
                writer.Write(longs, 0, longs.Length);
                // The swapped copy must not touch the caller's arrays
                CollectionAssert.AreEqual(intsBefore, ints);
                CollectionAssert.AreEqual(longsBefore, longs);

                stream.Position = 0;
                var reader = new NbtBinaryReader(stream, flavor);
                CollectionAssert.AreEqual(bytes, reader.ReadByteArray(bytes.Length));
                CollectionAssert.AreEqual(ints, reader.ReadInt32Array(ints.Length));
                CollectionAssert.AreEqual(longs, reader.ReadInt64Array(longs.Length));
                Assert.AreEqual(stream.Length, stream.Position);
            }
        }


        [TestMethod]
        public void OffsetArrayWritesRoundTripAcrossStagingChunks() {
            foreach ((NbtFlavor flavor, int chunkBytes) in new[] {
                (NbtFlavor.Java, 64 * 1024),
                (NbtFlavor.Bedrock, NbtBinaryWriter.MaxWriteChunk)
            }) {
                // Use an offset and leave a short final chunk.
                int[] ints = new int[chunkBytes / sizeof(int) + 5];
                long[] longs = new long[chunkBytes / sizeof(long) + 5];
                for (int i = 0; i < ints.Length; i++) ints[i] = unchecked(i * 16_777_619 ^ (int)0xA5A5A5A5);
                for (int i = 0; i < longs.Length; i++) longs[i] = unchecked((long)i * 1_099_511_627_791L ^ (long)0xA5A5A5A5A5A5A5A5UL);
                int[] intsBefore = (int[])ints.Clone();
                long[] longsBefore = (long[])longs.Clone();

                int intCount = ints.Length - 4;
                int longCount = longs.Length - 4;
                using (MemoryStream stream = new MemoryStream(intCount * sizeof(int) + longCount * sizeof(long))) {
                    NbtBinaryWriter writer = new NbtBinaryWriter(stream, flavor);
                    writer.Write(ints, 2, intCount);
                    writer.Write(longs, 2, longCount);
                    CollectionAssert.AreEqual(intsBefore, ints);
                    CollectionAssert.AreEqual(longsBefore, longs);
                    stream.Position = 0;
                    NbtBinaryReader reader = new NbtBinaryReader(stream, flavor);
                    int[] intsRead = reader.ReadInt32Array(intCount);
                    long[] longsRead = reader.ReadInt64Array(longCount);
                    for (int i = 0; i < intsRead.Length; i++) Assert.AreEqual(intsBefore[i + 2], intsRead[i]);
                    for (int i = 0; i < longsRead.Length; i++) Assert.AreEqual(longsBefore[i + 2], longsRead[i]);
                    Assert.AreEqual(stream.Length, stream.Position);
                }
            }
        }
    }
}
