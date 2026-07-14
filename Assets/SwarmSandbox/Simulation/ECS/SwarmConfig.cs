using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation
{

public enum SpatialIndexMode : byte
{
    UniformGrid = 0,
    KdTree = 1,
    KdTreeKNearest = 2,
}

public readonly struct SwarmConfig
{
    public const int ConfigHashSchemaVersion = 1;

    public static readonly FP DefaultMaxAcceleration = FP.FromInt(24);

    /// <summary>
    /// Deterministic cosine/sine pair for a 12 degree turn step. The raw pair
    /// has unit squared length under the Q16.16 arithmetic used by the simulation.
    /// </summary>
    public static readonly FPVector2 DefaultMaxTurnStep = new(
        FP.FromRaw(64104),
        FP.FromRaw(13626));

    /// <summary>Source-compatible v0.2.1 constructor using v0.2.2 motion defaults.</summary>
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
        : this(
            capacity,
            fixedDeltaTime,
            agentRadius,
            maxSpeed,
            DefaultMaxAcceleration,
            DefaultMaxTurnStep,
            neighborDistance,
            maxNeighbors,
            timeHorizon,
            worldHalfExtent,
            spatialIndexMode)
    {
    }

    public SwarmConfig(
        int capacity,
        FP fixedDeltaTime,
        FP agentRadius,
        FP maxSpeed,
        FP maxAcceleration,
        FPVector2 maxTurnStep,
        FP neighborDistance,
        int maxNeighbors,
        FP timeHorizon,
        FP worldHalfExtent,
        SpatialIndexMode spatialIndexMode)
    {
        if (maxAcceleration < FP.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAcceleration),
                "Maximum acceleration must be non-negative.");
        }

        ValidateMaxTurnStep(maxTurnStep);

        Capacity = capacity;
        FixedDeltaTime = fixedDeltaTime;
        AgentRadius = agentRadius;
        MaxSpeed = maxSpeed;
        MaxAcceleration = maxAcceleration;
        MaxTurnStep = maxTurnStep;
        NeighborDistance = neighborDistance;
        MaxNeighbors = maxNeighbors;
        TimeHorizon = timeHorizon;
        WorldHalfExtent = worldHalfExtent;
        SpatialIndexMode = spatialIndexMode;
        ConfigHash = ComputeConfigHash(
            capacity,
            fixedDeltaTime,
            agentRadius,
            maxSpeed,
            maxAcceleration,
            maxTurnStep,
            neighborDistance,
            maxNeighbors,
            timeHorizon,
            worldHalfExtent,
            spatialIndexMode);
    }

    public int Capacity { get; }

    public FP FixedDeltaTime { get; }

    public FP AgentRadius { get; }

    public FP MaxSpeed { get; }

    public FP MaxAcceleration { get; }

    /// <summary>
    /// Per-tick maximum turn encoded as (cos(delta angle), sin(delta angle)).
    /// Keeping the pair in raw fixed point avoids platform-dependent runtime trigonometry.
    /// </summary>
    public FPVector2 MaxTurnStep { get; }

    public FP NeighborDistance { get; }

    public int MaxNeighbors { get; }

    public FP TimeHorizon { get; }

    public FP WorldHalfExtent { get; }

    public SpatialIndexMode SpatialIndexMode { get; }

    /// <summary>
    /// Stable fingerprint of immutable simulation configuration. It is intentionally
    /// separate from the mutable world-state hash and is not copied into every snapshot.
    /// </summary>
    public ulong ConfigHash { get; }

    public SwarmConfig WithSpatialIndexMode(SpatialIndexMode spatialIndexMode)
    {
        return new SwarmConfig(
            Capacity,
            FixedDeltaTime,
            AgentRadius,
            MaxSpeed,
            MaxAcceleration,
            MaxTurnStep,
            NeighborDistance,
            MaxNeighbors,
            TimeHorizon,
            WorldHalfExtent,
            spatialIndexMode);
    }

    public static SwarmConfig DemoDefault(int capacity = 10_000)
    {
        return new SwarmConfig(
            capacity,
            FP.FromRatio(1, 30),
            FP.FromRatio(7, 20),
            FP.FromInt(6),
            DefaultMaxAcceleration,
            DefaultMaxTurnStep,
            FP.FromInt(4),
            8,
            FP.FromInt(2),
            FP.FromInt(80),
            SpatialIndexMode.UniformGrid);
    }

    private static ulong ComputeConfigHash(
        int capacity,
        FP fixedDeltaTime,
        FP agentRadius,
        FP maxSpeed,
        FP maxAcceleration,
        FPVector2 maxTurnStep,
        FP neighborDistance,
        int maxNeighbors,
        FP timeHorizon,
        FP worldHalfExtent,
        SpatialIndexMode spatialIndexMode)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        hash = MixHash(hash, ConfigHashSchemaVersion, prime);
        hash = MixHash(hash, capacity, prime);
        hash = MixHash(hash, fixedDeltaTime.Raw, prime);
        hash = MixHash(hash, agentRadius.Raw, prime);
        hash = MixHash(hash, maxSpeed.Raw, prime);
        hash = MixHash(hash, maxAcceleration.Raw, prime);
        hash = MixHash(hash, maxTurnStep.X.Raw, prime);
        hash = MixHash(hash, maxTurnStep.Y.Raw, prime);
        hash = MixHash(hash, neighborDistance.Raw, prime);
        hash = MixHash(hash, maxNeighbors, prime);
        hash = MixHash(hash, timeHorizon.Raw, prime);
        hash = MixHash(hash, worldHalfExtent.Raw, prime);
        hash = MixHash(hash, (int)spatialIndexMode, prime);
        return hash;
    }

    private static void ValidateMaxTurnStep(FPVector2 maxTurnStep)
    {
        if (maxTurnStep.X < FP.Zero || maxTurnStep.Y < FP.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTurnStep),
                "Maximum turn cosine/sine must be in the first quadrant.");
        }

        FP lengthSquared = maxTurnStep.SqrMagnitude;
        if (FPMath.Abs(lengthSquared - FP.One).Raw > 8)
        {
            throw new ArgumentException(
                "Maximum turn cosine/sine must form a Q16.16 unit pair.",
                nameof(maxTurnStep));
        }
    }

    private static ulong MixHash(ulong hash, int value, ulong prime)
    {
        unchecked
        {
            uint bits = (uint)value;
            hash = (hash ^ (byte)bits) * prime;
            hash = (hash ^ (byte)(bits >> 8)) * prime;
            hash = (hash ^ (byte)(bits >> 16)) * prime;
            hash = (hash ^ (byte)(bits >> 24)) * prime;
            return hash;
        }
    }
}
}
