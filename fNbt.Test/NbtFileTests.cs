using System;
using System.IO;

namespace fNbt.Test {
    [TestClass]
    public class NbtFileTests {
        const string TestDirName = "NbtFileTests";


        [TestInitialize]
        public void CreateTempDirectory() {
            Directory.CreateDirectory(TestDirName);
        }


        #region Loading Small Nbt Test File

        [TestMethod]
        public void LoadingSmallFileUncompressed() {
            var file = new NbtFile(TestFiles.Small);
            Assert.AreEqual(TestFiles.Small, file.FileName);
            Assert.AreEqual(NbtCompression.None, file.FileCompression);
            TestFiles.AssertSmallFile(file);
        }


        [TestMethod]
        public void LoadingSmallFileGZip() {
            var file = new NbtFile(TestFiles.SmallGZip);
            Assert.AreEqual(TestFiles.SmallGZip, file.FileName);
            Assert.AreEqual(NbtCompression.GZip, file.FileCompression);
            TestFiles.AssertSmallFile(file);
        }


        [TestMethod]
        public void LoadingSmallFileZLib() {
            var file = new NbtFile(TestFiles.SmallZLib);
            Assert.AreEqual(TestFiles.SmallZLib, file.FileName);
            Assert.AreEqual(NbtCompression.ZLib, file.FileCompression);
            TestFiles.AssertSmallFile(file);
        }

        #endregion


        #region Loading Big Nbt Test File

        [TestMethod]
        public void LoadingBigFileUncompressed() {
            var file = new NbtFile();
            long length = file.LoadFromFile(TestFiles.Big);
            TestFiles.AssertBigFile(file);
            Assert.AreEqual(new FileInfo(TestFiles.Big).Length, length);
        }


        [TestMethod]
        public void LoadingBigFileGZip() {
            var file = new NbtFile();
            long length = file.LoadFromFile(TestFiles.BigGZip);
            TestFiles.AssertBigFile(file);
            Assert.AreEqual(length, new FileInfo(TestFiles.BigGZip).Length);
        }


        [TestMethod]
        public void LoadingBigFileZLib() {
            var file = new NbtFile();
            long length = file.LoadFromFile(TestFiles.BigZLib);
            TestFiles.AssertBigFile(file);
            Assert.AreEqual(length, new FileInfo(TestFiles.BigZLib).Length);
        }


        [TestMethod]
        public void LoadingBigFileBuffer() {
            byte[] fileBytes = File.ReadAllBytes(TestFiles.Big);
            var file = new NbtFile();

            Assert.Throws<ArgumentNullException>(
                () => file.LoadFromBuffer(null, 0, fileBytes.Length, NbtCompression.AutoDetect, null));

            long length = file.LoadFromBuffer(fileBytes, 0, fileBytes.Length, NbtCompression.AutoDetect, null);
            TestFiles.AssertBigFile(file);
            Assert.AreEqual(new FileInfo(TestFiles.Big).Length, length);
        }


        [TestMethod]
        public void LoadingBigFileStream() {
            byte[] fileBytes = File.ReadAllBytes(TestFiles.Big);
            using (var ms = new MemoryStream(fileBytes)) {
                using (var nss = new NonSeekableStream(ms)) {
                    var file = new NbtFile();
                    long length = file.LoadFromStream(nss, NbtCompression.None, null);
                    TestFiles.AssertBigFile(file);
                    Assert.AreEqual(new FileInfo(TestFiles.Big).Length, length);
                }
            }
        }

        #endregion


        [TestMethod]
        public void SavingSmallFileUncompressed() {
            NbtFile file = TestFiles.MakeSmallFile();
            string testFileName = Path.Combine(TestDirName, "test.nbt");
            file.SaveToFile(testFileName, NbtCompression.None);
            FileAssert.AreEqual(TestFiles.Small, testFileName);
        }


        [TestMethod]
        public void SavingSmallFileUncompressedStream() {
            NbtFile file = TestFiles.MakeSmallFile();
            var nbtStream = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() => file.SaveToStream(null, NbtCompression.None));
            Assert.Throws<ArgumentException>(() => file.SaveToStream(nbtStream, NbtCompression.AutoDetect));
            Assert.Throws<ArgumentOutOfRangeException>(() => file.SaveToStream(nbtStream, (NbtCompression)255));
            file.SaveToStream(nbtStream, NbtCompression.None);
            nbtStream.Position = 0;
            using (FileStream testFileStream = File.OpenRead(TestFiles.Small)) {
                FileAssert.AreEqual(testFileStream, nbtStream);
            }
        }


        [TestMethod]
        public void ReloadFile() {
            ReloadFileInternal("bigtest.nbt", NbtCompression.None, true, true);
            ReloadFileInternal("bigtest.nbt.gz", NbtCompression.GZip, true, true);
            ReloadFileInternal("bigtest.nbt.z", NbtCompression.ZLib, true, true);
            ReloadFileInternal("bigtest.nbt", NbtCompression.None, false, true);
            ReloadFileInternal("bigtest.nbt.gz", NbtCompression.GZip, false, true);
            ReloadFileInternal("bigtest.nbt.z", NbtCompression.ZLib, false, true);
        }


        [TestMethod]
        public void ReloadFileUnbuffered() {
            ReloadFileInternal("bigtest.nbt", NbtCompression.None, true, false);
            ReloadFileInternal("bigtest.nbt.gz", NbtCompression.GZip, true, false);
            ReloadFileInternal("bigtest.nbt.z", NbtCompression.ZLib, true, false);
            ReloadFileInternal("bigtest.nbt", NbtCompression.None, false, false);
            ReloadFileInternal("bigtest.nbt.gz", NbtCompression.GZip, false, false);
            ReloadFileInternal("bigtest.nbt.z", NbtCompression.ZLib, false, false);
        }


        void ReloadFileInternal(string fileName, NbtCompression compression, bool bigEndian, bool buffered) {
            // Validation off: this test round-trips endianness, and bigtest's TAG_Long_Array
            // is (correctly) rejected by Bedrock conformance validation
            var loadedFile = new NbtFile(new NbtOptions { ValidateOnWrite = false });
            loadedFile.LoadFromFile(Path.Combine(TestFiles.DirName, fileName), NbtCompression.AutoDetect, null);
            // Flavor is fixed at construction; re-saving under another flavor means a new
            // NbtFile over the same root tag
            loadedFile = new NbtFile(loadedFile.RootTag, new NbtOptions {
                Flavor = bigEndian ? NbtFlavor.Java : NbtFlavor.Bedrock,
                ValidateOnWrite = false
            });
            if (!buffered) {
                loadedFile.BufferSize = 0;
            }
            long bytesWritten = loadedFile.SaveToFile(Path.Combine(TestDirName, fileName), compression);
            long bytesRead = loadedFile.LoadFromFile(Path.Combine(TestDirName, fileName), NbtCompression.AutoDetect,
                                                     null);
            Assert.AreEqual(bytesWritten, bytesRead);
            TestFiles.AssertBigFile(loadedFile);
        }


        [TestMethod]
        public void ReloadNonSeekableStream() {
            var loadedFile = new NbtFile(TestFiles.Big);
            using (var ms = new MemoryStream()) {
                using (var nss = new NonSeekableStream(ms)) {
                    long bytesWritten = loadedFile.SaveToStream(nss, NbtCompression.None);
                    ms.Position = 0;
                    Assert.Throws<NotSupportedException>(() => loadedFile.LoadFromStream(nss, NbtCompression.AutoDetect));
                    ms.Position = 0;
                    Assert.Throws<InvalidDataException>(() => loadedFile.LoadFromStream(nss, NbtCompression.ZLib));
                    ms.Position = 0;
                    long bytesRead = loadedFile.LoadFromStream(nss, NbtCompression.None);
                    Assert.AreEqual(bytesWritten, bytesRead);
                    TestFiles.AssertBigFile(loadedFile);
                }
            }
        }


        [TestMethod]
        public void LongStringRoundTrips() {
            // The length prefix is unsigned, so strings of 32,768..65,535 bytes are valid
            foreach (int len in new[] { 32767, 32768, 40000, 65535 }) {
                var value = new string('a', len);
                var root = new NbtCompound("root") { new NbtString("s", value) };
                byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.None);

                NbtFile file = TestFiles.Load(doc);
                Assert.AreEqual(value, file.RootTag.Get<NbtString>("s").Value);
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


        [TestMethod]
        public void SaveToBuffer() {
            var littleTag = new NbtCompound("Root");
            var testFile = new NbtFile(littleTag);

            byte[] buffer1 = testFile.SaveToBuffer(NbtCompression.None);
            var buffer2 = new byte[buffer1.Length];
            Assert.AreEqual(testFile.SaveToBuffer(buffer2, 0, NbtCompression.None), buffer2.Length);
            CollectionAssert.AreEqual(buffer1, buffer2);
        }


        [TestMethod]
        public void ZLibSmallWindowHeaderLoads() {
            // Valid ZLib headers are not always 0x78. Declare a 512-byte window instead,
            // legal here because the payload is tiny.
            var root = new NbtCompound("root") { new NbtInt("v", 12345) };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.ZLib);
            doc[0] = 0x18; // CM=8, CINFO=1
            doc[1] = 0x19; // FCHECK valid, FDICT clear

            var file = new NbtFile();
            file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.ZLib);
            Assert.AreEqual(12345, file.RootTag["v"].IntValue);

            file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.AutoDetect);
            Assert.AreEqual(NbtCompression.ZLib, file.FileCompression);
            Assert.AreEqual(12345, file.RootTag["v"].IntValue);
            Assert.AreEqual("root", NbtFile.ReadRootTagName(
                new MemoryStream(doc), NbtCompression.AutoDetect, NbtFlavor.Java));
        }


        [TestMethod]
        public void SaveToBufferCompressed() {
            // The compressed branch of SaveToBuffer is separate code from the exact-size
            // uncompressed path
            var root = new NbtCompound("root") { new NbtInt("v", 12345), new NbtString("s", "hello") };
            byte[] doc = new NbtFile(root).SaveToBuffer(NbtCompression.ZLib);
            var file = new NbtFile();
            file.LoadFromBuffer(doc, 0, doc.Length, NbtCompression.ZLib);
            Assert.AreEqual(12345, file.RootTag["v"].IntValue);
            Assert.AreEqual("hello", file.RootTag.Get<NbtString>("s").Value);
        }


        [TestMethod]
        public void ReadRootTag() {
            Assert.Throws<FileNotFoundException>(() => NbtFile.ReadRootTagName("NonExistentFile"));

            ReadRootTagInternal(TestFiles.Big, NbtCompression.None);
            ReadRootTagInternal(TestFiles.BigGZip, NbtCompression.GZip);
            ReadRootTagInternal(TestFiles.BigZLib, NbtCompression.ZLib);
        }


        void ReadRootTagInternal(string fileName, NbtCompression compression) {
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtFile.ReadRootTagName(fileName, (NbtCompression)255, NbtFlavor.Java));

            Assert.AreEqual("Level", NbtFile.ReadRootTagName(fileName));
            Assert.AreEqual("Level", NbtFile.ReadRootTagName(fileName, compression, NbtFlavor.Java));
            // The obsolete overload ignores bufferSize, even a negative one
#pragma warning disable 618
            Assert.AreEqual("Level", NbtFile.ReadRootTagName(fileName, compression, true, -1));
#pragma warning restore 618

            byte[] fileBytes = File.ReadAllBytes(fileName);
            using (var ms = new MemoryStream(fileBytes)) {
                using (var nss = new NonSeekableStream(ms)) {
                    Assert.AreEqual("Level", NbtFile.ReadRootTagName(nss, compression, NbtFlavor.Java));
                }
            }

            // Reading is chunked, so make sure a stream that hands back less than asked still works.
            using (var ms = new MemoryStream(fileBytes)) {
                using (var prs = new PartialReadStream(ms, 1)) {
                    Assert.AreEqual("Level", NbtFile.ReadRootTagName(prs, compression, NbtFlavor.Java));
                }
            }
        }


        [TestMethod]
        public void DefaultBufferSizeAppliesToNewFiles() {
            Assert.AreEqual(NbtFile.DefaultBufferSize, new NbtFile(new NbtCompound("Foo")).BufferSize);
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtFile.DefaultBufferSize = -1);
            NbtFile.DefaultBufferSize = 12345;
            Assert.AreEqual(12345, NbtFile.DefaultBufferSize);

            // Newly-created NbtFiles should use default buffer size
            NbtFile tempFile = new NbtFile(new NbtCompound("Foo"));
            Assert.AreEqual(NbtFile.DefaultBufferSize, tempFile.BufferSize);
            Assert.Throws<ArgumentOutOfRangeException>(() => tempFile.BufferSize = -1);
            tempFile.BufferSize = 54321;
            Assert.AreEqual(54321, tempFile.BufferSize);

            // Changing default buffer size should not retroactively change already-existing NbtFiles' buffer size.
            NbtFile.DefaultBufferSize = 8192;
            Assert.AreEqual(54321, tempFile.BufferSize);
        }


        [TestMethod]
        public void RootTagSetterRejectsNullAndUnnamed() {
            NbtCompound oldRoot = new NbtCompound("defaultRoot");
            NbtFile newFile = new NbtFile(oldRoot);

            // Ensure that inappropriate tags are not accepted as RootTag
            Assert.Throws<ArgumentNullException>(() => newFile.RootTag = null);
            Assert.Throws<ArgumentException>(() => newFile.RootTag = new NbtCompound());

            // Ensure that the root has not changed
            Assert.AreSame(oldRoot, newFile.RootTag);

            // Invalidate the root tag, and ensure that expected exception is thrown
            oldRoot.Name = null;
            Assert.Throws<NbtFormatException>(() => newFile.SaveToBuffer(NbtCompression.None));
        }


        [TestMethod]
        public void NullArgumentsThrow() {
            Assert.Throws<ArgumentNullException>(() => new NbtFile((NbtCompound)null));
            Assert.Throws<ArgumentNullException>(() => new NbtFile((string)null));

            NbtFile file = new NbtFile();
            Assert.Throws<ArgumentNullException>(() => file.LoadFromBuffer(null, 0, 1, NbtCompression.None));
            Assert.Throws<ArgumentNullException>(() => file.LoadFromBuffer(null, 0, 1, NbtCompression.None, tag => true));
            Assert.Throws<ArgumentNullException>(() => file.LoadFromFile(null));
            Assert.Throws<ArgumentNullException>(() => file.LoadFromFile(null, NbtCompression.None, tag => true));
            Assert.Throws<ArgumentNullException>(() => file.LoadFromStream(null, NbtCompression.AutoDetect));
            Assert.Throws<ArgumentNullException>(() => file.LoadFromStream(null, NbtCompression.AutoDetect, tag => true));

            Assert.Throws<ArgumentNullException>(() => file.SaveToBuffer(null, 0, NbtCompression.None));
            Assert.Throws<ArgumentNullException>(() => file.SaveToFile(null, NbtCompression.None));
            Assert.Throws<ArgumentNullException>(() => file.SaveToStream(null, NbtCompression.None));

            Assert.Throws<ArgumentNullException>(() => NbtFile.ReadRootTagName(null));
            Assert.Throws<ArgumentNullException>(
                () => NbtFile.ReadRootTagName((Stream)null, NbtCompression.None, NbtFlavor.Java));

        }


        [TestCleanup]
        public void DeleteTempDirectory() {
            if (Directory.Exists(TestDirName)) {
                foreach (string file in Directory.GetFiles(TestDirName)) {
                    File.Delete(file);
                }
                Directory.Delete(TestDirName);
            }
        }
    }
}
