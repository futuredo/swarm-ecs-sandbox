using System;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Pathfinding;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class NavigationSchedulerTests
    {
        [Test]
        public void DynamicTarget_IsReplannedAndBecomesTheActiveSharedRoute()
        {
            SwarmWorld world = CreateWorld(64, 101u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            int oldGoal = world.GroupPathStates[0].ResolvedGoalIndex;
            int newGoal = FindReachableAlternative(navigation, 0, oldGoal);

            world.SetGroupTarget(0, navigation.Map.CellCenter(newGoal));
            navigation.Execute(world);

            GroupPathState state = world.GroupPathStates[0];
            Assert.That(navigation.LastProcessedPathRequests, Is.EqualTo(1));
            Assert.That(state.Status, Is.EqualTo(GroupPathStatus.Active));
            Assert.That(state.HasPending, Is.False);
            Assert.That(state.ResolvedGoalIndex, Is.EqualTo(newGoal));
            Assert.That(navigation.GetGroupPath(0).GoalIndex, Is.EqualTo(newGoal));
            Assert.That(navigation.GetGroupPath(0).Count, Is.GreaterThan(1));
            Assert.That(world.PathCursors[0], Is.GreaterThanOrEqualTo(1));
            Assert.That(world.PathCursors[0], Is.LessThan(navigation.GetGroupPath(0).Count));
        }

        [Test]
        public void TenThousandAgentReplan_UsesLogicalSquadCenterWithoutDoubleFormationOffset()
        {
            SwarmWorld world = CreateWorld(10_000, 108u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            int initialStart = navigation.GetGroupPath(0).StartIndex;
            int alternativeGoal = FindReachableAlternative(
                navigation,
                0,
                world.GroupPathStates[0].ResolvedGoalIndex);

            world.SetGroupTarget(0, navigation.Map.CellCenter(alternativeGoal));
            navigation.Execute(world);

            Assert.That(world.GroupPathStates[0].ResolvedStartIndex, Is.EqualTo(initialStart));
            Assert.That(navigation.GetGroupPath(0).StartIndex, Is.EqualTo(initialStart));
        }

        [Test]
        public void RequestBudget_ProcessesOnlyOneSquadPerTickInStableOrder()
        {
            SwarmWorld world = CreateWorld(64, 102u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            int[] requestedGoals = new int[SwarmWorld.GroupCount];

            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                requestedGoals[group] = FindReachableAlternative(
                    navigation,
                    group,
                    world.GroupPathStates[group].ResolvedGoalIndex);
                world.SetGroupTarget(group, navigation.Map.CellCenter(requestedGoals[group]));
            }

            navigation.Execute(world);

            Assert.That(navigation.LastProcessedPathRequests, Is.EqualTo(1));
            Assert.That(navigation.PendingPathRequests, Is.EqualTo(3));
            Assert.That(world.GroupPathStates[0].ResolvedGoalIndex, Is.EqualTo(requestedGoals[0]));
            Assert.That(world.GroupPathStates[0].HasPending, Is.False);
            for (int group = 1; group < SwarmWorld.GroupCount; group++)
            {
                Assert.That(world.GroupPathStates[group].HasPending, Is.True);
            }

            navigation.Execute(world);
            Assert.That(navigation.LastProcessedPathRequests, Is.EqualTo(1));
            Assert.That(navigation.PendingPathRequests, Is.EqualTo(2));
            Assert.That(world.GroupPathStates[1].ResolvedGoalIndex, Is.EqualTo(requestedGoals[1]));
        }

        [Test]
        public void BlockedGoal_IsRejectedBeforeAStarAndStopsTheSquad()
        {
            SwarmWorld world = CreateWorld(64, 103u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            int blockedGoal = FindBlockedNode(navigation.Map);

            world.SetGroupTarget(0, navigation.Map.CellCenter(blockedGoal));
            navigation.Execute(world);

            GroupPathState state = world.GroupPathStates[0];
            Assert.That(state.Status, Is.EqualTo(GroupPathStatus.Unreachable));
            Assert.That(state.ResolvedGoalIndex, Is.EqualTo(blockedGoal));
            Assert.That(state.HasPending, Is.False);
            Assert.That(navigation.IslandRejectedRequests, Is.EqualTo(1));
            Assert.That(navigation.GetGroupPath(0).Count, Is.Zero);
            Assert.That(world.PreferredVelocities[0], Is.EqualTo(FPVector2.Zero));
        }

        [Test]
        public void WalkableGoalInDifferentIsland_IsRejectedBeforeAStar()
        {
            SwarmWorld world = CreateWorld(64, 110u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            GridMap map = navigation.Map;
            int start = navigation.GetGroupPath(0).StartIndex;
            int barrierX = map.Width / 2;
            map.IndexToCoordinates(start, out int startX, out _);
            Assert.That(startX, Is.LessThan(barrierX));

            for (int y = 0; y < map.Height; y++)
            {
                map.SetWalkable(barrierX, y, false);
            }

            int goal = FindWalkableNodeOnSide(map, barrierX + 1, map.Width);
            Assert.That(map.IsWalkable(start), Is.True);
            Assert.That(map.IsWalkable(goal), Is.True);
            Assert.That(navigation.Islands.GetRegionId(start), Is.Not.EqualTo(GridIslandMap.NoRegion));
            Assert.That(navigation.Islands.GetRegionId(goal), Is.Not.EqualTo(GridIslandMap.NoRegion));
            Assert.That(navigation.Islands.AreConnected(start, goal), Is.False);

            int cacheHitsBefore = navigation.CacheHits;
            int cacheMissesBefore = navigation.CacheMisses;
            int derivedAStarRebuildsBefore = navigation.DerivedAStarRebuilds;
            world.SetGroupTarget(0, map.CellCenter(goal));

            navigation.Execute(world);

            GroupPathState state = world.GroupPathStates[0];
            Assert.That(navigation.LastProcessedPathRequests, Is.EqualTo(1));
            Assert.That(navigation.IslandRejectedRequests, Is.EqualTo(1));
            Assert.That(navigation.CacheHits, Is.EqualTo(cacheHitsBefore));
            Assert.That(navigation.CacheMisses, Is.EqualTo(cacheMissesBefore));
            Assert.That(navigation.DerivedAStarRebuilds, Is.EqualTo(derivedAStarRebuildsBefore));
            Assert.That(state.Status, Is.EqualTo(GroupPathStatus.Unreachable));
            Assert.That(state.ResolvedStartIndex, Is.EqualTo(start));
            Assert.That(state.ResolvedGoalIndex, Is.EqualTo(goal));
            Assert.That(state.ResolvedMapRevision, Is.EqualTo(map.Revision));
            Assert.That(state.HasPending, Is.False);
            Assert.That(navigation.GetGroupPath(0).Count, Is.Zero);
            Assert.That(world.PreferredVelocities[0], Is.EqualTo(FPVector2.Zero));
        }

        [Test]
        public void ExternalMapRevision_ReplansAndRollbackWithinNewRevisionConverges()
        {
            SwarmWorld onTimeWorld = CreateWorld(96, 111u);
            SwarmWorld lateWorld = CreateWorld(96, 111u);
            using SwarmSimulation onTimeSimulation = new(onTimeWorld);
            using SwarmSimulation lateSimulation = new(lateWorld);
            RollbackController onTime = new(onTimeWorld, onTimeSimulation, 32);
            RollbackController late = new(lateWorld, lateSimulation, 32);

            for (int i = 0; i < 4; i++)
            {
                onTime.Step();
                late.Step();
            }

            SharedPath onTimePath = onTimeSimulation.Navigation.GetGroupPath(0);
            SharedPath latePath = lateSimulation.Navigation.GetGroupPath(0);
            int blockedRouteNode = onTimePath.NodeIndices[onTimePath.Count / 2];
            Assert.That(blockedRouteNode, Is.EqualTo(latePath.NodeIndices[latePath.Count / 2]));
            Assert.That(blockedRouteNode, Is.Not.EqualTo(onTimePath.StartIndex));
            Assert.That(blockedRouteNode, Is.Not.EqualTo(onTimePath.GoalIndex));

            GridMap onTimeMap = onTimeSimulation.Navigation.Map;
            GridMap lateMap = lateSimulation.Navigation.Map;
            int previousRevision = onTimeMap.Revision;
            onTimeMap.IndexToCoordinates(blockedRouteNode, out int blockedX, out int blockedY);
            onTimeMap.SetWalkable(blockedX, blockedY, false);
            lateMap.SetWalkable(blockedX, blockedY, false);

            Assert.That(onTimeMap.Revision, Is.Not.EqualTo(previousRevision));
            Assert.That(lateMap.Revision, Is.EqualTo(onTimeMap.Revision));
            int requestSequenceBefore = onTimeWorld.NextPathRequestSequence;
            int cacheMissesBefore = onTimeSimulation.Navigation.CacheMisses;

            onTime.Step();
            late.Step();

            GroupPathState replanned = onTimeWorld.GroupPathStates[0];
            SharedPath revisedPath = onTimeSimulation.Navigation.GetGroupPath(0);
            Assert.That(onTimeWorld.NextPathRequestSequence,
                Is.EqualTo(requestSequenceBefore + SwarmWorld.GroupCount));
            Assert.That(onTimeSimulation.Navigation.LastProcessedPathRequests, Is.EqualTo(1));
            Assert.That(onTimeSimulation.Navigation.PendingPathRequests,
                Is.EqualTo(SwarmWorld.GroupCount - 1));
            Assert.That(onTimeSimulation.Navigation.CacheMisses, Is.EqualTo(cacheMissesBefore + 1));
            Assert.That(replanned.Status, Is.EqualTo(GroupPathStatus.Active));
            Assert.That(replanned.ResolvedMapRevision, Is.EqualTo(onTimeMap.Revision));
            Assert.That(revisedPath.MapRevision, Is.EqualTo(onTimeMap.Revision));
            Assert.That(PathContainsNode(revisedPath, blockedRouteNode), Is.False);
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));

            for (int i = 1; i < SwarmWorld.GroupCount; i++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(onTimeSimulation.Navigation.PendingPathRequests, Is.Zero);
            Assert.That(lateSimulation.Navigation.PendingPathRequests, Is.Zero);
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));

            // Grid topology is external static data, not authoritative snapshot state.
            // Begin a fresh rollback epoch only after the identical revision is active.
            onTime.ResetHistory();
            late.ResetHistory();
            int commandTick = onTimeWorld.Tick + 5;
            int alternativeGoal = FindReachableAlternative(
                onTimeSimulation.Navigation,
                0,
                onTimeWorld.GroupPathStates[0].ResolvedGoalIndex);
            FPVector2 alternativeTarget = onTimeMap.CellCenter(alternativeGoal);
            SimulationCommand authoritativeCommand = new(
                commandTick,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                alternativeTarget);
            Assert.That(onTime.QueueCommand(authoritativeCommand), Is.True);

            for (int i = 0; i < 20; i++)
            {
                onTime.Step();
                late.Step();
            }

            int latencyTicks = lateWorld.Tick - commandTick;
            Assert.That(late.InjectLateCommand(authoritativeCommand), Is.True);
            Assert.That(late.LastResimulatedTicks, Is.EqualTo(latencyTicks));
            Assert.That(lateMap.Revision, Is.EqualTo(onTimeMap.Revision));
            Assert.That(lateWorld.GroupPathStates[0].ResolvedMapRevision,
                Is.EqualTo(onTimeWorld.GroupPathStates[0].ResolvedMapRevision));
            Assert.That(lateWorld.GroupPathStates[0].ResolvedGoalIndex,
                Is.EqualTo(onTimeWorld.GroupPathStates[0].ResolvedGoalIndex));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
        }

        [Test]
        public void ReturningToPreviousTarget_ReusesFixedCapacityPathCache()
        {
            SwarmWorld world = CreateWorld(64, 104u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            FPVector2 initialTarget = world.GroupTargets[0];
            int initialGoal = world.GroupPathStates[0].ResolvedGoalIndex;
            int alternativeGoal = FindReachableAlternative(navigation, 0, initialGoal);

            world.SetGroupTarget(0, navigation.Map.CellCenter(alternativeGoal));
            navigation.Execute(world);
            int missesAfterAlternative = navigation.CacheMisses;

            world.SetGroupTarget(0, initialTarget);
            navigation.Execute(world);
            int missesAfterWarmup = navigation.CacheMisses;
            int hitsAfterWarmup = navigation.CacheHits;

            world.SetGroupTarget(0, navigation.Map.CellCenter(alternativeGoal));
            navigation.Execute(world);
            world.SetGroupTarget(0, initialTarget);
            navigation.Execute(world);

            Assert.That(missesAfterAlternative, Is.EqualTo(1));
            Assert.That(missesAfterWarmup, Is.EqualTo(missesAfterAlternative));
            Assert.That(navigation.CacheMisses, Is.EqualTo(missesAfterWarmup));
            Assert.That(navigation.CacheHits, Is.EqualTo(hitsAfterWarmup + 2));
            Assert.That(world.GroupPathStates[0].ResolvedGoalIndex, Is.EqualTo(initialGoal));
            Assert.That(navigation.GetGroupPath(0).GoalIndex, Is.EqualTo(initialGoal));
        }

        [Test]
        public void CachedTargetChanges_AfterWarmupAllocateNoManagedBytes()
        {
            SwarmWorld world = CreateWorld(128, 105u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            FPVector2 firstTarget = world.GroupTargets[0];
            int firstGoal = world.GroupPathStates[0].ResolvedGoalIndex;
            int secondGoal = FindReachableAlternative(navigation, 0, firstGoal);
            FPVector2 secondTarget = navigation.Map.CellCenter(secondGoal);

            world.SetGroupTarget(0, secondTarget);
            navigation.Execute(world);
            world.SetGroupTarget(0, firstTarget);
            navigation.Execute(world);

            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 12; i++)
            {
                world.SetGroupTarget(0, (i & 1) == 0 ? secondTarget : firstTarget);
                navigation.Execute(world);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void UncachedAStarRequests_AfterWarmupAllocateNoManagedBytes()
        {
            SwarmWorld world = CreateWorld(128, 107u);
            SharedPathNavigationSystem navigation = CreateNavigation(world, 1);
            FPVector2[] targets = new FPVector2[8];
            int initialGoal = world.GroupPathStates[0].ResolvedGoalIndex;
            int start = navigation.GetGroupPath(0).StartIndex;
            int targetCount = 0;
            for (int index = navigation.Map.NodeCount - 1;
                index >= 0 && targetCount < targets.Length;
                index--)
            {
                if (index == initialGoal ||
                    !navigation.Map.IsWalkable(index) ||
                    !navigation.Islands.AreConnected(start, index))
                {
                    continue;
                }

                targets[targetCount++] = navigation.Map.CellCenter(index);
            }

            Assert.That(targetCount, Is.EqualTo(targets.Length));
            navigation.Execute(world);
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < targets.Length; i++)
            {
                world.SetGroupTarget(0, targets[i]);
                navigation.Execute(world);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(navigation.CacheMisses, Is.EqualTo(targets.Length));
        }

        [Test]
        public void Rollback_WithBudgetedBacklogMatchesOnTimeSimulation()
        {
            SwarmWorld onTimeWorld = CreateWorld(96, 106u);
            SwarmWorld lateWorld = CreateWorld(96, 106u);
            using SwarmSimulation onTimeSimulation = new(onTimeWorld);
            using SwarmSimulation lateSimulation = new(lateWorld);
            RollbackController onTime = new(onTimeWorld, onTimeSimulation, 32);
            RollbackController late = new(lateWorld, lateSimulation, 32);
            FPVector2 groupZeroTarget = new(FP.FromInt(10), FP.FromInt(-65));
            FPVector2 groupOneTarget = new(FP.FromInt(60), FP.FromInt(10));

            Assert.That(onTime.QueueCommand(new SimulationCommand(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                groupZeroTarget)), Is.True);
            Assert.That(onTime.QueueCommand(new SimulationCommand(
                5,
                1,
                SimulationCommandType.SetGroupTarget,
                1,
                groupOneTarget)), Is.True);
            Assert.That(late.QueueCommand(new SimulationCommand(
                5,
                1,
                SimulationCommandType.SetGroupTarget,
                1,
                groupOneTarget)), Is.True);

            for (int i = 0; i < 20; i++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(late.InjectLateCommand(new SimulationCommand(
                5,
                0,
                SimulationCommandType.SetGroupTarget,
                0,
                groupZeroTarget)), Is.True);
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                Assert.That(
                    lateWorld.GroupPathStates[group].ResolvedGoalIndex,
                    Is.EqualTo(onTimeWorld.GroupPathStates[group].ResolvedGoalIndex));
                Assert.That(
                    lateWorld.GroupPathStates[group].PendingSequence,
                    Is.EqualTo(onTimeWorld.GroupPathStates[group].PendingSequence));
            }
        }

        [Test]
        public void EvictedDerivedPath_IsRebuiltAllocationFreeWithoutConsumingRequestBudget()
        {
            SwarmWorld world = CreateWorld(64, 109u);
            StaticObstacleCollisionSystem obstacles = new();
            SharedPathNavigationSystem navigation = new(world, obstacles.Obstacles, 1, 1);
            WorldSnapshotRing snapshot = new(world.Config.Capacity, 4);
            snapshot.Save(world);
            int initialGoal = world.GroupPathStates[0].ResolvedGoalIndex;
            int alternativeGoal = FindReachableAlternative(navigation, 0, initialGoal);

            world.SetGroupTarget(0, navigation.Map.CellCenter(alternativeGoal));
            navigation.Execute(world);
            Assert.That(navigation.GetGroupPath(0).GoalIndex, Is.EqualTo(alternativeGoal));
            Assert.That(snapshot.TryRestore(world, 0), Is.True);

            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            navigation.Execute(world);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(navigation.LastProcessedPathRequests, Is.Zero);
            Assert.That(navigation.DerivedAStarRebuilds, Is.EqualTo(1));
            Assert.That(navigation.GetGroupPath(0).GoalIndex, Is.EqualTo(initialGoal));
            Assert.That(world.GroupPathStates[0].ResolvedGoalIndex, Is.EqualTo(initialGoal));
            Assert.That(allocated, Is.Zero);
        }

        private static SwarmWorld CreateWorld(int count, uint seed)
        {
            SwarmWorld world = new(SwarmConfig.PortfolioDefault(count));
            world.InitializeDeterministicFormation(count, seed);
            return world;
        }

        private static SharedPathNavigationSystem CreateNavigation(SwarmWorld world, int budget)
        {
            StaticObstacleCollisionSystem obstacles = new();
            return new SharedPathNavigationSystem(world, obstacles.Obstacles, budget);
        }

        private static int FindReachableAlternative(
            SharedPathNavigationSystem navigation,
            int group,
            int excludedGoal)
        {
            int start = navigation.GetGroupPath(group).StartIndex;
            GridMap map = navigation.Map;
            for (int index = map.NodeCount - 1; index >= 0; index--)
            {
                if (index != excludedGoal &&
                    map.IsWalkable(index) &&
                    navigation.Islands.AreConnected(start, index))
                {
                    return index;
                }
            }

            Assert.Fail("No reachable alternative goal was found for the test map.");
            return -1;
        }

        private static int FindBlockedNode(GridMap map)
        {
            for (int index = 0; index < map.NodeCount; index++)
            {
                if (!map.IsWalkable(index))
                {
                    return index;
                }
            }

            Assert.Fail("The static obstacle layout did not rasterize any blocked node.");
            return -1;
        }

        private static int FindWalkableNodeOnSide(GridMap map, int minXInclusive, int maxXExclusive)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = minXInclusive; x < maxXExclusive; x++)
                {
                    int index = map.ToIndex(x, y);
                    if (map.IsWalkable(index))
                    {
                        return index;
                    }
                }
            }

            Assert.Fail("No walkable node was found on the requested side of the barrier.");
            return -1;
        }

        private static bool PathContainsNode(SharedPath path, int node)
        {
            for (int i = 0; i < path.Count; i++)
            {
                if (path.NodeIndices[i] == node)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
