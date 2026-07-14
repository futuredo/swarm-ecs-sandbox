using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation
{

public enum SpatialIndexMode : byte
{
    UniformGrid = 0,
    KdTree = 1,
}

public readonly struct SwarmConfig
{
    public SwarmConfig(
        int capacity,
        FP fixedDeltaTime,
        FP agentRadius,
        FP maxSpeed,
        FP neighborDistance,
        int maxNeighbors,
        FP timeHorizon,
        FP worldHalfExtent,
        SpatialIndexMode spatialIndexMode)
    {
        Capacity = capacity;
        FixedDeltaTime = fixedDeltaTime;
        AgentRadius = agentRadius;
        MaxSpeed = maxSpeed;
        NeighborDistance = neighborDistance;
        MaxNeighbors = maxNeighbors;
        TimeHorizon = timeHorizon;
        WorldHalfExtent = worldHalfExtent;
        SpatialIndexMode = spatialIndexMode;
    }

    public int Capacity { get; }

    public FP FixedDeltaTime { get; }

    public FP AgentRadius { get; }

    public FP MaxSpeed { get; }

    public FP NeighborDistance { get; }

    public int MaxNeighbors { get; }

    public FP TimeHorizon { get; }

    public FP WorldHalfExtent { get; }

    public SpatialIndexMode SpatialIndexMode { get; }

    public static SwarmConfig PortfolioDefault(int capacity = 10_000)
    {
        return new SwarmConfig(
            capacity,
            FP.FromRatio(1, 30),
            FP.FromRatio(7, 20),
            FP.FromInt(6),
            FP.FromInt(4),
            8,
            FP.FromInt(2),
            FP.FromInt(80),
            SpatialIndexMode.UniformGrid);
    }
}
}
