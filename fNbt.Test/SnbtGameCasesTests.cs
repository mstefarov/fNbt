using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace fNbt.Test {
    // Every input the research harness ran through Minecraft's own parsers, checked against the
    // game's verdicts (TestFiles/snbt-cases.txt, generated from tools/snbt-harness). The expected
    // tree is the game's own printout parsed back, so the check is structural. The parser is meant
    // to read more than either game version, never less, and the deviations are listed here.
    [TestClass]
    public class SnbtGameCasesTests {
        // Inputs the game accepts that fNbt refuses: nesting past the 512 cap, and \N{name}
        static bool IsKnownRefusal(string escapedInput) {
            return escapedInput.Contains("\\N{") || DepthOf(escapedInput) > 512;
        }


        static int DepthOf(string escapedInput) {
            int at = escapedInput.IndexOf("~D", StringComparison.Ordinal);
            if (at < 0) at = escapedInput.IndexOf("~L", StringComparison.Ordinal);
            if (at < 0) return 0;
            int end = at + 2;
            while (end < escapedInput.Length && char.IsDigit(escapedInput[end])) end++;
            return int.Parse(escapedInput.Substring(at + 2, end - at - 2));
        }


        // Inputs fNbt reads as values where both game versions refuse them or the classic parser
        // made strings of them, each a documented widening
        static readonly HashSet<string> KnownWidenings = new HashSet<string>(StringComparer.Ordinal) {
            // array elements that fit the element range whatever their suffix or signedness
            "[B;1s]", "[B;1i]", "[B;1L]", "[B;128]", "[B;255]", "[I;1L]", "[I;1uL]", "[I;true]",
            // byte literals in the unsigned range, the same bytes as -128b and -1b
            "128b", "255b",
            // operation names in any case, and an unquoted argument that reads as a string
            "Bool(1)", "BOOL(1)", "BOOL(true)", "UUID(\"123e4567-e89b-12d3-a456-426614174000\")",
            "uuid(123e4567-e89b-12d3-a456-426614174000)",
            // array prefixes in either case
            "[b;1]", "[i;1]", "[l;1]",
        };

        static readonly Regex Abbreviated = new Regex(@"\.\.\.\(\d+ chars\)$");

        // Quoted empty keys, which every game printer writes and no game parser reads
        static readonly Regex EmptyKey = new Regex(@"[{,]\s*(""""|'')\s*:");

        // Inputs the game reads as strings that fNbt reads as the non-finite numbers they name
        static bool IsNonFiniteName(string input) {
            string s = input.TrimStart('+', '-');
            if (s.Length < 2 || "fFdD".IndexOf(s[s.Length - 1]) < 0) return false;
            string name = s.Substring(0, s.Length - 1);
            return name.Equals("NaN", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Infinity", StringComparison.OrdinalIgnoreCase);
        }


        [TestMethod]
        public void EveryHarnessCaseReadsAsTheGameDidOrMoreGenerously() {
            string[] lines = File.ReadAllLines(TestFiles.SnbtCases);
            StringBuilder failures = new StringBuilder();
            int checkedCount = 0;
            foreach (string line in lines) {
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = line.Split('\t');
                string escaped = parts[0];
                string input = Decode(escaped);
                string modern = parts[1];
                string classic = parts[2];
                string expectedText = modern.StartsWith("OK ", StringComparison.Ordinal) ? modern.Substring(3)
                                    : classic.StartsWith("OK ", StringComparison.Ordinal) ? classic.Substring(3)
                                    : null;
                checkedCount++;
                try {
                    Check(escaped, input, expectedText);
                } catch (Exception ex) {
                    failures.Append(escaped).Append(" => ").Append(ex.Message).Append('\n');
                }
            }
            Assert.IsTrue(checkedCount > 800, "Fixture holds " + checkedCount + " cases");
            Assert.AreEqual("", failures.ToString());
        }


        static void Check(string escaped, string input, string expectedText) {
            if (KnownWidenings.Contains(escaped)) {
                NbtTag.ParseSnbt(input);
                return;
            }
            if (expectedText == null) {
                // Both versions refused it
                if (EmptyKey.IsMatch(escaped)) {
                    NbtTag.ParseSnbt(input);
                } else {
                    Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt(input));
                }
                return;
            }
            if (IsKnownRefusal(escaped)) {
                Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt(input));
                return;
            }
            NbtTag actual = NbtTag.ParseSnbt(input);
            // The harness abbreviated printouts over 160 characters, so only the parse can be checked
            if (Abbreviated.IsMatch(expectedText)) return;
            if (IsNonFiniteName(input)) {
                double value = actual is NbtFloat f ? f.Value : ((NbtDouble)actual).Value;
                Assert.IsTrue(double.IsNaN(value) || double.IsInfinity(value), "non-finite expected");
                return;
            }
            NbtTag expected = NbtTag.ParseSnbt(expectedText);
            NbtAssert.AreEqual(expected, actual, "game printed " + expectedText);
        }


        // The harness's escapes for characters that cannot sit on one line of a case file
        static string Decode(string s) {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (c != '~' || i + 1 >= s.Length) {
                    sb.Append(c);
                    continue;
                }
                char d = s[i + 1];
                if (d == 'u' && i + 5 < s.Length) {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16));
                    i += 5;
                } else if (d == 'n') {
                    sb.Append('\n');
                    i++;
                } else if (d == 't') {
                    sb.Append('\t');
                    i++;
                } else if (d == 'r') {
                    sb.Append('\r');
                    i++;
                } else if (d == '~') {
                    sb.Append('~');
                    i++;
                } else if (d == 'D' || d == 'L' || d == 'S') {
                    int j = i + 2;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    int n = int.Parse(s.Substring(i + 2, j - i - 2));
                    if (d == 'D') {
                        for (int k = 0; k < n; k++) sb.Append("{a:");
                        sb.Append('1');
                        sb.Append('}', n);
                    } else if (d == 'L') {
                        sb.Append('[', n);
                        sb.Append(']', n);
                    } else {
                        sb.Append('x', n);
                    }
                    i = j - 1;
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
