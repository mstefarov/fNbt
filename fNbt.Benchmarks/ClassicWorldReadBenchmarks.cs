using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Reading ClassicWorld maps. Mostly block data, so these measure bulk throughput and GZip cost
// rather than per-tag overhead.
public class ClassicWorldReadBenchmarks {
    [Params(CwSize.Small, CwSize.Medium)]
    public CwSize Size;

    string filePath = null!;
    byte[] gzipBytes = null!;
    byte[] rawBytes = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        // The parsed tree is not an input to any read benchmark. Do not retain its two large
        // arrays and silently double the live payload while measuring another parse.
        CwMap map = ClassicWorldFiles.Load(Size);
        filePath = map.FilePath;
        gzipBytes = map.GZipBytes;
        rawBytes = map.RawBytes;
    }


    // Whole-Map Loading

    // No Baseline=true here: with --baseline runs, the NuGet job is the baseline, and a method
    // baseline on top would make every ratio compare against this method instead of per-method.
    [AverageBenchmark]
    [Benchmark(Description = "Load map from file (GZip)")]
    public NbtFile LoadFromFile() {
        var file = new NbtFile();
        file.LoadFromFile(filePath, NbtCompression.AutoDetect, null);
        return file;
    }


    [AverageBenchmark]
    [Benchmark(Description = "Load map from buffer (GZip)")]
    public NbtFile LoadFromBufferGZip() {
        var file = new NbtFile();
        file.LoadFromBuffer(gzipBytes, 0, gzipBytes.Length, NbtCompression.GZip, null);
        return file;
    }


    [UnstableBenchmark]
    [Benchmark(Description = "Load map from buffer (uncompressed)")]
    public NbtFile LoadFromBufferUncompressed() {
        var file = new NbtFile();
        file.LoadFromBuffer(rawBytes, 0, rawBytes.Length, NbtCompression.None, null);
        return file;
    }


    // Loading With a Tag-Skipping Filter

    static bool HeaderOnly(NbtTag tag) {
        return tag.Name != "BlockArray" && tag.Name != "BlockArray2";
    }


    [VeryStableBenchmark]
    [Benchmark(Description = "Load header only, selector (GZip)")]
    public NbtFile LoadHeaderOnlyGZip() {
        var file = new NbtFile();
        file.LoadFromBuffer(gzipBytes, 0, gzipBytes.Length, NbtCompression.GZip, HeaderOnly);
        return file;
    }


    // Skipping an array is a seek here, but an inflate-and-discard in the GZip case above.
    // Average stability because the 870 KB it allocates per op spreads its launches where the
    // GZip variant's stay put.
    [AverageBenchmark]
    [Benchmark(Description = "Load header only, selector (uncompressed)")]
    public NbtFile LoadHeaderOnlyUncompressed() {
        var file = new NbtFile();
        file.LoadFromBuffer(rawBytes, 0, rawBytes.Length, NbtCompression.None, HeaderOnly);
        return file;
    }


    static bool BlocksOnly(NbtTag tag) {
        return tag.Name != "Metadata";
    }


    // The inverse of the header-only rows: block arrays load, the ~9.3k-tag subtree is skipped.
    [UnstableBenchmark]
    [Benchmark(Description = "Load blocks only, selector (uncompressed)")]
    public NbtFile LoadBlocksOnlyUncompressed() {
        var file = new NbtFile();
        file.LoadFromBuffer(rawBytes, 0, rawBytes.Length, NbtCompression.None, BlocksOnly);
        return file;
    }


    // X/Y/Z sit near the front, so the reader stops before reaching the block arrays.
    [VeryStableBenchmark]
    [Benchmark(Description = "Read dimensions only (NbtReader, GZip)")]
    public int ReadDimensions() {
        using var ms = new MemoryStream(gzipBytes);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);

        var reader = new NbtReader(gzip);
        int x = 0, y = 0, z = 0;
        while (reader.ReadToFollowing()) {
            switch (reader.TagName) {
                case "X": x = reader.ReadValueAs<short>(); break;
                case "Y": y = reader.ReadValueAs<short>(); break;
                case "Z": z = reader.ReadValueAs<short>(); break;
            }
            if (x != 0 && y != 0 && z != 0) {
                break;
            }
        }
        return x * y * z;
    }


    // Bulk Array Access

    [UnstableBenchmark]
    [Benchmark(Description = "Read BlockArray (NbtReader)")]
    public byte[] ReadBlockArray() {
        using var ms = new MemoryStream(rawBytes);

        var reader = new NbtReader(ms);
        reader.ReadToFollowing("BlockArray");
        return (byte[])reader.ReadValue();
    }


    // The parser's floor cost.
    [VeryStableBenchmark]
    [Benchmark(Description = "Skip whole map (NbtReader)")]
    public int SkipWholeMap() {
        using var ms = new MemoryStream(rawBytes);

        var reader = new NbtReader(ms);
        reader.ReadToFollowing();
        return reader.Skip();
    }
}
