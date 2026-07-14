using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;

namespace SwarmECS.Tests.EditMode
{
    public sealed class OrcaSolverTests
    {
        private static readonly FP Half = FP.FromRatio(1, 2);
        private static readonly FP TimeStep = FP.FromRatio(1, 10);

        [Test]
        public void Solve_WithoutNeighbors_ClampsPreferredVelocityToSpeedCircle()
        {
            AgentNeighbor[] neighbors = new AgentNeighbor[0];
            OrcaLine[] lines = new OrcaLine[1];
            OrcaLine[] projections = new OrcaLine[1];

            int lineCount = OrcaSolver.Solve(
                7,
                FPVector2.Zero,
                V(10, 0),
                Half,
                FP.FromInt(5),
                FP.FromInt(2),
                TimeStep,
                neighbors,
                0,
                lines,
                projections,
                out FPVector2 velocity);

            Assert.That(lineCount, Is.EqualTo(0));
            Assert.That(velocity.X.Raw, Is.EqualTo(FP.FromInt(5).Raw));
            Assert.That(velocity.Y.Raw, Is.EqualTo(0));
        }

        [Test]
        public void Solve_HeadOnPair_ChoosesOppositeLateralVelocities()
        {
            AgentNeighbor[] neighborForA =
            {
                new AgentNeighbor(20, V(2, 0), V(-1, 0), Half)
            };
            AgentNeighbor[] neighborForB =
            {
                new AgentNeighbor(10, V(-2, 0), V(1, 0), Half)
            };

            FPVector2 velocityA = SolveOne(10, V(1, 0), V(1, 0), neighborForA, FP.FromInt(2));
            FPVector2 velocityB = SolveOne(20, V(-1, 0), V(-1, 0), neighborForB, FP.FromInt(2));

            Assert.That(velocityA.Y.Raw, Is.Not.EqualTo(0));
            Assert.That(velocityB.Y.Raw, Is.Not.EqualTo(0));
            Assert.That((long)velocityA.Y.Raw * velocityB.Y.Raw, Is.LessThan(0L));
            Assert.That(FPMath.Abs(velocityA.X).Raw, Is.LessThan(FP.One.Raw));
            Assert.That(FPMath.Abs(velocityB.X).Raw, Is.LessThan(FP.One.Raw));
        }

        [Test]
        public void Solve_NearbyNeighbors_NeverExceedsMaxSpeed()
        {
            AgentNeighbor[] neighbors =
            {
                new AgentNeighbor(2, V(1, 0), V(-3, 0), Half),
                new AgentNeighbor(3, V(0, 1), V(0, -3), Half),
                new AgentNeighbor(4, V(-1, 0), V(3, 0), Half),
                new AgentNeighbor(5, V(0, -1), V(0, 3), Half)
            };
            FP maxSpeed = FP.FromRatio(3, 2);

            FPVector2 velocity = SolveOne(1, V(3, 2), V(9, 7), neighbors, FP.One, maxSpeed);

            Assert.That(
                velocity.SqrMagnitude.Raw,
                Is.LessThanOrEqualTo((maxSpeed * maxSpeed).Raw));
        }

        [Test]
        public void Solve_RepeatedInput_ProducesBitIdenticalRawVelocityAndLines()
        {
            AgentNeighbor[] neighbors =
            {
                new AgentNeighbor(30, V(3, 1), V(-1, 1), Half),
                new AgentNeighbor(10, V(-2, 1), V(1, -1), FP.FromRatio(3, 4)),
                new AgentNeighbor(20, V(1, -2), V(0, 2), Half)
            };
            AgentNeighbor[] sameNeighborsInDifferentQueryOrder =
            {
                neighbors[2],
                neighbors[0],
                neighbors[1]
            };
            OrcaLine[] linesA = new OrcaLine[neighbors.Length];
            OrcaLine[] projectionsA = new OrcaLine[neighbors.Length];
            OrcaLine[] linesB = new OrcaLine[neighbors.Length];
            OrcaLine[] projectionsB = new OrcaLine[neighbors.Length];

            int countA = OrcaSolver.Solve(
                5,
                V(1, 0),
                V(2, 1),
                Half,
                FP.FromInt(3),
                FP.FromInt(3),
                TimeStep,
                neighbors,
                neighbors.Length,
                linesA,
                projectionsA,
                out FPVector2 velocityA);

            int countB = OrcaSolver.Solve(
                5,
                V(1, 0),
                V(2, 1),
                Half,
                FP.FromInt(3),
                FP.FromInt(3),
                TimeStep,
                sameNeighborsInDifferentQueryOrder,
                sameNeighborsInDifferentQueryOrder.Length,
                linesB,
                projectionsB,
                out FPVector2 velocityB);

            Assert.That(countB, Is.EqualTo(countA));
            Assert.That(velocityB.X.Raw, Is.EqualTo(velocityA.X.Raw));
            Assert.That(velocityB.Y.Raw, Is.EqualTo(velocityA.Y.Raw));

            for (int i = 0; i < countA; i++)
            {
                Assert.That(linesB[i].NeighborId, Is.EqualTo(linesA[i].NeighborId));
                Assert.That(linesB[i].Point.X.Raw, Is.EqualTo(linesA[i].Point.X.Raw));
                Assert.That(linesB[i].Point.Y.Raw, Is.EqualTo(linesA[i].Point.Y.Raw));
                Assert.That(linesB[i].Direction.X.Raw, Is.EqualTo(linesA[i].Direction.X.Raw));
                Assert.That(linesB[i].Direction.Y.Raw, Is.EqualTo(linesA[i].Direction.Y.Raw));
            }
        }

        private static FPVector2 SolveOne(
            int stableId,
            FPVector2 current,
            FPVector2 preferred,
            AgentNeighbor[] neighbors,
            FP horizon,
            FP? maxSpeed = null)
        {
            OrcaLine[] lines = new OrcaLine[neighbors.Length];
            OrcaLine[] projections = new OrcaLine[neighbors.Length];

            OrcaSolver.Solve(
                stableId,
                current,
                preferred,
                Half,
                maxSpeed ?? FP.FromInt(2),
                horizon,
                TimeStep,
                neighbors,
                neighbors.Length,
                lines,
                projections,
                out FPVector2 velocity);

            return velocity;
        }

        private static FPVector2 V(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }
    }
}
