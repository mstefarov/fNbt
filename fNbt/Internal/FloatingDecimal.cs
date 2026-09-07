using System;
using System.Globalization;
using System.Numerics;

namespace fNbt {
    // Correctly rounded conversions between decimal digit strings and binary floating point, in
    // exact integer arithmetic. Formatting follows Java's Double.toString, the digits Minecraft
    // prints: the fewest digits that read back as the value, but never fewer than two, the closest
    // such decimal, a tie going to the even digit. .NET Core's own shortest form agrees for every
    // normal value and differs only for the smallest subnormals, where one digit would do. .NET
    // Framework is not correctly rounded in either direction (measured 2026-09-06 on 300,000
    // random doubles: Parse lands one unit off for 0.4% of shortest texts, ToString past 15 digits
    // is often wrong, and zero loses its sign both ways). The netstandard2.0 build routes SNBT
    // numbers through this class both ways; net8.0 uses the runtime. This class is compiled
    // everywhere so the tests can check it against a correct runtime and against Java's output.
    internal static class FloatingDecimal {
        const int DoubleMantissaBits = 53;
        const int DoubleMinExponent = -1074;
        const int DoubleMaxDigits = 17;
        const int SingleMantissaBits = 24;
        const int SingleMinExponent = -149;
        const int SingleMaxDigits = 9;

        // Java prints at least one fractional digit, so a one-digit form never wins over the
        // closest two-digit one
        const int MinDigits = 2;

        // Digits kept before a sticky one stands in for the rest. No halfway point between two
        // doubles has more than 767 significant digits, so nothing further down can change the
        // rounding, and the arithmetic stays bounded whatever the text's length.
        const int MaxDigits = 800;

        // Exponents stop counting here; the callers' cutoffs decide long before
        const long ExponentCap = 1000000000;

        #region Formatting

        // Java's digits for the value, laid out as "d.dddE<exp>" ("0" for zero, sign omitted),
        // the form AppendJavaLayout takes
        public static string ShortestDouble(double value) {
            long bits = BitConverter.DoubleToInt64Bits(value) & long.MaxValue;
            int exponentField = (int)(bits >> 52);
            ulong mantissa = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (exponentField == 0) {
                return Shortest(mantissa, DoubleMinExponent, DoubleMantissaBits, DoubleMinExponent, DoubleMaxDigits, MinDigits);
            }
            int start = Math.Max(MinDigits, SignificantDigits(value.ToString("G15", CultureInfo.InvariantCulture)));
            return Shortest(mantissa | (1UL << 52), exponentField - 1075, DoubleMantissaBits, DoubleMinExponent, DoubleMaxDigits, start);
        }


        public static string ShortestSingle(float value) {
            int bits = SingleBits(value) & int.MaxValue;
            int exponentField = bits >> 23;
            ulong mantissa = (ulong)(bits & 0x7FFFFF);
            if (exponentField == 0) {
                return Shortest(mantissa, SingleMinExponent, SingleMantissaBits, SingleMinExponent, SingleMaxDigits, MinDigits);
            }
            int start = Math.Max(MinDigits, SignificantDigits(value.ToString("G6", CultureInfo.InvariantCulture)));
            return Shortest(mantissa | (1UL << 23), exponentField - 150, SingleMantissaBits, SingleMinExponent, SingleMaxDigits, start);
        }


        // Where the search for the shortest form starts. For a normal value, the runtime's
        // 15-digit (6-digit) form is the shortest form padded with zeros whenever that form is no
        // longer than 15 (6) digits: half a decimal step at those precisions is wider than half a
        // binary one, so the shortest form is the nearest grid point. Its digit count is then the
        // answer, and otherwise a lower bound. A subnormal's wide rounding interval breaks the
        // argument, so it starts at the minimum. Seven digits would not do for floats: 2^-24 is
        // wider than half a step of the finest 7-digit grid.
        static int SignificantDigits(string text) {
            int at = text.IndexOf('E');
            string mantissa = (at < 0 ? text : text.Substring(0, at)).Replace(".", "").Replace("-", "").Trim('0');
            return Math.Max(1, mantissa.Length);
        }


        static string Shortest(ulong mantissa, int exponent, int mantissaBits, int minExponent, int maxDigits, int startDigits) {
            if (mantissa == 0) return "0";
            int magnitude = Magnitude(mantissa, exponent);
            for (int digits = startDigits; digits <= maxDigits; digits++) {
                // The two grid points around the value at this precision. The closer one inside
                // the rounding interval wins, a tie going to the even digit; at the last precision
                // one of them always fits.
                int scale = magnitude + 1 - digits;
                BigInteger floor = FloorDiv(mantissa, exponent, scale, out bool exact);
                BigInteger ceiling = floor + 1;
                bool floorFits = Compare(floor, scale, mantissa, exponent, mantissaBits, minExponent) == 0;
                bool ceilingFits = !exact && Compare(ceiling, scale, mantissa, exponent, mantissaBits, minExponent) == 0;
                if (!floorFits && !ceilingFits && digits < maxDigits) continue;
                BigInteger chosen;
                if (floorFits != ceilingFits) {
                    chosen = floorFits ? floor : ceiling;
                } else {
                    int side = CompareScaled(floor + ceiling, scale, 2 * (BigInteger)mantissa, exponent);
                    chosen = side < 0 ? ceiling : side > 0 ? floor : floor.IsEven ? floor : ceiling;
                }
                return Layout(chosen, scale);
            }
            throw new InvalidOperationException("unreachable");
        }


        // "d.dddE<exp>" for digits * 10^scale, trailing zeros dropped; a ceiling that carried into
        // one more digit lays out as that digit alone
        static string Layout(BigInteger digits, int scale) {
            string text = digits.ToString(CultureInfo.InvariantCulture);
            int exponent = scale + text.Length - 1;
            string tail = text.Substring(1).TrimEnd('0');
            return text.Substring(0, 1) + (tail.Length > 0 ? "." + tail : "") +
                   "E" + exponent.ToString(CultureInfo.InvariantCulture);
        }


        // floor(log10) of mantissa * 2^exponent, exact: an estimate from the double, corrected
        static int Magnitude(ulong mantissa, int exponent) {
            int magnitude = (int)Math.Floor(Math.Log10(mantissa * Math.Pow(2, exponent)));
            while (CompareScaled(BigInteger.One, magnitude + 1, mantissa, exponent) <= 0) magnitude++;
            while (CompareScaled(BigInteger.One, magnitude, mantissa, exponent) > 0) magnitude--;
            return magnitude;
        }


        // floor(mantissa * 2^exponent / 10^scale), and whether the division was exact
        static BigInteger FloorDiv(ulong mantissa, int exponent, int scale, out bool exact) {
            BigInteger numerator = mantissa;
            BigInteger denominator = BigInteger.One;
            if (exponent >= 0) numerator <<= exponent; else denominator <<= -exponent;
            if (scale >= 0) denominator *= Pow10(scale); else numerator *= Pow10(-scale);
            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            exact = remainder.IsZero;
            return quotient;
        }

        #endregion


        #region Parsing

        // The correctly rounded double for a decimal text ("-12.5e-3", digits and an optional
        // point, exponent and sign), starting from the BCL's reading, which is within a few units
        public static double CorrectDouble(string text, double parsed) {
            return BitConverter.Int64BitsToDouble(CorrectBits(text, BitConverter.DoubleToInt64Bits(parsed),
                DoubleMantissaBits, DoubleMinExponent, -400, 310));
        }


        public static float CorrectSingle(string text, float parsed) {
            return SingleFromBits(unchecked((int)CorrectBits(text, SingleBits(parsed),
                SingleMantissaBits, SingleMinExponent, -60, 40)));
        }


        // Positive IEEE encodings are ordered integers, for both single and double precision.
        // A reading the runtime refused as overflow comes in as infinity and is decided from the
        // largest finite value, against the halfway point above it.
        static long CorrectBits(string text, long bits, int mantissaBits, int minExponent, int underflowCutoff, int overflowCutoff) {
            Scan(text, out bool negative, out BigInteger digits, out long scale, out int digitCount);
            long magnitudeMask = mantissaBits == DoubleMantissaBits ? long.MaxValue : int.MaxValue;
            long sign = negative ? ~magnitudeMask : 0;
            ulong hiddenBit = 1UL << (mantissaBits - 1);
            long infinity = magnitudeMask - (long)(hiddenBit - 1);
            // Far outside the format the magnitude alone decides, which also keeps the powers of
            // ten below within reach
            if (digits.IsZero || digitCount + scale < underflowCutoff) return sign;
            if (digitCount + scale > overflowCutoff) return infinity | sign;
            bits &= magnitudeMask;
            if (bits >= infinity) bits = infinity - 1;
            for (int step = 0; step < 16 && bits < infinity; step++) {
                int exponentField = (int)(bits >> (mantissaBits - 1));
                ulong mantissa = (ulong)bits & (hiddenBit - 1);
                int exponent = minExponent;
                if (exponentField != 0) {
                    mantissa |= hiddenBit;
                    exponent = exponentField + minExponent - 1;
                }
                // Within reach of the cutoffs above, the scale fits an int
                int side = Compare(digits, (int)scale, mantissa, exponent, mantissaBits, minExponent);
                if (side == 0) break;
                bits += side;
            }
            return bits | sign;
        }


        // Splits validated decimal text into its digits (as one integer) and their power of ten.
        // Trailing zeros are dropped, and past MaxDigits a sticky one replaces the rest.
        static void Scan(string text, out bool negative, out BigInteger digits, out long scale, out int digitCount) {
            negative = false;
            int pos = 0;
            if (pos < text.Length && (text[pos] == '-' || text[pos] == '+')) {
                negative = text[pos] == '-';
                pos++;
            }
            int start = pos;
            int exponentAt = text.IndexOf('e', start);
            if (exponentAt < 0) exponentAt = text.IndexOf('E', start);
            pos = exponentAt < 0 ? text.Length : exponentAt;
            int pointAt = text.IndexOf('.', start, pos - start);
            int fraction = pointAt < 0 ? 0 : pos - pointAt - 1;
            string mantissaText = text.Substring(start, pos - start).Replace(".", "").TrimStart('0');
            int allDigits = mantissaText.Length;
            mantissaText = mantissaText.TrimEnd('0');
            if (mantissaText.Length > MaxDigits) mantissaText = mantissaText.Substring(0, MaxDigits) + "1";
            digitCount = mantissaText.Length;
            digits = digitCount == 0 ? BigInteger.Zero : BigInteger.Parse(mantissaText, CultureInfo.InvariantCulture);
            long exponent = 0;
            if (pos < text.Length) {
                int at = pos + 1;
                bool exponentNegative = false;
                if (at < text.Length && (text[at] == '-' || text[at] == '+')) {
                    exponentNegative = text[at] == '-';
                    at++;
                }
                for (; at < text.Length && exponent < ExponentCap; at++) {
                    exponent = exponent * 10 + (text[at] - '0');
                }
                if (exponentNegative) exponent = -exponent;
            }
            scale = exponent - fraction + (allDigits - digitCount);
        }

        #endregion


        #region Exact comparison

        // Where digits * 10^scale falls against the value mantissa * 2^exponent under round to
        // nearest even: 0 when it rounds to that value, -1 when it rounds below, 1 when above
        static int Compare(BigInteger digits, int scale, ulong mantissa, int exponent, int mantissaBits, int minExponent) {
            // The halfway point to the next value up; a tie goes to the even mantissa
            int upper = CompareScaled(digits, scale, 2 * (BigInteger)mantissa + 1, exponent - 1);
            if (upper > 0 || (upper == 0 && (mantissa & 1) == 1)) return 1;
            if (mantissa == 0) return 0;
            // The value below is closer when this is the first of its binade
            bool firstOfBinade = mantissa == 1UL << (mantissaBits - 1) && exponent > minExponent;
            BigInteger lowerMantissa = firstOfBinade ? 4 * (BigInteger)mantissa - 1 : 2 * (BigInteger)mantissa - 1;
            int lowerExponent = firstOfBinade ? exponent - 2 : exponent - 1;
            int lower = CompareScaled(digits, scale, lowerMantissa, lowerExponent);
            if (lower < 0 || (lower == 0 && (mantissa & 1) == 1)) return -1;
            return 0;
        }


        // digits * 10^scale against mantissa * 2^exponent
        static int CompareScaled(BigInteger digits, int scale, BigInteger mantissa, int exponent) {
            if (scale >= 0) digits *= Pow10(scale); else mantissa *= Pow10(-scale);
            if (exponent >= 0) mantissa <<= exponent; else digits <<= -exponent;
            return digits.CompareTo(mantissa);
        }


        static readonly BigInteger[] powersOfTen = new BigInteger[1300];

        static BigInteger Pow10(int power) {
            if (power >= powersOfTen.Length) return BigInteger.Pow(10, power);
            BigInteger cached = powersOfTen[power];
            if (cached.IsZero) {
                cached = BigInteger.Pow(10, power);
                powersOfTen[power] = cached;
            }
            return cached;
        }

        #endregion


        static int SingleBits(float value) {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }


        static float SingleFromBits(int bits) {
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
    }
}
