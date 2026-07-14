using System;

namespace SwarmECS.FixedPoint
{
    [Serializable]
    public readonly struct FPVector2 : IEquatable<FPVector2>
    {
        public static readonly FPVector2 Zero = new FPVector2(FP.Zero, FP.Zero);
        public static readonly FPVector2 One = new FPVector2(FP.One, FP.One);
        public static readonly FPVector2 UnitX = new FPVector2(FP.One, FP.Zero);
        public static readonly FPVector2 UnitY = new FPVector2(FP.Zero, FP.One);
        public static readonly FPVector2 Right = UnitX;
        public static readonly FPVector2 Up = UnitY;

        public readonly FP X;
        public readonly FP Y;

        public FPVector2(FP x, FP y)
        {
            X = x;
            Y = y;
        }

        public FP SqrMagnitude => FPMath.Dot(this, this);

        public FP MagnitudeSquared => SqrMagnitude;

        public FP Magnitude => FPMath.Sqrt(SqrMagnitude);

        public FPVector2 Normalized => FPMath.NormalizeSafe(this);

        public FP this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return X;
                    case 1:
                        return Y;
                    default:
                        throw new IndexOutOfRangeException("FPVector2 index must be 0 or 1.");
                }
            }
        }

        public static FP Dot(FPVector2 left, FPVector2 right)
        {
            return FPMath.Dot(left, right);
        }

        public static FP Det(FPVector2 left, FPVector2 right)
        {
            return FPMath.Det(left, right);
        }

        public static FP Distance(FPVector2 left, FPVector2 right)
        {
            return FPMath.Distance(left, right);
        }

        public static FPVector2 Min(FPVector2 left, FPVector2 right)
        {
            return new FPVector2(FP.Min(left.X, right.X), FP.Min(left.Y, right.Y));
        }

        public static FPVector2 Max(FPVector2 left, FPVector2 right)
        {
            return new FPVector2(FP.Max(left.X, right.X), FP.Max(left.Y, right.Y));
        }

        public bool Equals(FPVector2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is FPVector2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }

        public static FPVector2 operator +(FPVector2 left, FPVector2 right)
        {
            return new FPVector2(left.X + right.X, left.Y + right.Y);
        }

        public static FPVector2 operator -(FPVector2 left, FPVector2 right)
        {
            return new FPVector2(left.X - right.X, left.Y - right.Y);
        }

        public static FPVector2 operator -(FPVector2 value)
        {
            return new FPVector2(-value.X, -value.Y);
        }

        public static FPVector2 operator *(FPVector2 value, FP scalar)
        {
            return new FPVector2(value.X * scalar, value.Y * scalar);
        }

        public static FPVector2 operator *(FP scalar, FPVector2 value)
        {
            return value * scalar;
        }

        public static FPVector2 operator /(FPVector2 value, FP scalar)
        {
            return new FPVector2(value.X / scalar, value.Y / scalar);
        }

        public static bool operator ==(FPVector2 left, FPVector2 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FPVector2 left, FPVector2 right)
        {
            return !left.Equals(right);
        }
    }
}
