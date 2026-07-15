using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Systems
{

public sealed class MovementIntegrationSystem
{
    public int LastAccelerationLimitedAgents { get; private set; }

    public int LastTurnLimitedAgents { get; private set; }

    public void Execute(SwarmWorld world)
    {
        Execute(world, null);
    }

    public void Execute(
        SwarmWorld world,
        StaticObstacleCollisionSystem staticObstacles)
    {
        FP deltaTime = world.Config.FixedDeltaTime;
        FP extent = world.Config.WorldHalfExtent;
        int accelerationLimitedAgents = 0;
        int turnLimitedAgents = 0;
        staticObstacles?.BeginTick();

        for (int i = 0; i < world.Count; i++)
        {
            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                i,
                world.Velocities[i],
                world.NextVelocities[i],
                world.MaxSpeeds[i],
                world.Config.MaxAcceleration,
                deltaTime,
                world.Config.MaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);
            if (accelerationLimited)
            {
                accelerationLimitedAgents++;
            }

            if (turnLimited)
            {
                turnLimitedAgents++;
            }

            FPVector2 position;
            if (staticObstacles == null)
            {
                position = world.Positions[i] + velocity * deltaTime;
            }
            else
            {
                staticObstacles.MoveAgent(
                    i,
                    world.Positions[i],
                    velocity,
                    world.Radii[i],
                    deltaTime,
                    out position,
                    out velocity);
            }

            FP x = position.X;
            FP y = position.Y;
            FP vx = velocity.X;
            FP vy = velocity.Y;

            if (x < -extent)
            {
                x = -extent;
                vx = FPMath.Abs(vx);
            }
            else if (x > extent)
            {
                x = extent;
                vx = -FPMath.Abs(vx);
            }

            if (y < -extent)
            {
                y = -extent;
                vy = FPMath.Abs(vy);
            }
            else if (y > extent)
            {
                y = extent;
                vy = -FPMath.Abs(vy);
            }

            world.Positions[i] = new FPVector2(x, y);
            world.Velocities[i] = new FPVector2(vx, vy);
        }

        LastAccelerationLimitedAgents = accelerationLimitedAgents;
        LastTurnLimitedAgents = turnLimitedAgents;
    }
}
}
