using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Collision
{
    /// <summary>Deterministic fixed-point separating-axis collision tests.</summary>
    public static class FPSat2D
    {
        /// <summary>
        /// Tests the four face-normal axes of two OBBs. On overlap, normal points
        /// from a toward b and depth is the minimum translation distance.
        /// Touching boxes are considered overlapping with zero depth.
        /// </summary>
        public static bool Intersect(in FPOrientedBox2 a, in FPOrientedBox2 b, out FPVector2 normal, out FP depth)
        {
            FPVector2 centerDelta = b.Center - a.Center;
            normal = FPVector2.Zero;
            depth = FP.FromRaw(int.MaxValue);

            if (!TestAxis(a.AxisX, in a, in b, centerDelta, ref normal, ref depth) ||
                !TestAxis(a.AxisY, in a, in b, centerDelta, ref normal, ref depth) ||
                !TestAxis(b.AxisX, in a, in b, centerDelta, ref normal, ref depth) ||
                !TestAxis(b.AxisY, in a, in b, centerDelta, ref normal, ref depth))
            {
                normal = FPVector2.Zero;
                depth = FP.Zero;
                return false;
            }

            return true;
        }

        public static bool Overlaps(in FPOrientedBox2 a, in FPOrientedBox2 b)
        {
            return Intersect(in a, in b, out _, out _);
        }

        /// <summary>
        /// Tests a box and circle. On overlap, normal points from box toward circle.
        /// </summary>
        public static bool Intersect(in FPOrientedBox2 box, in FPCircle2 circle, out FPVector2 normal, out FP depth)
        {
            FPVector2 centerDelta = circle.Center - box.Center;
            FP localX = FPMath.Dot(centerDelta, box.AxisX);
            FP localY = FPMath.Dot(centerDelta, box.AxisY);
            FP absoluteX = FPMath.Abs(localX);
            FP absoluteY = FPMath.Abs(localY);

            bool centerInside = absoluteX <= box.HalfExtents.X && absoluteY <= box.HalfExtents.Y;
            if (centerInside)
            {
                FP distanceToXFace = box.HalfExtents.X - absoluteX;
                FP distanceToYFace = box.HalfExtents.Y - absoluteY;
                if (distanceToXFace <= distanceToYFace)
                {
                    normal = localX >= FP.Zero ? box.AxisX : FPVector2.Zero - box.AxisX;
                    depth = circle.Radius + distanceToXFace;
                }
                else
                {
                    normal = localY >= FP.Zero ? box.AxisY : FPVector2.Zero - box.AxisY;
                    depth = circle.Radius + distanceToYFace;
                }

                return true;
            }

            FP clampedX = FPMath.Min(FPMath.Max(localX, FP.Zero - box.HalfExtents.X), box.HalfExtents.X);
            FP clampedY = FPMath.Min(FPMath.Max(localY, FP.Zero - box.HalfExtents.Y), box.HalfExtents.Y);
            FPVector2 closest = box.Center + box.AxisX * clampedX + box.AxisY * clampedY;
            FPVector2 separation = circle.Center - closest;
            FP distanceSquared = separation.SqrMagnitude;
            FP radiusSquared = circle.Radius * circle.Radius;
            if (distanceSquared > radiusSquared)
            {
                normal = FPVector2.Zero;
                depth = FP.Zero;
                return false;
            }

            FP distance = FPMath.Sqrt(distanceSquared);
            if (distance > FP.Zero)
            {
                normal = separation / distance;
                depth = circle.Radius - distance;
            }
            else
            {
                // Degenerate zero-radius/boundary case; keep a stable direction.
                normal = box.AxisX;
                depth = circle.Radius;
            }

            return true;
        }

        private static bool TestAxis(
            FPVector2 axis,
            in FPOrientedBox2 a,
            in FPOrientedBox2 b,
            FPVector2 centerDelta,
            ref FPVector2 bestNormal,
            ref FP bestDepth)
        {
            FP projectedCenterDistance = FPMath.Dot(centerDelta, axis);
            FP radiusA = a.HalfExtents.X * FPMath.Abs(FPMath.Dot(a.AxisX, axis)) +
                         a.HalfExtents.Y * FPMath.Abs(FPMath.Dot(a.AxisY, axis));
            FP radiusB = b.HalfExtents.X * FPMath.Abs(FPMath.Dot(b.AxisX, axis)) +
                         b.HalfExtents.Y * FPMath.Abs(FPMath.Dot(b.AxisY, axis));
            FP overlap = radiusA + radiusB - FPMath.Abs(projectedCenterDistance);
            if (overlap < FP.Zero)
            {
                return false;
            }

            if (bestNormal == FPVector2.Zero || overlap < bestDepth)
            {
                bestDepth = overlap;
                bestNormal = projectedCenterDistance >= FP.Zero ? axis : FPVector2.Zero - axis;
            }

            return true;
        }
    }
}
