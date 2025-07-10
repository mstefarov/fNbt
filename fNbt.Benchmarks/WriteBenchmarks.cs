using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

[MemoryDiagnoser]
public class WriteBenchmarks {
    private const string TempFileName = "temp_benchmark.nbt";
    private NbtFile bigFile;
    private NbtCompound complexCompound;

    private byte[] largeByteArray;

    [GlobalSetup]
    public void GlobalSetup() {
        BenchmarkTestFiles.Setup();
        bigFile = BenchmarkTestFiles.GetBigFile();
        complexCompound = BenchmarkTestFiles.MakeComplexCompound();

        // Setup for raw array benchmarks
        largeByteArray = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeByteArray);
    }

    [GlobalCleanup]
    public void GlobalCleanup() {
        if (File.Exists(TempFileName)) {
            File.Delete(TempFileName);
        }
    }

    // High-Level File Saving
    [Benchmark(Description = "SaveToFile")]
    [Arguments("None")]
    [Arguments("GZip")]
    public void FileSave(string compressionName) {
        var compression = compressionName == "None" ? NbtCompression.None : NbtCompression.GZip;
        bigFile.SaveToFile(TempFileName, compression);
    }


    // Full Save vs. NbtWriter for Building a File
    [Benchmark(Description = "NbtFile to Stream")]
    public void BuildAndSave_FullSave() {
        var file = new NbtFile(complexCompound);
        file.SaveToStream(Stream.Null, NbtCompression.None);
    }

    [Benchmark(Description = "NbtWriter to Stream")]
    public void BuildAndSave_NbtWriter() {
        var writer = new NbtWriter(Stream.Null, "root");
        foreach (var tag in complexCompound) {
            writer.WriteTag(tag);
        }
        writer.EndCompound();
        writer.Finish();
    }


    // Raw Byte Array Writing Performance
    [Benchmark(Description = "NbtWriter.WriteByteArray")]
    public void RawByteArray_Write() {
        var writer = new NbtWriter(Stream.Null, "root");
        writer.WriteByteArray("payload", largeByteArray);
        writer.EndCompound();
        writer.Finish();
    }
}
