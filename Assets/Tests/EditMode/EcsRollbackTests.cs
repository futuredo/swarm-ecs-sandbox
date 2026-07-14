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
        public void SpatialQueryMode_IsIncludedInHashAndRollbackSnapshot()
        {
            SwarmWorld world = CreateWorld(32, 0x51A7u);
            WorldSnapshotRing snapshots = new(world.Config.Capacity, 8);
            ulong uniformGridHash = world.ComputeStateHash();
            snapshots.Save(world);

            world.SetSpatialIndexMode(SpatialIndexMode.KdTreeKNearest);

            Assert.That(world.ComputeStateHash(), Is.Not.EqualTo(uniformGridHash));
            Assert.That(snapshots.TryRestore(world, 0), Is.True);
            Assert.That(world.SpatialIndexMode, Is.EqualTo(SpatialIndexMode.UniformGrid));
            Assert.That(world.ComputeStateHash(), Is.EqualTo(uniformGridHash));
        }

        [Test]
        public void FullPipeline_TwoWorldsRemainBitIdentical()
        {
            SwarmWorld first = CreateWorld(96, 42u);
            SwarmWorld second = CreateWorld(96, 42u);
            using SwarmSimulation firstSimulation = new(first);
            using SwarmSimulation secondSimulation = new(second);

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
        public void FullPipeline_GridAndKdRadiusProduceEquivalentCanonicalState()
        {
            const int agentCount = 512;
            const int simulatedTicks = 32;
            const uint seed = 0x52414449u;
            SwarmWorld gridWorld = CreateWorld(agentCount, seed, SpatialIndexMode.UniformGrid);
            SwarmWorld kdRadiusWorld = CreateWorld(agentCount, seed, SpatialIndexMode.KdTree);
            using SwarmSimulation gridSimulation = new(gridWorld);
            using SwarmSimulation kdRadiusSimulation = new(kdRadiusWorld);

            for (int tick = 0; tick < simulatedTicks; tick++)
            {
                gridSimulation.Step(gridWorld);
                kdRadiusSimulation.Step(kdRadiusWorld);
                gridWorld.AdvanceTick();
                kdRadiusWorld.AdvanceTick();
            }

            ulong gridFullHash = gridWorld.ComputeStateHash();
            ulong kdRadiusFullHash = kdRadiusWorld.ComputeStateHash();
            Assert.That(
                kdRadiusFullHash,
                Is.Not.EqualTo(gridFullHash),
                "The authoritative mode is intentionally part of the full state hash.");

            ulong gridCanonicalHash = ComputeCanonicalSpatialComparisonHash(gridWorld);
            ulong kdRadiusCanonicalHash = ComputeCanonicalSpatialComparisonHash(kdRadiusWorld);
            Assert.That(
                kdRadiusCanonicalHash,
                Is.EqualTo(gridCanonicalHash),
                "After neutralizing only SpatialIndexMode, Grid and KD radius must leave " +
                "the same kinematic and navigation authoritative state.");
            Assert.That(gridWorld.SpatialIndexMode, Is.EqualTo(SpatialIndexMode.UniformGrid));
            Assert.That(kdRadiusWorld.SpatialIndexMode, Is.EqualTo(SpatialIndexMode.KdTree));
        }

        [Test]
        public void LateAuthoritativeCommand_RollbackMatchesOnTimeSimulation()
        {
            SwarmWorld onTimeWorld = CreateWorld(80, 123u);
            SwarmWorld lateWorld = CreateWorld(80, 123u);
            using SwarmSimulation onTimeSimulation = new(onTimeWorld);
            using SwarmSimulation lateSimulation = new(lateWorld);
            RollbackController onTime = new(onTimeWorld, onTimeSimulation, 32);
            RollbackController late = new(lateWorld, lateSimulation, 32);
            FPVector2 correctedTarget = new(FP.FromInt(10), FP.FromInt(-65));
            SimulationCommand authoritativeCommand = new(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                correctedTarget);

            Assert.That(onTime.QueueCommand(authoritativeCommand), Is.True);

            for (int i = 0; i < 20; i++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(late.InjectLateCommand(authoritativeCommand), Is.True);
            Assert.That(late.LastResimulatedTicks, Is.EqualTo(15));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
        }

        [Test]
        public void LateAuthoritativeCommands_PreserveSequenceWhenArrivingOutOfOrder()
        {
            SwarmWorld onTimeWorld = CreateWorld(80, 126u);
            SwarmWorld lateWorld = CreateWorld(80, 126u);
            using SwarmSimulation onTimeSimulation = new(onTimeWorld);
            using SwarmSimulation lateSimulation = new(lateWorld);
            RollbackController onTime = new(onTimeWorld, onTimeSimulation, 32);
            RollbackController late = new(lateWorld, lateSimulation, 32);
            SimulationCommand sequenceZero = new(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(12), FP.FromInt(-50)));
            SimulationCommand sequenceOne = new(
                5,
                1,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(-45), FP.FromInt(18)));

            Assert.That(onTime.QueueCommand(sequenceZero), Is.True);
            Assert.That(onTime.QueueCommand(sequenceOne), Is.True);

            for (int i = 0; i < 20; i++)
            {
                onTime.Step();
                late.Step();
            }

            // The network delivers sequence 1 first. InjectLateCommand must keep the
            // authority-provided ordering key rather than assigning an arrival-order key.
            Assert.That(late.InjectLateCommand(sequenceOne), Is.True);
            Assert.That(late.InjectLateCommand(sequenceZero), Is.True);

            Assert.That(lateWorld.GroupTargets[0], Is.EqualTo(sequenceOne.Value));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
        }

        [Test]
        public void QueryModeCommand_IsReappliedWhenRollbackCrossesTheSwitchTick()
        {
            SwarmWorld onTimeWorld = CreateWorld(80, 124u);
            SwarmWorld lateWorld = CreateWorld(80, 124u);
            using SwarmSimulation onTimeSimulation = new(onTimeWorld);
            using SwarmSimulation lateSimulation = new(lateWorld);
            RollbackController onTime = new(onTimeWorld, onTimeSimulation, 32);
            RollbackController late = new(lateWorld, lateSimulation, 32);
            FPVector2 correctedTarget = new(FP.FromInt(10), FP.FromInt(-65));

            Assert.That(onTime.QueueCommand(new SimulationCommand(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                correctedTarget)), Is.True);

            for (int tick = 0; tick < 20; tick++)
            {
                if (tick == 8)
                {
                    Assert.That(onTime.QueueSpatialIndexMode(SpatialIndexMode.KdTreeKNearest), Is.True);
                    Assert.That(late.QueueSpatialIndexMode(SpatialIndexMode.KdTreeKNearest), Is.True);
                }

                onTime.Step();
                late.Step();
            }

            Assert.That(late.InjectLateCommand(new SimulationCommand(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                correctedTarget)), Is.True);
            Assert.That(lateWorld.SpatialIndexMode, Is.EqualTo(SpatialIndexMode.KdTreeKNearest));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
        }

        [Test]
        public void LateCommandWithoutRestorableSnapshot_DoesNotPolluteTimeline()
        {
            SwarmWorld world = CreateWorld(32, 125u);
            using SwarmSimulation simulation = new(world);
            RollbackController rollback = new(world, simulation, 32);
            for (int i = 0; i < 20; i++)
            {
                rollback.Step();
            }

            rollback.ResetHistory();
            int commandCountBefore = rollback.CommandCount;
            int sequenceBefore = rollback.NextLocallyGeneratedSequence;

            Assert.That(rollback.InjectLateCommand(new SimulationCommand(
                5,
                37,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(4), FP.FromInt(5)))), Is.False);
            Assert.That(rollback.CommandCount, Is.EqualTo(commandCountBefore));
            Assert.That(rollback.NextLocallyGeneratedSequence, Is.EqualTo(sequenceBefore));
        }

        [Test]
        public void CommandTimeline_RecyclesExpiredHistoryAcrossMoreThanFiveHundredCommands()
        {
            const int historyLength = 8;
            const int commandCapacity = 16;
            const int commandCount = 600;
            SwarmWorld firstWorld = CreateWorld(8, 0x54494D45u);
            SwarmWorld secondWorld = CreateWorld(8, 0x54494D45u);
            RollbackController first = new(
                firstWorld,
                new NoOpSimulation(),
                historyLength,
                commandCapacity);
            RollbackController second = new(
                secondWorld,
                new NoOpSimulation(),
                historyLength,
                commandCapacity);
            FPVector2[] expectedTargets = new FPVector2[SwarmWorld.GroupCount];

            for (int sequence = 0; sequence < commandCount; sequence++)
            {
                int group = sequence & 3;
                FPVector2 target = new(
                    FP.FromInt((sequence % 101) - 50),
                    FP.FromInt(((sequence * 7) % 101) - 50));
                SimulationCommand command = new(
                    firstWorld.Tick,
                    sequence,
                    SimulationCommandType.SetGroupTarget,
                    (byte)group,
                    target);

                Assert.That(first.QueueCommand(command), Is.True, "First timeline rejected sequence " + sequence);
                Assert.That(second.QueueCommand(command), Is.True, "Second timeline rejected sequence " + sequence);
                first.Step();
                second.Step();
                expectedTargets[group] = target;

                Assert.That(first.CommandCount, Is.LessThanOrEqualTo(historyLength));
                Assert.That(second.CommandCount, Is.EqualTo(first.CommandCount));
            }

            Assert.That(firstWorld.Tick, Is.EqualTo(commandCount));
            Assert.That(secondWorld.ComputeStateHash(), Is.EqualTo(firstWorld.ComputeStateHash()));
            Assert.That(first.NextLocallyGeneratedSequence, Is.EqualTo(commandCount));
            Assert.That(second.NextLocallyGeneratedSequence, Is.EqualTo(commandCount));
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                Assert.That(firstWorld.GroupTargets[group], Is.EqualTo(expectedTargets[group]));
                Assert.That(secondWorld.GroupTargets[group], Is.EqualTo(expectedTargets[group]));
            }
        }

        [Test]
        public void OldestRestorableTick_RetainsCommandUntilLateReplayCompletes()
        {
            const int historyLength = 8;
            SwarmWorld onTimeWorld = CreateWorld(8, 0x57494E44u);
            SwarmWorld lateWorld = CreateWorld(8, 0x57494E44u);
            RollbackController onTime = new(onTimeWorld, new NoOpSimulation(), historyLength, 16);
            RollbackController late = new(lateWorld, new NoOpSimulation(), historyLength, 16);
            SimulationCommand command = new(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                2,
                new FPVector2(FP.FromInt(17), FP.FromInt(-23)));
            Assert.That(onTime.QueueCommand(command), Is.True);

            // At current tick 13, an eight-slot ring contains snapshots 5..12.
            // Tick 5 is the inclusive rollback boundary and its command must remain.
            for (int tick = 0; tick < 13; tick++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(onTime.CommandCount, Is.EqualTo(1));
            Assert.That(late.InjectLateCommand(command), Is.True);
            Assert.That(late.LastResimulatedTicks, Is.EqualTo(historyLength));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));

            // Saving tick 13 advances the inclusive boundary to tick 6, so the
            // tick-5 command is now expired on both deterministic timelines.
            onTime.Step();
            late.Step();
            Assert.That(onTime.CommandCount, Is.Zero);
            Assert.That(late.CommandCount, Is.Zero);
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
        public void CommandTimeline_OrderedAppendUsesSequentialCursorAndRewindsDeterministically()
        {
            SwarmWorld world = CreateWorld(8, 18u);
            CommandTimeline timeline = new(4);
            SimulationCommand first = new(
                1,
                10,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(11), FP.FromInt(12)));
            SimulationCommand second = new(
                3,
                11,
                SimulationCommandType.SetGroupTarget,
                0,
                new FPVector2(FP.FromInt(31), FP.FromInt(32)));

            Assert.That(timeline.AppendOrdered(first), Is.True);
            Assert.That(timeline.AppendOrdered(second), Is.True);
            Assert.That(timeline.AppendOrdered(first), Is.False);
            Assert.That(timeline.Count, Is.EqualTo(2));

            timeline.ApplyAtTick(world, 0);
            timeline.ApplyAtTick(world, 1);
            Assert.That(world.GroupTargets[0], Is.EqualTo(first.Value));
            timeline.ApplyAtTick(world, 2);
            timeline.ApplyAtTick(world, 3);
            Assert.That(world.GroupTargets[0], Is.EqualTo(second.Value));

            timeline.ApplyAtTick(world, 1);
            Assert.That(world.GroupTargets[0], Is.EqualTo(first.Value));
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
            return CreateWorld(count, seed, SpatialIndexMode.UniformGrid);
        }

        private static SwarmWorld CreateWorld(int count, uint seed, SpatialIndexMode mode)
        {
            SwarmConfig baseline = SwarmConfig.DemoDefault(count);
            SwarmConfig config = baseline.WithSpatialIndexMode(mode);
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(count, seed);
            return world;
        }

        private static ulong ComputeCanonicalSpatialComparisonHash(SwarmWorld world)
        {
            SpatialIndexMode mode = world.SpatialIndexMode;
            try
            {
                world.SetSpatialIndexMode(SpatialIndexMode.UniformGrid);
                return world.ComputeStateHash();
            }
            finally
            {
                world.SetSpatialIndexMode(mode);
            }
        }

        private sealed class NoOpSimulation : IDeterministicSimulation
        {
            public void Step(SwarmWorld world)
            {
            }
        }
    }
}
