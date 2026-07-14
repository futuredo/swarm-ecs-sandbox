using System;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class KdTreeKNearestRuntimeTests
    {
        [Test]
        public void KNearestMode_IgnoresRadiusAndCapsNeighborsAfterFilteringSelf()
        {
            const int agentCount = 5;
            const int maxNeighbors = 3;
            SwarmWorld world = CreateSpacedWorld(agentCount, maxNeighbors);
            using NeighborAvoidanceSystem avoidance = new(world.Config);

            world.SetSpatialIndexMode(SpatialIndexMode.KdTree);
            avoidance.Execute(world);
            Assert.That(avoidance.LastNeighborLinks, Is.Zero,
                "Radius mode should not find agents spaced beyond NeighborDistance.");

            world.SetSpatialIndexMode(SpatialIndexMode.KdTreeKNearest);
            avoidance.Execute(world);
            Assert.That(avoidance.LastNeighborLinks, Is.EqualTo(agentCount * maxNeighbors));
            Assert.That(avoidance.LastOrcaLines, Is.EqualTo(agentCount * maxNeighbors));
        }

        [Test]
        public void KNearestMode_WhenWorldIsSmallerThanK_UsesEveryOtherAgentOnce()
        {
            const int agentCount = 4;
            SwarmWorld world = CreateSpacedWorld(agentCount, 8);
            using NeighborAvoidanceSystem avoidance = new(world.Config);
            world.SetSpatialIndexMode(SpatialIndexMode.KdTreeKNearest);

            avoidance.Execute(world);

            Assert.That(avoidance.LastNeighborLinks, Is.EqualTo(agentCount * (agentCount - 1)));
        }

        [Test]
        public void KNearestMode_AfterWarmupAllocatesNoManagedBytesOnCallingThread()
        {
            SwarmWorld world = CreateSpacedWorld(128, 8);
            using NeighborAvoidanceSystem avoidance = new(world.Config);
            world.SetSpatialIndexMode(SpatialIndexMode.KdTreeKNearest);

            for (int i = 0; i < 4; i++)
            {
                avoidance.Execute(world);
            }

            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4; i++)
            {
                avoidance.Execute(world);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        private static SwarmWorld CreateSpacedWorld(int agentCount, int maxNeighbors)
        {
            SwarmConfig config = new(
                agentCount,
                FP.FromRatio(1, 30),
                FP.FromRatio(7, 20),
                FP.FromInt(6),
                FP.FromInt(4),
                maxNeighbors,
                FP.FromInt(2),
                FP.FromInt(2048),
                SpatialIndexMode.UniformGrid);
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(agentCount, 0x4B4E4E31u);

            for (int i = 0; i < agentCount; i++)
            {
                world.Positions[i] = new FPVector2(FP.FromInt(i * 10), FP.Zero);
                world.Velocities[i] = FPVector2.Zero;
                world.PreferredVelocities[i] = FPVector2.Zero;
            }

            return world;
        }
    }
}
