using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Standalone large-array comparisons, which the metadata comparer row leaves uncovered.
// Equal inputs are the worst case: no early exit.
public class ComparerBenchmarks {
    NbtByteArray byteArrayA = null!;
    NbtByteArray byteArrayB = null!;
    NbtIntArray intArrayA = null!;
    NbtIntArray intArrayB = null!;
    NbtLongArray longArrayA = null!;
    NbtLongArray longArrayB = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        var rng = new Random(42);
        var bytes = new byte[8 * 1024 * 1024];
        rng.NextBytes(bytes);
        byteArrayA = new NbtByteArray("bytes", bytes);
        byteArrayB = new NbtByteArray("bytes", bytes);

        var ints = new int[2 * 1024 * 1024];
        for (int i = 0; i < ints.Length; i++) ints[i] = rng.Next();
        intArrayA = new NbtIntArray("ints", ints);
        intArrayB = new NbtIntArray("ints", ints);

        var longs = new long[1024 * 1024];
        for (int i = 0; i < longs.Length; i++) longs[i] = rng.Next();
        longArrayA = new NbtLongArray("longs", longs);
        longArrayB = new NbtLongArray("longs", longs);
    }

    // Large-Array Equality

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 8 MiB byte arrays")]
    public bool EqualByteArrays() {
        return NbtComparer.Instance.Equals(byteArrayA, byteArrayB);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 2M-element int arrays")]
    public bool EqualIntArrays() {
        return NbtComparer.Instance.Equals(intArrayA, intArrayB);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 1M-element long arrays")]
    public bool EqualLongArrays() {
        return NbtComparer.Instance.Equals(longArrayA, longArrayB);
    }
}
