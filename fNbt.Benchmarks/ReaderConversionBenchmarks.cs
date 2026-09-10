#if !FNBT_BASELINE
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Keep arrays small for more consistent allocation measurements.
[BenchmarkCategory(Program.BaselineIncompatible)]
public class ReaderConversionBenchmarks {
    const int ValueCount = 128;
    byte[] document = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        NbtList values = new NbtList("values", NbtTagType.Int);
        for (int i = 0; i < ValueCount; i++) {
            values.Add(new NbtInt(i));
        }
        document = NbtCodec.For(NbtFlavor.Java).WriteTag(new NbtCompound("root") { values });
    }

    [AverageBenchmark]
    [Benchmark(Description = "Int list to byte enum (128 values)")]
    public ByteCode[] ReadNarrowingEnumList() {
        using (MemoryStream stream = new MemoryStream(document)) {
            NbtReader reader = new NbtReader(stream, NbtFlavor.Java);
            reader.ReadToFollowing();
            reader.ReadToFollowing();
            return reader.ReadListAsArray<ByteCode>();
        }
    }

    public enum ByteCode : byte { Zero }
}
#endif
