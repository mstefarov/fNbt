#if !FNBT_BASELINE_2_0
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// SNBT text over the two realistic inputs: the ClassicWorld metadata subtree (~9,300 small tags,
// mostly names and small numbers) and the Bedrock block palette (16,913 compound roots, quoted
// strings and ints). ToSnbt and ParseSnbt postdate 2.0.0, so this needs a 2.1 baseline.
[BenchmarkCategory(Program.Baseline20Incompatible)]
public class SnbtBenchmarks {
    static readonly SnbtOptions Indented = new SnbtOptions { WriteLayout = SnbtLayout.Indented };

    NbtCompound metadataRoot = null!;
    string metadataCompact = null!;
    string metadataIndented = null!;
    NbtTag[] paletteRoots = null!;
    string[] paletteTexts = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        metadataRoot = new NbtFile(ClassicWorldFiles.LoadTemplate()).RootTag;
        metadataCompact = metadataRoot.ToSnbt();
        metadataIndented = metadataRoot.ToSnbt(Indented);
        byte[] paletteBytes = File.ReadAllBytes(Path.Combine(BenchmarkTestFiles.DirName, "canonical_block_states.nbt"));
        using var ms = new MemoryStream(paletteBytes);
        paletteRoots = NbtCodec.For(NbtFlavor.BedrockNetwork).ReadConcatenatedTags(ms).ToArray();
        paletteTexts = paletteRoots.Select(root => root.ToSnbt()).ToArray();
    }

    // Metadata Subtree

    [AverageBenchmark]
    [Benchmark(Description = "Print metadata compact (~9.3k tags)")]
    public string PrintMetadataCompact() {
        return metadataRoot.ToSnbt();
    }

    [AverageBenchmark]
    [Benchmark(Description = "Print metadata indented (~9.3k tags)")]
    public string PrintMetadataIndented() {
        return metadataRoot.ToSnbt(Indented);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse metadata compact (~9.3k tags)")]
    public NbtTag ParseMetadataCompact() {
        return NbtTag.ParseSnbt(metadataCompact);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse metadata indented (~9.3k tags)")]
    public NbtTag ParseMetadataIndented() {
        return NbtTag.ParseSnbt(metadataIndented);
    }

    // Block Palette

    [AverageBenchmark]
    [Benchmark(Description = "Print block palette (16,913 roots)")]
    public int PrintPalette() {
        int chars = 0;
        foreach (NbtTag root in paletteRoots) {
            chars += root.ToSnbt().Length;
        }
        return chars;
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse block palette (16,913 roots)")]
    public int ParsePalette() {
        int tags = 0;
        foreach (string text in paletteTexts) {
            NbtTag.ParseSnbt(text);
            tags++;
        }
        return tags;
    }
}
#endif
