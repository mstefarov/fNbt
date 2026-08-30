#if !FNBT_BASELINE
using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Fixed-width int/long array throughput: chunk data and heightmaps in real Java worlds.
// Java swaps endianness on this host, Bedrock does not. NbtCodec postdates 1.1.1.
[BenchmarkCategory(Program.BaselineIncompatible)]
public class NumericArrayBenchmarks {
    [ParamsSource(nameof(Flavors))]
    public NbtFlavor Flavor = null!;

    [Params(4096, 1048576)]
    public int Length;

    public static IEnumerable<NbtFlavor> Flavors => new[] { NbtFlavor.Java, NbtFlavor.Bedrock };

    NbtCodec codec = null!;
    byte[] intDoc = null!;
    byte[] intListDoc = null!;
    NbtCompound intRoot = null!;
    MemoryStream sink = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        codec = NbtCodec.For(Flavor);
        var rng = new Random(42);
        var ints = new int[Length];
        for (int i = 0; i < Length; i++) {
            ints[i] = rng.Next(int.MinValue, int.MaxValue);
        }
        intRoot = new NbtCompound("r") { new NbtIntArray("a", ints) };
        intDoc = codec.WriteTag(intRoot);
        var intList = new NbtList("a");
        foreach (int value in ints) intList.Add(new NbtInt(value));
        intListDoc = codec.WriteTag(new NbtCompound("r") { intList });
        sink = new MemoryStream(intDoc.Length + 1024);
    }

    // Fixed-Width Array Throughput

    [AverageBenchmark]
    [Benchmark(Description = "Parse int array doc")]
    public NbtTag ReadIntArray() {
        return codec.ReadTag(intDoc, 0, intDoc.Length, out _);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Write int array doc")]
    public long WriteIntArray() {
        sink.Position = 0;
        codec.WriteTag(intRoot, sink);
        return sink.Position;
    }

    [AverageBenchmark]
    [Benchmark(Description = "ReadListAsArray<int>")]
    public int[] ReadListAsIntArray() {
        using var ms = new MemoryStream(intListDoc);
        var reader = new NbtReader(ms, Flavor);
        reader.ReadToFollowing();
        reader.ReadToFollowing();
        return reader.ReadListAsArray<int>();
    }
}


// TAG_Long_Array is a Java-only reality: both Bedrock flavors predate it and their
// validation rejects it, so long-array throughput measures Java alone.
[BenchmarkCategory(Program.BaselineIncompatible)]
public class LongArrayBenchmarks {
    [Params(4096, 1048576)]
    public int Length;

    NbtCodec codec = null!;
    byte[] longDoc = null!;
    NbtCompound longRoot = null!;
    MemoryStream sink = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        codec = NbtCodec.For(NbtFlavor.Java);
        var rng = new Random(42);
        var longs = new long[Length];
        for (int i = 0; i < Length; i++) {
            longs[i] = (long)rng.Next() << 32 | (uint)rng.Next();
        }
        longRoot = new NbtCompound("r") { new NbtLongArray("a", longs) };
        longDoc = codec.WriteTag(longRoot);
        sink = new MemoryStream(longDoc.Length + 1024);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse long array doc")]
    public NbtTag ReadLongArray() {
        return codec.ReadTag(longDoc, 0, longDoc.Length, out _);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Write long array doc")]
    public long WriteLongArray() {
        sink.Position = 0;
        codec.WriteTag(longRoot, sink);
        return sink.Position;
    }
}
#endif
