using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Systems
{

public sealed class PreferredVelocitySystem
{
    private static readonly FP ArrivalRadiusSquared = FP.FromInt(1);

    public void Execute(SwarmWorld world)
    {
        for (int i = 0; i < world.Count; i++)
        {
            FPVector2 toTarget = world.GetTargetForAgent(i) - world.Positions[i];
            FP distanceSquared = toTarget.SqrMagnitude;
            if (distanceSquared <= ArrivalRadiusSquared)
            {
                world.PreferredVelocities[i] = FPVector2.Zero;
                continue;
            }

            world.PreferredVelocities[i] = FPMath.NormalizeSafe(toTarget) * world.MaxSpeeds[i];
        }
    }
}
}
