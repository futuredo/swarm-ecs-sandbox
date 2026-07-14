using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;
using SwarmECS.Simulation.Collision;

namespace SwarmECS.Tests.EditMode
{
    public sealed class StaticObstacleOrcaTests
    {
        private static readonly FP AgentRadius = FP.FromRatio(1, 2);
        private static readonly FP MaxSpeed = FP.FromInt(3);
        private static readonly FP TimeHorizon = FP.FromInt(2);
        private static readonly FP TimeStep = FP.FromRatio(1, 10);

        [Test]
        public void SegmentBuilder_WritesCounterClockwiseLinkedStableEdges()
        {
            FPOrientedBox2[] boxes =
            {
                new FPOrientedBox2(V(3, -2), V(4, 1), new FPVector2(FP.FromRatio(4, 5), FP.FromRatio(3, 5)))
            };
            ObstacleSegment[] segments = new ObstacleSegment[4];

            int count = StaticObstacleSegmentBuilder.Fill(boxes, boxes.Length, segments);

            Assert.That(count, Is.EqualTo(4));
            for (int i = 0; i < count; i++)
            {
                ObstacleSegment segment = segments[i];
                Assert.That(segment.ObstacleId, Is.Zero);
                Assert.That(segment.EdgeIndex, Is.EqualTo(i));
                Assert.That(segment.StableId, Is.EqualTo(i));
                Assert.That(
                    FPMath.Det(segment.Direction, boxes[0].Center - segment.Start),
                    Is.GreaterThan(FP.Zero),
                    "The filled OBB must remain on the left side of every directed edge.");

                ObstacleSegment previous = segments[(i + 3) & 3];
                ObstacleSegment next = segments[(i + 1) & 3];
                AssertVectorRaw(segment.PreviousDirection, previous.Direction);
                AssertVectorRaw(segment.NextDirection, next.Direction);
            }
        }

        [Test]
        public void Solve_SingleWall_ProducesObstacleConstraintAndRejectsUnsafeVelocity()
        {
            FPVector2 position = FPVector2.Zero;
            ObstacleNeighbor[] obstacles = CreateBoxNeighbors(
                new FPOrientedBox2(V(0, 2), V(4, 1)));
            OrcaLine[] lines = new OrcaLine[obstacles.Length];
            OrcaLine[] projections = new OrcaLine[obstacles.Length];

            int lineCount = Solve(
                position,
                FPVector2.Zero,
                V(0, 2),
                obstacles,
                null,
                lines,
                projections,
                out int obstacleLineCount,
                out FPVector2 velocity);

            Assert.That(obstacleLineCount, Is.EqualTo(1));
            Assert.That(lineCount, Is.EqualTo(obstacleLineCount));
            Assert.That(lines[0].SourceId, Is.EqualTo(0));
            Assert.That(velocity.Y, Is.LessThan(FP.FromInt(2)));
            AssertObstaclePrefix(lines, obstacleLineCount);
            AssertSatisfiesLines(lines, lineCount, velocity);
        }

        [Test]
        public void Solve_ParallelToWall_PreservesSafeTangentialVelocity()
        {
            FPVector2 position = FPVector2.Zero;
            ObstacleNeighbor[] obstacles = CreateBoxNeighbors(
                new FPOrientedBox2(V(0, 2), V(4, 1)));
            OrcaLine[] lines = new OrcaLine[obstacles.Length];
            OrcaLine[] projections = new OrcaLine[obstacles.Length];

            Solve(
                position,
                V(2, 0),
                V(2, 0),
                obstacles,
                null,
                lines,
                projections,
                out int obstacleLineCount,
                out FPVector2 velocity);

            Assert.That(obstacleLineCount, Is.GreaterThan(0));
            Assert.That(FPMath.Abs(velocity.X - FP.FromInt(2)).Raw, Is.LessThanOrEqualTo(4));
            Assert.That(FPMath.Abs(velocity.Y).Raw, Is.LessThanOrEqualTo(4));
            AssertSatisfiesLines(lines, obstacleLineCount, velocity);
        }

        [Test]
        public void Solve_ConvexCorner_UsesObstacleLegInsteadOfKeepingDiagonalImpactVelocity()
        {
            FPVector2 position = FPVector2.Zero;
            ObstacleNeighbor[] obstacles = CreateBoxNeighbors(
                new FPOrientedBox2(V(2, 2), V(1, 1)));
            OrcaLine[] lines = new OrcaLine[obstacles.Length];
            OrcaLine[] projections = new OrcaLine[obstacles.Length];
            FPVector2 preferred = V(2, 2);

            int lineCount = Solve(
                position,
                preferred,
                preferred,
                obstacles,
                null,
                lines,
                projections,
                out int obstacleLineCount,
                out FPVector2 velocity);

            Assert.That(obstacleLineCount, Is.GreaterThan(0));
            Assert.That(velocity, Is.Not.EqualTo(preferred));
            Assert.That(FPMath.Dot(velocity, FPVector2.One), Is.LessThan(FPMath.Dot(preferred, FPVector2.One)));
            AssertObstaclePrefix(lines, obstacleLineCount);
            AssertSatisfiesLines(lines, lineCount, velocity);
        }

        [Test]
        public void Solve_ShuffledObstacleNeighbors_ProducesBitIdenticalStableOrder()
        {
            FPVector2 position = FPVector2.Zero;
            ObstacleNeighbor[] forward = CreateBoxNeighbors(
                new FPOrientedBox2(V(0, 2), V(4, 1)));
            ObstacleNeighbor[] reverse = new ObstacleNeighbor[forward.Length];
            for (int i = 0; i < forward.Length; i++)
            {
                reverse[i] = forward[forward.Length - 1 - i];
            }

            OrcaLine[] linesA = new OrcaLine[forward.Length];
            OrcaLine[] projectionsA = new OrcaLine[forward.Length];
            OrcaLine[] linesB = new OrcaLine[reverse.Length];
            OrcaLine[] projectionsB = new OrcaLine[reverse.Length];

            int countA = Solve(
                position,
                FPVector2.Zero,
                V(0, 2),
                forward,
                null,
                linesA,
                projectionsA,
                out int obstacleCountA,
                out FPVector2 velocityA);
            int countB = Solve(
                position,
                FPVector2.Zero,
                V(0, 2),
                reverse,
                null,
                linesB,
                projectionsB,
                out int obstacleCountB,
                out FPVector2 velocityB);

            Assert.That(countB, Is.EqualTo(countA));
            Assert.That(obstacleCountB, Is.EqualTo(obstacleCountA));
            AssertVectorRaw(velocityB, velocityA);
            for (int i = 0; i < countA; i++)
            {
                Assert.That(linesB[i].SourceKind, Is.EqualTo(linesA[i].SourceKind));
                Assert.That(linesB[i].SourceId, Is.EqualTo(linesA[i].SourceId));
                AssertVectorRaw(linesB[i].Point, linesA[i].Point);
                AssertVectorRaw(linesB[i].Direction, linesA[i].Direction);
            }
        }

        [Test]
        public void Solve_ObstaclePrefixRemainsAheadOfSortedAgentLinesForLp3()
        {
            FPVector2 position = FPVector2.Zero;
            ObstacleNeighbor[] obstacles = CreateBoxNeighbors(
                new FPOrientedBox2(V(0, 2), V(4, 1)));
            AgentNeighbor[] agents =
            {
                new AgentNeighbor(20, V(1, 0), V(-2, 0), AgentRadius),
                new AgentNeighbor(10, V(-1, 0), V(2, 0), AgentRadius),
            };
            int capacity = obstacles.Length + agents.Length;
            OrcaLine[] lines = new OrcaLine[capacity];
            OrcaLine[] projections = new OrcaLine[capacity];

            int lineCount = Solve(
                position,
                FPVector2.Zero,
                V(0, 3),
                obstacles,
                agents,
                lines,
                projections,
                out int obstacleLineCount,
                out FPVector2 velocity);

            Assert.That(obstacleLineCount, Is.GreaterThan(0));
            Assert.That(lineCount, Is.EqualTo(obstacleLineCount + agents.Length));
            AssertObstaclePrefix(lines, obstacleLineCount);
            Assert.That(lines[obstacleLineCount].SourceKind, Is.EqualTo(OrcaLineSourceKind.Agent));
            Assert.That(lines[obstacleLineCount].NeighborId, Is.EqualTo(10));
            Assert.That(lines[obstacleLineCount + 1].NeighborId, Is.EqualTo(20));
            AssertSatisfiesLines(lines, obstacleLineCount, velocity);
        }

        private static int Solve(
            FPVector2 position,
            FPVector2 current,
            FPVector2 preferred,
            ObstacleNeighbor[] obstacleNeighbors,
            AgentNeighbor[] agentNeighbors,
            OrcaLine[] lines,
            OrcaLine[] projections,
            out int obstacleLineCount,
            out FPVector2 velocity)
        {
            int obstacleCount = obstacleNeighbors?.Length ?? 0;
            int agentCount = agentNeighbors?.Length ?? 0;
            return OrcaSolver.Solve(
                7,
                position,
                current,
                preferred,
                AgentRadius,
                MaxSpeed,
                TimeHorizon,
                TimeStep,
                obstacleNeighbors,
                obstacleCount,
                agentNeighbors,
                agentCount,
                lines,
                projections,
                out obstacleLineCount,
                out velocity);
        }

        private static ObstacleNeighbor[] CreateBoxNeighbors(FPOrientedBox2 box)
        {
            ObstacleSegment[] segments = new ObstacleSegment[4];
            StaticObstacleSegmentBuilder.Fill(new[] { box }, 1, segments);
            ObstacleNeighbor[] neighbors = new ObstacleNeighbor[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                neighbors[i] = new ObstacleNeighbor(segments[i]);
            }

            return neighbors;
        }

        private static void AssertObstaclePrefix(OrcaLine[] lines, int obstacleLineCount)
        {
            for (int i = 0; i < obstacleLineCount; i++)
            {
                Assert.That(lines[i].SourceKind, Is.EqualTo(OrcaLineSourceKind.StaticObstacle));
                Assert.That(lines[i].NeighborId, Is.EqualTo(-1));
            }
        }

        private static void AssertSatisfiesLines(OrcaLine[] lines, int count, FPVector2 velocity)
        {
            FP tolerance = FP.FromRaw(8);
            for (int i = 0; i < count; i++)
            {
                FP violation = FPMath.Det(lines[i].Direction, lines[i].Point - velocity);
                Assert.That(violation, Is.LessThanOrEqualTo(tolerance), "Violated ORCA line " + i);
            }
        }

        private static void AssertVectorRaw(FPVector2 actual, FPVector2 expected)
        {
            Assert.That(actual.X.Raw, Is.EqualTo(expected.X.Raw));
            Assert.That(actual.Y.Raw, Is.EqualTo(expected.Y.Raw));
        }

        private static FPVector2 V(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }
    }
}
