// Portions of the static-obstacle ORCA constraint construction and linear-program
// solver are adapted from RVO2 Agent.cc, then substantially modified for C#,
// deterministic Q16.16 arithmetic, caller-owned buffers, and stable ordering.
//
// SPDX-FileCopyrightText: 2008 University of North Carolina at Chapel Hill
// SPDX-License-Identifier: Apache-2.0
// Source: https://github.com/snape/RVO2/blob/main/src/Agent.cc

using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Avoidance
{
    /// <summary>
    /// Deterministic, allocation-free two-dimensional RVO2/ORCA velocity solver.
    ///
    /// The caller owns the neighbor, line, and projection-line buffers. For the static
    /// obstacle overload, both line buffers must hold obstacleNeighborCount +
    /// neighborCount entries. No floating-point operation is used here.
    /// </summary>
    public static class OrcaSolver
    {
        // Two raw Q16.16 units are enough to classify numerically parallel lines while
        // preserving the smallest useful geometric distinctions in the simulation.
        private static readonly FP ParallelEpsilon = FP.FromRaw(2);
        private static readonly FP NormalizationEpsilon = FP.FromRaw(2);
        private static readonly FP ObstacleCoverageEpsilon = FP.FromRaw(2);
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
            return Solve(
                agentStableId,
                FPVector2.Zero,
                currentVelocity,
                preferredVelocity,
                radius,
                maxSpeed,
                timeHorizon,
                timeStep,
                null,
                0,
                neighbors,
                neighborCount,
                lines,
                projectionLines,
                out _,
                out newVelocity);
        }

        /// <summary>
        /// Solves one agent against immutable static obstacle segments followed by Agent
        /// constraints. The active obstacle-neighbor prefix is distance-sorted in place.
        /// The returned count includes both kinds of lines; obstacleLineCount identifies
        /// the immutable prefix consumed by LP3.
        /// </summary>
        public static int Solve(
            int agentStableId,
            FPVector2 agentPosition,
            FPVector2 currentVelocity,
            FPVector2 preferredVelocity,
            FP radius,
            FP maxSpeed,
            FP timeHorizon,
            FP timeStep,
            ObstacleNeighbor[] obstacleNeighbors,
            int obstacleNeighborCount,
            AgentNeighbor[] neighbors,
            int neighborCount,
            OrcaLine[] lines,
            OrcaLine[] projectionLines,
            out int obstacleLineCount,
            out FPVector2 newVelocity)
        {
            ValidateBuffers(
                obstacleNeighbors,
                obstacleNeighborCount,
                neighbors,
                neighborCount,
                lines,
                projectionLines);

            obstacleLineCount = 0;

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

            PrepareObstacleNeighborOrder(agentPosition, obstacleNeighbors, obstacleNeighborCount);
            for (int i = 0; i < obstacleNeighborCount; i++)
            {
                ObstacleSegment segment = obstacleNeighbors[i].Segment;
                // CCW obstacle interiors lie on the left. RVO2 obstacle constraints
                // are one-sided and only the right/free side can be visible. Keep the
                // check here as part of the public solver contract even when the
                // broadphase collector has already removed back faces.
                if (FPMath.Det(
                    segment.Direction,
                    agentPosition - segment.Start) >= FP.Zero)
                {
                    continue;
                }

                FPVector2 relativeStart = segment.Start - agentPosition;
                FPVector2 relativeEnd = segment.End - agentPosition;

                if (IsObstacleAlreadyCovered(
                    relativeStart,
                    relativeEnd,
                    agentRadius,
                    inverseTimeHorizon,
                    lines,
                    lineCount))
                {
                    continue;
                }

                if (TryBuildObstacleLine(
                    currentVelocity,
                    agentRadius,
                    inverseTimeHorizon,
                    in segment,
                    relativeStart,
                    relativeEnd,
                    i,
                    out OrcaLine obstacleLine))
                {
                    lines[lineCount++] = obstacleLine;
                }
            }

            obstacleLineCount = lineCount;

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

                InsertLineSorted(lines, obstacleLineCount, ref lineCount, line);
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
                    obstacleLineCount,
                    failedLine,
                    maxSpeed,
                    projectionLines,
                    ref newVelocity);
            }

            newVelocity = ClampMagnitude(newVelocity, maxSpeed);
            return lineCount;
        }

        private static void PrepareObstacleNeighborOrder(
            FPVector2 agentPosition,
            ObstacleNeighbor[] obstacleNeighbors,
            int obstacleNeighborCount)
        {
            for (int i = 0; i < obstacleNeighborCount; i++)
            {
                ObstacleSegment segment = obstacleNeighbors[i].Segment;
                ulong distanceSquared = StaticObstacleSegmentBuilder.DistanceSquaredRaw(
                    agentPosition,
                    in segment);
                obstacleNeighbors[i] = new ObstacleNeighbor(segment, distanceSquared);
            }

            for (int i = 1; i < obstacleNeighborCount; i++)
            {
                ObstacleNeighbor candidate = obstacleNeighbors[i];
                int insert = i;
                while (insert > 0 && CompareObstacleNeighbor(candidate, obstacleNeighbors[insert - 1]) < 0)
                {
                    obstacleNeighbors[insert] = obstacleNeighbors[insert - 1];
                    insert--;
                }

                obstacleNeighbors[insert] = candidate;
            }
        }

        private static int CompareObstacleNeighbor(ObstacleNeighbor left, ObstacleNeighbor right)
        {
            if (left.DistanceSquared < right.DistanceSquared)
            {
                return -1;
            }

            if (left.DistanceSquared > right.DistanceSquared)
            {
                return 1;
            }

            if (left.Segment.ObstacleId != right.Segment.ObstacleId)
            {
                return left.Segment.ObstacleId < right.Segment.ObstacleId ? -1 : 1;
            }

            if (left.Segment.EdgeIndex != right.Segment.EdgeIndex)
            {
                return left.Segment.EdgeIndex < right.Segment.EdgeIndex ? -1 : 1;
            }

            return 0;
        }

        private static bool IsObstacleAlreadyCovered(
            FPVector2 relativeStart,
            FPVector2 relativeEnd,
            FP radius,
            FP inverseTimeHorizon,
            OrcaLine[] lines,
            int obstacleLineCount)
        {
            FP scaledRadius = radius * inverseTimeHorizon;
            FP threshold = -ObstacleCoverageEpsilon;

            for (int i = 0; i < obstacleLineCount; i++)
            {
                OrcaLine previous = lines[i];
                FP startClearance = FPMath.Det(
                    (relativeStart * inverseTimeHorizon) - previous.Point,
                    previous.Direction) - scaledRadius;
                FP endClearance = FPMath.Det(
                    (relativeEnd * inverseTimeHorizon) - previous.Point,
                    previous.Direction) - scaledRadius;

                if (startClearance >= threshold && endClearance >= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildObstacleLine(
            FPVector2 currentVelocity,
            FP radius,
            FP inverseTimeHorizon,
            in ObstacleSegment segment,
            FPVector2 relativeStart,
            FPVector2 relativeEnd,
            int sourceOrder,
            out OrcaLine line)
        {
            line = new OrcaLine
            {
                NeighborId = -1,
                SourceKind = OrcaLineSourceKind.StaticObstacle,
                SourceId = segment.StableId,
                SourceOrder = sourceOrder,
            };

            FP radiusSquared = radius * radius;
            FP distanceSquaredStart = relativeStart.SqrMagnitude;
            FP distanceSquaredEnd = relativeEnd.SqrMagnitude;
            FPVector2 obstacleVector = relativeEnd - relativeStart;
            FP obstacleLengthSquared = obstacleVector.SqrMagnitude;
            if (obstacleLengthSquared <= (NormalizationEpsilon * NormalizationEpsilon))
            {
                return false;
            }

            FP segmentProjection = FPMath.Dot(-relativeStart, obstacleVector) / obstacleLengthSquared;
            FPVector2 lineOffset = -relativeStart - (obstacleVector * segmentProjection);
            FP distanceSquaredLine = lineOffset.SqrMagnitude;

            if (segmentProjection < FP.Zero && distanceSquaredStart <= radiusSquared)
            {
                line.Point = FPVector2.Zero;
                line.Direction = NormalizeOrFallback(
                    FPMath.PerpendicularLeft(relativeStart),
                    -segment.Direction);
                return true;
            }

            if (segmentProjection > FP.One && distanceSquaredEnd <= radiusSquared)
            {
                if (FPMath.Det(relativeEnd, segment.NextDirection) < FP.Zero)
                {
                    return false;
                }

                line.Point = FPVector2.Zero;
                line.Direction = NormalizeOrFallback(
                    FPMath.PerpendicularLeft(relativeEnd),
                    -segment.NextDirection);
                return true;
            }

            if (segmentProjection >= FP.Zero &&
                segmentProjection <= FP.One &&
                distanceSquaredLine <= radiusSquared)
            {
                line.Point = FPVector2.Zero;
                line.Direction = -segment.Direction;
                return true;
            }

            FPVector2 activeStart = relativeStart;
            FPVector2 activeEnd = relativeEnd;
            FPVector2 activeEdgeDirection = segment.Direction;
            FPVector2 activePreviousDirection = segment.PreviousDirection;
            FPVector2 activeNextDirection = segment.NextDirection;
            FPVector2 leftLegDirection;
            FPVector2 rightLegDirection;
            bool singleVertex = false;

            if (segmentProjection < FP.Zero && distanceSquaredLine <= radiusSquared)
            {
                FP leg = FPMath.Sqrt(FPMath.Max(distanceSquaredStart - radiusSquared, FP.Zero));
                leftLegDirection = new FPVector2(
                    (relativeStart.X * leg) - (relativeStart.Y * radius),
                    (relativeStart.X * radius) + (relativeStart.Y * leg)) / distanceSquaredStart;
                rightLegDirection = new FPVector2(
                    (relativeStart.X * leg) + (relativeStart.Y * radius),
                    (-relativeStart.X * radius) + (relativeStart.Y * leg)) / distanceSquaredStart;
                activeEnd = activeStart;
                activeNextDirection = segment.Direction;
                singleVertex = true;
            }
            else if (segmentProjection > FP.One && distanceSquaredLine <= radiusSquared)
            {
                FP leg = FPMath.Sqrt(FPMath.Max(distanceSquaredEnd - radiusSquared, FP.Zero));
                leftLegDirection = new FPVector2(
                    (relativeEnd.X * leg) - (relativeEnd.Y * radius),
                    (relativeEnd.X * radius) + (relativeEnd.Y * leg)) / distanceSquaredEnd;
                rightLegDirection = new FPVector2(
                    (relativeEnd.X * leg) + (relativeEnd.Y * radius),
                    (-relativeEnd.X * radius) + (relativeEnd.Y * leg)) / distanceSquaredEnd;
                activeStart = activeEnd;
                activeEdgeDirection = segment.NextDirection;
                activePreviousDirection = segment.Direction;
                singleVertex = true;
            }
            else
            {
                FP leftLeg = FPMath.Sqrt(FPMath.Max(distanceSquaredStart - radiusSquared, FP.Zero));
                leftLegDirection = new FPVector2(
                    (relativeStart.X * leftLeg) - (relativeStart.Y * radius),
                    (relativeStart.X * radius) + (relativeStart.Y * leftLeg)) / distanceSquaredStart;

                FP rightLeg = FPMath.Sqrt(FPMath.Max(distanceSquaredEnd - radiusSquared, FP.Zero));
                rightLegDirection = new FPVector2(
                    (relativeEnd.X * rightLeg) + (relativeEnd.Y * radius),
                    (-relativeEnd.X * radius) + (relativeEnd.Y * rightLeg)) / distanceSquaredEnd;
            }

            leftLegDirection = NormalizeOrFallback(leftLegDirection, -activePreviousDirection);
            rightLegDirection = NormalizeOrFallback(rightLegDirection, activeNextDirection);

            bool leftLegIsForeign = false;
            bool rightLegIsForeign = false;
            if (FPMath.Det(leftLegDirection, -activePreviousDirection) >= FP.Zero)
            {
                leftLegDirection = -activePreviousDirection;
                leftLegIsForeign = true;
            }

            if (FPMath.Det(rightLegDirection, activeNextDirection) <= FP.Zero)
            {
                rightLegDirection = activeNextDirection;
                rightLegIsForeign = true;
            }

            FPVector2 leftCutoff = activeStart * inverseTimeHorizon;
            FPVector2 rightCutoff = activeEnd * inverseTimeHorizon;
            FPVector2 cutoffVector = rightCutoff - leftCutoff;
            FP cutoffLengthSquared = cutoffVector.SqrMagnitude;
            bool cutoffIsDegenerate = singleVertex ||
                cutoffLengthSquared <= (NormalizationEpsilon * NormalizationEpsilon);
            FP cutoffProjection = cutoffIsDegenerate
                ? FP.Half
                : FPMath.Dot(currentVelocity - leftCutoff, cutoffVector) / cutoffLengthSquared;
            FP leftProjection = FPMath.Dot(currentVelocity - leftCutoff, leftLegDirection);
            FP rightProjection = FPMath.Dot(currentVelocity - rightCutoff, rightLegDirection);

            if ((cutoffProjection < FP.Zero && leftProjection < FP.Zero) ||
                (singleVertex && leftProjection < FP.Zero && rightProjection < FP.Zero))
            {
                FPVector2 fallback = NormalizeOrFallback(
                    -activeStart,
                    FPMath.PerpendicularRight(activeEdgeDirection));
                FPVector2 unitW = NormalizeOrFallback(currentVelocity - leftCutoff, fallback);
                line.Direction = FPMath.PerpendicularRight(unitW);
                line.Point = leftCutoff + (unitW * (radius * inverseTimeHorizon));
                return true;
            }

            if (cutoffProjection > FP.One && rightProjection < FP.Zero)
            {
                FPVector2 fallback = NormalizeOrFallback(
                    -activeEnd,
                    FPMath.PerpendicularRight(activeEdgeDirection));
                FPVector2 unitW = NormalizeOrFallback(currentVelocity - rightCutoff, fallback);
                line.Direction = FPMath.PerpendicularRight(unitW);
                line.Point = rightCutoff + (unitW * (radius * inverseTimeHorizon));
                return true;
            }

            FP distanceSquaredCutoff = cutoffIsDegenerate ||
                cutoffProjection < FP.Zero ||
                cutoffProjection > FP.One
                ? FP.MaxValue
                : (currentVelocity - (leftCutoff + (cutoffVector * cutoffProjection))).SqrMagnitude;
            FP distanceSquaredLeft = leftProjection < FP.Zero
                ? FP.MaxValue
                : (currentVelocity - (leftCutoff + (leftLegDirection * leftProjection))).SqrMagnitude;
            FP distanceSquaredRight = rightProjection < FP.Zero
                ? FP.MaxValue
                : (currentVelocity - (rightCutoff + (rightLegDirection * rightProjection))).SqrMagnitude;

            FP scaledRadius = radius * inverseTimeHorizon;
            if (distanceSquaredCutoff <= distanceSquaredLeft &&
                distanceSquaredCutoff <= distanceSquaredRight)
            {
                line.Direction = -activeEdgeDirection;
                line.Point = leftCutoff + (FPMath.PerpendicularLeft(line.Direction) * scaledRadius);
                return true;
            }

            if (distanceSquaredLeft <= distanceSquaredRight)
            {
                if (leftLegIsForeign)
                {
                    return false;
                }

                line.Direction = leftLegDirection;
                line.Point = leftCutoff + (FPMath.PerpendicularLeft(line.Direction) * scaledRadius);
                return true;
            }

            if (rightLegIsForeign)
            {
                return false;
            }

            line.Direction = -rightLegDirection;
            line.Point = rightCutoff + (FPMath.PerpendicularLeft(line.Direction) * scaledRadius);
            return true;
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
                SourceKind = OrcaLineSourceKind.Agent,
                SourceId = neighbor.StableId,
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
                    projected.SourceKind = other.SourceKind;
                    projected.SourceId = other.SourceId;
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

        private static void InsertLineSorted(
            OrcaLine[] lines,
            int sortStart,
            ref int lineCount,
            OrcaLine line)
        {
            int insert = lineCount;

            while (insert > sortStart && CompareLineOrder(line, lines[insert - 1]) < 0)
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
            return FPMath.NormalizeSafe(value, fallback, NormalizationEpsilon);
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
            ObstacleNeighbor[] obstacleNeighbors,
            int obstacleNeighborCount,
            AgentNeighbor[] neighbors,
            int neighborCount,
            OrcaLine[] lines,
            OrcaLine[] projectionLines)
        {
            if (obstacleNeighborCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacleNeighborCount));
            }

            if (obstacleNeighbors == null)
            {
                if (obstacleNeighborCount != 0)
                {
                    throw new ArgumentNullException(nameof(obstacleNeighbors));
                }
            }
            else if (obstacleNeighborCount > obstacleNeighbors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacleNeighborCount));
            }

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

            long requiredLineCount = (long)obstacleNeighborCount + neighborCount;
            if (lines.Length < requiredLineCount)
            {
                throw new ArgumentException(
                    "Line buffer is smaller than obstacleNeighborCount + neighborCount.",
                    nameof(lines));
            }

            if (projectionLines.Length < requiredLineCount)
            {
                throw new ArgumentException(
                    "Projection-line buffer is smaller than obstacleNeighborCount + neighborCount.",
                    nameof(projectionLines));
            }

            if (requiredLineCount > 0 && ReferenceEquals(lines, projectionLines))
            {
                throw new ArgumentException("ORCA line and projection-line buffers must be different arrays.");
            }
        }
    }
}
