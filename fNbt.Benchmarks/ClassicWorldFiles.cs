namespace fNbt.Benchmarks;

public enum CwSize {
    Small,  // 1 MiB of block data
    Medium  // 32 MiB of block data
}


public sealed class CwMap {
    public string FilePath = null!;
    public byte[] GZipBytes = null!;
    public byte[] RawBytes = null!;
    public NbtCompound Root = null!;
}


// Builds ClassicWorld (.cw) maps: two large ByteArrays of block data plus ~9,300 small tags.
// Only block data is synthesized, and it has to compress about as well as real terrain. Random
// bytes would not compress at all, flat terrain far too well, and either skews the GZip numbers.
public static class ClassicWorldFiles {
    public const string DirEnvVar = "FNBT_CW_DIR";

    // One scattered block per this many cells. The knob for how well block data compresses.
    const int NoiseDensity = 900;


    public static CwMap Load(CwSize size) {
        string path = ResolveFile(size);
        var file = new NbtFile();
        file.LoadFromFile(path, NbtCompression.AutoDetect, null);
        return new CwMap {
            FilePath = path,
            GZipBytes = File.ReadAllBytes(path),
            RawBytes = file.SaveToBuffer(NbtCompression.None),
            Root = file.RootTag
        };
    }


    // A real map minus its block arrays.
    public static NbtCompound LoadTemplate() {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "cw-template.cw");
        var file = new NbtFile();
        file.LoadFromFile(path, NbtCompression.AutoDetect, null);
        return file.RootTag;
    }


    public static NbtCompound MakeMap(CwSize size) {
        (int sizeX, int sizeY, int sizeZ) = Dimensions(size);
        NbtCompound root = LoadTemplate();
        ((NbtShort)root["X"]!).Value = (short)sizeX;
        ((NbtShort)root["Y"]!).Value = (short)sizeY;
        ((NbtShort)root["Z"]!).Value = (short)sizeZ;

        var blocks = new byte[sizeX * sizeY * sizeZ];
        var blocks2 = new byte[blocks.Length];
        FillBlocks(blocks, blocks2, sizeY / 4 * (sizeX * sizeZ), sizeX * sizeZ);

        // Metadata goes last, so the block arrays precede it as in a real file.
        NbtTag metadata = root["Metadata"]!;
        root.Remove(metadata);
        root.Add(new NbtByteArray("BlockArray", blocks));
        root.Add(new NbtByteArray("BlockArray2", blocks2));
        root.Add(metadata);
        return root;
    }


    static (int X, int Y, int Z) Dimensions(CwSize size) {
        return size == CwSize.Small ? (64, 128, 64) : (256, 256, 256);
    }


    // Ground gives the long uniform runs that make a map compressible; the scattered blocks supply
    // the entropy that keeps it from compressing too well. Block data is Y-major, so ground is flat.
    static void FillBlocks(byte[] blocks, byte[] blocks2, int groundEnd, int layerSize) {
        var rng = new Random(1234);
        for (int i = 0; i < groundEnd; i++) {
            blocks[i] = (byte)(i < groundEnd - 2 * layerSize ? 1 : 3);  // stone, capped with dirt
        }
        for (int n = blocks.Length / NoiseDensity; n > 0; n--) {
            int i = rng.Next(groundEnd, blocks.Length);
            blocks[i] = (byte)(4 + rng.Next(120));
            if (rng.Next(3) == 0) {
                blocks2[i] = 1;  // high bits of an extended block ID
            }
        }
    }


    static string ResolveFile(CwSize size) {
        string? realDir = Environment.GetEnvironmentVariable(DirEnvVar);
        if (!string.IsNullOrEmpty(realDir)) {
            string[] files = Directory.GetFiles(realDir!, "*.cw").OrderBy(f => new FileInfo(f).Length).ToArray();
            if (files.Length == 0) {
                throw new FileNotFoundException($"No *.cw files found in {DirEnvVar} directory: {realDir}");
            }
            return size == CwSize.Small ? files[0] : files[files.Length / 2];
        }

        // Cached in temp: BenchmarkDotNet uses a fresh process per benchmark and wipes its artifacts.
        (int sizeX, int sizeY, int sizeZ) = Dimensions(size);
        string path = Path.Combine(Path.GetTempPath(), $"fNbt-bench-{sizeX}x{sizeY}x{sizeZ}-n{NoiseDensity}.cw");
        if (!File.Exists(path)) {
            new NbtFile(MakeMap(size)).SaveToFile(path, NbtCompression.GZip);
        }
        return path;
    }
}
