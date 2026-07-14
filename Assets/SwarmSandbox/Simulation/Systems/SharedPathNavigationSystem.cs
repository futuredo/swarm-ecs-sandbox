using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Simulation.Systems
{
    /// <summary>
    /// Budgeted shared-path scheduler for four squads. Request metadata is kept
    /// in rollback state while large waypoint buffers remain reconstructible
    /// derived caches. Steady-state execution performs no managed allocations.
    /// </summary>
    public sealed class SharedPathNavigationSystem
    {
        private const int InvalidNode = -1;
        private const int DefaultRollbackHistoryLength = 64;
        private const int DefaultPathCacheCapacity = SwarmWorld.GroupCount + DefaultRollbackHistoryLength;
        private static readonly FP WaypointRadiusSquared = FP.FromInt(9);
        private static readonly FP ArrivalRadiusSquared = FP.FromRatio(1, 4);

        private readonly GridMap _map;
        private readonly SwarmWorld _world;
        private readonly GridIslandMap _islands;
        private readonly AStarPathfinder _pathfinder;
        private readonly SharedPathCache _pathCache;
        private readonly SharedPath[] _groupPaths;
        private readonly int[] _initialStartIndices;
        private readonly bool[] _groupPathReady;
        private readonly bool[] _useExactGroupTarget;

        public SharedPathNavigationSystem(
            SwarmWorld world,
            FPOrientedBox2[] obstacles,
            int maxPathRequestsPerTick = 1,
            int pathCacheCapacity = DefaultPathCacheCapacity)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (obstacles == null)
            {
                throw new ArgumentNullException(nameof(obstacles));
            }

            if (maxPathRequestsPerTick <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPathRequestsPerTick));
            }

            MaxPathRequestsPerTick = maxPathRequestsPerTick;
            _world = world;
            _map = new GridMap(
                64,
                64,
                FP.FromRatio(5, 2),
                new FPVector2(FP.FromInt(-80), FP.FromInt(-80)));
            RasterizeObstacles(obstacles, world.Config.AgentRadius);
            BlurTraversalPenalties();

            _islands = new GridIslandMap(_map);
            _pathfinder = new AStarPathfinder(_map);
            _pathCache = new SharedPathCache(pathCacheCapacity, _map.NodeCount);
            _groupPaths = new SharedPath[SwarmWorld.GroupCount];
            _initialStartIndices = new int[SwarmWorld.GroupCount];
            _groupPathReady = new bool[SwarmWorld.GroupCount];
            _useExactGroupTarget = new bool[SwarmWorld.GroupCount];

            world.NextPathRequestSequence = 0;
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                _groupPaths[group] = new SharedPath(_map.NodeCount);
                FPVector2 startPosition = GetInitialStartPosition(group);
                _initialStartIndices[group] = TryResolveNode(startPosition, out int startIndex)
                    ? startIndex
                    : InvalidNode;
                int goalIndex = TryResolveNode(world.GroupTargets[group], out int resolvedGoal)
                    ? resolvedGoal
                    : InvalidNode;

                GroupPathState state = GroupPathState.CreateEmpty();
                if (CanSearch(_initialStartIndices[group], goalIndex) &&
                    _pathfinder.FindSharedPath(_initialStartIndices[group], goalIndex, _groupPaths[group]))
                {
                    state.ResolveActive(_initialStartIndices[group], goalIndex, _map.Revision);
                    _pathCache.Store(_groupPaths[group]);
                    ResetGroupPathCursors(world, group, _groupPaths[group].Count);
                }
                else
                {
                    state.ResolveUnreachable(_initialStartIndices[group], goalIndex, _map.Revision);
                    ResetGroupPathCursors(world, group, 0);
                }

                world.GroupPathStates[group] = state;
            }
        }

        public GridMap Map => _map;

        public GridIslandMap Islands => _islands;

        public int MaxPathRequestsPerTick { get; }

        public int PathCacheCapacity => _pathCache.Capacity;

        public int LastProcessedPathRequests { get; private set; }

        public int CacheHits { get; private set; }

        public int CacheMisses { get; private set; }

        public int IslandRejectedRequests { get; private set; }

        public int DerivedPathRestores { get; private set; }

        public int DerivedCacheRestores { get; private set; }

        /// <summary>
        /// Exceptional rollback reconstruction work after a derived-cache miss.
        /// It is separate from the authoritative request budget because changing
        /// replay timing would change simulation state.
        /// </summary>
        public int DerivedAStarRebuilds { get; private set; }

        public int PendingPathRequests
        {
            get
            {
                int count = 0;
                for (int group = 0; group < SwarmWorld.GroupCount; group++)
                {
                    if (_world.GroupPathStates[group].HasPending)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int TotalSharedWaypoints
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _groupPaths.Length; i++)
                {
                    count += _groupPaths[i].Count;
                }

                return count;
            }
        }

        public SharedPath GetGroupPath(int group)
        {
            if ((uint)group >= SwarmWorld.GroupCount)
            {
                throw new ArgumentOutOfRangeException(nameof(group));
            }

            return _groupPaths[group];
        }

        public void Execute(SwarmWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!ReferenceEquals(world, _world))
            {
                throw new ArgumentException("A navigation system can only execute against its constructor world.", nameof(world));
            }

            DetectPathRequests(world);
            ProcessPathRequestBudget(world);
            PrepareDerivedPaths(world);

            for (int i = 0; i < world.Count; i++)
            {
                int group = world.Groups[i];
                if (!_groupPathReady[group])
                {
                    world.PreferredVelocities[i] = FPVector2.Zero;
                    continue;
                }

                SharedPath path = _groupPaths[group];
                int cursor = world.PathCursors[i];
                if (cursor >= path.Count)
                {
                    cursor = path.Count - 1;
                    world.PathCursors[i] = (ushort)cursor;
                }

                bool isFinalWaypoint = cursor + 1 >= path.Count;
                FPVector2 target = isFinalWaypoint && _useExactGroupTarget[group]
                    ? world.GetTargetForAgent(i)
                    : path.Waypoints[cursor] + world.FormationOffsets[i];

                FPVector2 delta = target - world.Positions[i];
                if (delta.SqrMagnitude <= WaypointRadiusSquared && cursor + 1 < path.Count)
                {
                    cursor++;
                    world.PathCursors[i] = (ushort)cursor;
                    isFinalWaypoint = cursor + 1 >= path.Count;
                    target = isFinalWaypoint && _useExactGroupTarget[group]
                        ? world.GetTargetForAgent(i)
                        : path.Waypoints[cursor] + world.FormationOffsets[i];
                }

                FPVector2 toTarget = target - world.Positions[i];
                world.PreferredVelocities[i] = toTarget.SqrMagnitude <= ArrivalRadiusSquared
                    ? FPVector2.Zero
                    : FPMath.NormalizeSafe(toTarget) * world.MaxSpeeds[i];
            }
        }

        private void DetectPathRequests(SwarmWorld world)
        {
            int mapRevision = _map.Revision;
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                int goalIndex = TryResolveNode(world.GroupTargets[group], out int goal)
                    ? goal
                    : InvalidNode;
                GroupPathState state = world.GroupPathStates[group];

                if (state.IsResolvedFor(goalIndex, mapRevision))
                {
                    if (state.HasPending)
                    {
                        state.ClearPending();
                        world.GroupPathStates[group] = state;
                    }

                    continue;
                }

                if (state.IsPendingFor(goalIndex, mapRevision))
                {
                    continue;
                }

                int startIndex = FindGroupAnchor(world, group);
                int sequence = world.NextPathRequestSequence;
                world.NextPathRequestSequence = sequence == int.MaxValue ? 0 : sequence + 1;
                state.Queue(startIndex, goalIndex, mapRevision, sequence);
                world.GroupPathStates[group] = state;
            }
        }

        private void ProcessPathRequestBudget(SwarmWorld world)
        {
            LastProcessedPathRequests = 0;
            for (int request = 0; request < MaxPathRequestsPerTick; request++)
            {
                int group = FindOldestPendingGroup(world);
                if (group < 0)
                {
                    break;
                }

                ProcessPathRequest(world, group);
                LastProcessedPathRequests++;
            }
        }

        private int FindOldestPendingGroup(SwarmWorld world)
        {
            int selectedGroup = -1;
            int selectedSequence = int.MaxValue;
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                GroupPathState state = world.GroupPathStates[group];
                if (!state.HasPending)
                {
                    continue;
                }

                if (selectedGroup < 0 || state.PendingSequence < selectedSequence)
                {
                    selectedGroup = group;
                    selectedSequence = state.PendingSequence;
                }
            }

            return selectedGroup;
        }

        private void ProcessPathRequest(SwarmWorld world, int group)
        {
            GroupPathState state = world.GroupPathStates[group];
            int startIndex = state.PendingStartIndex;
            int goalIndex = state.PendingGoalIndex;
            int mapRevision = state.PendingMapRevision;
            SharedPath destination = _groupPaths[group];

            if (mapRevision != _map.Revision || !CanSearch(startIndex, goalIndex))
            {
                state.ResolveUnreachable(startIndex, goalIndex, mapRevision);
                destination.Invalidate();
                ResetGroupPathCursors(world, group, 0);
                IslandRejectedRequests++;
                world.GroupPathStates[group] = state;
                return;
            }

            bool found;
            if (_pathCache.TryCopyTo(startIndex, goalIndex, mapRevision, destination))
            {
                CacheHits++;
                found = true;
            }
            else
            {
                CacheMisses++;
                found = _pathfinder.FindSharedPath(startIndex, goalIndex, destination);
                if (found)
                {
                    _pathCache.Store(destination);
                }
            }

            if (found)
            {
                state.ResolveActive(startIndex, goalIndex, mapRevision);
                ResetGroupPathCursors(world, group, destination.Count);
            }
            else
            {
                state.ResolveUnreachable(startIndex, goalIndex, mapRevision);
                destination.Invalidate();
                ResetGroupPathCursors(world, group, 0);
            }

            world.GroupPathStates[group] = state;
        }

        private void PrepareDerivedPaths(SwarmWorld world)
        {
            int mapRevision = _map.Revision;
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                GroupPathState state = world.GroupPathStates[group];
                bool ready = state.Status == GroupPathStatus.Active &&
                    state.ResolvedMapRevision == mapRevision &&
                    EnsureDerivedPath(state, _groupPaths[group]);
                _groupPathReady[group] = ready;

                int currentGoal = TryResolveNode(world.GroupTargets[group], out int goal)
                    ? goal
                    : InvalidNode;
                _useExactGroupTarget[group] = ready &&
                    !state.HasPending &&
                    state.IsResolvedFor(currentGoal, mapRevision);

                if (!ready)
                {
                    _groupPaths[group].Invalidate();
                }
            }
        }

        private bool EnsureDerivedPath(GroupPathState state, SharedPath destination)
        {
            if (destination.IsReusableFor(
                _map,
                state.ResolvedStartIndex,
                state.ResolvedGoalIndex))
            {
                return true;
            }

            if (_pathCache.TryCopyTo(
                state.ResolvedStartIndex,
                state.ResolvedGoalIndex,
                state.ResolvedMapRevision,
                destination))
            {
                DerivedPathRestores++;
                DerivedCacheRestores++;
                return true;
            }

            if (!_pathfinder.FindSharedPath(
                state.ResolvedStartIndex,
                state.ResolvedGoalIndex,
                destination))
            {
                return false;
            }

            _pathCache.Store(destination);
            DerivedPathRestores++;
            DerivedAStarRebuilds++;
            return true;
        }

        private bool CanSearch(int startIndex, int goalIndex)
        {
            return (uint)startIndex < (uint)_map.NodeCount &&
                (uint)goalIndex < (uint)_map.NodeCount &&
                _map.IsWalkable(startIndex) &&
                _map.IsWalkable(goalIndex) &&
                _islands.AreConnected(startIndex, goalIndex);
        }

        private int FindGroupAnchor(SwarmWorld world, int group)
        {
            long centerXRaw = 0;
            long centerYRaw = 0;
            int memberCount = 0;
            for (int i = 0; i < world.Count; i++)
            {
                if (world.Groups[i] != group)
                {
                    continue;
                }

                // Waypoints later receive FormationOffsets[i], so the shared
                // route must start from the logical squad center rather than a
                // raw member position that already contains that offset.
                centerXRaw += (long)world.Positions[i].X.Raw - world.FormationOffsets[i].X.Raw;
                centerYRaw += (long)world.Positions[i].Y.Raw - world.FormationOffsets[i].Y.Raw;
                memberCount++;
            }

            if (memberCount > 0)
            {
                FPVector2 center = new(
                    FP.FromRaw((int)(centerXRaw / memberCount)),
                    FP.FromRaw((int)(centerYRaw / memberCount)));
                int nearest = FindNearestWalkableNode(center);
                if (nearest != InvalidNode)
                {
                    return nearest;
                }
            }

            int initial = _initialStartIndices[group];
            return (uint)initial < (uint)_map.NodeCount && _map.IsWalkable(initial)
                ? initial
                : InvalidNode;
        }

        private int FindNearestWalkableNode(FPVector2 worldPosition)
        {
            if (TryResolveNode(worldPosition, out int direct) && _map.IsWalkable(direct))
            {
                return direct;
            }

            int bestNode = InvalidNode;
            long bestDistanceSquared = long.MaxValue;
            for (int node = 0; node < _map.NodeCount; node++)
            {
                if (!_map.IsWalkable(node))
                {
                    continue;
                }

                FPVector2 center = _map.CellCenter(node);
                long deltaX = (long)center.X.Raw - worldPosition.X.Raw;
                long deltaY = (long)center.Y.Raw - worldPosition.Y.Raw;
                long distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestNode = node;
                }
            }

            return bestNode;
        }

        private bool TryResolveNode(FPVector2 worldPosition, out int nodeIndex)
        {
            if (_map.TryWorldToCell(worldPosition, out int x, out int y))
            {
                nodeIndex = _map.ToIndex(x, y);
                return true;
            }

            nodeIndex = InvalidNode;
            return false;
        }

        private static void ResetGroupPathCursors(SwarmWorld world, int group, int pathCount)
        {
            ushort cursor = pathCount > 1 ? (ushort)1 : (ushort)0;
            for (int i = group; i < world.Count; i += SwarmWorld.GroupCount)
            {
                if (world.Groups[i] == group)
                {
                    world.PathCursors[i] = cursor;
                }
            }
        }

        private static FPVector2 GetInitialStartPosition(int group)
        {
            return group switch
            {
                0 => new FPVector2(FP.FromInt(-46), FP.FromInt(-46)),
                1 => new FPVector2(FP.FromInt(46), FP.FromInt(-46)),
                2 => new FPVector2(FP.FromInt(46), FP.FromInt(46)),
                _ => new FPVector2(FP.FromInt(-46), FP.FromInt(46)),
            };
        }

        private void RasterizeObstacles(FPOrientedBox2[] obstacles, FP agentRadius)
        {
            FP clearance = (_map.CellSize * FP.FromRatio(3, 5)) + agentRadius;
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    FPCircle2 sample = new(_map.CellCenter(x, y), clearance);
                    for (int obstacleIndex = 0; obstacleIndex < obstacles.Length; obstacleIndex++)
                    {
                        FPOrientedBox2 obstacle = obstacles[obstacleIndex];
                        if (!FPSat2D.Intersect(in obstacle, in sample, out _, out _))
                        {
                            continue;
                        }

                        _map.SetWalkable(x, y, false);
                        break;
                    }
                }
            }
        }

        private void BlurTraversalPenalties()
        {
            const int radius = 3;
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    if (!_map.IsWalkable(x, y))
                    {
                        continue;
                    }

                    int penalty = 0;
                    for (int offsetY = -radius; offsetY <= radius; offsetY++)
                    {
                        for (int offsetX = -radius; offsetX <= radius; offsetX++)
                        {
                            int sampleX = x + offsetX;
                            int sampleY = y + offsetY;
                            if (!_map.IsInside(sampleX, sampleY) || _map.IsWalkable(sampleX, sampleY))
                            {
                                continue;
                            }

                            penalty += GaussianWeight(offsetX) * GaussianWeight(offsetY);
                        }
                    }

                    if (penalty > 0)
                    {
                        _map.SetPenalty(x, y, penalty);
                    }
                }
            }
        }

        private static int GaussianWeight(int offset)
        {
            int absolute = offset < 0 ? -offset : offset;
            return absolute switch
            {
                0 => 20,
                1 => 12,
                2 => 5,
                3 => 1,
                _ => 0,
            };
        }
    }
}
