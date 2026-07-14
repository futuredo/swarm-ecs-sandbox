using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Spatial;

namespace SwarmECS.Simulation.Avoidance
{
    /// <summary>
    /// One counter-clockwise edge of an immutable convex static obstacle. The solid
    /// obstacle is on the left side of Direction. PreviousDirection and NextDirection
    /// describe the two adjacent edges at Start and End respectively.
    /// </summary>
    public readonly struct ObstacleSegment
    {
        internal ObstacleSegment(
            int obstacleId,
            int edgeIndex,
            FPVector2 start,
            FPVector2 end,
            FPVector2 direction,
            FPVector2 previousDirection,
            FPVector2 nextDirection)
        {
            ObstacleId = obstacleId;
            EdgeIndex = edgeIndex;
            StableId = checked((obstacleId * StaticObstacleSegmentBuilder.EdgesPerBox) + edgeIndex);
            Start = start;
            End = end;
            Direction = direction;
            PreviousDirection = previousDirection;
            NextDirection = nextDirection;
        }

        public int ObstacleId { get; }

        public int EdgeIndex { get; }

        public int StableId { get; }

        public FPVector2 Start { get; }

        public FPVector2 End { get; }

        public FPVector2 Direction { get; }

        public FPVector2 PreviousDirection { get; }

        public FPVector2 NextDirection { get; }
    }

    /// <summary>
    /// Per-query obstacle scratch. OrcaSolver refreshes DistanceSquared and sorts the
    /// active prefix in place, so broadphase traversal order cannot affect line order.
    /// </summary>
    public readonly struct ObstacleNeighbor
    {
        public ObstacleNeighbor(ObstacleSegment segment)
            : this(segment, 0UL)
        {
        }

        internal ObstacleNeighbor(ObstacleSegment segment, ulong distanceSquared)
        {
            Segment = segment;
            DistanceSquared = distanceSquared;
        }

        public ObstacleSegment Segment { get; }

        public ulong DistanceSquared { get; }
    }

    /// <summary>
    /// Allocation-free preprocessing for the four directed edges of each static OBB.
    /// OBB array order and local edge order form the stable obstacle identity.
    /// </summary>
    public static class StaticObstacleSegmentBuilder
    {
        public const int EdgesPerBox = 4;

        public static int RequiredCapacity(int obstacleCount)
        {
            if (obstacleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacleCount));
            }

            return checked(obstacleCount * EdgesPerBox);
        }

        public static int Fill(
            FPOrientedBox2[] obstacles,
            int obstacleCount,
            ObstacleSegment[] destination)
        {
            if (obstacles == null)
            {
                throw new ArgumentNullException(nameof(obstacles));
            }

            if (obstacleCount < 0 || obstacleCount > obstacles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacleCount));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            int required = RequiredCapacity(obstacleCount);
            if (destination.Length < required)
            {
                throw new ArgumentException("Obstacle segment destination is too small.", nameof(destination));
            }

            int write = 0;
            for (int obstacleId = 0; obstacleId < obstacleCount; obstacleId++)
            {
                FPOrientedBox2 box = obstacles[obstacleId];
                if (box.HalfExtents.X <= FP.Zero || box.HalfExtents.Y <= FP.Zero)
                {
                    throw new ArgumentException("Static obstacle OBBs must have two positive half extents.", nameof(obstacles));
                }

                if (FPMath.Det(box.AxisX, box.AxisY) <= FP.Zero)
                {
                    throw new ArgumentException("Static obstacle OBB axes must form a right-handed basis.", nameof(obstacles));
                }

                FPVector2 extentX = box.AxisX * box.HalfExtents.X;
                FPVector2 extentY = box.AxisY * box.HalfExtents.Y;
                FPVector2 corner0 = box.Center - extentX - extentY;
                FPVector2 corner1 = box.Center + extentX - extentY;
                FPVector2 corner2 = box.Center + extentX + extentY;
                FPVector2 corner3 = box.Center - extentX + extentY;

                if (corner0 == corner1 || corner1 == corner2 ||
                    corner2 == corner3 || corner3 == corner0)
                {
                    throw new ArgumentException(
                        "Static obstacle OBB produced a degenerate fixed-point edge.",
                        nameof(obstacles));
                }

                // FPOrientedBox2 guarantees an exact Q16.16 orthonormal basis. Reuse
                // it directly so SAT, CCD and obstacle ORCA share identical normals.
                FPVector2 direction0 = box.AxisX;
                FPVector2 direction1 = box.AxisY;
                FPVector2 direction2 = -box.AxisX;
                FPVector2 direction3 = -box.AxisY;

                destination[write++] = new ObstacleSegment(
                    obstacleId, 0, corner0, corner1, direction0, direction3, direction1);
                destination[write++] = new ObstacleSegment(
                    obstacleId, 1, corner1, corner2, direction1, direction0, direction2);
                destination[write++] = new ObstacleSegment(
                    obstacleId, 2, corner2, corner3, direction2, direction1, direction3);
                destination[write++] = new ObstacleSegment(
                    obstacleId, 3, corner3, corner0, direction3, direction2, direction0);
            }

            return write;
        }

        public static ulong DistanceSquaredRaw(FPVector2 point, in ObstacleSegment segment)
        {
            FPVector2 edge = segment.End - segment.Start;
            FP edgeLengthSquared = edge.SqrMagnitude;
            if (edgeLengthSquared <= FP.Zero)
            {
                return SpatialQueryDistance.Squared(point, segment.Start);
            }

            FP projection = FPMath.Dot(point - segment.Start, edge);
            if (projection <= FP.Zero)
            {
                return SpatialQueryDistance.Squared(point, segment.Start);
            }

            if (projection >= edgeLengthSquared)
            {
                return SpatialQueryDistance.Squared(point, segment.End);
            }

            FPVector2 closest = segment.Start + (edge * (projection / edgeLengthSquared));
            return SpatialQueryDistance.Squared(point, closest);
        }
    }
}
