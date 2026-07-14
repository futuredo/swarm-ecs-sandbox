using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Spatial
{
    /// <summary>
    /// Unsigned 65-bit squared distance. The full Q16.16 two-dimensional domain
    /// needs one carry bit beyond ulong because each axis delta can span uint.
    /// </summary>
    internal readonly struct WideSquaredDistance
    {
        public WideSquaredDistance(byte high, ulong low)
        {
            High = high;
            Low = low;
        }

        public byte High { get; }

        public ulong Low { get; }
    }

    /// <summary>
    /// Exact raw-coordinate distance helpers shared by deterministic spatial indexes.
    /// Q16.16 multiplication is deliberately avoided because it truncates fractional
    /// raw units and saturates long before the coordinate domain is exhausted.
    /// </summary>
    internal static class SpatialQueryDistance
    {
        /// <summary>
        /// Returns dx^2 + dy^2 in raw Q16.16 coordinate units. A coordinate delta
        /// fits in uint even when subtracting int.MinValue from int.MaxValue, so each
        /// axis square is exact. This ulong form saturates only when the two-axis sum
        /// exceeds ulong. Radius checks remain exact because a non-negative FP radius
        /// square is at most int.MaxValue^2; KNN uses <see cref="SquaredWide"/> instead.
        /// </summary>
        public static ulong Squared(FPVector2 left, FPVector2 right)
        {
            long deltaX = (long)left.X.Raw - right.X.Raw;
            long deltaY = (long)left.Y.Raw - right.Y.Raw;
            ulong xSquared = Square(deltaX);
            ulong ySquared = Square(deltaY);
            return ulong.MaxValue - xSquared < ySquared
                ? ulong.MaxValue
                : xSquared + ySquared;
        }

        /// <summary>
        /// Returns the exact 65-bit dx^2 + dy^2 for any two Q16.16 coordinates.
        /// </summary>
        public static WideSquaredDistance SquaredWide(FPVector2 left, FPVector2 right)
        {
            long deltaX = (long)left.X.Raw - right.X.Raw;
            long deltaY = (long)left.Y.Raw - right.Y.Raw;
            ulong xSquared = Square(deltaX);
            ulong ySquared = Square(deltaY);
            ulong low = unchecked(xSquared + ySquared);
            byte high = low < xSquared ? (byte)1 : (byte)0;
            return new WideSquaredDistance(high, low);
        }

        public static int Compare(WideSquaredDistance left, WideSquaredDistance right)
        {
            if (left.High != right.High)
            {
                return left.High < right.High ? -1 : 1;
            }

            return left.Low < right.Low ? -1 : left.Low > right.Low ? 1 : 0;
        }

        /// <summary>
        /// A kd-tree split-axis square is a lower bound with a zero high bit.
        /// </summary>
        public static bool AxisSquareIsWithin(ulong axisSquare, WideSquaredDistance limit)
        {
            return limit.High != 0 || axisSquare <= limit.Low;
        }

        /// <summary>
        /// Squares a raw coordinate delta exactly when its magnitude fits in uint;
        /// larger values cannot have a representable ulong square and saturate.
        /// </summary>
        public static ulong Square(long value)
        {
            ulong magnitude = value < 0L
                ? (ulong)(-(value + 1L)) + 1UL
                : (ulong)value;
            return magnitude > uint.MaxValue
                ? ulong.MaxValue
                : magnitude * magnitude;
        }
    }
}
