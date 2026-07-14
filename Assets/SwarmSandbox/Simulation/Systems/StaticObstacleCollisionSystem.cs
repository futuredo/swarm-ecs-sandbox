using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;

namespace SwarmECS.Simulation.Systems
{

public sealed class StaticObstacleCollisionSystem
{
    public StaticObstacleCollisionSystem()
    {
        Obstacles = new FPOrientedBox2[StaticObstacleLayout.DefaultObstacleCount];
        StaticObstacleLayout.FillDefault(Obstacles);
    }

    public FPOrientedBox2[] Obstacles { get; }

    public int LastContactCount { get; private set; }

    public void Execute(SwarmWorld world)
    {
        int contacts = 0;
        for (int i = 0; i < world.Count; i++)
        {
            FPVector2 position = world.Positions[i];
            FPVector2 velocity = world.Velocities[i];
            FPCircle2 circle = new(position, world.Radii[i]);

            for (int obstacleIndex = 0; obstacleIndex < Obstacles.Length; obstacleIndex++)
            {
                FPOrientedBox2 obstacle = Obstacles[obstacleIndex];
                if (!FPSat2D.Intersect(in obstacle, in circle, out FPVector2 normal, out FP depth) || depth <= FP.Zero)
                {
                    continue;
                }

                position += normal * depth;
                FP inwardSpeed = FPMath.Dot(velocity, normal);
                if (inwardSpeed < FP.Zero)
                {
                    velocity -= normal * inwardSpeed;
                }

                circle = new FPCircle2(position, world.Radii[i]);
                contacts++;
            }

            world.Positions[i] = position;
            world.Velocities[i] = velocity;
        }

        LastContactCount = contacts;
    }
}
}
