using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Reading ClassicWorld maps. Mostly block data, so these measure bulk throughput and GZip cost
// rather than per-tag overhead.
// See ClassicWorldWriteBenchmarks for why these are memory-randomized.
[MemoryDiagnoser]
[MemoryRandomization]
public class ClassicWorldReadBenchmarks {
    [Params(CwSize.Small, CwSize.Medium)]
    public CwSize Size;

    CwMap map = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        map = ClassicWorldFiles.Load(Size);
    }


    // Whole-Map Loading

    [Benchmark(Description = "Load map from file (GZip)", Baseline = true)]
    public NbtFile LoadFromFile() {
        var file = new NbtFile();
        file.LoadFromFile(map.FilePath, NbtCompression.AutoDetect, null);
        return file;
    }


    [Benchmark(Description = "Load map from buffer (GZip)")]
    public NbtFile LoadFromBufferGZip() {
        var file = new NbtFile();
        file.LoadFromBuffer(map.GZipBytes, 0, map.GZipBytes.Length, NbtCompression.GZip, null);
        return file;
    }


    [Benchmark(Description = "Load map from buffer (uncompressed)")]
    public NbtFile LoadFromBufferUncompressed() {
        var file = new NbtFile();
        file.LoadFromBuffer(map.RawBytes, 0, map.RawBytes.Length, NbtCompression.None, null);
        return file;
    }


    // Loading With a Tag-Skipping Filter

    static bool HeaderOnly(NbtTag tag) {
        return tag.Name != "BlockArray" && tag.Name != "BlockArray2";
    }


    [Benchmark(Description = "Load header only, selector (GZip)")]
    public NbtFile LoadHeaderOnlyGZip() {
        var file = new NbtFile();
        file.LoadFromBuffer(map.GZipBytes, 0, map.GZipBytes.Length, NbtCompression.GZip, HeaderOnly);
        return file;
    }


    // Skipping an array is a seek here, but an inflate-and-discard in the GZip case above.
    [Benchmark(Description = "Load header only, selector (uncompressed)")]
    public NbtFile LoadHeaderOnlyUncompressed() {
        var file = new NbtFile();
        file.LoadFromBuffer(map.RawBytes, 0, map.RawBytes.Length, NbtCompression.None, HeaderOnly);
        return file;
    }


    // X/Y/Z sit near the front, so the reader stops before reaching the block arrays.
    [Benchmark(Description = "Read dimensions only (NbtReader, GZip)")]
    public int ReadDimensions() {
        using var ms = new MemoryStream(map.GZipBytes);
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

    [Benchmark(Description = "Read BlockArray (NbtReader)")]
    public byte[] ReadBlockArray() {
        using var ms = new MemoryStream(map.RawBytes);

        var reader = new NbtReader(ms);
        reader.ReadToFollowing("BlockArray");
        return (byte[])reader.ReadValue();
    }


    // The parser's floor cost.
    [Benchmark(Description = "Skip whole map (NbtReader)")]
    public int SkipWholeMap() {
        using var ms = new MemoryStream(map.RawBytes);

        var reader = new NbtReader(ms);
        reader.ReadToFollowing();
        return reader.Skip();
    }
}
