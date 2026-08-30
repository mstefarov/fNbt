using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Uncompressed copy throughput on the benchmark host is sensitive to where the buffers land.
// Use independent process launches rather than randomizing setup every iteration.
public class ClassicWorldWriteBenchmarks {
    [Params(CwSize.Small, CwSize.Medium)]
    public CwSize Size;

    NbtCompound root = null!;
    NbtFile mapFile = null!;

    // Not Stream.Null: it discards writes without reading the source buffer, so the block arrays
    // would cost nothing and a 32 MiB map would look as cheap as a 1 MiB one.
    MemoryStream sink = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        // map stays local: holding it would keep a second copy of the payload on the LOH and add noise.
        CwMap map = ClassicWorldFiles.Load(Size);
        root = map.Root;
        mapFile = new NbtFile(root);
        sink = new MemoryStream(map.RawBytes.Length + 1024);
    }


    // High-Level Map Saving

    // No Baseline=true here, for the same reason as ClassicWorldReadBenchmarks
    [UnstableBenchmark]
    [Benchmark(Description = "Save map to stream (uncompressed)")]
    public void SaveUncompressed() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.None);
    }


    [VeryStableBenchmark]
    [Benchmark(Description = "Save map to stream (GZip)")]
    public void SaveGZip() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.GZip);
    }


    [VeryStableBenchmark]
    [Benchmark(Description = "Save map to stream (ZLib)")]
    public void SaveZLib() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.ZLib);
    }


    [AverageBenchmark]
    [Benchmark(Description = "Save map to buffer (uncompressed)")]
    public byte[] SaveToBuffer() {
        return mapFile.SaveToBuffer(NbtCompression.None);
    }


    // Compressed output size is unknowable up front, so these exercise the grow-and-copy path.
    [AverageBenchmark]
    [Benchmark(Description = "Save map to buffer (GZip)")]
    public byte[] SaveToBufferGZip() {
        return mapFile.SaveToBuffer(NbtCompression.GZip);
    }


    [AverageBenchmark]
    [Benchmark(Description = "Save map to buffer (ZLib)")]
    public byte[] SaveToBufferZLib() {
        return mapFile.SaveToBuffer(NbtCompression.ZLib);
    }


    // Full Save vs. NbtWriter for Streaming a Map Out

    [UnstableBenchmark]
    [Benchmark(Description = "Write map via NbtWriter (uncompressed)")]
    public void WriteWithNbtWriter() {
        sink.Position = 0;
        var writer = new NbtWriter(sink, "ClassicWorld");
        writer.WriteByte("FormatVersion", 1);
        writer.WriteShort("X", root["X"]!.ShortValue);
        writer.WriteShort("Y", root["Y"]!.ShortValue);
        writer.WriteShort("Z", root["Z"]!.ShortValue);
        writer.WriteByteArray("BlockArray", ((NbtByteArray)root["BlockArray"]!).Value);
        writer.WriteByteArray("BlockArray2", ((NbtByteArray)root["BlockArray2"]!).Value);
        writer.WriteTag(root["Metadata"]!);
        writer.EndCompound();
        writer.Finish();
    }
}
