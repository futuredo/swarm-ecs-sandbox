using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Spatial;
using SwarmECS.Simulation.Systems.Parallel;

namespace SwarmECS.Simulation.Systems
{

public sealed class NeighborAvoidanceSystem : IDisposable
{
    private const int ParallelCapacityThreshold = 2048;
    private const int MaxExecutionLanes = 16;
    private const int AgentsPerUsefulLane = 512;

    private readonly UniformGrid2D _uniformGrid;
    private readonly DataOrientedKdTree2D _kdTree;
    private readonly int[] _kdQueryResults;
    private readonly StaticObstacleBvh2D _obstacleBroadphase;
    private readonly ObstacleSegment[] _obstacleSegments;
    private readonly int _obstacleSegmentCount;
    private readonly AvoidanceWorkerScratch _mainScratch;
    private readonly AvoidanceWorkerPool _workerPool;
    private bool _disposed;

    public NeighborAvoidanceSystem(SwarmConfig config)
        : this(config, null)
    {
    }

    public NeighborAvoidanceSystem(
        SwarmConfig config,
        StaticObstacleCollisionSystem staticObstacles)
    {
        _obstacleBroadphase = staticObstacles?.Broadphase;
        _obstacleSegments = staticObstacles?.ObstacleSegmentData;
        _obstacleSegmentCount = staticObstacles?.ObstacleSegmentCount ?? 0;
        _uniformGrid = new UniformGrid2D(config.Capacity, config.NeighborDistance);
        _kdTree = new DataOrientedKdTree2D(config.Capacity);
        _mainScratch = new AvoidanceWorkerScratch(
            config.Capacity,
            config.MaxNeighbors,
            _obstacleBroadphase,
            _obstacleSegmentCount);
        _kdQueryResults = new int[_mainScratch.QueryLimit];

        if (config.Capacity >= ParallelCapacityThreshold)
        {
            int backgroundWorkerCount = DetermineBackgroundWorkerCount(config.Capacity);
            _workerPool = new AvoidanceWorkerPool(
                this,
                backgroundWorkerCount,
                config.Capacity,
                config.MaxNeighbors,
                _obstacleBroadphase,
                _obstacleSegmentCount);
        }

        Mode = config.SpatialIndexMode;
    }

    public SpatialIndexMode Mode { get; private set; }

    public int BackgroundWorkerCount => _workerPool?.BackgroundWorkerCount ?? 0;

    public int LastNeighborLinks { get; private set; }

    public int LastOrcaLines { get; private set; }

    public int LastObstacleOrcaLines { get; private set; }

    public int LastAgentOrcaLines { get; private set; }

    public int LastObstacleBroadphaseQueries { get; private set; }

    public void Execute(SwarmWorld world)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NeighborAvoidanceSystem));
        }

        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        Mode = world.SpatialIndexMode;
        LastObstacleBroadphaseQueries = _obstacleBroadphase == null || _obstacleSegmentCount == 0
            ? 0
            : world.Count;

        if (Mode == SpatialIndexMode.KdTree || Mode == SpatialIndexMode.KdTreeKNearest)
        {
            _kdTree.Build(world.Positions, world.Count);
            ExecuteKdTree(
                world,
                Mode == SpatialIndexMode.KdTreeKNearest,
                out int kdNeighborLinks,
                out int kdObstacleOrcaLines,
                out int kdAgentOrcaLines);
            LastNeighborLinks = kdNeighborLinks;
            SetLineMetrics(kdObstacleOrcaLines, kdAgentOrcaLines);
            return;
        }

        _uniformGrid.Build(world.Positions, world.Count);
        if (_workerPool != null && world.Count >= ParallelCapacityThreshold)
        {
            _workerPool.Execute(
                world,
                _mainScratch,
                out int parallelNeighborLinks,
                out int parallelObstacleOrcaLines,
                out int parallelAgentOrcaLines);
            LastNeighborLinks = parallelNeighborLinks;
            SetLineMetrics(parallelObstacleOrcaLines, parallelAgentOrcaLines);
            return;
        }

        ExecuteUniformGridRange(
            world,
            0,
            world.Count,
            _mainScratch,
            out int neighborLinks,
            out int obstacleOrcaLines,
            out int agentOrcaLines);
        LastNeighborLinks = neighborLinks;
        SetLineMetrics(obstacleOrcaLines, agentOrcaLines);
    }

    internal void ExecuteUniformGridRange(
        SwarmWorld world,
        int start,
        int end,
        AvoidanceWorkerScratch scratch,
        out int neighborLinks,
        out int obstacleOrcaLines,
        out int agentOrcaLines)
    {
        int rangeNeighborLinks = 0;
        int rangeObstacleOrcaLines = 0;
        int rangeAgentOrcaLines = 0;
        for (int i = start; i < end; ++i)
        {
            _uniformGrid.QueryRadius(
                world.Positions[i],
                world.Config.NeighborDistance,
                scratch.QueryLimit,
                scratch.QueryEntityIds,
                scratch.QueryDistances,
                out int queryCount);

            int neighborCount = 0;
            for (int resultIndex = 0;
                resultIndex < queryCount && neighborCount < scratch.Neighbors.Length;
                ++resultIndex)
            {
                int other = scratch.QueryEntityIds[resultIndex];
                if (other == i)
                {
                    continue;
                }

                scratch.Neighbors[neighborCount++] = new AgentNeighbor(
                    other,
                    world.Positions[other] - world.Positions[i],
                    world.Velocities[other],
                    world.Radii[other]);
            }

            int obstacleNeighborCount = CollectObstacleNeighbors(world, i, scratch);
            int totalLineCount = OrcaSolver.Solve(
                i,
                world.Positions[i],
                world.Velocities[i],
                world.PreferredVelocities[i],
                world.Radii[i],
                world.MaxSpeeds[i],
                world.Config.TimeHorizon,
                world.Config.FixedDeltaTime,
                scratch.ObstacleNeighbors,
                obstacleNeighborCount,
                scratch.Neighbors,
                neighborCount,
                scratch.Lines,
                scratch.ProjectionLines,
                out int obstacleLineCount,
                out world.NextVelocities[i]);
            rangeObstacleOrcaLines += obstacleLineCount;
            rangeAgentOrcaLines += totalLineCount - obstacleLineCount;
            rangeNeighborLinks += neighborCount;
        }

        neighborLinks = rangeNeighborLinks;
        obstacleOrcaLines = rangeObstacleOrcaLines;
        agentOrcaLines = rangeAgentOrcaLines;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workerPool?.Dispose();
    }

    private void ExecuteKdTree(
        SwarmWorld world,
        bool useKNearest,
        out int neighborLinks,
        out int obstacleOrcaLines,
        out int agentOrcaLines)
    {
        neighborLinks = 0;
        obstacleOrcaLines = 0;
        agentOrcaLines = 0;
        for (int i = 0; i < world.Count; ++i)
        {
            int queryCount;
            if (useKNearest)
            {
                // QueryLimit reserves one slot for the querying entity itself.
                // Filtering below therefore still yields at most MaxNeighbors.
                _kdTree.QueryKNearest(
                    world.Positions[i],
                    _mainScratch.QueryLimit,
                    _kdQueryResults,
                    out queryCount);
            }
            else
            {
                _kdTree.QueryRadius(
                    world.Positions[i],
                    world.Config.NeighborDistance,
                    _kdQueryResults,
                    out queryCount);
            }

            int neighborCount = 0;
            for (int resultIndex = 0;
                resultIndex < queryCount && neighborCount < _mainScratch.Neighbors.Length;
                ++resultIndex)
            {
                int other = _kdQueryResults[resultIndex];
                if (other == i)
                {
                    continue;
                }

                _mainScratch.Neighbors[neighborCount++] = new AgentNeighbor(
                    other,
                    world.Positions[other] - world.Positions[i],
                    world.Velocities[other],
                    world.Radii[other]);
            }

            int obstacleNeighborCount = CollectObstacleNeighbors(world, i, _mainScratch);
            int totalLineCount = OrcaSolver.Solve(
                i,
                world.Positions[i],
                world.Velocities[i],
                world.PreferredVelocities[i],
                world.Radii[i],
                world.MaxSpeeds[i],
                world.Config.TimeHorizon,
                world.Config.FixedDeltaTime,
                _mainScratch.ObstacleNeighbors,
                obstacleNeighborCount,
                _mainScratch.Neighbors,
                neighborCount,
                _mainScratch.Lines,
                _mainScratch.ProjectionLines,
                out int obstacleLineCount,
                out world.NextVelocities[i]);
            obstacleOrcaLines += obstacleLineCount;
            agentOrcaLines += totalLineCount - obstacleLineCount;
            neighborLinks += neighborCount;
        }
    }

    private int CollectObstacleNeighbors(
        SwarmWorld world,
        int entityIndex,
        AvoidanceWorkerScratch scratch)
    {
        if (_obstacleBroadphase == null || _obstacleSegmentCount == 0)
        {
            return 0;
        }

        FP safeRadius = FPMath.Max(world.Radii[entityIndex], FP.Zero);
        FP safeSpeed = FPMath.Max(world.MaxSpeeds[entityIndex], FP.Zero);
        FP queryRadius = (world.Config.TimeHorizon * safeSpeed) + safeRadius;
        FPVector2 position = world.Positions[entityIndex];
        FPAabb2 query = new FPAabb2(position, position).Expanded(queryRadius);
        _obstacleBroadphase.QueryAabb(in query, scratch.ObstacleQuery, out int obstacleCount);

        ulong queryRadiusRaw = (ulong)(uint)queryRadius.Raw;
        ulong queryRadiusSquared = queryRadiusRaw * queryRadiusRaw;
        int neighborCount = 0;
        for (int candidateIndex = 0; candidateIndex < obstacleCount; ++candidateIndex)
        {
            int obstacleId = scratch.ObstacleQuery.ObstacleIds[candidateIndex];
            int firstSegment = obstacleId * StaticObstacleSegmentBuilder.EdgesPerBox;
            int segmentEnd = firstSegment + StaticObstacleSegmentBuilder.EdgesPerBox;
            for (int segmentIndex = firstSegment; segmentIndex < segmentEnd; ++segmentIndex)
            {
                ObstacleSegment segment = _obstacleSegments[segmentIndex];
                // Segments are CCW and the solid is on their left. RVO2 obstacle
                // neighbors are one-sided: an outside agent may only see the edge
                // from its right/free side. Feeding the opposite box faces would add
                // back-face half-planes and can over-constrain otherwise safe motion.
                if (FPMath.Det(segment.Direction, position - segment.Start) >= FP.Zero)
                {
                    continue;
                }

                if (StaticObstacleSegmentBuilder.DistanceSquaredRaw(position, in segment) >
                    queryRadiusSquared)
                {
                    continue;
                }

                scratch.ObstacleNeighbors[neighborCount++] = new ObstacleNeighbor(segment);
            }
        }

        return neighborCount;
    }

    private void SetLineMetrics(int obstacleOrcaLines, int agentOrcaLines)
    {
        LastObstacleOrcaLines = obstacleOrcaLines;
        LastAgentOrcaLines = agentOrcaLines;
        LastOrcaLines = obstacleOrcaLines + agentOrcaLines;
    }

    private static int DetermineBackgroundWorkerCount(int capacity)
    {
        int usefulLaneCount = (int)(((long)capacity + AgentsPerUsefulLane - 1L) / AgentsPerUsefulLane);
        if (usefulLaneCount < 2)
        {
            usefulLaneCount = 2;
        }
        else if (usefulLaneCount > MaxExecutionLanes)
        {
            usefulLaneCount = MaxExecutionLanes;
        }

        int cpuBound = Environment.ProcessorCount - 1;
        if (cpuBound < 1)
        {
            cpuBound = 1;
        }

        int usefulBackgroundWorkers = usefulLaneCount - 1;
        return cpuBound < usefulBackgroundWorkers ? cpuBound : usefulBackgroundWorkers;
    }
}
}
