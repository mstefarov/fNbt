using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fNbt.Benchmarks {
    public static class BenchmarkTestFiles {
        public static readonly string DirName = Path.Combine(AppContext.BaseDirectory, "TestFiles");
        public static readonly string BigTestFile = Path.Combine(DirName, "bigtest.nbt");
        private static NbtFile bigFile;

        // --- Pre-allocated names and values for benchmarks ---
        private const int ComplexCompoundTagCount = 1000;
        private static readonly string[] IntNames = new string[ComplexCompoundTagCount / 4];
        private static readonly string[] StringNames = new string[ComplexCompoundTagCount / 4];
        private static readonly string[] StringValues = new string[ComplexCompoundTagCount / 4];
        private static readonly string[] ListNames = new string[ComplexCompoundTagCount / 4];
        private static readonly string[] ByteArrayNames = new string[ComplexCompoundTagCount / 4];

        public static void Setup() {
            if (!File.Exists(BigTestFile)) {
                throw new FileNotFoundException(
                    "Benchmark data file not found. Please ensure 'bigtest.nbt' is in the TestFiles sub-directory and set to 'Copy to Output Directory'.",
                    BigTestFile);
            }

            // Pre-generate all strings to avoid allocation during benchmarks
            for (int i = 0; i < ComplexCompoundTagCount / 4; i++) {
                IntNames[i] = $"int_{i}";
                StringNames[i] = $"string_{i}";
                StringValues[i] = $"value_{i}";
                ListNames[i] = $"list_{i}";
                ByteArrayNames[i] = $"byteArray_{i}";
            }
        }

        public static NbtFile GetBigFile() {
            return bigFile ?? (bigFile = new NbtFile(BigTestFile));
        }

        public static NbtCompound MakeComplexCompound() {
            var root = new NbtCompound("root");
            for (int i = 0; i < ComplexCompoundTagCount / 4; i++) {
                root.Add(new NbtInt(IntNames[i], i));
                root.Add(new NbtString(StringNames[i], StringValues[i]));
                root.Add(new NbtList(ListNames[i], NbtTagType.Byte) {
                new NbtByte(1), new NbtByte(2), new NbtByte(3)
            });
                root.Add(new NbtByteArray(ByteArrayNames[i], new byte[64]));
            }
            return root;
        }
    }
}
