using System;
using SwarmECS.Simulation.Collision;

namespace SwarmECS.Simulation.Systems
{

public sealed class SwarmSimulation : IDeterministicSimulation, IDisposable
{
    private bool _disposed;

    public SwarmSimulation(SwarmWorld world)
        : this(world, null)
    {
    }

    public SwarmSimulation(
        SwarmWorld world,
        FPOrientedBox2[] staticObstacles)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        Obstacles = staticObstacles == null
            ? new StaticObstacleCollisionSystem()
            : new StaticObstacleCollisionSystem(staticObstacles);
        Navigation = new SharedPathNavigationSystem(world, Obstacles.ObstacleData);
        Avoidance = new NeighborAvoidanceSystem(world.Config, Obstacles);
        Movement = new MovementIntegrationSystem();
    }

    public SharedPathNavigationSystem Navigation { get; }

    public NeighborAvoidanceSystem Avoidance { get; }

    public StaticObstacleCollisionSystem Obstacles { get; }

    public MovementIntegrationSystem Movement { get; }

    public void Step(SwarmWorld world)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SwarmSimulation));
        }

        Navigation.Execute(world);
        Avoidance.Execute(world);
        Movement.Execute(world, Obstacles);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Avoidance.Dispose();
    }
}
}
