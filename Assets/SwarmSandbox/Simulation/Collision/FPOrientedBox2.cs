using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Collision
{
    /// <summary>A 2D oriented box represented by an orthonormal local basis.</summary>
    public readonly struct FPOrientedBox2
    {
        public FPOrientedBox2(FPVector2 center, FPVector2 halfExtents)
            : this(center, halfExtents, new FPVector2(FP.One, FP.Zero), new FPVector2(FP.Zero, FP.One))
        {
        }

        /// <summary>Builds an oriented box from its local X direction.</summary>
        public FPOrientedBox2(FPVector2 center, FPVector2 halfExtents, FPVector2 axisX)
        {
            Center = center;
            HalfExtents = new FPVector2(FPMath.Abs(halfExtents.X), FPMath.Abs(halfExtents.Y));
            AxisX = NormalizeOrFallback(axisX, new FPVector2(FP.One, FP.Zero));
            AxisY = new FPVector2(FP.Zero - AxisX.Y, AxisX.X);
        }

        /// <summary>
        /// Builds from explicit orthogonal axes. Inputs are normalized independently;
        /// callers are responsible for supplying an orthogonal basis.
        /// </summary>
        public FPOrientedBox2(FPVector2 center, FPVector2 halfExtents, FPVector2 axisX, FPVector2 axisY)
        {
            Center = center;
            HalfExtents = new FPVector2(FPMath.Abs(halfExtents.X), FPMath.Abs(halfExtents.Y));
            AxisX = NormalizeOrFallback(axisX, new FPVector2(FP.One, FP.Zero));
            FPVector2 perpendicular = new FPVector2(FP.Zero - AxisX.Y, AxisX.X);
            AxisY = NormalizeOrFallback(axisY, perpendicular);
        }

        public FPVector2 Center { get; }

        public FPVector2 HalfExtents { get; }

        public FPVector2 AxisX { get; }

        public FPVector2 AxisY { get; }

        private static FPVector2 NormalizeOrFallback(FPVector2 value, FPVector2 fallback)
        {
            FP lengthSquared = value.SqrMagnitude;
            if (lengthSquared <= FP.Zero)
            {
                return fallback;
            }

            FP length = FPMath.Sqrt(lengthSquared);
            return length <= FP.Zero ? fallback : value / length;
        }
    }

    public readonly struct FPCircle2
    {
        public FPCircle2(FPVector2 center, FP radius)
        {
            if (radius < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            Center = center;
            Radius = radius;
        }

        public FPVector2 Center { get; }

        public FP Radius { get; }
    }
}
