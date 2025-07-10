using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

[MemoryDiagnoser]
public class ReadBenchmarks {
    private byte[] bigFileUncompressedBytes;
    private byte[] bigFileGZipBytes;

    private byte[] largeByteArray;
    private byte[] serializedLargeByteArrayNbt;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();

        bigFileUncompressedBytes = File.ReadAllBytes(BenchmarkTestFiles.BigTestFile);

        using (var ms = new MemoryStream()) {
            using (var zipStream = new GZipStream(ms, CompressionMode.Compress, true)) {
                zipStream.Write(bigFileUncompressedBytes, 0, bigFileUncompressedBytes.Length);
            }
            bigFileGZipBytes = ms.ToArray();
        }

        // Setup for raw array benchmarks
        largeByteArray = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeByteArray);

        var nbtFileWithLargeArray = new NbtFile(new NbtCompound("root") {
            new NbtByteArray("payload", largeByteArray)
        });
        serializedLargeByteArrayNbt = nbtFileWithLargeArray.SaveToBuffer(NbtCompression.None);
    }

    // #region 1. High-Level File Loading
    [Benchmark(Description = "NbtFile.LoadFromFile")]
    public NbtFile FileLoad() {
        var file = new NbtFile();
        file.LoadFromFile(BenchmarkTestFiles.BigTestFile, NbtCompression.AutoDetect, null);
        return file;
    }


    // Full Load vs. NbtReader for Finding a Tag
    [Benchmark(Description = "Find Tag (Full Load)")]
    public NbtTag? FindTag_FullLoad() {
        var file = new NbtFile();
        file.LoadFromBuffer(bigFileGZipBytes, 0, bigFileGZipBytes.Length, NbtCompression.GZip, null);
        return file.RootTag["listTest (compound)"];
    }

    [Benchmark(Description = "Find Tag (NbtReader)")]
    public NbtTag? FindTag_NbtReader() {
        using var ms = new MemoryStream(bigFileGZipBytes);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);

        var reader = new NbtReader(gzip);
        if (reader.ReadToDescendant("listTest (compound)")) {
            return reader.ReadAsTag();
        }
        return null;
    }

    // Loading with Tag-Skipping Filter
    [Benchmark(Description = "Load with Filter (Skip All)")]
    public NbtFile LoadWithFilter_SkipAll() {
        var file = new NbtFile();
        file.LoadFromBuffer(bigFileUncompressedBytes, 0, bigFileUncompressedBytes.Length, NbtCompression.None, tag => tag.Parent == null);
        return file;
    }

    // NbtReader: Skipping vs. Parsing a Subtree
    [Benchmark(Description = "Reader: Skip Subtree")]
    public void ReaderParseVsSkip_Skip() {
        using (var ms = new MemoryStream(bigFileUncompressedBytes)) {
            var reader = new NbtReader(ms);
            reader.ReadToDescendant("nested compound test");
            reader.Skip();
        }
    }

    [Benchmark(Description = "Reader: Parse Subtree")]
    public NbtTag ReaderParseVsSkip_Parse() {
        using (var ms = new MemoryStream(bigFileUncompressedBytes)) {
            var reader = new NbtReader(ms);
            reader.ReadToDescendant("nested compound test");
            return reader.ReadAsTag();
        }
    }

    // Reading a List of Primitives
    [Benchmark(Description = "Reader: List")]
    public long[] ReadList_AsArray() {
        using (var ms = new MemoryStream(bigFileUncompressedBytes)) {
            var reader = new NbtReader(ms);
            reader.ReadToDescendant("listTest (long)");
            return reader.ReadListAsArray<long>();
        }
    }

    // Raw Byte Array Reading Performance
    [Benchmark(Description = "Reader: ByteArray")]
    public byte[] RawByteArray_Read() {
        using (var ms = new MemoryStream(serializedLargeByteArrayNbt)) {
            var reader = new NbtReader(ms);
            reader.ReadToDescendant("payload");
            return (byte[])reader.ReadValue();
        }
    }
}
