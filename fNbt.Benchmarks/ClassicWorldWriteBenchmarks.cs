using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// A Medium map moves about as much as L3 holds, so copy throughput swings between 1.5 and 16 GB/s on
// where the buffers land. MemoryRandomization keeps one process's luck from looking like a result.
[MemoryDiagnoser]
[MemoryRandomization]
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

    [Benchmark(Description = "Save map to stream (uncompressed)", Baseline = true)]
    public void SaveUncompressed() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.None);
    }


    [Benchmark(Description = "Save map to stream (GZip)")]
    public void SaveGZip() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.GZip);
    }


    [Benchmark(Description = "Save map to stream (ZLib)")]
    public void SaveZLib() {
        sink.Position = 0;
        mapFile.SaveToStream(sink, NbtCompression.ZLib);
    }


    [Benchmark(Description = "Save map to buffer (uncompressed)")]
    public byte[] SaveToBuffer() {
        return mapFile.SaveToBuffer(NbtCompression.None);
    }


    // Full Save vs. NbtWriter for Streaming a Map Out

    [Benchmark(Description = "Write map via NbtWriter (uncompressed)")]
    public void WriteWithNbtWriter() {
        sink.Position = 0;
        var writer = new NbtWriter(sink, "ClassicWorld");
        writer.WriteByte("FormatVersion", 1);
        writer.WriteShort("X", root["X"]!.ShortValue);
        writer.WriteShort("Y", root["Y"]!.ShortValue);
        writer.WriteShort("Z", root["Z"]!.ShortValue);
        writer.WriteByteArray("BlockArray", ((NbtByteArray)root["BlockArray"]!).Value);
        writer.WriteTag(root["Metadata"]!);
        writer.EndCompound();
        writer.Finish();
    }
}
