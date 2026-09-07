using System;
using System.Globalization;
using System.Numerics;

namespace fNbt {
    // Correctly rounded conversions between decimal digit strings and binary floating point, in
    // exact integer arithmetic. .NET Core 3.0 and later get both directions right in the BCL.
    // .NET Framework does not: measured on 300,000 random doubles (2026-09-06), its Parse lands one
    // unit off for 0.4% of shortest texts, its ToString past 15 digits is not correctly rounded, and
    // it loses the sign of zero both ways. The netstandard2.0 build routes SNBT numbers through this
    // class; it is compiled everywhere so the tests can check it against a correct runtime.
    internal static class FloatingDecimal {
        const int DoubleMantissaBits = 53;
        const int DoubleMinExponent = -1074;
        const int DoubleMaxDigits = 17;
        const int SingleMantissaBits = 24;
        const int SingleMinExponent = -149;
        const int SingleMaxDigits = 9;

        // Past this many significant digits the BCL's reading is kept; nothing real is that long
        const int MaxDigits = 800;

        #region Formatting

        // The shortest correctly rounded digits that read back as the value, laid out as
        // "d.dddE<exp>" ("0" for zero, sign omitted), the form AppendJavaLayout takes
        public static string ShortestDouble(double value) {
            long bits = BitConverter.DoubleToInt64Bits(value) & long.MaxValue;
            int exponentField = (int)(bits >> 52);
            ulong mantissa = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (exponentField == 0) {
                return Shortest(mantissa, DoubleMinExponent, DoubleMantissaBits, DoubleMinExponent, DoubleMaxDigits, 1);
            }
            int start = SignificantDigits(value.ToString("G15", CultureInfo.InvariantCulture));
            return Shortest(mantissa | (1UL << 52), exponentField - 1075, DoubleMantissaBits, DoubleMinExponent, DoubleMaxDigits, start);
        }


        public static string ShortestSingle(float value) {
            int bits = SingleBits(value) & int.MaxValue;
            int exponentField = bits >> 23;
            ulong mantissa = (ulong)(bits & 0x7FFFFF);
            if (exponentField == 0) {
                return Shortest(mantissa, SingleMinExponent, SingleMantissaBits, SingleMinExponent, SingleMaxDigits, 1);
            }
            int start = SignificantDigits(value.ToString("G6", CultureInfo.InvariantCulture));
            return Shortest(mantissa | (1UL << 23), exponentField - 150, SingleMantissaBits, SingleMinExponent, SingleMaxDigits, start);
        }


        // Where the search for the shortest form starts. For a normal value, the runtime's
        // 15-digit (6-digit) form is the shortest form padded with zeros whenever that form is no
        // longer than 15 (6) digits: half a decimal step at those precisions is wider than half a
        // binary one, so the shortest form is the nearest grid point. Its digit count is then the
        // answer, and otherwise a lower bound. A subnormal's wide rounding interval breaks the
        // argument, so it starts at one. Seven digits would not do for floats: 2^-24 is wider than
        // half a step of the finest 7-digit grid.
        static int SignificantDigits(string text) {
            int at = text.IndexOf('E');
            string mantissa = (at < 0 ? text : text.Substring(0, at)).Replace(".", "").Replace("-", "").Trim('0');
            return Math.Max(1, mantissa.Length);
        }


        static string Shortest(ulong mantissa, int exponent, int mantissaBits, int minExponent, int maxDigits, int startDigits) {
            if (mantissa == 0) return "0";
            double approximate = mantissa * Math.Pow(2, exponent);
            int magnitude = (int)Math.Floor(Math.Log10(approximate));
            for (int digits = startDigits; digits <= maxDigits; digits++) {
                BigInteger rounded = RoundToDigits(mantissa, exponent, digits, ref magnitude);
                int scale = magnitude + 1 - digits;
                if (digits == maxDigits || Compare(rounded, scale, mantissa, exponent, mantissaBits, minExponent) == 0) {
                    string text = rounded.ToString(CultureInfo.InvariantCulture);
                    string tail = text.Substring(1).TrimEnd('0');
                    return text.Substring(0, 1) + (tail.Length > 0 ? "." + tail : "") +
                           "E" + magnitude.ToString(CultureInfo.InvariantCulture);
                }
            }
            throw new InvalidOperationException("unreachable");
        }


        // The value rounded half-even to the given number of significant digits, as an integer with
        // exactly that many digits; magnitude comes in as an estimate of floor(log10) and leaves exact
        static BigInteger RoundToDigits(ulong mantissa, int exponent, int digits, ref int magnitude) {
            while (true) {
                int scale = magnitude + 1 - digits;
                BigInteger numerator = mantissa;
                BigInteger denominator = BigInteger.One;
                if (exponent >= 0) numerator <<= exponent; else denominator <<= -exponent;
                if (scale >= 0) denominator *= Pow10(scale); else numerator *= Pow10(-scale);
                BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
                int half = (remainder * 2).CompareTo(denominator);
                if (half > 0 || (half == 0 && !quotient.IsEven)) quotient += 1;
                if (quotient >= Pow10(digits)) {
                    magnitude++;
                } else if (quotient < Pow10(digits - 1)) {
                    magnitude--;
                } else {
                    return quotient;
                }
            }
        }

        #endregion


        #region Parsing

        // The correctly rounded double for a decimal text ("-12.5e-3", digits and an optional
        // point, exponent and sign), starting from the BCL's reading, which is within a few units
        public static double CorrectDouble(string text, double parsed) {
            if (!Scan(text, out bool negative, out BigInteger digits, out int scale, out int digitCount)) return parsed;
            if (digits.IsZero || digitCount + scale < -400) return negative ? -0.0 : 0.0;
            long bits = BitConverter.DoubleToInt64Bits(parsed) & long.MaxValue;
            for (int step = 0; step < 16; step++) {
                int exponentField = (int)(bits >> 52);
                if (exponentField == 0x7FF) break;
                ulong mantissa = (ulong)bits & 0xFFFFFFFFFFFFFUL;
                int exponent = DoubleMinExponent;
                if (exponentField != 0) {
                    mantissa |= 1UL << 52;
                    exponent = exponentField - 1075;
                }
                int side = Compare(digits, scale, mantissa, exponent, DoubleMantissaBits, DoubleMinExponent);
                if (side == 0) break;
                bits += side;
            }
            double magnitude = BitConverter.Int64BitsToDouble(bits);
            return negative ? -magnitude : magnitude;
        }


        public static float CorrectSingle(string text, float parsed) {
            if (!Scan(text, out bool negative, out BigInteger digits, out int scale, out int digitCount)) return parsed;
            if (digits.IsZero || digitCount + scale < -60) return negative ? -0.0f : 0.0f;
            int bits = SingleBits(parsed) & int.MaxValue;
            for (int step = 0; step < 16; step++) {
                int exponentField = bits >> 23;
                if (exponentField == 0xFF) break;
                ulong mantissa = (ulong)(bits & 0x7FFFFF);
                int exponent = SingleMinExponent;
                if (exponentField != 0) {
                    mantissa |= 1UL << 23;
                    exponent = exponentField - 150;
                }
                int side = Compare(digits, scale, mantissa, exponent, SingleMantissaBits, SingleMinExponent);
                if (side == 0) break;
                bits += side;
            }
            float magnitude = SingleFromBits(bits);
            return negative ? -magnitude : magnitude;
        }


        // Splits a decimal text into its digits (as one integer) and the power of ten they carry.
        // False for anything this class should leave to the BCL.
        static bool Scan(string text, out bool negative, out BigInteger digits, out int scale, out int digitCount) {
            negative = false;
            digits = BigInteger.Zero;
            scale = 0;
            digitCount = 0;
            int pos = 0;
            if (pos < text.Length && (text[pos] == '-' || text[pos] == '+')) {
                negative = text[pos] == '-';
                pos++;
            }
            int start = pos;
            int fraction = 0;
            bool afterPoint = false;
            while (pos < text.Length && (text[pos] == '.' || (text[pos] >= '0' && text[pos] <= '9'))) {
                if (text[pos] == '.') {
                    afterPoint = true;
                } else if (afterPoint) {
                    fraction++;
                }
                pos++;
            }
            string mantissaText = text.Substring(start, pos - start).Replace(".", "");
            mantissaText = mantissaText.TrimStart('0');
            if (mantissaText.Length > MaxDigits) return false;
            digitCount = mantissaText.Length;
            digits = mantissaText.Length == 0 ? BigInteger.Zero : BigInteger.Parse(mantissaText, CultureInfo.InvariantCulture);
            int exponent = 0;
            if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E')) {
                if (!int.TryParse(text.Substring(pos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) {
                    return false;
                }
            }
            scale = exponent - fraction;
            return true;
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
