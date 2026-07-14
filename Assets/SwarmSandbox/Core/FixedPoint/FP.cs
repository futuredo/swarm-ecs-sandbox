using System;
using System.Globalization;

namespace SwarmECS.FixedPoint
{
    /// <summary>
    /// Signed Q16.16 fixed-point number.
    ///
    /// All arithmetic overflow is handled by saturating to <see cref="MinValue"/>
    /// or <see cref="MaxValue"/>. Multiplication and division truncate toward zero.
    /// These rules are independent of the caller's checked/unchecked context.
    /// </summary>
    [Serializable]
    public readonly struct FP : IComparable<FP>, IEquatable<FP>, IFormattable
    {
        public const int FractionalBits = 16;
        public const int OneRaw = 1 << FractionalBits;
        public const int FractionMask = OneRaw - 1;

        public static readonly FP MinValue = new FP(int.MinValue);
        public static readonly FP MaxValue = new FP(int.MaxValue);
        public static readonly FP Zero = new FP(0);
        public static readonly FP Epsilon = new FP(1);
        public static readonly FP Half = new FP(OneRaw >> 1);
        public static readonly FP One = new FP(OneRaw);
        public static readonly FP Two = new FP(OneRaw << 1);
        public static readonly FP Pi = new FP(205887);
        public static readonly FP TwoPi = new FP(411775);
        public static readonly FP Deg2Rad = new FP(1144);
        public static readonly FP Rad2Deg = new FP(3754936);

        /// <summary>The encoded Q16.16 value. One is encoded as 65536.</summary>
        public int Raw { get; }

        /// <summary>Compatibility alias for code that names the encoded value explicitly.</summary>
        public int RawValue => Raw;

        private FP(int raw)
        {
            Raw = raw;
        }

        public static FP FromRaw(int raw)
        {
            return new FP(raw);
        }

        public static FP FromInt(int value)
        {
            return FromLong(value);
        }

        public static FP FromLong(long value)
        {
            if (value >= 32768L)
            {
                return MaxValue;
            }

            if (value <= -32768L)
            {
                return MinValue;
            }

            return new FP((int)(value * OneRaw));
        }

        /// <summary>
        /// Creates a fixed-point value from an exact integer ratio, truncated toward zero.
        /// Intermediate calculations cannot overflow, including for long.MinValue.
        /// </summary>
        public static FP FromRatio(long numerator, long denominator)
        {
            if (denominator == 0L)
            {
                throw new DivideByZeroException("Cannot create a fixed-point value with a zero denominator.");
            }

            if (numerator == 0L)
            {
                return Zero;
            }

            bool isNegative = (numerator < 0L) != (denominator < 0L);
            ulong absoluteNumerator = UnsignedMagnitude(numerator);
            ulong absoluteDenominator = UnsignedMagnitude(denominator);
            ulong whole = absoluteNumerator / absoluteDenominator;
            ulong remainder = absoluteNumerator % absoluteDenominator;

            if ((!isNegative && whole >= 32768UL) || (isNegative && whole > 32768UL))
            {
                return isNegative ? MinValue : MaxValue;
            }

            if (isNegative && whole == 32768UL)
            {
                return MinValue;
            }

            ulong fractional = DivideRemainderToFraction(remainder, absoluteDenominator);
            ulong magnitude = (whole << FractionalBits) + fractional;

            if (isNegative)
            {
                return magnitude >= 0x80000000UL
                    ? MinValue
                    : new FP(-(int)magnitude);
            }

            return magnitude > int.MaxValue
                ? MaxValue
                : new FP((int)magnitude);
        }

        /// <summary>
        /// Boundary/test conversion from binary floating point. Values are rounded to the
        /// nearest raw unit with midpoint values rounded away from zero, then saturated.
        /// Runtime simulation code should prefer FromRaw, FromInt, or FromRatio.
        /// </summary>
        public static FP FromDouble(double value)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException("NaN cannot be represented by FP.", nameof(value));
            }

            if (value >= (double)int.MaxValue / OneRaw)
            {
                return MaxValue;
            }

            if (value <= (double)int.MinValue / OneRaw)
            {
                return MinValue;
            }

            double scaled = Math.Round(value * OneRaw, MidpointRounding.AwayFromZero);
            return new FP((int)scaled);
        }

        public int ToIntTruncated()
        {
            return Raw / OneRaw;
        }

        public int FloorToInt()
        {
            return Raw >> FractionalBits;
        }

        public int CeilToInt()
        {
            return (int)-((-(long)Raw) >> FractionalBits);
        }

        public int RoundToInt()
        {
            long raw = Raw;
            return raw >= 0L
                ? (int)((raw + (OneRaw >> 1)) / OneRaw)
                : (int)((raw - (OneRaw >> 1)) / OneRaw);
        }

        public double ToDouble()
        {
            return (double)Raw / OneRaw;
        }

        public static FP Abs(FP value)
        {
            if (value.Raw == int.MinValue)
            {
                return MaxValue;
            }

            return value.Raw < 0 ? new FP(-value.Raw) : value;
        }

        public static FP Min(FP left, FP right)
        {
            return left.Raw <= right.Raw ? left : right;
        }

        public static FP Max(FP left, FP right)
        {
            return left.Raw >= right.Raw ? left : right;
        }

        public static FP Clamp(FP value, FP min, FP max)
        {
            if (min > max)
            {
                throw new ArgumentException("Minimum must not exceed maximum.");
            }

            return value < min ? min : value > max ? max : value;
        }

        public static FP Sqrt(FP value)
        {
            return FPMath.Sqrt(value);
        }

        public int CompareTo(FP other)
        {
            return Raw.CompareTo(other.Raw);
        }

        public bool Equals(FP other)
        {
            return Raw == other.Raw;
        }

        public override bool Equals(object obj)
        {
            return obj is FP other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Raw;
        }

        public override string ToString()
        {
            return ToString(null, CultureInfo.InvariantCulture);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return ToDouble().ToString(format ?? "0.#####", formatProvider ?? CultureInfo.InvariantCulture);
        }

        public static FP operator +(FP left, FP right)
        {
            return Saturate((long)left.Raw + right.Raw);
        }

        public static FP operator -(FP left, FP right)
        {
            return Saturate((long)left.Raw - right.Raw);
        }

        public static FP operator -(FP value)
        {
            return value.Raw == int.MinValue ? MaxValue : new FP(-value.Raw);
        }

        public static FP operator +(FP value)
        {
            return value;
        }

        public static FP operator *(FP left, FP right)
        {
            long product = (long)left.Raw * right.Raw;
            return Saturate(product / OneRaw);
        }

        public static FP operator /(FP left, FP right)
        {
            if (right.Raw == 0)
            {
                throw new DivideByZeroException("Cannot divide an FP value by zero.");
            }

            long scaledDividend = (long)left.Raw * OneRaw;
            return Saturate(scaledDividend / right.Raw);
        }

        public static FP operator %(FP left, FP right)
        {
            if (right.Raw == 0)
            {
                throw new DivideByZeroException("Cannot divide an FP value by zero.");
            }

            // int.MinValue % -1 is the sole overflowing integral remainder case in C#.
            if (left.Raw == int.MinValue && right.Raw == -1)
            {
                return Zero;
            }

            return new FP(left.Raw % right.Raw);
        }

        public static FP operator ++(FP value)
        {
            return value + One;
        }

        public static FP operator --(FP value)
        {
            return value - One;
        }

        public static bool operator ==(FP left, FP right)
        {
            return left.Raw == right.Raw;
        }

        public static bool operator !=(FP left, FP right)
        {
            return left.Raw != right.Raw;
        }

        public static bool operator <(FP left, FP right)
        {
            return left.Raw < right.Raw;
        }

        public static bool operator >(FP left, FP right)
        {
            return left.Raw > right.Raw;
        }

        public static bool operator <=(FP left, FP right)
        {
            return left.Raw <= right.Raw;
        }

        public static bool operator >=(FP left, FP right)
        {
            return left.Raw >= right.Raw;
        }

        public static implicit operator FP(int value)
        {
            return FromInt(value);
        }

        public static explicit operator int(FP value)
        {
            return value.ToIntTruncated();
        }

        public static explicit operator double(FP value)
        {
            return value.ToDouble();
        }

        private static FP Saturate(long raw)
        {
            if (raw > int.MaxValue)
            {
                return MaxValue;
            }

            if (raw < int.MinValue)
            {
                return MinValue;
            }

            return new FP((int)raw);
        }

        private static ulong UnsignedMagnitude(long value)
        {
            return value < 0L
                ? (ulong)(-(value + 1L)) + 1UL
                : (ulong)value;
        }

        private static ulong DivideRemainderToFraction(ulong remainder, ulong denominator)
        {
            ulong fraction = 0UL;

            for (int bit = 0; bit < FractionalBits; bit++)
            {
                fraction <<= 1;

                // Compute remainder * 2 without overflowing ulong.
                if (remainder >= denominator - remainder)
                {
                    remainder -= denominator - remainder;
                    fraction |= 1UL;
                }
                else
                {
                    remainder <<= 1;
                }
            }

            return fraction;
        }
    }
}
