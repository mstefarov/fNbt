using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

public class ReadBenchmarks {
    byte[] serializedLargeByteArrayNbt = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        var largeByteArray = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeByteArray);

        var nbtFileWithLargeArray = new NbtFile(new NbtCompound("root") {
            new NbtByteArray("payload", largeByteArray)
        });
        serializedLargeByteArrayNbt = nbtFileWithLargeArray.SaveToBuffer(NbtCompression.None);
    }

    // Raw Byte Array Reading Performance
    [AverageBenchmark]
    [Benchmark(Description = "Reader: ByteArray")]
    public byte[] RawByteArray_Read() {
        using (var ms = new MemoryStream(serializedLargeByteArrayNbt)) {
            var reader = new NbtReader(ms);
            reader.ReadToDescendant("payload");
            return (byte[])reader.ReadValue();
        }
    }
}
