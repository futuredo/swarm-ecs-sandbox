using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Simulation
{

/// <summary>
/// Data-oriented simulation world. Hot component columns are contiguous arrays and never resize.
/// UnityEngine types are intentionally absent from this assembly.
/// </summary>
public sealed class SwarmWorld
{
    public const int GroupCount = 4;

    public SwarmWorld(SwarmConfig config)
    {
        Config = config;
        Entities = new EntityAllocator(config.Capacity);
        Positions = new FPVector2[config.Capacity];
        Velocities = new FPVector2[config.Capacity];
        PreferredVelocities = new FPVector2[config.Capacity];
        NextVelocities = new FPVector2[config.Capacity];
        FormationOffsets = new FPVector2[config.Capacity];
        Radii = new FP[config.Capacity];
        MaxSpeeds = new FP[config.Capacity];
        Groups = new byte[config.Capacity];
        PathCursors = new ushort[config.Capacity];
        GroupTargets = new FPVector2[GroupCount];
        GroupPathStates = new GroupPathState[GroupCount];
    }

    public SwarmConfig Config { get; }

    public EntityAllocator Entities { get; }

    public FPVector2[] Positions { get; }

    public FPVector2[] Velocities { get; }

    public FPVector2[] PreferredVelocities { get; }

    public FPVector2[] NextVelocities { get; }

    public FPVector2[] FormationOffsets { get; }

    public FP[] Radii { get; }

    public FP[] MaxSpeeds { get; }

    public byte[] Groups { get; }

    public ushort[] PathCursors { get; }

    public FPVector2[] GroupTargets { get; }

    /// <summary>Rollback-authoritative shared path request/result state.</summary>
    public GroupPathState[] GroupPathStates { get; }

    public int NextPathRequestSequence { get; internal set; }

    public SpatialIndexMode SpatialIndexMode { get; internal set; }

    public int Count { get; private set; }

    public int Tick { get; internal set; }

    public uint Seed { get; private set; }

    public void InitializeDeterministicFormation(int requestedCount, uint seed)
    {
        Count = requestedCount < 0 ? 0 : Math.Min(requestedCount, Config.Capacity);
        Seed = seed;
        Tick = 0;
        NextPathRequestSequence = 0;
        SpatialIndexMode = Config.SpatialIndexMode;
        Entities.Reset();

        for (int group = 0; group < GroupCount; group++)
        {
            GroupPathStates[group] = GroupPathState.CreateEmpty();
        }

        GroupTargets[0] = new FPVector2(FP.FromInt(46), FP.FromInt(46));
        GroupTargets[1] = new FPVector2(FP.FromInt(-46), FP.FromInt(46));
        GroupTargets[2] = new FPVector2(FP.FromInt(-46), FP.FromInt(-46));
        GroupTargets[3] = new FPVector2(FP.FromInt(46), FP.FromInt(-46));

        int agentsPerGroup = (Count + GroupCount - 1) / GroupCount;
        int side = FPMath.CeilingIntegerSquareRoot(agentsPerGroup);
        FP spacing = FP.FromRatio(11, 10);
        FP halfSide = FP.FromRatio(side - 1, 2);

        for (int i = 0; i < Count; i++)
        {
            Entity entity = Entities.Create();
            int group = i & 3;
            int rank = i >> 2;
            int column = rank % side;
            int row = rank / side;

            FP localX = (FP.FromInt(column) - halfSide) * spacing;
            FP localY = (FP.FromInt(row) - halfSide) * spacing;
            uint noise = Mix(seed + (uint)(i * 0x9E3779B9));
            FP jitterX = FP.FromRatio((int)(noise & 255) - 128, 4096);
            FP jitterY = FP.FromRatio((int)((noise >> 8) & 255) - 128, 4096);
            FPVector2 local = new(localX + jitterX, localY + jitterY);

            FPVector2 origin = group switch
            {
                0 => new FPVector2(FP.FromInt(-46), FP.FromInt(-46)),
                1 => new FPVector2(FP.FromInt(46), FP.FromInt(-46)),
                2 => new FPVector2(FP.FromInt(46), FP.FromInt(46)),
                _ => new FPVector2(FP.FromInt(-46), FP.FromInt(46)),
            };

            Positions[entity.Index] = origin + local;
            Velocities[entity.Index] = FPVector2.Zero;
            PreferredVelocities[entity.Index] = FPVector2.Zero;
            NextVelocities[entity.Index] = FPVector2.Zero;
            FormationOffsets[entity.Index] = local * FP.FromRatio(3, 5);
            Radii[entity.Index] = Config.AgentRadius;
            MaxSpeeds[entity.Index] = Config.MaxSpeed;
            Groups[entity.Index] = (byte)group;
            PathCursors[entity.Index] = 1;
        }

        for (int i = Count; i < Config.Capacity; i++)
        {
            Positions[i] = FPVector2.Zero;
            Velocities[i] = FPVector2.Zero;
            PathCursors[i] = 0;
        }
    }

    public FPVector2 GetTargetForAgent(int entityIndex)
    {
        return GroupTargets[Groups[entityIndex]] + FormationOffsets[entityIndex];
    }

    public void SetGroupTarget(int group, FPVector2 target)
    {
        if ((uint)group >= GroupCount)
        {
            return;
        }

        GroupTargets[group] = target;
    }

    public void SetSpatialIndexMode(SpatialIndexMode mode)
    {
        if ((uint)mode > (uint)SpatialIndexMode.KdTreeKNearest)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        SpatialIndexMode = mode;
    }

    public void AdvanceTick()
    {
        Tick++;
    }

    public ulong ComputeStateHash()
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        hash = MixHash(hash, Tick, prime);
        hash = MixHash(hash, Count, prime);
        hash = MixHash(hash, unchecked((int)Seed), prime);
        hash = MixHash(hash, (int)SpatialIndexMode, prime);

        for (int i = 0; i < GroupCount; i++)
        {
            hash = MixHash(hash, GroupTargets[i].X.Raw, prime);
            hash = MixHash(hash, GroupTargets[i].Y.Raw, prime);
            GroupPathState pathState = GroupPathStates[i];
            hash = MixHash(hash, pathState.ResolvedStartIndex, prime);
            hash = MixHash(hash, pathState.ResolvedGoalIndex, prime);
            hash = MixHash(hash, pathState.ResolvedMapRevision, prime);
            hash = MixHash(hash, (int)pathState.Status, prime);
            hash = MixHash(hash, pathState.PendingStartIndex, prime);
            hash = MixHash(hash, pathState.PendingGoalIndex, prime);
            hash = MixHash(hash, pathState.PendingMapRevision, prime);
            hash = MixHash(hash, pathState.PendingSequence, prime);
        }

        hash = MixHash(hash, NextPathRequestSequence, prime);

        for (int i = 0; i < Count; i++)
        {
            hash = MixHash(hash, Positions[i].X.Raw, prime);
            hash = MixHash(hash, Positions[i].Y.Raw, prime);
            hash = MixHash(hash, Velocities[i].X.Raw, prime);
            hash = MixHash(hash, Velocities[i].Y.Raw, prime);
            hash = MixHash(hash, PathCursors[i], prime);
        }

        return hash;
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

    private static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352D;
            value ^= value >> 15;
            value *= 0x846CA68B;
            value ^= value >> 16;
            return value;
        }
    }
}
}
