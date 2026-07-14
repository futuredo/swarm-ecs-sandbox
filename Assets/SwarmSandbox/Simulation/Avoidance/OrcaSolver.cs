using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Avoidance
{
    /// <summary>
    /// Deterministic, allocation-free two-dimensional RVO2/ORCA velocity solver.
    ///
    /// The caller owns the neighbor, line, and projection-line buffers. A line buffer
    /// must have at least neighborCount entries and projectionLines must be a separate
    /// buffer of the same minimum size. No floating-point operation is used here.
    /// </summary>
    public static class OrcaSolver
    {
        // Two raw Q16.16 units are enough to classify numerically parallel lines while
        // preserving the smallest useful geometric distinctions in the simulation.
        private static readonly FP ParallelEpsilon = FP.FromRaw(2);
        private static readonly FP NormalizationEpsilon = FP.FromRaw(2);
        private static readonly FP ClampSafetyScale = FP.FromRaw(FP.OneRaw - 2);

        /// <summary>
        /// Solves one agent's collision-free velocity. The return value is the number
        /// of ORCA half-plane constraints written to <paramref name="lines"/>.
        /// </summary>
        public static int Solve(
            FPVector2 currentVelocity,
            FPVector2 preferredVelocity,
            FP radius,
            FP maxSpeed,
            FP timeHorizon,
            FP timeStep,
            AgentNeighbor[] neighbors,
            int neighborCount,
            OrcaLine[] lines,
            OrcaLine[] projectionLines,
            out FPVector2 newVelocity)
        {
            return Solve(
                0,
                currentVelocity,
                preferredVelocity,
                radius,
                maxSpeed,
                timeHorizon,
                timeStep,
                neighbors,
                neighborCount,
                lines,
                projectionLines,
                out newVelocity);
        }

        /// <summary>
        /// Solves one agent's collision-free velocity. Supplying stable agent IDs makes
        /// the exact-overlap fallback anti-symmetric for a pair of agents.
        /// </summary>
        public static int Solve(
            int agentStableId,
            FPVector2 currentVelocity,
            FPVector2 preferredVelocity,
            FP radius,
            FP maxSpeed,
            FP timeHorizon,
            FP timeStep,
            AgentNeighbor[] neighbors,
            int neighborCount,
            OrcaLine[] lines,
            OrcaLine[] projectionLines,
            out FPVector2 newVelocity)
        {
            ValidateBuffers(neighbors, neighborCount, lines, projectionLines);

            if (maxSpeed <= FP.Zero)
            {
                newVelocity = FPVector2.Zero;
                return 0;
            }

            if (timeHorizon <= FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeHorizon), "ORCA time horizon must be positive.");
            }

            if (timeStep <= FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeStep), "ORCA time step must be positive.");
            }

            FP agentRadius = FPMath.Max(radius, FP.Zero);
            FP inverseTimeHorizon = FP.One / timeHorizon;
            FP inverseTimeStep = FP.One / timeStep;
            int lineCount = 0;

            for (int i = 0; i < neighborCount; i++)
            {
                AgentNeighbor neighbor = neighbors[i];
                OrcaLine line = BuildAgentLine(
                    agentStableId,
                    currentVelocity,
                    agentRadius,
                    inverseTimeHorizon,
                    inverseTimeStep,
                    neighbor,
                    i);

                InsertLineSorted(lines, ref lineCount, line);
            }

            int failedLine = LinearProgram2(
                lines,
                lineCount,
                maxSpeed,
                preferredVelocity,
                false,
                out newVelocity);

            if (failedLine < lineCount)
            {
                LinearProgram3(
                    lines,
                    lineCount,
                    0,
                    failedLine,
                    maxSpeed,
                    projectionLines,
                    ref newVelocity);
            }

            newVelocity = ClampMagnitude(newVelocity, maxSpeed);
            return lineCount;
        }

        private static OrcaLine BuildAgentLine(
            int agentStableId,
            FPVector2 currentVelocity,
            FP agentRadius,
            FP inverseTimeHorizon,
            FP inverseTimeStep,
            AgentNeighbor neighbor,
            int sourceOrder)
        {
            FPVector2 relativePosition = neighbor.RelativePosition;
            FPVector2 relativeVelocity = currentVelocity - neighbor.Velocity;
            FP distanceSquared = relativePosition.SqrMagnitude;
            FP combinedRadius = agentRadius + FPMath.Max(neighbor.Radius, FP.Zero);
            FP combinedRadiusSquared = combinedRadius * combinedRadius;

            FPVector2 direction;
            FPVector2 correction;

            if (distanceSquared > combinedRadiusSquared)
            {
                // No current collision. Construct either the cut-off-circle tangent or
                // one of the two legs of the truncated velocity obstacle.
                FPVector2 w = relativeVelocity - (relativePosition * inverseTimeHorizon);
                FP wLengthSquared = w.SqrMagnitude;
                FP projection = FPMath.Dot(w, relativePosition);

                if (projection < FP.Zero &&
                    (projection * projection) > (combinedRadiusSquared * wLengthSquared))
                {
                    FPVector2 unitW = NormalizeOrFallback(
                        w,
                        AwayFromNeighbor(relativePosition, agentStableId, neighbor.StableId));
                    FP wLength = FPMath.Sqrt(FPMath.Max(wLengthSquared, FP.Zero));

                    direction = FPMath.PerpendicularRight(unitW);
                    correction = unitW * ((combinedRadius * inverseTimeHorizon) - wLength);
                }
                else
                {
                    FP leg = FPMath.Sqrt(FPMath.Max(distanceSquared - combinedRadiusSquared, FP.Zero));

                    if (FPMath.Det(relativePosition, w) > FP.Zero)
                    {
                        direction = new FPVector2(
                            (relativePosition.X * leg) - (relativePosition.Y * combinedRadius),
                            (relativePosition.X * combinedRadius) + (relativePosition.Y * leg)) / distanceSquared;
                    }
                    else
                    {
                        direction = -new FPVector2(
                            (relativePosition.X * leg) + (relativePosition.Y * combinedRadius),
                            (-relativePosition.X * combinedRadius) + (relativePosition.Y * leg)) / distanceSquared;
                    }

                    direction = NormalizeOrFallback(
                        direction,
                        FPMath.PerpendicularRight(AwayFromNeighbor(relativePosition, agentStableId, neighbor.StableId)));

                    FP projectionOnLeg = FPMath.Dot(relativeVelocity, direction);
                    correction = (direction * projectionOnLeg) - relativeVelocity;
                }
            }
            else
            {
                // Already colliding. The next simulation step, rather than the long
                // horizon, bounds the velocity obstacle so overlap is resolved quickly.
                FPVector2 w = relativeVelocity - (relativePosition * inverseTimeStep);
                FPVector2 unitW = NormalizeOrFallback(
                    w,
                    AwayFromNeighbor(relativePosition, agentStableId, neighbor.StableId));
                FP wLength = FPMath.Sqrt(FPMath.Max(w.SqrMagnitude, FP.Zero));

                direction = FPMath.PerpendicularRight(unitW);
                correction = unitW * ((combinedRadius * inverseTimeStep) - wLength);
            }

            direction = NormalizeOrFallback(
                direction,
                FPMath.PerpendicularRight(AwayFromNeighbor(relativePosition, agentStableId, neighbor.StableId)));

            return new OrcaLine
            {
                Point = currentVelocity + (correction * FP.Half),
                Direction = direction,
                NeighborId = neighbor.StableId,
                SourceOrder = sourceOrder
            };
        }

        /// <summary>
        /// Finds the feasible interval on one constraint line inside the speed circle
        /// and all previously accepted half-planes.
        /// </summary>
        private static bool LinearProgram1(
            OrcaLine[] lines,
            int lineNo,
            FP radius,
            FPVector2 optimumVelocity,
            bool directionOptimum,
            ref FPVector2 result)
        {
            OrcaLine target = lines[lineNo];
            FP dot = FPMath.Dot(target.Point, target.Direction);
            FP discriminant = (dot * dot) + (radius * radius) - target.Point.SqrMagnitude;

            if (discriminant < FP.Zero)
            {
                return false;
            }

            FP sqrtDiscriminant = FPMath.Sqrt(discriminant);
            FP tLeft = -dot - sqrtDiscriminant;
            FP tRight = -dot + sqrtDiscriminant;

            for (int i = 0; i < lineNo; i++)
            {
                OrcaLine previous = lines[i];
                FP denominator = FPMath.Det(target.Direction, previous.Direction);
                FP numerator = FPMath.Det(previous.Direction, target.Point - previous.Point);

                if (FPMath.Abs(denominator) <= ParallelEpsilon)
                {
                    if (numerator < FP.Zero)
                    {
                        return false;
                    }

                    continue;
                }

                FP t = numerator / denominator;

                if (denominator >= FP.Zero)
                {
                    tRight = FPMath.Min(tRight, t);
                }
                else
                {
                    tLeft = FPMath.Max(tLeft, t);
                }

                if (tLeft > tRight)
                {
                    return false;
                }
            }

            if (directionOptimum)
            {
                result = target.Point +
                    (target.Direction * (FPMath.Dot(optimumVelocity, target.Direction) > FP.Zero ? tRight : tLeft));
            }
            else
            {
                FP t = FPMath.Dot(target.Direction, optimumVelocity - target.Point);
                t = FPMath.Clamp(t, tLeft, tRight);
                result = target.Point + (target.Direction * t);
            }

            return true;
        }

        /// <summary>
        /// Incrementally solves the closest point to an optimum within a speed circle
        /// and a set of ORCA half-planes. Returns lineCount on success.
        /// </summary>
        private static int LinearProgram2(
            OrcaLine[] lines,
            int lineCount,
            FP radius,
            FPVector2 optimumVelocity,
            bool directionOptimum,
            out FPVector2 result)
        {
            if (directionOptimum)
            {
                result = NormalizeOrFallback(optimumVelocity, FPVector2.Zero) * radius;
            }
            else if (optimumVelocity.SqrMagnitude > (radius * radius))
            {
                result = NormalizeOrFallback(optimumVelocity, FPVector2.Zero) * radius;
            }
            else
            {
                result = optimumVelocity;
            }

            for (int i = 0; i < lineCount; i++)
            {
                OrcaLine line = lines[i];

                if (FPMath.Det(line.Direction, line.Point - result) > FP.Zero)
                {
                    FPVector2 previousResult = result;

                    if (!LinearProgram1(
                        lines,
                        i,
                        radius,
                        optimumVelocity,
                        directionOptimum,
                        ref result))
                    {
                        result = previousResult;
                        return i;
                    }
                }
            }

            return lineCount;
        }

        /// <summary>
        /// Repairs an infeasible LP2 result by projecting later constraints into a
        /// one-dimensional optimization problem. This is RVO2's third linear program.
        /// </summary>
        private static void LinearProgram3(
            OrcaLine[] lines,
            int lineCount,
            int obstacleLineCount,
            int beginLine,
            FP radius,
            OrcaLine[] projectionLines,
            ref FPVector2 result)
        {
            FP distance = FP.Zero;

            for (int i = beginLine; i < lineCount; i++)
            {
                OrcaLine current = lines[i];
                FP violation = FPMath.Det(current.Direction, current.Point - result);

                if (violation <= distance)
                {
                    continue;
                }

                int projectionCount = 0;

                for (int j = 0; j < obstacleLineCount; j++)
                {
                    projectionLines[projectionCount++] = lines[j];
                }

                for (int j = obstacleLineCount; j < i; j++)
                {
                    OrcaLine other = lines[j];
                    FP determinant = FPMath.Det(current.Direction, other.Direction);
                    OrcaLine projected = default;

                    if (FPMath.Abs(determinant) <= ParallelEpsilon)
                    {
                        if (FPMath.Dot(current.Direction, other.Direction) > FP.Zero)
                        {
                            continue;
                        }

                        projected.Point = (current.Point + other.Point) * FP.Half;
                    }
                    else
                    {
                        FP offset = FPMath.Det(
                            other.Direction,
                            current.Point - other.Point) / determinant;
                        projected.Point = current.Point + (current.Direction * offset);
                    }

                    projected.Direction = NormalizeOrFallback(
                        other.Direction - current.Direction,
                        FPMath.PerpendicularLeft(current.Direction));
                    projected.NeighborId = other.NeighborId;
                    projected.SourceOrder = other.SourceOrder;
                    projectionLines[projectionCount++] = projected;
                }

                FPVector2 previousResult = result;
                FPVector2 directionOptimum = FPMath.PerpendicularLeft(current.Direction);
                int failedProjection = LinearProgram2(
                    projectionLines,
                    projectionCount,
                    radius,
                    directionOptimum,
                    true,
                    out result);

                if (failedProjection < projectionCount)
                {
                    // Fixed-point rounding can make the projected LP marginally
                    // infeasible. Preserve the last valid deterministic result.
                    result = previousResult;
                }

                distance = FPMath.Det(current.Direction, current.Point - result);
            }
        }

        private static void InsertLineSorted(OrcaLine[] lines, ref int lineCount, OrcaLine line)
        {
            int insert = lineCount;

            while (insert > 0 && CompareLineOrder(line, lines[insert - 1]) < 0)
            {
                lines[insert] = lines[insert - 1];
                insert--;
            }

            lines[insert] = line;
            lineCount++;
        }

        private static int CompareLineOrder(OrcaLine left, OrcaLine right)
        {
            if (left.NeighborId < right.NeighborId)
            {
                return -1;
            }

            if (left.NeighborId > right.NeighborId)
            {
                return 1;
            }

            if (left.SourceOrder < right.SourceOrder)
            {
                return -1;
            }

            return left.SourceOrder > right.SourceOrder ? 1 : 0;
        }

        private static FPVector2 AwayFromNeighbor(
            FPVector2 relativePosition,
            int agentStableId,
            int neighborStableId)
        {
            FPVector2 away = NormalizeOrFallback(-relativePosition, FPVector2.Zero);

            if (away != FPVector2.Zero)
            {
                return away;
            }

            return PairTieBreakDirection(agentStableId, neighborStableId);
        }

        private static FPVector2 PairTieBreakDirection(int agentStableId, int neighborStableId)
        {
            int low = agentStableId <= neighborStableId ? agentStableId : neighborStableId;
            int high = agentStableId <= neighborStableId ? neighborStableId : agentStableId;
            uint hash;

            unchecked
            {
                hash = ((uint)low * 0x9E3779B9u) ^ ((uint)high * 0x85EBCA6Bu) ^ 0xC2B2AE35u;
                hash ^= hash >> 16;
            }

            FP pairSign = (hash & 2u) == 0u ? FP.One : -FP.One;
            FP orientation = agentStableId <= neighborStableId ? FP.One : -FP.One;
            FP sign = pairSign * orientation;

            return (hash & 1u) == 0u
                ? new FPVector2(sign, FP.Zero)
                : new FPVector2(FP.Zero, sign);
        }

        private static FPVector2 NormalizeOrFallback(FPVector2 value, FPVector2 fallback)
        {
            FP squaredLength = value.SqrMagnitude;

            if (squaredLength <= (NormalizationEpsilon * NormalizationEpsilon))
            {
                return fallback;
            }

            FP length = FPMath.Sqrt(squaredLength);
            return length <= NormalizationEpsilon ? fallback : value / length;
        }

        private static FPVector2 ClampMagnitude(FPVector2 value, FP maxMagnitude)
        {
            FP maxSquared = maxMagnitude * maxMagnitude;
            FP squaredLength = value.SqrMagnitude;

            if (squaredLength <= maxSquared)
            {
                return value;
            }

            FP length = FPMath.Sqrt(squaredLength);

            if (length <= FP.Zero)
            {
                return FPVector2.Zero;
            }

            FPVector2 clamped = value * (maxMagnitude / length);

            // Q16.16 truncation normally puts this on or just inside the circle. If
            // accumulated component rounding leaves it outside, bias inward by 2 ulp.
            if (clamped.SqrMagnitude > maxSquared)
            {
                clamped = clamped * ClampSafetyScale;
            }

            return clamped;
        }

        private static void ValidateBuffers(
            AgentNeighbor[] neighbors,
            int neighborCount,
            OrcaLine[] lines,
            OrcaLine[] projectionLines)
        {
            if (neighborCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(neighborCount));
            }

            if (neighbors == null)
            {
                if (neighborCount != 0)
                {
                    throw new ArgumentNullException(nameof(neighbors));
                }
            }
            else if (neighborCount > neighbors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(neighborCount));
            }

            if (lines == null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            if (projectionLines == null)
            {
                throw new ArgumentNullException(nameof(projectionLines));
            }

            if (lines.Length < neighborCount)
            {
                throw new ArgumentException("Line buffer is smaller than neighborCount.", nameof(lines));
            }

            if (projectionLines.Length < neighborCount)
            {
                throw new ArgumentException("Projection-line buffer is smaller than neighborCount.", nameof(projectionLines));
            }

            if (neighborCount > 0 && ReferenceEquals(lines, projectionLines))
            {
                throw new ArgumentException("ORCA line and projection-line buffers must be different arrays.");
            }
        }
    }
}
