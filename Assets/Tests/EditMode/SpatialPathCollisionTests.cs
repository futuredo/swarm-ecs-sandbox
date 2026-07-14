using System.Collections.Generic;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Pathfinding;
using SwarmECS.Simulation.Spatial;

namespace SwarmECS.Tests
{
    public sealed class SpatialPathCollisionTests
    {
        [Test]
        public void UniformGridRadius_MatchesBruteForceAndUsesStableOrdering()
        {
            FPVector2[] positions =
            {
                Point(-2, 0),
                Point(-1, 0),
                Point(0, 0),
                Point(1, 0),
                Point(2, 0),
                Point(1, 1),
                Point(4, 4),
                Point(-1, -1)
            };

            var grid = new UniformGrid2D(positions.Length, FP.One);
            grid.Build(positions, positions.Length);
            int[] actual = new int[positions.Length];
            grid.QueryRadius(FPVector2.Zero, FP.FromInt(2), actual, out int actualCount);

            List<int> expected = BruteForceRadius(positions, FPVector2.Zero, FP.FromInt(2));
            Assert.That(actualCount, Is.EqualTo(expected.Count));
            for (int i = 0; i < actualCount; ++i)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]), "Mismatch at stable result slot " + i);
            }
        }

        [Test]
        public void UniformGridRadius_WhenResultIsTruncated_ReturnsNearestTopK()
        {
            FPVector2[] positions =
            {
                Point(1, 0),
                Point(-1, 0),
                Point(0, 1),
                Point(0, -1),
                Point(2, 0),
                Point(3, 0),
            };

            var grid = new UniformGrid2D(positions.Length, FP.One);
            grid.Build(positions, positions.Length);
            int[] nearest = new int[3];
            grid.QueryRadius(FPVector2.Zero, FP.FromInt(4), nearest, out int count);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(nearest, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void KdTreeRadius_MatchesBruteForceAcrossDeterministicPointCloud()
        {
            const int pointCount = 97;
            FPVector2[] positions = new FPVector2[pointCount];
            uint state = 0x12345678u;
            for (int i = 0; i < positions.Length; ++i)
            {
                state = state * 1664525u + 1013904223u;
                int xQuarter = (int)((state >> 8) % 161u) - 80;
                state = state * 1664525u + 1013904223u;
                int yQuarter = (int)((state >> 8) % 161u) - 80;
                positions[i] = new FPVector2(FP.FromRatio(xQuarter, 4), FP.FromRatio(yQuarter, 4));
            }

            var tree = new DataOrientedKdTree2D(pointCount);
            tree.Build(positions, positions.Length);
            FPVector2 center = new FPVector2(FP.FromRatio(3, 2), FP.FromRatio(-5, 4));
            FP radius = FP.FromRatio(29, 4);
            int[] actual = new int[pointCount];
            tree.QueryRadius(center, radius, actual, out int actualCount);

            List<int> expected = BruteForceRadius(positions, center, radius);
            Assert.That(actualCount, Is.EqualTo(expected.Count));
            for (int i = 0; i < actualCount; ++i)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]), "Mismatch at kd-tree result slot " + i);
            }
        }

        [Test]
        public void KdTreeKNearest_OrdersEqualDistancesByEntityId()
        {
            FPVector2[] positions =
            {
                Point(1, 0),
                Point(-1, 0),
                Point(0, 1),
                Point(0, -1),
                Point(2, 0),
                Point(3, 0)
            };

            var tree = new DataOrientedKdTree2D(positions.Length);
            tree.Build(positions, positions.Length);
            int[] nearest = new int[3];
            tree.QueryKNearest(FPVector2.Zero, 3, nearest, out int count);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(nearest, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void AStar_RoutesThroughWallGapAndNeverUsesBlockedCells()
        {
            var map = new GridMap(5, 5);
            for (int y = 0; y < 4; ++y)
            {
                map.SetWalkable(2, y, false);
            }

            var pathfinder = new AStarPathfinder(map);
            int[] path = new int[map.NodeCount];
            bool found = pathfinder.FindPath(0, 0, 4, 0, path, out int count);

            Assert.That(found, Is.True);
            Assert.That(count, Is.GreaterThan(0));
            bool usedGap = false;
            int previousX = -1;
            int previousY = -1;
            for (int i = 0; i < count; ++i)
            {
                map.IndexToCoordinates(path[i], out int x, out int y);
                Assert.That(map.IsWalkable(x, y), Is.True);
                if (x == 2 && y == 4)
                {
                    usedGap = true;
                }

                if (i > 0)
                {
                    int deltaX = x > previousX ? x - previousX : previousX - x;
                    int deltaY = y > previousY ? y - previousY : previousY - y;
                    Assert.That(deltaX, Is.LessThanOrEqualTo(1));
                    Assert.That(deltaY, Is.LessThanOrEqualTo(1));
                }

                previousX = x;
                previousY = y;
            }

            Assert.That(usedGap, Is.True);
        }

        [Test]
        public void AStar_DoesNotCutBlockedDiagonalCorner()
        {
            var map = new GridMap(2, 2);
            map.SetWalkable(1, 0, false);
            map.SetWalkable(0, 1, false);
            var pathfinder = new AStarPathfinder(map);
            int[] path = new int[4];

            Assert.That(pathfinder.FindPath(0, 0, 1, 1, path, out int count), Is.False);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void SharedPath_IsReusableUntilGridRevisionChanges()
        {
            var map = new GridMap(4, 4);
            var pathfinder = new AStarPathfinder(map);
            var shared = new SharedPath(map.NodeCount);
            int start = map.ToIndex(0, 0);
            int goal = map.ToIndex(3, 3);

            Assert.That(pathfinder.FindSharedPath(start, goal, shared), Is.True);
            Assert.That(shared.IsReusableFor(map, start, goal), Is.True);
            Assert.That(shared.Count, Is.GreaterThan(0));

            map.SetPenalty(1, 1, 50);
            Assert.That(shared.IsReusableFor(map, start, goal), Is.False);
            Assert.That(pathfinder.FindSharedPath(start, goal, shared), Is.True);
            Assert.That(shared.MapRevision, Is.EqualTo(map.Revision));
        }

        [Test]
        public void Sat_SeparatesDistantBoxes()
        {
            var left = new FPOrientedBox2(Point(0, 0), Point(1, 1));
            var right = new FPOrientedBox2(Point(3, 0), Point(1, 1));

            Assert.That(FPSat2D.Intersect(in left, in right, out FPVector2 normal, out FP depth), Is.False);
            Assert.That(normal, Is.EqualTo(FPVector2.Zero));
            Assert.That(depth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void Sat_ReturnsMinimumPenetrationForOverlappingBoxes()
        {
            var left = new FPOrientedBox2(Point(0, 0), Point(1, 1));
            var right = new FPOrientedBox2(new FPVector2(FP.FromRatio(3, 2), FP.Zero), Point(1, 1));

            Assert.That(FPSat2D.Intersect(in left, in right, out FPVector2 normal, out FP depth), Is.True);
            Assert.That(normal, Is.EqualTo(FPVector2.UnitX));
            Assert.That(depth, Is.EqualTo(FP.Half));
        }

        [Test]
        public void CircleVsBox_ReturnsOutwardNormalAndDepth()
        {
            var box = new FPOrientedBox2(Point(0, 0), Point(1, 1));
            var circle = new FPCircle2(new FPVector2(FP.FromRatio(3, 2), FP.Zero), FP.One);

            Assert.That(FPSat2D.Intersect(in box, in circle, out FPVector2 normal, out FP depth), Is.True);
            Assert.That(normal, Is.EqualTo(FPVector2.UnitX));
            Assert.That(depth, Is.EqualTo(FP.Half));
        }

        private static FPVector2 Point(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }

        private static List<int> BruteForceRadius(FPVector2[] positions, FPVector2 center, FP radius)
        {
            FP radiusSquared = radius * radius;
            var result = new List<int>();
            for (int i = 0; i < positions.Length; ++i)
            {
                if ((positions[i] - center).SqrMagnitude <= radiusSquared)
                {
                    result.Add(i);
                }
            }

            result.Sort((left, right) =>
            {
                FP leftDistance = (positions[left] - center).SqrMagnitude;
                FP rightDistance = (positions[right] - center).SqrMagnitude;
                int distanceComparison = leftDistance.CompareTo(rightDistance);
                return distanceComparison != 0 ? distanceComparison : left.CompareTo(right);
            });
            return result;
        }
    }
}
