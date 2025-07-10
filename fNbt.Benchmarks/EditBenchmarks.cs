using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

public class EditBenchmarks {

    private NbtTag bigFileRoot = null!;
    private NbtTag bigFileRootClone = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();
        bigFileRoot = BenchmarkTestFiles.GetBigFile().RootTag;
        bigFileRootClone = (NbtTag)bigFileRoot.Clone();
    }

    [Benchmark(Description = "Create Complex Compound")]
    public NbtCompound InMemoryCreation() {
        return BenchmarkTestFiles.MakeComplexCompound();
    }

    [Benchmark(Description = "Deep Clone Compound")]
    public object CloneBigCompound() {
        return bigFileRoot.Clone();
    }

    [Benchmark(Description = "Lookup Nested Tag")]
    public NbtTag? LookupNestedTag() {
        return bigFileRoot["nested compound test"]!["ham"]!["name"];
    }

    [Benchmark(Description = "Compare Two Compounds")]
    public bool CompareBigCompounds() {
        return NbtComparer.Instance.Equals(bigFileRoot, bigFileRootClone);
    }
}
