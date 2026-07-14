using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Systems
{

public sealed class MovementIntegrationSystem
{
    public void Execute(SwarmWorld world)
    {
        FP deltaTime = world.Config.FixedDeltaTime;
        FP extent = world.Config.WorldHalfExtent;

        for (int i = 0; i < world.Count; i++)
        {
            FPVector2 velocity = world.NextVelocities[i];
            FPVector2 position = world.Positions[i] + velocity * deltaTime;
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
    }
}
}
