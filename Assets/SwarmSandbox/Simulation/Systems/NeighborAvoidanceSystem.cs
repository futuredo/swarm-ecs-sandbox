using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;
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
    private readonly AvoidanceWorkerScratch _mainScratch;
    private readonly AvoidanceWorkerPool _workerPool;
    private bool _disposed;

    public NeighborAvoidanceSystem(SwarmConfig config)
    {
        _uniformGrid = new UniformGrid2D(config.Capacity, config.NeighborDistance);
        _kdTree = new DataOrientedKdTree2D(config.Capacity);
        _mainScratch = new AvoidanceWorkerScratch(config.Capacity, config.MaxNeighbors);
        _kdQueryResults = new int[_mainScratch.QueryLimit];

        if (config.Capacity >= ParallelCapacityThreshold)
        {
            int backgroundWorkerCount = DetermineBackgroundWorkerCount(config.Capacity);
            _workerPool = new AvoidanceWorkerPool(
                this,
                backgroundWorkerCount,
                config.Capacity,
                config.MaxNeighbors);
        }

        Mode = config.SpatialIndexMode;
    }

    public SpatialIndexMode Mode { get; private set; }

    public int BackgroundWorkerCount => _workerPool?.BackgroundWorkerCount ?? 0;

    public int LastNeighborLinks { get; private set; }

    public int LastOrcaLines { get; private set; }

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

        if (Mode == SpatialIndexMode.KdTree || Mode == SpatialIndexMode.KdTreeKNearest)
        {
            _kdTree.Build(world.Positions, world.Count);
            ExecuteKdTree(
                world,
                Mode == SpatialIndexMode.KdTreeKNearest,
                out int kdNeighborLinks,
                out int kdOrcaLines);
            LastNeighborLinks = kdNeighborLinks;
            LastOrcaLines = kdOrcaLines;
            return;
        }

        _uniformGrid.Build(world.Positions, world.Count);
        if (_workerPool != null && world.Count >= ParallelCapacityThreshold)
        {
            _workerPool.Execute(
                world,
                _mainScratch,
                out int parallelNeighborLinks,
                out int parallelOrcaLines);
            LastNeighborLinks = parallelNeighborLinks;
            LastOrcaLines = parallelOrcaLines;
            return;
        }

        ExecuteUniformGridRange(
            world,
            0,
            world.Count,
            _mainScratch,
            out int neighborLinks,
            out int orcaLines);
        LastNeighborLinks = neighborLinks;
        LastOrcaLines = orcaLines;
    }

    internal void ExecuteUniformGridRange(
        SwarmWorld world,
        int start,
        int end,
        AvoidanceWorkerScratch scratch,
        out int neighborLinks,
        out int orcaLines)
    {
        int rangeNeighborLinks = 0;
        int rangeOrcaLines = 0;
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

            rangeOrcaLines += OrcaSolver.Solve(
                i,
                world.Velocities[i],
                world.PreferredVelocities[i],
                world.Radii[i],
                world.MaxSpeeds[i],
                world.Config.TimeHorizon,
                world.Config.FixedDeltaTime,
                scratch.Neighbors,
                neighborCount,
                scratch.Lines,
                scratch.ProjectionLines,
                out world.NextVelocities[i]);
            rangeNeighborLinks += neighborCount;
        }

        neighborLinks = rangeNeighborLinks;
        orcaLines = rangeOrcaLines;
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
        out int orcaLines)
    {
        neighborLinks = 0;
        orcaLines = 0;
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

            orcaLines += OrcaSolver.Solve(
                i,
                world.Velocities[i],
                world.PreferredVelocities[i],
                world.Radii[i],
                world.MaxSpeeds[i],
                world.Config.TimeHorizon,
                world.Config.FixedDeltaTime,
                _mainScratch.Neighbors,
                neighborCount,
                _mainScratch.Lines,
                _mainScratch.ProjectionLines,
                out world.NextVelocities[i]);
            neighborLinks += neighborCount;
        }
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
