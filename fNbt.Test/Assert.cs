using System;
using System.Collections.Generic;
using System.IO;

// Extra asserts that were in NUnit but not in MSTest2
namespace fNbt.Test {
    internal class FileAssert {
        const int BufferSize = 8 * 1024; // 8 KiB

        public static void AreEqual(string expectedPath, string actualPath) {
            // Expected file missing? inconclusive
            if (expectedPath is null || !File.Exists(expectedPath)) {
                Assert.Inconclusive($"Expected file not found: '{expectedPath}'.");
            }

            // Actual file missing? test failure
            if (actualPath is null || !File.Exists(actualPath)) {
                Assert.Fail($"Actual file not found: '{actualPath}'.");
            }

            // Open both files for reading
            using var expectedStream = File.OpenRead(expectedPath);
            using var actualStream = File.OpenRead(actualPath);
            AreEqual(expectedStream, actualStream);
        }

        public static void AreEqual(Stream expectedStream, Stream actualStream) {
            if (expectedStream == null)
                Assert.Inconclusive("Expected stream is null.");
            if (actualStream == null)
                Assert.Fail("Actual stream is null.");

            // Wrap in BufferedStream to batch underlying reads
            using var be = expectedStream as BufferedStream ?? new BufferedStream(expectedStream, BufferSize);
            using var ba = actualStream as BufferedStream ?? new BufferedStream(actualStream, BufferSize);
            long position = 0;
            try {
                int bExp;
                while ((bExp = be.ReadByte()) != -1) {
                    int bAct = ba.ReadByte();
                    if (bAct == -1)
                        Assert.Fail($"Actual stream ended at offset {position}, expected more data.");

                    if (bAct != bExp)
                        Assert.Fail(
                            $"Streams differ at byte offset {position}: " +
                            $"expected=0x{bExp:X2}, actual=0x{bAct:X2}"
                        );

                    position++;
                }

                // Make sure actual isn't longer
                if (ba.ReadByte() != -1)
                    Assert.Fail($"Actual stream is longer than expected; extra data starts at offset {position}.");
            } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
                Assert.Inconclusive($"I/O error while comparing streams: {ex.Message}");
            }
        }
    }

    internal class ListAssert {
        public static void AreEqual<T>(IList<T> expected, IList<T> actual, IEqualityComparer<T> comparer) {
            // Inconclusive if expected or comparer is null
            if (expected == null) {
                Assert.Inconclusive("Expected collection is null.");
            }
            if (comparer == null) {
                Assert.Inconclusive("Comparer is null.");
            }

            // Fail if actual is null
            if (actual == null) {
                Assert.Fail("Actual collection is null.");
            }

            // Fail on length mismatch
            if (expected.Count != actual.Count) {
                Assert.Fail(
                    $"Collections differ in length. Expected: {expected.Count}, Actual: {actual.Count}."
                );
            }

            // Compare each element in order
            for (int i = 0; i < expected.Count; i++) {
                if (!comparer.Equals(expected[i], actual[i])) {
                    Assert.Fail(
                        $"Collections differ at index {i}. " +
                        $"Expected: {expected[i] ?? default}, Actual: {actual[i] ?? default}."
                    );
                }
            }

            // Implicit pass if we get here
        }
    }
}
