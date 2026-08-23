using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

public class EditBenchmarks {

    private NbtTag bigFileRoot = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();
        bigFileRoot = BenchmarkTestFiles.GetBigFile().RootTag;
    }

    [Benchmark(Description = "Create Complex Compound")]
    public NbtCompound InMemoryCreation() {
        return BenchmarkTestFiles.MakeComplexCompound();
    }

    [Benchmark(Description = "Lookup Nested Tag")]
    public NbtTag? LookupNestedTag() {
        return bigFileRoot["nested compound test"]!["ham"]!["name"];
    }
}
