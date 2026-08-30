#if !FNBT_BASELINE
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// The BedrockNetwork varint encoding over real data: the full Bedrock block-state palette
// from pmmp/BedrockData (CC0-1.0), 2.3 MB of 16,913 small compound roots back to back.
// This is the shape a Bedrock server serializes into StartGame, so it is the realistic
// workload for varint throughput. NbtCodec and NbtFlavor postdate 1.1.1, so this cannot
// build or run against a released baseline.
[BenchmarkCategory(Program.BaselineIncompatible)]
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
    [AverageBenchmark]
    [Benchmark(Description = "Parse block palette (NbtCodec)")]
    public int ParsePalette() {
        using var ms = new MemoryStream(paletteBytes);
        int count = 0;
        foreach (NbtTag root in codec.ReadConcatenatedTags(ms)) {
            count++;
        }
        return count;
    }

    [AverageBenchmark]
    [Benchmark(Description = "Write block palette (NbtCodec)")]
    public long WritePalette() {
        using var ms = new MemoryStream(paletteBytes.Length);
        codec.WriteConcatenatedTags(paletteRoots, ms);
        return ms.Length;
    }

    [AverageBenchmark]
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

    // The real Skip operation, which discards whole subtrees instead of visiting each tag.
    [AverageBenchmark]
    [Benchmark(Description = "Skip palette roots (NbtReader.Skip)")]
    public long SkipPaletteRoots() {
        using var ms = new MemoryStream(paletteBytes);
        long tags = 0;
        while (ms.Position < ms.Length) {
            var reader = new NbtReader(ms, NbtFlavor.BedrockNetwork);
            reader.ReadToFollowing();
            tags += reader.Skip();
        }
        return tags;
    }

    // The measure-then-write double walk, 16,913 small exact buffers.
    [AverageBenchmark]
    [Benchmark(Description = "Write palette roots to exact buffers")]
    public long WritePaletteExactBuffers() {
        long total = 0;
        foreach (NbtTag root in paletteRoots) {
            total += codec.WriteTag(root).Length;
        }
        return total;
    }

#if NET8_0_OR_GREATER
    [AverageBenchmark]
    [Benchmark(Description = "Write block palette (IBufferWriter)")]
    public long WritePaletteBufferWriter() {
        var output = new System.Buffers.ArrayBufferWriter<byte>(paletteBytes.Length);
        codec.WriteConcatenatedTags(paletteRoots, output);
        return output.WrittenCount;
    }
#endif
}
#endif
