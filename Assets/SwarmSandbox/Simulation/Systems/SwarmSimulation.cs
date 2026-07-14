using System;

namespace SwarmECS.Simulation.Systems
{

public sealed class SwarmSimulation : IDeterministicSimulation, IDisposable
{
    private readonly MovementIntegrationSystem _movement;
    private bool _disposed;

    public SwarmSimulation(SwarmWorld world)
    {
        Obstacles = new StaticObstacleCollisionSystem();
        Navigation = new SharedPathNavigationSystem(world, Obstacles.Obstacles);
        Avoidance = new NeighborAvoidanceSystem(world.Config);
        _movement = new MovementIntegrationSystem();
    }

    public SharedPathNavigationSystem Navigation { get; }

    public NeighborAvoidanceSystem Avoidance { get; }

    public StaticObstacleCollisionSystem Obstacles { get; }

    public void Step(SwarmWorld world)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SwarmSimulation));
        }

        Navigation.Execute(world);
        Avoidance.Execute(world);
        _movement.Execute(world);
        Obstacles.Execute(world);
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
