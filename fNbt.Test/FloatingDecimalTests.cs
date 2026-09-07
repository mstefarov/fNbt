using System;
using System.Globalization;
using System.IO;

namespace fNbt.Test {
    // The exact decimal conversions behind SNBT numbers on netstandard2.0. On .NET Core the BCL is
    // correctly rounded, so there the class is checked against it on random values; everywhere
    // else it is checked against itself and against the fixture .NET 8 wrote.
    [TestClass]
    public class FloatingDecimalTests {
        static ulong state;

        static ulong Next() {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }


        static long DoubleBits(double value) {
            return BitConverter.DoubleToInt64Bits(value);
        }


        static int SingleBits(float value) {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }


        static float SingleFromBits(int bits) {
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }


        static double Parse(string text) {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }


        [TestMethod]
        public void KnownValuesRoundBothWays() {
            // Bits 4588307191414157804 print with 17 digits; the 16-digit text is the value below
            double value = BitConverter.Int64BitsToDouble(4588307191414157804);
            Assert.AreEqual("5.6526799757396023E-2", FloatingDecimal.ShortestDouble(value));
            Assert.AreEqual(4588307191414157804, DoubleBits(FloatingDecimal.CorrectDouble("0.056526799757396023", value)));
            Assert.AreEqual(4588307191414157803, DoubleBits(FloatingDecimal.CorrectDouble("0.05652679975739602", value)));
            Assert.AreEqual(4588307191414157804, DoubleBits(FloatingDecimal.CorrectDouble("0.056526799757396023",
                BitConverter.Int64BitsToDouble(4588307191414157802))));

            Assert.AreEqual("0", FloatingDecimal.ShortestDouble(0.0));
            Assert.AreEqual("0", FloatingDecimal.ShortestDouble(-0.0));
            Assert.AreEqual(long.MinValue, DoubleBits(FloatingDecimal.CorrectDouble("-0.0", 0.0)));
            Assert.AreEqual(long.MinValue, DoubleBits(FloatingDecimal.CorrectDouble("-0e5", 0.0)));
            Assert.AreEqual(int.MinValue, SingleBits(FloatingDecimal.CorrectSingle("-0.0", 0f)));
            Assert.AreEqual(0L, DoubleBits(FloatingDecimal.CorrectDouble("0.0", -0.0)));

            // The smallest and largest doubles, and the halfway points around them
            Assert.AreEqual("5E-324", FloatingDecimal.ShortestDouble(double.Epsilon));
            Assert.AreEqual("1.7976931348623157E308", FloatingDecimal.ShortestDouble(double.MaxValue));
            Assert.AreEqual("2.2250738585072014E-308", FloatingDecimal.ShortestDouble(2.2250738585072014E-308));
            Assert.AreEqual(1L, DoubleBits(FloatingDecimal.CorrectDouble("4.94065645841247E-324", 0.0)));
            Assert.AreEqual(1L, DoubleBits(FloatingDecimal.CorrectDouble("2.4703282292062328e-324", 0.0)));
            Assert.AreEqual(0L, DoubleBits(FloatingDecimal.CorrectDouble("2.4703282292062327e-324", double.Epsilon)));
            Assert.AreEqual(DoubleBits(double.MaxValue), DoubleBits(FloatingDecimal.CorrectDouble("1.7976931348623158E308", double.MaxValue)));
            Assert.IsTrue(double.IsPositiveInfinity(FloatingDecimal.CorrectDouble("1.7976931348623159E308", double.MaxValue)));

            // Floats
            Assert.AreEqual("1E-45", FloatingDecimal.ShortestSingle(float.Epsilon));
            Assert.AreEqual("3.4028235E38", FloatingDecimal.ShortestSingle(float.MaxValue));
            Assert.AreEqual("1.15527086E5", FloatingDecimal.ShortestSingle(115527.086f));
            Assert.AreEqual(SingleBits(115527.086f), SingleBits(FloatingDecimal.CorrectSingle("115527.086", 115527.09f)));
            Assert.AreEqual("1E-1", FloatingDecimal.ShortestSingle(0.1f));
            Assert.AreEqual("1E-1", FloatingDecimal.ShortestDouble(0.1));
            Assert.AreEqual("3.0000000000000004E-1", FloatingDecimal.ShortestDouble(0.1 + 0.2));

            // Texts left to the BCL's reading: a huge exponent, an absurd number of digits
            Assert.AreEqual(1.0, FloatingDecimal.CorrectDouble("1e99999999999", 1.0));
            Assert.AreEqual(1.0, FloatingDecimal.CorrectDouble(new string('9', 801), 1.0));
            Assert.AreEqual(0L, DoubleBits(FloatingDecimal.CorrectDouble("1e-500", 1.0)));
        }


        [TestMethod]
        public void RandomValuesRoundTripThroughTheirShortestText() {
            state = 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < 20000; i++) {
                long bits = (long)Next();
                double value = BitConverter.Int64BitsToDouble(bits);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                string text = FloatingDecimal.ShortestDouble(value);
                double magnitude = Math.Abs(value);
                // From the value itself, and from a neighbor on each side, the text leads back to it
                long expected = DoubleBits(magnitude);
                Assert.AreEqual(expected, DoubleBits(FloatingDecimal.CorrectDouble(text, magnitude)), text);
                Assert.AreEqual(expected, DoubleBits(FloatingDecimal.CorrectDouble(text, BitConverter.Int64BitsToDouble(expected + 1))), text);
                if (expected > 0) {
                    Assert.AreEqual(expected, DoubleBits(FloatingDecimal.CorrectDouble(text, BitConverter.Int64BitsToDouble(expected - 1))), text);
                }

                int singleBits = (int)(Next() >> 32);
                float single = SingleFromBits(singleBits);
                if (float.IsNaN(single) || float.IsInfinity(single)) continue;
                string singleText = FloatingDecimal.ShortestSingle(single);
                float singleMagnitude = Math.Abs(single);
                int expectedSingle = SingleBits(singleMagnitude);
                Assert.AreEqual(expectedSingle, SingleBits(FloatingDecimal.CorrectSingle(singleText, singleMagnitude)), singleText);
                Assert.AreEqual(expectedSingle, SingleBits(FloatingDecimal.CorrectSingle(singleText, SingleFromBits(expectedSingle + 1))), singleText);
                if (expectedSingle > 0) {
                    Assert.AreEqual(expectedSingle, SingleBits(FloatingDecimal.CorrectSingle(singleText, SingleFromBits(expectedSingle - 1))), singleText);
                }
            }
        }


        [TestMethod]
        public void FixtureFromDotNet8PrintsAndParsesTheSameHere() {
            int checkedLines = 0;
            foreach (string line in File.ReadLines(TestFiles.SnbtNumbers)) {
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = line.Split(' ');
                if (parts[0] == "D") {
                    long bits = long.Parse(parts[1], CultureInfo.InvariantCulture);
                    Assert.AreEqual(parts[2], new NbtDouble(BitConverter.Int64BitsToDouble(bits)).ToSnbt());
                    Assert.AreEqual(bits, DoubleBits(NbtTag.ParseSnbt(parts[2]).DoubleValue), parts[2]);
                } else {
                    int bits = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    Assert.AreEqual(parts[2], new NbtFloat(SingleFromBits(bits)).ToSnbt());
                    Assert.AreEqual(bits, SingleBits(((NbtFloat)NbtTag.ParseSnbt(parts[2])).Value), parts[2]);
                }
                checkedLines++;
            }
            Assert.IsTrue(checkedLines > 2500, "fixture holds " + checkedLines + " lines");
        }


#if NETCOREAPP
        [TestMethod]
        public void MatchesTheCorrectlyRoundedRuntime() {
            state = 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < 20000; i++) {
                long bits = (long)Next() & long.MaxValue;
                double value = BitConverter.Int64BitsToDouble(bits);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                string text = FloatingDecimal.ShortestDouble(value);
                string reference = value.ToString("R", CultureInfo.InvariantCulture);
                // The same value, and no more digits than the runtime's shortest form
                Assert.AreEqual(bits, DoubleBits(Parse(text)), text);
                Assert.AreEqual(Digits(reference), Digits(text), text + " vs " + reference);

                int singleBits = (int)(Next() >> 32) & int.MaxValue;
                float single = SingleFromBits(singleBits);
                if (float.IsNaN(single) || float.IsInfinity(single)) continue;
                string singleText = FloatingDecimal.ShortestSingle(single);
                string singleReference = single.ToString("R", CultureInfo.InvariantCulture);
                Assert.AreEqual(singleBits, SingleBits(float.Parse(singleText, NumberStyles.Float, CultureInfo.InvariantCulture)), singleText);
                Assert.AreEqual(Digits(singleReference), Digits(singleText), singleText + " vs " + singleReference);
            }
        }


        [TestMethod]
        public void CorrectionAgreesWithTheRuntimeOnRandomTexts() {
            state = 0x2545F4914F6CDD1DUL;
            for (int i = 0; i < 20000; i++) {
                // Random digit strings with random exponents, including the subnormal and overflow edges
                int length = 1 + (int)(Next() % 25);
                char[] digits = new char[length];
                for (int d = 0; d < length; d++) digits[d] = (char)('0' + (int)(Next() % 10));
                int exponent = (int)(Next() % 660) - 340;
                string text = new string(digits) + "e" + exponent.ToString(CultureInfo.InvariantCulture);
                double expected = Parse(text);
                if (double.IsInfinity(expected)) continue;
                long expectedBits = DoubleBits(expected);
                long nudged = expectedBits == 0 ? 1 : expectedBits + (Next() % 2 == 0 ? 1 : -1);
                Assert.AreEqual(expectedBits, DoubleBits(FloatingDecimal.CorrectDouble(text, BitConverter.Int64BitsToDouble(nudged))), text);

                int singleExponent = (int)(Next() % 100) - 55;
                string singleText = new string(digits) + "e" + singleExponent.ToString(CultureInfo.InvariantCulture);
                float expectedSingle = float.Parse(singleText, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (float.IsInfinity(expectedSingle)) continue;
                int expectedSingleBits = SingleBits(expectedSingle);
                int nudgedSingle = expectedSingleBits == 0 ? 1 : expectedSingleBits + (Next() % 2 == 0 ? 1 : -1);
                Assert.AreEqual(expectedSingleBits, SingleBits(FloatingDecimal.CorrectSingle(singleText, SingleFromBits(nudgedSingle))), singleText);
            }
        }


        // Significant digits of a decimal text: the runtime's plain layout ("0.001", "100") carries
        // zeros that are not digits of the shortest form
        static int Digits(string text) {
            int at = text.IndexOf('E');
            string mantissa = (at < 0 ? text : text.Substring(0, at)).Replace(".", "").Trim('0');
            return Math.Max(1, mantissa.Length);
        }
#endif
    }
}
