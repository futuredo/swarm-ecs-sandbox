using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Collision
{
    /// <summary>An inclusive, deterministic two-dimensional fixed-point AABB.</summary>
    public readonly struct FPAabb2
    {
        // FP dot products truncate each component product independently. Even when
        // an OBB basis has SqrMagnitude == One, converting an exact slab boundary
        // back to world coordinates can therefore extend up to three raw units past
        // the Axis * HalfExtent vertex envelope. This construction-time padding is
        // the proven two-dimensional upper bound and keeps the BVH conservative.
        private const int OrientedBoxTruncationPaddingRaw = 3;

        public FPAabb2(FPVector2 first, FPVector2 second)
        {
            Min = FPVector2.Min(first, second);
            Max = FPVector2.Max(first, second);
        }

        public FPVector2 Min { get; }

        public FPVector2 Max { get; }

        public bool Overlaps(in FPAabb2 other)
        {
            return Min.X <= other.Max.X && Max.X >= other.Min.X &&
                   Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;
        }

        public bool Contains(FPVector2 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y;
        }

        public FPAabb2 Expanded(FP amount)
        {
            if (amount < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            int minX = SaturateRaw((long)Min.X.Raw - amount.Raw);
            int minY = SaturateRaw((long)Min.Y.Raw - amount.Raw);
            int maxX = SaturateRaw((long)Max.X.Raw + amount.Raw);
            int maxY = SaturateRaw((long)Max.Y.Raw + amount.Raw);
            return new FPAabb2(
                new FPVector2(FP.FromRaw(minX), FP.FromRaw(minY)),
                new FPVector2(FP.FromRaw(maxX), FP.FromRaw(maxY)));
        }

        public static FPAabb2 Merge(in FPAabb2 left, in FPAabb2 right)
        {
            return new FPAabb2(
                FPVector2.Min(left.Min, right.Min),
                FPVector2.Max(left.Max, right.Max));
        }

        /// <summary>
        /// Computes a conservative world AABB for an OBB. Each non-negative raw
        /// product is rounded upward, then the fixed two-dimensional dot-product
        /// truncation bound is added so the broadphase cannot under-estimate a
        /// rotated fixed-point slab.
        /// </summary>
        public static FPAabb2 FromOrientedBox(in FPOrientedBox2 box)
        {
            int extentX = SaturateRaw(
                MultiplyAbsRawCeiling(box.AxisX.X.Raw, box.HalfExtents.X.Raw) +
                MultiplyAbsRawCeiling(box.AxisY.X.Raw, box.HalfExtents.Y.Raw) +
                OrientedBoxTruncationPaddingRaw);
            int extentY = SaturateRaw(
                MultiplyAbsRawCeiling(box.AxisX.Y.Raw, box.HalfExtents.X.Raw) +
                MultiplyAbsRawCeiling(box.AxisY.Y.Raw, box.HalfExtents.Y.Raw) +
                OrientedBoxTruncationPaddingRaw);

            return new FPAabb2(
                new FPVector2(
                    FP.FromRaw(SaturateRaw((long)box.Center.X.Raw - extentX)),
                    FP.FromRaw(SaturateRaw((long)box.Center.Y.Raw - extentY))),
                new FPVector2(
                    FP.FromRaw(SaturateRaw((long)box.Center.X.Raw + extentX)),
                    FP.FromRaw(SaturateRaw((long)box.Center.Y.Raw + extentY))));
        }

        public static FPAabb2 FromSegment(FPVector2 start, FPVector2 end, FP expansion)
        {
            if (expansion < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(expansion));
            }

            return new FPAabb2(FPVector2.Min(start, end), FPVector2.Max(start, end)).Expanded(expansion);
        }

        internal long CenterRaw(int axis)
        {
            return axis == 0
                ? ((long)Min.X.Raw + Max.X.Raw) / 2L
                : ((long)Min.Y.Raw + Max.Y.Raw) / 2L;
        }

        internal long ExtentRaw(int axis)
        {
            return axis == 0
                ? (long)Max.X.Raw - Min.X.Raw
                : (long)Max.Y.Raw - Min.Y.Raw;
        }

        private static long MultiplyAbsRawCeiling(int leftRaw, int rightRaw)
        {
            long leftMagnitude = leftRaw < 0 ? -(long)leftRaw : leftRaw;
            long rightMagnitude = rightRaw < 0 ? -(long)rightRaw : rightRaw;
            long product = leftMagnitude * rightMagnitude;
            return product == 0L
                ? 0L
                : (product + FP.OneRaw - 1L) / FP.OneRaw;
        }

        private static int SaturateRaw(long raw)
        {
            return raw > int.MaxValue
                ? int.MaxValue
                : raw < int.MinValue
                    ? int.MinValue
                    : (int)raw;
        }
    }
}
