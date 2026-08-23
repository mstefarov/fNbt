using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

[MemoryDiagnoser]
public class WriteBenchmarks {
    private NbtCompound complexCompound = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();
        complexCompound = BenchmarkTestFiles.MakeComplexCompound();
    }

    // Full Save vs. NbtWriter for Building a File.
    // Stream.Null is safe for small tags but not large arrays: it discards writes without reading them.
    [Benchmark(Description = "NbtFile to Stream")]
    public void BuildAndSave_FullSave() {
        var file = new NbtFile(complexCompound);
        file.SaveToStream(Stream.Null, NbtCompression.None);
    }

    [Benchmark(Description = "NbtWriter to Stream")]
    public void BuildAndSave_NbtWriter() {
        var writer = new NbtWriter(Stream.Null, "root");
        foreach (var tag in complexCompound) {
            writer.WriteTag(tag);
        }
        writer.EndCompound();
        writer.Finish();
    }
}
