using System;

namespace SwarmECS.FixedPoint
{
    /// <summary>Allocation-free deterministic helpers for the fixed-point simulation.</summary>
    public static class FPMath
    {
        private static readonly FP NormalizationInwardScale = FP.FromRaw(FP.OneRaw - 2);

        public static FP Abs(FP value) => FP.Abs(value);

        public static FP Min(FP left, FP right) => FP.Min(left, right);

        public static FP Max(FP left, FP right) => FP.Max(left, right);

        public static FP Clamp(FP value, FP min, FP max) => FP.Clamp(value, min, max);

        public static FP Clamp01(FP value) => FP.Clamp(value, FP.Zero, FP.One);

        public static int Sign(FP value)
        {
            return value.Raw < 0 ? -1 : value.Raw > 0 ? 1 : 0;
        }

        /// <summary>Returns floor(sqrt(value)) at Q16.16 precision using integer arithmetic only.</summary>
        public static FP Sqrt(FP value)
        {
            if (value.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative FP value.");
            }

            if (value.Raw == 0)
            {
                return FP.Zero;
            }

            ulong radicand = (ulong)(uint)value.Raw << FP.FractionalBits;
            ulong root = IntegerSqrt(radicand);
            return FP.FromRaw((int)root);
        }

        /// <summary>
        /// Returns the smallest non-negative integer whose square is greater than or
        /// equal to <paramref name="value"/>. The implementation is integer-only and
        /// therefore safe for authoritative layout decisions.
        /// </summary>
        public static int CeilingIntegerSquareRoot(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            int low = 0;
            int high = value < 46341 ? value : 46341;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if ((long)middle * middle >= value)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return low;
        }

        public static FP Dot(FPVector2 left, FPVector2 right)
        {
            return (left.X * right.X) + (left.Y * right.Y);
        }

        public static FP Dot(FPVector3 left, FPVector3 right)
        {
            return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
        }

        public static FP Det(FPVector2 left, FPVector2 right)
        {
            return (left.X * right.Y) - (left.Y * right.X);
        }

        public static FPVector3 Cross(FPVector3 left, FPVector3 right)
        {
            return new FPVector3(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));
        }

        public static FP Length(FPVector2 value) => Sqrt(Dot(value, value));

        public static FP Length(FPVector3 value) => Sqrt(Dot(value, value));

        public static FP LengthSquared(FPVector2 value) => Dot(value, value);

        public static FP LengthSquared(FPVector3 value) => Dot(value, value);

        public static FP Distance(FPVector2 left, FPVector2 right) => Length(left - right);

        public static FP Distance(FPVector3 left, FPVector3 right) => Length(left - right);

        public static FP DistanceSquared(FPVector2 left, FPVector2 right) => LengthSquared(left - right);

        public static FP DistanceSquared(FPVector3 left, FPVector3 right) => LengthSquared(left - right);

        public static FPVector2 NormalizeSafe(FPVector2 value)
        {
            return NormalizeSafe(value, FPVector2.Zero, FP.Epsilon);
        }

        public static FPVector2 NormalizeSafe(FPVector2 value, FPVector2 fallback)
        {
            return NormalizeSafe(value, fallback, FP.Epsilon);
        }

        public static FPVector2 NormalizeSafe(FPVector2 value, FP minimumLength)
        {
            return NormalizeSafe(value, FPVector2.Zero, minimumLength);
        }

        public static FPVector2 NormalizeSafe(FPVector2 value, FPVector2 fallback, FP minimumLength)
        {
            FP threshold = Abs(minimumLength);
            FP lengthSquared = Dot(value, value);

            if (lengthSquared <= threshold * threshold || lengthSquared <= FP.Zero)
            {
                return fallback;
            }

            FP maximumComponent = Max(Abs(value.X), Abs(value.Y));
            if (maximumComponent <= FP.Zero)
            {
                return fallback;
            }

            // Normalize a scaled vector so dot products cannot saturate for large raw
            // coordinates. Integer sqrt rounds down, therefore bias a rare overshoot
            // back inside the unit circle by two raw units.
            FPVector2 scaled = value / maximumComponent;
            FP length = Sqrt(Dot(scaled, scaled));
            if (length <= FP.Zero)
            {
                return fallback;
            }

            FPVector2 normalized = scaled / length;
            return Dot(normalized, normalized) > FP.One
                ? normalized * NormalizationInwardScale
                : normalized;
        }

        public static FPVector3 NormalizeSafe(FPVector3 value)
        {
            return NormalizeSafe(value, FPVector3.Zero, FP.Epsilon);
        }

        public static FPVector3 NormalizeSafe(FPVector3 value, FPVector3 fallback)
        {
            return NormalizeSafe(value, fallback, FP.Epsilon);
        }

        public static FPVector3 NormalizeSafe(FPVector3 value, FP minimumLength)
        {
            return NormalizeSafe(value, FPVector3.Zero, minimumLength);
        }

        public static FPVector3 NormalizeSafe(FPVector3 value, FPVector3 fallback, FP minimumLength)
        {
            FP threshold = Abs(minimumLength);
            FP lengthSquared = Dot(value, value);

            if (lengthSquared <= threshold * threshold || lengthSquared <= FP.Zero)
            {
                return fallback;
            }

            FP maximumComponent = Max(Abs(value.X), Max(Abs(value.Y), Abs(value.Z)));
            if (maximumComponent <= FP.Zero)
            {
                return fallback;
            }

            FPVector3 scaled = value / maximumComponent;
            FP length = Sqrt(Dot(scaled, scaled));
            if (length <= FP.Zero)
            {
                return fallback;
            }

            FPVector3 normalized = scaled / length;
            return Dot(normalized, normalized) > FP.One
                ? normalized * NormalizationInwardScale
                : normalized;
        }

        /// <summary>Unclamped linear interpolation.</summary>
        public static FP Lerp(FP from, FP to, FP t)
        {
            return from + ((to - from) * t);
        }

        /// <summary>Unclamped linear interpolation.</summary>
        public static FPVector2 Lerp(FPVector2 from, FPVector2 to, FP t)
        {
            return from + ((to - from) * t);
        }

        /// <summary>Unclamped linear interpolation.</summary>
        public static FPVector3 Lerp(FPVector3 from, FPVector3 to, FP t)
        {
            return from + ((to - from) * t);
        }

        public static FP LerpClamped(FP from, FP to, FP t) => Lerp(from, to, Clamp01(t));

        public static FPVector2 LerpClamped(FPVector2 from, FPVector2 to, FP t) => Lerp(from, to, Clamp01(t));

        public static FPVector3 LerpClamped(FPVector3 from, FPVector3 to, FP t) => Lerp(from, to, Clamp01(t));

        public static FPVector2 PerpendicularLeft(FPVector2 value)
        {
            return new FPVector2(-value.Y, value.X);
        }

        public static FPVector2 PerpendicularRight(FPVector2 value)
        {
            return new FPVector2(value.Y, -value.X);
        }

        private static ulong IntegerSqrt(ulong value)
        {
            ulong remainder = value;
            ulong result = 0UL;
            ulong bit = 1UL << 62;

            while (bit > remainder)
            {
                bit >>= 2;
            }

            while (bit != 0UL)
            {
                if (remainder >= result + bit)
                {
                    remainder -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return result;
        }
    }
}
