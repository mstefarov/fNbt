#if !FNBT_BASELINE
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// The BedrockNetwork varint encoding over real data: the full Bedrock block-state palette
// from pmmp/BedrockData (CC0-1.0), 2.3 MB of 16,913 small compound roots back to back.
// This is the shape a Bedrock server serializes into StartGame, so it is the realistic
// workload for varint throughput. Guarded from baseline builds because NbtCodec and
// NbtFlavor postdate 1.1.1.
[MemoryDiagnoser]
public class BedrockNetworkBenchmarks {
    private byte[] paletteBytes = null!;
    private NbtTag[] paletteRoots = null!;
    private NbtCodec codec = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        codec = NbtCodec.For(NbtFlavor.BedrockNetwork);
        paletteBytes = File.ReadAllBytes(Path.Combine(BenchmarkTestFiles.DirName, "canonical_block_states.nbt"));
        using var ms = new MemoryStream(paletteBytes);
        paletteRoots = codec.ReadConcatenatedTags(ms).ToArray();
    }

    // Block Palette Throughput
    [Benchmark(Description = "Parse block palette (NbtCodec)")]
    public int ParsePalette() {
        using var ms = new MemoryStream(paletteBytes);
        int count = 0;
        foreach (NbtTag root in codec.ReadConcatenatedTags(ms)) {
            count++;
        }
        return count;
    }

    [Benchmark(Description = "Write block palette (NbtCodec)")]
    public long WritePalette() {
        using var ms = new MemoryStream(paletteBytes.Length);
        foreach (NbtTag root in paletteRoots) {
            codec.WriteTag(root, ms);
        }
        return ms.Length;
    }

    [Benchmark(Description = "Skip block palette (NbtReader)")]
    public long SkipPalette() {
        using var ms = new MemoryStream(paletteBytes);
        long tags = 0;
        while (ms.Position < ms.Length) {
            var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
            while (reader.ReadToFollowing()) {
                tags++;
            }
        }
        return tags;
    }
}
#endif
