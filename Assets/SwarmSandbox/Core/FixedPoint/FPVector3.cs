using System;

namespace SwarmECS.FixedPoint
{
    [Serializable]
    public readonly struct FPVector3 : IEquatable<FPVector3>
    {
        public static readonly FPVector3 Zero = new FPVector3(FP.Zero, FP.Zero, FP.Zero);
        public static readonly FPVector3 One = new FPVector3(FP.One, FP.One, FP.One);
        public static readonly FPVector3 UnitX = new FPVector3(FP.One, FP.Zero, FP.Zero);
        public static readonly FPVector3 UnitY = new FPVector3(FP.Zero, FP.One, FP.Zero);
        public static readonly FPVector3 UnitZ = new FPVector3(FP.Zero, FP.Zero, FP.One);
        public static readonly FPVector3 Right = UnitX;
        public static readonly FPVector3 Up = UnitY;
        public static readonly FPVector3 Forward = UnitZ;

        public readonly FP X;
        public readonly FP Y;
        public readonly FP Z;

        public FPVector3(FP x, FP y, FP z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public FP SqrMagnitude => FPMath.Dot(this, this);

        public FP MagnitudeSquared => SqrMagnitude;

        public FP Magnitude => FPMath.Sqrt(SqrMagnitude);

        public FPVector3 Normalized => FPMath.NormalizeSafe(this);

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
                    case 2:
                        return Z;
                    default:
                        throw new IndexOutOfRangeException("FPVector3 index must be 0, 1, or 2.");
                }
            }
        }

        public static FP Dot(FPVector3 left, FPVector3 right)
        {
            return FPMath.Dot(left, right);
        }

        public static FPVector3 Cross(FPVector3 left, FPVector3 right)
        {
            return FPMath.Cross(left, right);
        }

        public static FP Distance(FPVector3 left, FPVector3 right)
        {
            return FPMath.Distance(left, right);
        }

        public bool Equals(FPVector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is FPVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }

        public static FPVector3 operator +(FPVector3 left, FPVector3 right)
        {
            return new FPVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static FPVector3 operator -(FPVector3 left, FPVector3 right)
        {
            return new FPVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static FPVector3 operator -(FPVector3 value)
        {
            return new FPVector3(-value.X, -value.Y, -value.Z);
        }

        public static FPVector3 operator *(FPVector3 value, FP scalar)
        {
            return new FPVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static FPVector3 operator *(FP scalar, FPVector3 value)
        {
            return value * scalar;
        }

        public static FPVector3 operator /(FPVector3 value, FP scalar)
        {
            return new FPVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        }

        public static bool operator ==(FPVector3 left, FPVector3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FPVector3 left, FPVector3 right)
        {
            return !left.Equals(right);
        }
    }
}
