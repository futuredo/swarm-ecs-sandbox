using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Collision
{
    public readonly struct FPSweepHit2D
    {
        public FPSweepHit2D(FP fraction, FPVector2 normal, int featureId)
        {
            Fraction = fraction;
            Normal = normal;
            FeatureId = featureId;
        }

        /// <summary>Conservative time of impact in the inclusive range [0, 1].</summary>
        public FP Fraction { get; }

        /// <summary>Outward OBB face normal opposing the incoming displacement.</summary>
        public FPVector2 Normal { get; }

        /// <summary>-X, +X, -Y, +Y are encoded as 0, 1, 2, 3.</summary>
        public int FeatureId { get; }
    }

    /// <summary>
    /// Allocation-free conservative swept-circle tests. The OBB is expanded by the
    /// circle radius and optional skin and then tested with a segment/slab query.
    /// Consequently its corners are square rather than the exact rounded Minkowski
    /// corners: the test can report an early corner hit, but cannot tunnel through.
    /// </summary>
    public static class FPSweptCircle2D
    {
        private const int NegativeXFeature = 0;
        private const int PositiveXFeature = 1;
        private const int NegativeYFeature = 2;
        private const int PositiveYFeature = 3;

        public static bool SweepAgainstBox(
            in FPCircle2 circle,
            FPVector2 displacement,
            in FPOrientedBox2 box,
            out FPSweepHit2D hit)
        {
            return SweepAgainstBox(in circle, displacement, FP.Zero, in box, out hit);
        }

        public static bool SweepAgainstBox(
            in FPCircle2 circle,
            FPVector2 displacement,
            FP skin,
            in FPOrientedBox2 box,
            out FPSweepHit2D hit)
        {
            if (skin < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(skin));
            }

            hit = default;
            if (displacement == FPVector2.Zero)
            {
                return false;
            }

            FPVector2 relativeStart = circle.Center - box.Center;
            FP localX = FPMath.Dot(relativeStart, box.AxisX);
            FP localY = FPMath.Dot(relativeStart, box.AxisY);
            FP deltaX = FPMath.Dot(displacement, box.AxisX);
            FP deltaY = FPMath.Dot(displacement, box.AxisY);
            FP expandedX = box.HalfExtents.X + circle.Radius + skin;
            FP expandedY = box.HalfExtents.Y + circle.Radius + skin;

            bool startsInside = IsInsideExtent(localX.Raw, expandedX.Raw) &&
                                IsInsideExtent(localY.Raw, expandedY.Raw);
            if (startsInside)
            {
                return TryBuildContainingHit(
                    localX,
                    localY,
                    expandedX,
                    expandedY,
                    displacement,
                    in box,
                    out hit);
            }

            long enterRaw = 0L;
            long exitRaw = FP.OneRaw;
            int enterFeature = int.MaxValue;
            FPVector2 enterNormal = FPVector2.Zero;

            if (!UpdateSlab(
                    localX.Raw,
                    deltaX.Raw,
                    expandedX.Raw,
                    NegativeXFeature,
                    PositiveXFeature,
                    box.AxisX,
                    ref enterRaw,
                    ref exitRaw,
                    ref enterFeature,
                    ref enterNormal) ||
                !UpdateSlab(
                    localY.Raw,
                    deltaY.Raw,
                    expandedY.Raw,
                    NegativeYFeature,
                    PositiveYFeature,
                    box.AxisY,
                    ref enterRaw,
                    ref exitRaw,
                    ref enterFeature,
                    ref enterNormal))
            {
                return false;
            }

            if (exitRaw < 0L || enterRaw > FP.OneRaw || enterRaw > exitRaw || enterFeature == int.MaxValue)
            {
                return false;
            }

            int clampedFractionRaw = enterRaw <= 0L
                ? 0
                : enterRaw >= FP.OneRaw
                    ? FP.OneRaw
                    : (int)enterRaw;
            hit = new FPSweepHit2D(FP.FromRaw(clampedFractionRaw), enterNormal, enterFeature);
            return true;
        }

        private static bool TryBuildContainingHit(
            FP localX,
            FP localY,
            FP expandedX,
            FP expandedY,
            FPVector2 displacement,
            in FPOrientedBox2 box,
            out FPSweepHit2D hit)
        {
            FP distanceToX = expandedX - FPMath.Abs(localX);
            FP distanceToY = expandedY - FPMath.Abs(localY);
            bool positiveX = localX >= FP.Zero;
            bool positiveY = localY >= FP.Zero;
            FPVector2 xNormal = positiveX ? box.AxisX : -box.AxisX;
            FPVector2 yNormal = positiveY ? box.AxisY : -box.AxisY;
            bool movingInwardX = FPMath.Dot(displacement, xNormal) < FP.Zero;
            bool movingInwardY = FPMath.Dot(displacement, yNormal) < FP.Zero;

            // A point may be inside the conservative square expansion while still
            // outside the exact rounded Minkowski corner. Checking only the nearest
            // face can miss entry along the other axis (for example, a +Y move from
            // the lower-left corner region). Block every inward component; repeated
            // fixed-budget sweeps resolve the second axis when both are inward.
            if (!movingInwardX && !movingInwardY)
            {
                hit = default;
                return false;
            }

            FPVector2 normal;
            int feature;

            if (movingInwardX && (!movingInwardY || distanceToX <= distanceToY))
            {
                normal = xNormal;
                feature = positiveX ? PositiveXFeature : NegativeXFeature;
            }
            else
            {
                normal = yNormal;
                feature = positiveY ? PositiveYFeature : NegativeYFeature;
            }

            hit = new FPSweepHit2D(FP.Zero, normal, feature);
            return true;
        }

        private static bool UpdateSlab(
            int positionRaw,
            int displacementRaw,
            int extentRaw,
            int negativeFeature,
            int positiveFeature,
            FPVector2 positiveAxis,
            ref long enterRaw,
            ref long exitRaw,
            ref int enterFeature,
            ref FPVector2 enterNormal)
        {
            if (displacementRaw == 0)
            {
                return positionRaw >= -extentRaw && positionRaw <= extentRaw;
            }

            long nearNumerator;
            long farNumerator;
            int feature;
            FPVector2 normal;
            if (displacementRaw > 0)
            {
                nearNumerator = -(long)extentRaw - positionRaw;
                farNumerator = (long)extentRaw - positionRaw;
                feature = negativeFeature;
                normal = -positiveAxis;
            }
            else
            {
                nearNumerator = (long)extentRaw - positionRaw;
                farNumerator = -(long)extentRaw - positionRaw;
                feature = positiveFeature;
                normal = positiveAxis;
            }

            long nearRaw = DivideScaledFloor(nearNumerator, displacementRaw);
            long farRaw = DivideScaledCeiling(farNumerator, displacementRaw);
            if (nearRaw > enterRaw || (nearRaw == enterRaw && feature < enterFeature))
            {
                enterRaw = nearRaw;
                enterFeature = feature;
                enterNormal = normal;
            }

            if (farRaw < exitRaw)
            {
                exitRaw = farRaw;
            }

            return enterRaw <= exitRaw;
        }

        private static bool IsInsideExtent(int positionRaw, int extentRaw)
        {
            return (long)positionRaw >= -(long)extentRaw && positionRaw <= extentRaw;
        }

        private static long DivideScaledFloor(long numerator, long denominator)
        {
            long scaled = numerator * FP.OneRaw;
            long quotient = scaled / denominator;
            long remainder = scaled % denominator;
            if (remainder != 0L && (scaled < 0L) != (denominator < 0L))
            {
                --quotient;
            }

            return quotient;
        }

        private static long DivideScaledCeiling(long numerator, long denominator)
        {
            long scaled = numerator * FP.OneRaw;
            long quotient = scaled / denominator;
            long remainder = scaled % denominator;
            if (remainder != 0L && (scaled < 0L) == (denominator < 0L))
            {
                ++quotient;
            }

            return quotient;
        }
    }
}
