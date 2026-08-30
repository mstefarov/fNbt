using BenchmarkDotNet.Attributes;

namespace fNbt.Benchmarks;

// Controls for tag-name handling on parse: the same document shape with names that repeat
// across compounds (like real palettes and schemas), names unique across the whole document,
// and repeated non-ASCII names. 2,000 compounds of 5 int fields each.
public class ParseControlBenchmarks {
    const int CompoundCount = 2000;
    const int FieldCount = 5;

    static readonly string[] RepeatedNames = { "Version", "Name", "Value", "States", "Extra" };
    static readonly string[] NonAsciiNames = { "Версия", "Имя", "Значение", "Состояния", "Прочее" };

    byte[] uniqueNameBytes = null!;
    byte[] repeatedNameBytes = null!;
    byte[] nonAsciiNameBytes = null!;

    [GlobalSetup]
    public void GlobalSetup() {
        uniqueNameBytes = MakeDocument((i, f) => "field" + i + "_" + f);
        repeatedNameBytes = MakeDocument((i, f) => RepeatedNames[f]);
        nonAsciiNameBytes = MakeDocument((i, f) => NonAsciiNames[f]);
    }

    static byte[] MakeDocument(Func<int, int, string> namer) {
        var list = new NbtList("Items", NbtTagType.Compound);
        for (int i = 0; i < CompoundCount; i++) {
            var compound = new NbtCompound();
            for (int f = 0; f < FieldCount; f++) {
                compound.Add(new NbtInt(namer(i, f), i * FieldCount + f));
            }
            list.Add(compound);
        }
        var file = new NbtFile(new NbtCompound("Root") { list });
        return file.SaveToBuffer(NbtCompression.None);
    }

    NbtFile Parse(byte[] bytes) {
        var file = new NbtFile();
        file.LoadFromBuffer(bytes, 0, bytes.Length, NbtCompression.None, null);
        return file;
    }

    // Tag Name Shapes

    [AverageBenchmark]
    [Benchmark(Description = "Parse 2k compounds, repeated names")]
    public NbtFile ParseRepeatedNames() {
        return Parse(repeatedNameBytes);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse 2k compounds, unique names")]
    public NbtFile ParseUniqueNames() {
        return Parse(uniqueNameBytes);
    }

    [AverageBenchmark]
    [Benchmark(Description = "Parse 2k compounds, non-ASCII names")]
    public NbtFile ParseNonAsciiNames() {
        return Parse(nonAsciiNameBytes);
    }
}
