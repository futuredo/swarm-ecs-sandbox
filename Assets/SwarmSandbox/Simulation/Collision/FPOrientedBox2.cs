using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Collision
{
    /// <summary>
    /// A 2D oriented box represented by a basis that is exactly orthonormal under
    /// the simulation's Q16.16 dot-product rules.
    /// </summary>
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
            AxisY = FPMath.PerpendicularLeft(AxisX);
        }

        /// <summary>
        /// Builds a right-handed orthogonal basis. Axis X is authoritative when it is
        /// non-zero. Axis Y is only used to recover a stable X direction when axis X
        /// is degenerate; the stored Y direction is always perpendicular to stored X.
        /// </summary>
        public FPOrientedBox2(FPVector2 center, FPVector2 halfExtents, FPVector2 axisX, FPVector2 axisY)
        {
            Center = center;
            HalfExtents = new FPVector2(FPMath.Abs(halfExtents.X), FPMath.Abs(halfExtents.Y));
            FPVector2 normalizedY = NormalizeOrFallback(axisY, new FPVector2(FP.Zero, FP.One));
            FPVector2 fallbackX = FPMath.PerpendicularRight(normalizedY);
            AxisX = NormalizeOrFallback(axisX, fallbackX);
            AxisY = FPMath.PerpendicularLeft(AxisX);
        }

        public FPVector2 Center { get; }

        public FPVector2 HalfExtents { get; }

        public FPVector2 AxisX { get; }

        public FPVector2 AxisY { get; }

        private static FPVector2 NormalizeOrFallback(FPVector2 value, FPVector2 fallback)
        {
            FP maximumComponent = FPMath.Max(FPMath.Abs(value.X), FPMath.Abs(value.Y));
            if (maximumComponent <= FP.Zero)
            {
                return fallback;
            }

            // Scaling first prevents SqrMagnitude from saturating for large direction
            // inputs while preserving the direction in the useful Q16.16 domain.
            FPVector2 scaled = value / maximumComponent;
            FP length = FPMath.Sqrt(scaled.SqrMagnitude);
            if (length <= FP.Zero)
            {
                return fallback;
            }

            return QuantizeExactUnit(scaled / length, fallback);
        }

        /// <summary>
        /// Finds the nearest raw vector whose component-square terms add to exactly
        /// FP.OneRaw after Q16.16 truncation. A merely approximate unit vector is not
        /// sufficient here: a basis shorter than one makes dot-product slabs larger
        /// than the Axis * HalfExtent vertices used by the BVH and obstacle segments.
        ///
        /// The search walks candidate Y values away from the normalized input. For a
        /// fixed Y, the valid X interval is derived with integer square roots. Once the
        /// Y error alone exceeds the best two-dimensional error, no later candidate can
        /// improve it. Axis-aligned unit vectors guarantee that a solution exists.
        /// This runs only while immutable obstacle geometry is being constructed.
        /// </summary>
        private static FPVector2 QuantizeExactUnit(FPVector2 value, FPVector2 fallback)
        {
            int signX = value.X.Raw < 0 ? -1 : 1;
            int signY = value.Y.Raw < 0 ? -1 : 1;
            int desiredX = AbsoluteRaw(value.X.Raw);
            int desiredY = AbsoluteRaw(value.Y.Raw);
            desiredX = Math.Min(desiredX, FP.OneRaw);
            desiredY = Math.Min(desiredY, FP.OneRaw);

            int bestX = 0;
            int bestY = 0;
            ulong bestError = ulong.MaxValue;
            int bestManhattan = int.MaxValue;

            for (int offset = 0; offset <= FP.OneRaw; ++offset)
            {
                ulong minimumFutureError = (ulong)(uint)offset * (uint)offset;
                if (bestError != ulong.MaxValue && minimumFutureError > bestError)
                {
                    break;
                }

                int lowerY = desiredY - offset;
                if (lowerY >= 0)
                {
                    EvaluateCandidate(
                        desiredX,
                        desiredY,
                        lowerY,
                        ref bestX,
                        ref bestY,
                        ref bestError,
                        ref bestManhattan);
                }

                if (offset == 0)
                {
                    continue;
                }

                int upperY = desiredY + offset;
                if (upperY <= FP.OneRaw)
                {
                    EvaluateCandidate(
                        desiredX,
                        desiredY,
                        upperY,
                        ref bestX,
                        ref bestY,
                        ref bestError,
                        ref bestManhattan);
                }
            }

            if (bestError == ulong.MaxValue)
            {
                return fallback;
            }

            return new FPVector2(
                FP.FromRaw(signX < 0 ? -bestX : bestX),
                FP.FromRaw(signY < 0 ? -bestY : bestY));
        }

        private static void EvaluateCandidate(
            int desiredX,
            int desiredY,
            int candidateY,
            ref int bestX,
            ref int bestY,
            ref ulong bestError,
            ref int bestManhattan)
        {
            long ySquare = (long)candidateY * candidateY;
            int yTerm = (int)(ySquare / FP.OneRaw);
            int targetXTerm = FP.OneRaw - yTerm;
            if (targetXTerm < 0)
            {
                return;
            }

            ulong lowerSquare = (ulong)(uint)targetXTerm * FP.OneRaw;
            ulong upperSquareExclusive = (ulong)(uint)(targetXTerm + 1) * FP.OneRaw;
            int minimumX = (int)CeilingSquareRoot(lowerSquare);
            int maximumX = (int)CeilingSquareRoot(upperSquareExclusive) - 1;
            minimumX = Math.Max(0, minimumX);
            maximumX = Math.Min(FP.OneRaw, maximumX);
            if (minimumX > maximumX)
            {
                return;
            }

            int candidateX = Math.Max(minimumX, Math.Min(desiredX, maximumX));
            int xTerm = (int)(((long)candidateX * candidateX) / FP.OneRaw);
            if (xTerm + yTerm != FP.OneRaw)
            {
                return;
            }

            long deltaX = (long)candidateX - desiredX;
            long deltaY = (long)candidateY - desiredY;
            ulong error = (ulong)((deltaX * deltaX) + (deltaY * deltaY));
            int manhattan = (int)(Math.Abs(deltaX) + Math.Abs(deltaY));
            if (error > bestError ||
                (error == bestError && manhattan > bestManhattan) ||
                (error == bestError && manhattan == bestManhattan && candidateX > bestX) ||
                (error == bestError && manhattan == bestManhattan && candidateX == bestX && candidateY >= bestY))
            {
                return;
            }

            bestX = candidateX;
            bestY = candidateY;
            bestError = error;
            bestManhattan = manhattan;
        }

        private static int AbsoluteRaw(int raw)
        {
            return raw == int.MinValue ? int.MaxValue : Math.Abs(raw);
        }

        private static ulong CeilingSquareRoot(ulong value)
        {
            ulong floor = IntegerSquareRoot(value);
            return floor * floor == value ? floor : floor + 1UL;
        }

        private static ulong IntegerSquareRoot(ulong value)
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
