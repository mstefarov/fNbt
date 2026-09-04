using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Standalone array comparisons, which the metadata comparer row leaves uncovered. Equal inputs
// are the worst case: no early exit. 128 KB per array keeps the data in L2 with room to spare:
// the comparer owns only the type dispatch around the BCL's vectorized SequenceEqual. At 8 MiB
// these rows measured memory placement instead (145 to 370 us for the byte compare within one
// day); at 256 KB the byte row still ran 8% apart between processes when its 64 pages crowded
// one L2 page color; at 64 KB the 1 us op picked up a fixed 3 to 4% alignment offset per job.
public class ComparerBenchmarks {
    const int BlobBytes = 128 * 1024;

    NbtByteArray byteArrayA = null!;
    NbtByteArray byteArrayB = null!;
    NbtIntArray intArrayA = null!;
    NbtIntArray intArrayB = null!;
    NbtLongArray longArrayA = null!;
    NbtLongArray longArrayB = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        var rng = new Random(42);
        var bytes = new byte[BlobBytes];
        rng.NextBytes(bytes);
        byteArrayA = new NbtByteArray("bytes", bytes);
        byteArrayB = new NbtByteArray("bytes", bytes);

        var ints = new int[BlobBytes / sizeof(int)];
        for (int i = 0; i < ints.Length; i++) ints[i] = rng.Next();
        intArrayA = new NbtIntArray("ints", ints);
        intArrayB = new NbtIntArray("ints", ints);

        var longs = new long[BlobBytes / sizeof(long)];
        for (int i = 0; i < longs.Length; i++) longs[i] = rng.Next();
        longArrayA = new NbtLongArray("longs", longs);
        longArrayB = new NbtLongArray("longs", longs);
    }

    // Array Equality

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 128 KB byte arrays")]
    public bool EqualByteArrays() {
        return NbtComparer.Instance.Equals(byteArrayA, byteArrayB);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 32K-element int arrays")]
    public bool EqualIntArrays() {
        return NbtComparer.Instance.Equals(intArrayA, intArrayB);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Compare equal 16K-element long arrays")]
    public bool EqualLongArrays() {
        return NbtComparer.Instance.Equals(longArrayA, longArrayB);
    }
}
