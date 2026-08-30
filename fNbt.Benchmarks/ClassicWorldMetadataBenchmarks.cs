using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// A map's Metadata subtree on its own: ~9,300 small tags and no block arrays, which the whole-map
// benchmarks cannot measure. What is left is tag names, compound inserts and small allocations.
public class ClassicWorldMetadataBenchmarks {
    NbtFile metadataFile = null!;
    NbtCompound metadataRoot = null!;
    NbtCompound metadataClone = null!;
    NbtCompound blockDefinitions = null!;
    byte[] rawBytes = null!;
    string[] definitionNames = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        metadataFile = new NbtFile(ClassicWorldFiles.LoadTemplate());
        metadataRoot = metadataFile.RootTag;
        metadataClone = (NbtCompound)metadataRoot.Clone();
        blockDefinitions = (NbtCompound)metadataRoot["Metadata"]!["CPE"]!["BlockDefinitions"]!;
        rawBytes = metadataFile.SaveToBuffer(NbtCompression.None);
        definitionNames = blockDefinitions.Names.ToArray();
    }


    // Parsing and Writing Many Small Tags

    [AverageBenchmark]
    [Benchmark(Description = "Parse metadata (~9.3k tags)")]
    public NbtFile ParseMetadata() {
        var file = new NbtFile();
        file.LoadFromBuffer(rawBytes, 0, rawBytes.Length, NbtCompression.None, null);
        return file;
    }


    [VeryStableBenchmark]
    [Benchmark(Description = "Write metadata (~9.3k tags)")]
    public void WriteMetadata() {
        metadataFile.SaveToStream(Stream.Null, NbtCompression.None);
    }


    // In-Memory Traversal

    [VeryStableBenchmark]
    [Benchmark(Description = "Look up every block definition")]
    public int LookupAllDefinitions() {
        int found = 0;
        foreach (string name in definitionNames) {
            if (blockDefinitions[name] != null) {
                found++;
            }
        }
        return found;
    }


    [AverageBenchmark]
    [Benchmark(Description = "Deep clone metadata")]
    public object CloneMetadata() {
        return metadataRoot.Clone();
    }


    // Worst case for the comparer, since identical trees cannot exit early.
    // NbtComparer postdates v1.0.0, so this cannot build against a released baseline.
#if !FNBT_BASELINE
    [AverageBenchmark]
    [BenchmarkCategory(Program.BaselineIncompatible)]
    [Benchmark(Description = "Compare two metadata trees")]
    public bool CompareMetadata() {
        return NbtComparer.Instance.Equals(metadataRoot, metadataClone);
    }
#endif
}
