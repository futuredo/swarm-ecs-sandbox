using System;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Spatial;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class StaticObstacleBroadphaseCcdTests
    {
        [Test]
        public void OrientedBox_ExplicitAxesAlwaysProduceRightHandedOrthogonalBasis()
        {
            var box = new FPOrientedBox2(
                FPVector2.Zero,
                V(2, 3),
                V(3, 4),
                V(5, -2));

            Assert.That(FPMath.Dot(box.AxisX, box.AxisY), Is.EqualTo(FP.Zero));
            Assert.That(box.AxisX.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(box.AxisY.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(FPMath.Det(box.AxisX, box.AxisY), Is.EqualTo(FP.One));

            var recoveredFromY = new FPOrientedBox2(
                FPVector2.Zero,
                V(1, 1),
                FPVector2.Zero,
                FPVector2.UnitY);
            Assert.That(recoveredFromY.AxisX, Is.EqualTo(FPVector2.UnitX));
            Assert.That(recoveredFromY.AxisY, Is.EqualTo(FPVector2.UnitY));
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(3, 4)]
        [TestCase(5, 12)]
        [TestCase(-7, 11)]
        [TestCase(1, 32767)]
        public void OrientedBox_RepresentativeDirectionsProduceExactQ16UnitBasis(int x, int y)
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(2, 3), V(x, y));

            Assert.That(box.AxisX.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(box.AxisY.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(FPMath.Dot(box.AxisX, box.AxisY), Is.EqualTo(FP.Zero));
            Assert.That(FPMath.Det(box.AxisX, box.AxisY), Is.EqualTo(FP.One));
        }

        [Test]
        public void OrientedBox_QuantizedBasisIsExactAndAabbContainsItsVertices()
        {
            var box = new FPOrientedBox2(
                V(3, -2),
                V(14, 9),
                V(1, 4));

            Assert.That(box.AxisX.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(box.AxisY.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(FPMath.Dot(box.AxisX, box.AxisY), Is.EqualTo(FP.Zero));

            FPVector2 extentX = box.AxisX * box.HalfExtents.X;
            FPVector2 extentY = box.AxisY * box.HalfExtents.Y;
            FPVector2[] vertices =
            {
                box.Center - extentX - extentY,
                box.Center + extentX - extentY,
                box.Center + extentX + extentY,
                box.Center - extentX + extentY,
            };
            FPAabb2 worldBounds = FPAabb2.FromOrientedBox(in box);

            for (int i = 0; i < vertices.Length; ++i)
            {
                Assert.That(worldBounds.Contains(vertices[i]), Is.True);
            }
        }

        [Test]
        public void RotatedObb_BroadphaseContainsQuantizedCornerCcdHit()
        {
            var box = new FPOrientedBox2(
                FPVector2.Zero,
                FPVector2.One,
                V(1, 1));
            var circle = new FPCircle2(
                new FPVector2(FP.FromRaw(92683), FP.FromRaw(-1)),
                FP.Zero);
            FPVector2 displacement = new(FP.Zero, FP.FromRaw(-2));

            Assert.That(box.AxisX.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(
                    in circle,
                    displacement,
                    in box,
                    out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Fraction, Is.EqualTo(FP.Zero));

            FPAabb2 sweptBounds = FPAabb2.FromSegment(
                circle.Center,
                circle.Center + displacement,
                circle.Radius);
            FPAabb2 obstacleBounds = FPAabb2.FromOrientedBox(in box);
            Assert.That(obstacleBounds.Overlaps(in sweptBounds), Is.True);

            var bvh = new StaticObstacleBvh2D(new[] { box });
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            bvh.QueryAabb(in sweptBounds, scratch, out int count);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(scratch.ObstacleIds[0], Is.Zero);
        }

        [Test]
        public void RotatedObb_BroadphaseContainsQuantizedCornerSatHit()
        {
            var box = new FPOrientedBox2(
                FPVector2.Zero,
                FPVector2.One,
                V(1, 1));
            var circle = new FPCircle2(
                new FPVector2(FP.FromRaw(92683), FP.FromRaw(-1)),
                FP.Zero);

            Assert.That(FPSat2D.Intersect(in box, in circle, out _, out _), Is.True);

            FPAabb2 circleBounds = new FPAabb2(circle.Center, circle.Center);
            FPAabb2 obstacleBounds = FPAabb2.FromOrientedBox(in box);
            Assert.That(obstacleBounds.Overlaps(in circleBounds), Is.True);

            var bvh = new StaticObstacleBvh2D(new[] { box });
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            bvh.QueryAabb(in circleBounds, scratch, out int count);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(scratch.ObstacleIds[0], Is.Zero);
        }

        [Test]
        public void CircleSat_UsesExactRawCornerDistanceBelowOneFixedPointUnit()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, FPVector2.One);
            var falsePositiveUnderTruncatedSquares = new FPCircle2(
                new FPVector2(
                    FP.FromRaw(FP.OneRaw + 200),
                    FP.FromRaw(FP.OneRaw + 200)),
                FP.Epsilon);
            var oneRawFaceContact = new FPCircle2(
                new FPVector2(FP.FromRaw(FP.OneRaw + 1), FP.One),
                FP.Epsilon);

            Assert.That(
                FPSat2D.Intersect(in box, in falsePositiveUnderTruncatedSquares, out _, out _),
                Is.False);
            Assert.That(
                FPSat2D.Intersect(in box, in oneRawFaceContact, out FPVector2 normal, out FP depth),
                Is.True);
            Assert.That(normal, Is.EqualTo(FPVector2.UnitX));
            Assert.That(depth, Is.EqualTo(FP.Zero));

            FPAabb2 contactBounds = new FPAabb2(
                oneRawFaceContact.Center,
                oneRawFaceContact.Center).Expanded(oneRawFaceContact.Radius);
            var bvh = new StaticObstacleBvh2D(new[] { box });
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            bvh.QueryAabb(in contactBounds, scratch, out int count);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(scratch.ObstacleIds[0], Is.Zero);
        }

        [Test]
        public void AabbFromRotatedObb_RoundsEveryRawExtentOutward()
        {
            var box = new FPOrientedBox2(
                FPVector2.Zero,
                new FPVector2(FP.Epsilon, FP.Epsilon),
                V(3, 4));

            FPAabb2 bounds = FPAabb2.FromOrientedBox(in box);

            Assert.That(bounds.Min.X.Raw, Is.EqualTo(-5));
            Assert.That(bounds.Max.X.Raw, Is.EqualTo(5));
            Assert.That(bounds.Min.Y.Raw, Is.EqualTo(-5));
            Assert.That(bounds.Max.Y.Raw, Is.EqualTo(5));
        }

        [Test]
        public void BvhQuery_DefaultAndGeneratedQueriesMatchBruteForceInStableIdOrder()
        {
            FPOrientedBox2[] obstacles = BuildObstacles();
            var bvh = new StaticObstacleBvh2D(obstacles);
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            int[] expected = new int[obstacles.Length];

            for (int queryIndex = 0; queryIndex < 96; ++queryIndex)
            {
                int x = ((queryIndex * 17) % 71) - 35;
                int y = ((queryIndex * 29) % 67) - 33;
                FP halfX = FP.FromRatio((queryIndex % 5) + 1, 2);
                FP halfY = FP.FromRatio((queryIndex % 7) + 1, 3);
                FPAabb2 query = new FPAabb2(
                    new FPVector2(FP.FromInt(x) - halfX, FP.FromInt(y) - halfY),
                    new FPVector2(FP.FromInt(x) + halfX, FP.FromInt(y) + halfY));

                int expectedCount = BruteForce(bvh, in query, expected);
                bvh.QueryAabb(in query, scratch, out int actualCount);

                Assert.That(actualCount, Is.EqualTo(expectedCount), "Count mismatch for query " + queryIndex);
                for (int i = 0; i < expectedCount; ++i)
                {
                    Assert.That(scratch.ObstacleIds[i], Is.EqualTo(expected[i]), "Id mismatch for query " + queryIndex);
                }
            }
        }

        [Test]
        public void BvhQuery_IncludesTouchingBoundsAndScratchInstancesAreIndependent()
        {
            FPOrientedBox2[] obstacles =
            {
                new FPOrientedBox2(FPVector2.Zero, V(1, 1)),
                new FPOrientedBox2(V(4, 0), V(1, 1)),
            };
            var bvh = new StaticObstacleBvh2D(obstacles);
            StaticObstacleQueryScratch leftScratch = bvh.CreateScratch();
            StaticObstacleQueryScratch rightScratch = bvh.CreateScratch();
            FPAabb2 touchingLeft = new FPAabb2(V(1, -1), V(2, 1));
            FPAabb2 touchingRight = new FPAabb2(V(3, -1), V(3, 1));

            bvh.QueryAabb(in touchingLeft, leftScratch, out int leftCount);
            bvh.QueryAabb(in touchingRight, rightScratch, out int rightCount);

            Assert.That(leftCount, Is.EqualTo(1));
            Assert.That(leftScratch.ObstacleIds[0], Is.EqualTo(0));
            Assert.That(rightCount, Is.EqualTo(1));
            Assert.That(rightScratch.ObstacleIds[0], Is.EqualTo(1));
            Assert.That(leftScratch.ObstacleIds[0], Is.EqualTo(0));
        }

        [Test]
        public void Bvh_EmptySetReturnsNoCandidates()
        {
            var bvh = new StaticObstacleBvh2D(Array.Empty<FPOrientedBox2>());
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            FPAabb2 query = new FPAabb2(V(-1, -1), V(1, 1));

            bvh.QueryAabb(in query, scratch, out int count);

            Assert.That(bvh.NodeCount, Is.Zero);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void Bvh_SnapshotsBoundsAndIsUnaffectedBySourceArrayMutation()
        {
            FPOrientedBox2[] obstacles =
            {
                new FPOrientedBox2(FPVector2.Zero, V(1, 1)),
            };
            var bvh = new StaticObstacleBvh2D(obstacles);
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            FPAabb2 originalQuery = new FPAabb2(V(-1, -1), V(1, 1));
            obstacles[0] = new FPOrientedBox2(V(20, 20), V(1, 1));

            bvh.QueryAabb(in originalQuery, scratch, out int count);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(scratch.ObstacleIds[0], Is.Zero);
        }

        [Test]
        public void BvhQuery_WarmPathAllocatesZeroManagedBytes()
        {
            FPOrientedBox2[] obstacles = BuildObstacles();
            var bvh = new StaticObstacleBvh2D(obstacles);
            StaticObstacleQueryScratch scratch = bvh.CreateScratch();
            FPAabb2 query = new FPAabb2(V(-8, -8), V(8, 8));
            bvh.QueryAabb(in query, scratch, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; ++i)
            {
                bvh.QueryAabb(in query, scratch, out _);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [Test]
        public void ConservativeSweep_HighSpeedCrossingReturnsFirstExpandedFace()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var circle = new FPCircle2(V(-4, 0), FP.Half);

            bool found = FPSweptCircle2D.SweepAgainstBox(
                in circle,
                V(8, 0),
                in box,
                out FPSweepHit2D hit);

            Assert.That(found, Is.True);
            Assert.That(hit.Fraction, Is.EqualTo(FP.FromRatio(5, 16)));
            Assert.That(hit.Normal, Is.EqualTo(-FPVector2.UnitX));
            Assert.That(hit.FeatureId, Is.EqualTo(0));
        }

        [Test]
        public void ConservativeSweep_NonDivisibleEntryUsesFloorFraction()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var circle = new FPCircle2(V(-3, 0), FP.Zero);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in circle, V(3, 0), in box, out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Fraction.Raw, Is.EqualTo((2 * FP.OneRaw) / 3));
        }

        [Test]
        public void ConservativeSweep_ParallelOutsideAndZeroMotionDoNotHit()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var outside = new FPCircle2(V(-3, 3), FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in outside, V(6, 0), in box, out _),
                Is.False);
            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in outside, FPVector2.Zero, in box, out _),
                Is.False);
        }

        [Test]
        public void ConservativeSweep_AtExpandedBoundaryOnlyInwardMotionIsBlocked()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var touching = new FPCircle2(new FPVector2(FP.FromRatio(-3, 2), FP.Zero), FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in touching, V(1, 0), in box, out FPSweepHit2D inward),
                Is.True);
            Assert.That(inward.Fraction, Is.EqualTo(FP.Zero));
            Assert.That(inward.Normal, Is.EqualTo(-FPVector2.UnitX));
            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in touching, V(-1, 0), in box, out _),
                Is.False);
            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in touching, V(0, 1), in box, out _),
                Is.False);
        }

        [Test]
        public void ConservativeSweep_InsideSquareCornerBlocksEntryAlongEitherAxis()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            FP cornerCoordinate = FP.FromRatio(-7, 5);
            var circle = new FPCircle2(
                new FPVector2(cornerCoordinate, cornerCoordinate),
                FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(
                    in circle,
                    V(0, 1),
                    in box,
                    out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Fraction, Is.EqualTo(FP.Zero));
            Assert.That(hit.FeatureId, Is.EqualTo(2));
            Assert.That(hit.Normal, Is.EqualTo(-FPVector2.UnitY));
        }

        [Test]
        public void ConservativeSweep_InsideUpperCornerCannotCrossBoxAlongOtherAxis()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var circle = new FPCircle2(
                new FPVector2(FP.FromRatio(7, 5), FP.FromRatio(149, 100)),
                FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(
                    in circle,
                    V(-4, 0),
                    in box,
                    out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Fraction, Is.EqualTo(FP.Zero));
            Assert.That(hit.FeatureId, Is.EqualTo(1));
            Assert.That(hit.Normal, Is.EqualTo(FPVector2.UnitX));
        }

        [Test]
        public void ConservativeSweep_CornerEntryUsesStableXFeatureTieBreak()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1));
            var circle = new FPCircle2(V(-3, -3), FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(in circle, V(4, 4), in box, out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Fraction, Is.EqualTo(FP.FromRatio(3, 8)));
            Assert.That(hit.FeatureId, Is.EqualTo(0));
            Assert.That(hit.Normal, Is.EqualTo(-FPVector2.UnitX));
        }

        [Test]
        public void ConservativeSweep_RotatedBoxReturnsItsOutwardFaceNormal()
        {
            var box = new FPOrientedBox2(FPVector2.Zero, V(1, 1), V(3, 4));
            FPVector2 start = -box.AxisX * FP.FromInt(4);
            var circle = new FPCircle2(start, FP.Half);

            Assert.That(
                FPSweptCircle2D.SweepAgainstBox(
                    in circle,
                    box.AxisX * FP.FromInt(8),
                    in box,
                    out FPSweepHit2D hit),
                Is.True);
            Assert.That(hit.Normal, Is.EqualTo(-box.AxisX));
            Assert.That(hit.FeatureId, Is.EqualTo(0));
            Assert.That(hit.Fraction.Raw, Is.InRange(FP.FromRatio(3, 10).Raw, FP.FromRatio(1, 3).Raw));
        }

        private static FPOrientedBox2[] BuildObstacles()
        {
            const int generatedCount = 32;
            var obstacles = new FPOrientedBox2[StaticObstacleLayout.DefaultObstacleCount + generatedCount];
            StaticObstacleLayout.FillDefault(obstacles);
            for (int i = 0; i < generatedCount; ++i)
            {
                int x = ((i * 13) % 59) - 29;
                int y = ((i * 19) % 61) - 30;
                FPVector2 axis = (i & 1) == 0 ? V(3, 4) : V(5, 12);
                obstacles[StaticObstacleLayout.DefaultObstacleCount + i] = new FPOrientedBox2(
                    V(x, y),
                    new FPVector2(FP.FromRatio((i % 4) + 1, 2), FP.FromRatio((i % 5) + 1, 3)),
                    axis);
            }

            return obstacles;
        }

        private static int BruteForce(
            StaticObstacleBvh2D bvh,
            in FPAabb2 query,
            int[] expected)
        {
            int count = 0;
            for (int obstacleId = 0; obstacleId < bvh.ObstacleCount; ++obstacleId)
            {
                FPAabb2 bounds = bvh.GetObstacleBounds(obstacleId);
                if (bounds.Overlaps(in query))
                {
                    expected[count++] = obstacleId;
                }
            }

            return count;
        }

        private static FPVector2 V(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }
    }
}
