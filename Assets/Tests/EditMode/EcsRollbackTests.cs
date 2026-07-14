using System;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class EcsRollbackTests
    {
        [Test]
        public void EntityAllocator_RecyclesIndexAndAdvancesGeneration()
        {
            EntityAllocator allocator = new(2);
            Entity first = allocator.Create();
            Assert.That(allocator.Destroy(first), Is.True);

            Entity recycled = allocator.Create();
            Assert.That(recycled.Index, Is.EqualTo(first.Index));
            Assert.That(recycled.Generation, Is.Not.EqualTo(first.Generation));
            Assert.That(allocator.IsAlive(first), Is.False);
            Assert.That(allocator.IsAlive(recycled), Is.True);
        }

        [Test]
        public void WorldInitialization_WithSameSeedHasSameRawStateHash()
        {
            SwarmWorld first = CreateWorld(128, 0xC0FFEEu);
            SwarmWorld second = CreateWorld(128, 0xC0FFEEu);

            Assert.That(first.ComputeStateHash(), Is.EqualTo(second.ComputeStateHash()));
        }

        [Test]
        public void FullPipeline_TwoWorldsRemainBitIdentical()
        {
            SwarmWorld first = CreateWorld(96, 42u);
            SwarmWorld second = CreateWorld(96, 42u);
            SwarmSimulation firstSimulation = new(first);
            SwarmSimulation secondSimulation = new(second);

            for (int tick = 0; tick < 24; tick++)
            {
                firstSimulation.Step(first);
                secondSimulation.Step(second);
                first.AdvanceTick();
                second.AdvanceTick();
            }

            Assert.That(first.ComputeStateHash(), Is.EqualTo(second.ComputeStateHash()));
        }

        [Test]
        public void LateAuthoritativeCommand_RollbackMatchesOnTimeSimulation()
        {
            SwarmWorld onTimeWorld = CreateWorld(80, 123u);
            SwarmWorld lateWorld = CreateWorld(80, 123u);
            RollbackController onTime = new(onTimeWorld, new SwarmSimulation(onTimeWorld), 32);
            RollbackController late = new(lateWorld, new SwarmSimulation(lateWorld), 32);
            FPVector2 correctedTarget = new(FP.FromInt(10), FP.FromInt(-65));

            Assert.That(onTime.QueueCommand(new SimulationCommand(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                correctedTarget)), Is.True);

            for (int i = 0; i < 20; i++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(late.InjectLateGroupTarget(15, 0, correctedTarget), Is.True);
            Assert.That(late.LastResimulatedTicks, Is.EqualTo(15));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
        }

        [Test]
        public void CommandTimeline_SameTickArrivalOrderDoesNotChangeResult()
        {
            SwarmWorld orderedWorld = CreateWorld(8, 17u);
            SwarmWorld reorderedWorld = CreateWorld(8, 17u);
            CommandTimeline ordered = new(8);
            CommandTimeline reordered = new(8);
            SimulationCommand first = new(
                3,
                10,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(10), FP.FromInt(20)));
            SimulationCommand second = new(
                3,
                11,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(-30), FP.FromInt(40)));

            Assert.That(ordered.Add(first), Is.True);
            Assert.That(ordered.Add(second), Is.True);
            Assert.That(reordered.Add(second), Is.True);
            Assert.That(reordered.Add(first), Is.True);

            ordered.ApplyAtTick(orderedWorld, 3);
            reordered.ApplyAtTick(reorderedWorld, 3);

            Assert.That(reorderedWorld.GroupTargets[0], Is.EqualTo(orderedWorld.GroupTargets[0]));
            Assert.That(reorderedWorld.GroupTargets[0], Is.EqualTo(second.Value));
        }

        [Test]
        public void SharedAStarRoutes_AreGeneratedForAllFourSquads()
        {
            SwarmWorld world = CreateWorld(32, 7u);
            SwarmSimulation simulation = new(world);

            Assert.That(simulation.Navigation.TotalSharedWaypoints, Is.GreaterThan(40));
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                Assert.That(simulation.Navigation.GetGroupPath(group).Count, Is.GreaterThan(1));
            }
        }

        [Test]
        public void SimulationHotPath_AfterWarmupAllocatesNoManagedBytes()
        {
            SwarmWorld world = CreateWorld(128, 99u);
            SwarmSimulation simulation = new(world);
            for (int i = 0; i < 12; i++)
            {
                simulation.Step(world);
                world.AdvanceTick();
            }

            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; i++)
            {
                simulation.Step(world);
                world.AdvanceTick();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private static SwarmWorld CreateWorld(int count, uint seed)
        {
            SwarmConfig config = SwarmConfig.PortfolioDefault(count);
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(count, seed);
            return world;
        }
    }
}
