using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

public class EditBenchmarks {

    NbtTag bigFileRoot = null!;
    string[] addNames = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();
        bigFileRoot = BenchmarkTestFiles.GetBigFile().RootTag;
        addNames = new string[1000];
        for (int i = 0; i < addNames.Length; i++) {
            addNames[i] = "tag_" + i;
        }
    }


    // The insert path alone: many one-at-a-time Adds into a single large compound
    [AverageBenchmark]
    [Benchmark(Description = "Add 1000 tags one by one")]
    public NbtCompound AddManyTags() {
        var root = new NbtCompound("root");
        for (int i = 0; i < addNames.Length; i++) {
            root.Add(new NbtInt(addNames[i], i));
        }
        return root;
    }

    [AverageBenchmark]
    [Benchmark(Description = "Create Complex Compound")]
    public NbtCompound InMemoryCreation() {
        return BenchmarkTestFiles.MakeComplexCompound();
    }

    [AverageBenchmark]
    [Benchmark(Description = "Lookup Nested Tag")]
    public NbtTag? LookupNestedTag() {
        return bigFileRoot["nested compound test"]!["ham"]!["name"];
    }
}
