using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Spatial;

namespace SwarmECS.Simulation.Systems
{

/// <summary>
/// Immutable static-obstacle data plus allocation-free continuous collision queries.
/// The broadphase and segment topology are snapshots of <see cref="Obstacles"/> at
/// construction time; changing obstacle topology requires constructing a new system
/// and starting a new rollback epoch.
/// </summary>
public sealed class StaticObstacleCollisionSystem
{
    public const int MaxSweepIterations = 4;
    public const int MaxPenetrationPasses = 4;

    private static readonly FP SweepSkin = FP.FromRaw(4);
    private readonly FPOrientedBox2[] _obstacles;
    private readonly ObstacleSegment[] _obstacleSegments;
    private readonly StaticObstacleQueryScratch _queryScratch;

    public StaticObstacleCollisionSystem()
        : this(CreateDefaultObstacles())
    {
    }

    public StaticObstacleCollisionSystem(FPOrientedBox2[] obstacles)
    {
        if (obstacles == null)
        {
            throw new ArgumentNullException(nameof(obstacles));
        }

        _obstacles = new FPOrientedBox2[obstacles.Length];
        Array.Copy(obstacles, _obstacles, obstacles.Length);
        Broadphase = new StaticObstacleBvh2D(_obstacles);
        _queryScratch = Broadphase.CreateScratch();

        _obstacleSegments = new ObstacleSegment[
            StaticObstacleSegmentBuilder.RequiredCapacity(_obstacles.Length)];
        ObstacleSegmentCount = StaticObstacleSegmentBuilder.Fill(
            _obstacles,
            _obstacles.Length,
            _obstacleSegments);
    }

    /// <summary>
    /// Returns a defensive construction-time snapshot. Mutating the returned array
    /// cannot desynchronize SAT, BVH, navigation, and ORCA geometry.
    /// </summary>
    public FPOrientedBox2[] Obstacles => CloneObstacles();

    public StaticObstacleBvh2D Broadphase { get; }

    /// <summary>Returns a defensive copy of the immutable directed segment topology.</summary>
    public ObstacleSegment[] ObstacleSegments => CloneObstacleSegments();

    public int ObstacleSegmentCount { get; }

    public int ObstacleCount => _obstacles.Length;

    internal FPOrientedBox2[] ObstacleData => _obstacles;

    internal ObstacleSegment[] ObstacleSegmentData => _obstacleSegments;

    /// <summary>Compatibility name for SAT recovery contacts, not CCD impacts.</summary>
    public int LastContactCount => LastPenetrationRecoveries;

    public int LastBroadphaseCandidates { get; private set; }

    public int LastBroadphaseQueries { get; private set; }

    public int LastSweepHits { get; private set; }

    public int LastPenetrationRecoveries { get; private set; }

    public FP LastMaxResidualDepth { get; private set; }

    public void BeginTick()
    {
        LastBroadphaseCandidates = 0;
        LastBroadphaseQueries = 0;
        LastSweepHits = 0;
        LastPenetrationRecoveries = 0;
        LastMaxResidualDepth = FP.Zero;
    }

    /// <summary>
    /// Moves one circle through the static scene. Each impact advances to the earliest
    /// conservative TOI and slides the remaining displacement. SAT is only used after
    /// the sweep to recover exceptional pre-existing or rounding penetration.
    /// </summary>
    public void MoveAgent(
        FPVector2 startPosition,
        FPVector2 requestedVelocity,
        FP radius,
        FP deltaTime,
        out FPVector2 finalPosition,
        out FPVector2 finalVelocity)
    {
        FP safeRadius = FPMath.Max(radius, FP.Zero);
        FP safeDeltaTime = FPMath.Max(deltaTime, FP.Zero);
        FPVector2 position = startPosition;
        FPVector2 velocity = requestedVelocity;

        FPVector2 remaining = velocity * safeDeltaTime;
        bool performedSweepQuery = false;
        bool sweepFoundBroadphaseCandidate = false;
        for (int iteration = 0;
            iteration < MaxSweepIterations && remaining != FPVector2.Zero;
            ++iteration)
        {
            FPAabb2 sweptBounds = FPAabb2.FromSegment(
                position,
                position + remaining,
                safeRadius + SweepSkin);
            Query(in sweptBounds, out int candidateCount);
            performedSweepQuery = true;
            sweepFoundBroadphaseCandidate |= candidateCount > 0;

            bool found = false;
            FPSweepHit2D earliest = default;
            int earliestObstacleId = int.MaxValue;
            FPCircle2 circle = new(position, safeRadius);
            for (int candidateIndex = 0; candidateIndex < candidateCount; ++candidateIndex)
            {
                int obstacleId = _queryScratch.ObstacleIds[candidateIndex];
                FPOrientedBox2 obstacle = _obstacles[obstacleId];
                if (!FPSweptCircle2D.SweepAgainstBox(
                    in circle,
                    remaining,
                    SweepSkin,
                    in obstacle,
                    out FPSweepHit2D hit))
                {
                    continue;
                }

                if (!found ||
                    hit.Fraction < earliest.Fraction ||
                    (hit.Fraction == earliest.Fraction && obstacleId < earliestObstacleId) ||
                    (hit.Fraction == earliest.Fraction &&
                     obstacleId == earliestObstacleId &&
                     hit.FeatureId < earliest.FeatureId))
                {
                    found = true;
                    earliest = hit;
                    earliestObstacleId = obstacleId;
                }
            }

            if (!found)
            {
                position += remaining;
                remaining = FPVector2.Zero;
                break;
            }

            LastSweepHits++;
            position += remaining * earliest.Fraction;

            FP remainingFraction = FP.One - earliest.Fraction;
            FPVector2 slidRemaining = remaining * remainingFraction;
            FP inwardDisplacement = FPMath.Dot(slidRemaining, earliest.Normal);
            if (inwardDisplacement < FP.Zero)
            {
                slidRemaining -= earliest.Normal * inwardDisplacement;
            }

            FP inwardVelocity = FPMath.Dot(velocity, earliest.Normal);
            if (inwardVelocity < FP.Zero)
            {
                velocity -= earliest.Normal * inwardVelocity;
            }

            // A fixed iteration budget is part of the deterministic rules. If the
            // final slot was consumed, dropping unresolved displacement is conservative.
            remaining = slidRemaining;
        }

        FP residualDepth = FP.Zero;
        if (!performedSweepQuery || sweepFoundBroadphaseCandidate)
        {
            int recoveriesBeforeFinalPass = LastPenetrationRecoveries;
            RecoverPenetrations(ref position, ref velocity, safeRadius);
            if (LastPenetrationRecoveries != recoveriesBeforeFinalPass)
            {
                residualDepth = MeasureMaxResidualDepth(position, safeRadius);
            }
        }
        if (residualDepth > LastMaxResidualDepth)
        {
            LastMaxResidualDepth = residualDepth;
        }

        finalPosition = position;
        finalVelocity = velocity;
    }

    /// <summary>
    /// Backward-compatible discrete recovery entry point. The v0.2.2 main pipeline
    /// calls <see cref="MoveAgent"/> from integration instead.
    /// </summary>
    public void Execute(SwarmWorld world)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        BeginTick();
        for (int i = 0; i < world.Count; ++i)
        {
            FPVector2 position = world.Positions[i];
            FPVector2 velocity = world.Velocities[i];
            FP radius = FPMath.Max(world.Radii[i], FP.Zero);
            int recoveriesBeforeAgent = LastPenetrationRecoveries;
            RecoverPenetrations(ref position, ref velocity, radius);
            FP residualDepth = LastPenetrationRecoveries == recoveriesBeforeAgent
                ? FP.Zero
                : MeasureMaxResidualDepth(position, radius);
            if (residualDepth > LastMaxResidualDepth)
            {
                LastMaxResidualDepth = residualDepth;
            }

            world.Positions[i] = position;
            world.Velocities[i] = velocity;
        }
    }

    private void RecoverPenetrations(
        ref FPVector2 position,
        ref FPVector2 velocity,
        FP radius)
    {
        for (int pass = 0; pass < MaxPenetrationPasses; ++pass)
        {
            FPAabb2 bounds = new FPAabb2(position, position).Expanded(radius);
            Query(in bounds, out int candidateCount);
            bool recovered = false;

            for (int candidateIndex = 0; candidateIndex < candidateCount; ++candidateIndex)
            {
                int obstacleId = _queryScratch.ObstacleIds[candidateIndex];
                FPOrientedBox2 obstacle = _obstacles[obstacleId];
                FPCircle2 circle = new(position, radius);
                if (!FPSat2D.Intersect(
                    in obstacle,
                    in circle,
                    out FPVector2 normal,
                    out FP depth) ||
                    depth <= FP.Zero)
                {
                    continue;
                }

                position += normal * depth;
                FP inwardSpeed = FPMath.Dot(velocity, normal);
                if (inwardSpeed < FP.Zero)
                {
                    velocity -= normal * inwardSpeed;
                }

                LastPenetrationRecoveries++;
                recovered = true;
            }

            if (!recovered)
            {
                return;
            }
        }
    }

    private FP MeasureMaxResidualDepth(FPVector2 position, FP radius)
    {
        FPAabb2 bounds = new FPAabb2(position, position).Expanded(radius);
        Query(in bounds, out int candidateCount);
        FP maxDepth = FP.Zero;
        FPCircle2 circle = new(position, radius);
        for (int candidateIndex = 0; candidateIndex < candidateCount; ++candidateIndex)
        {
            int obstacleId = _queryScratch.ObstacleIds[candidateIndex];
            FPOrientedBox2 obstacle = _obstacles[obstacleId];
            if (FPSat2D.Intersect(in obstacle, in circle, out _, out FP depth) &&
                depth > maxDepth)
            {
                maxDepth = depth;
            }
        }

        return maxDepth;
    }

    private void Query(in FPAabb2 bounds, out int candidateCount)
    {
        Broadphase.QueryAabb(in bounds, _queryScratch, out candidateCount);
        LastBroadphaseQueries++;
        LastBroadphaseCandidates += candidateCount;
    }

    private static FPOrientedBox2[] CreateDefaultObstacles()
    {
        FPOrientedBox2[] obstacles = new FPOrientedBox2[StaticObstacleLayout.DefaultObstacleCount];
        StaticObstacleLayout.FillDefault(obstacles);
        return obstacles;
    }

    private FPOrientedBox2[] CloneObstacles()
    {
        FPOrientedBox2[] clone = new FPOrientedBox2[_obstacles.Length];
        Array.Copy(_obstacles, clone, _obstacles.Length);
        return clone;
    }

    private ObstacleSegment[] CloneObstacleSegments()
    {
        ObstacleSegment[] clone = new ObstacleSegment[_obstacleSegments.Length];
        Array.Copy(_obstacleSegments, clone, _obstacleSegments.Length);
        return clone;
    }
}
}
