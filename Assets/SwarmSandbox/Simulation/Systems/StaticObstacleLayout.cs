using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;

namespace SwarmECS.Simulation.Systems
{

public static class StaticObstacleLayout
{
    public const int DefaultObstacleCount = 5;

    public static void FillDefault(FPOrientedBox2[] destination)
    {
        if (destination == null || destination.Length < DefaultObstacleCount)
        {
            throw new System.ArgumentException("The obstacle destination must contain at least five slots.", nameof(destination));
        }

        destination[0] = new FPOrientedBox2(
            new FPVector2(FP.Zero, FP.FromInt(-27)),
            new FPVector2(FP.FromInt(4), FP.FromInt(14)));
        destination[1] = new FPOrientedBox2(
            new FPVector2(FP.Zero, FP.FromInt(27)),
            new FPVector2(FP.FromInt(4), FP.FromInt(14)));
        destination[2] = new FPOrientedBox2(
            new FPVector2(FP.FromInt(-27), FP.Zero),
            new FPVector2(FP.FromInt(14), FP.FromInt(3)));
        destination[3] = new FPOrientedBox2(
            new FPVector2(FP.FromInt(27), FP.Zero),
            new FPVector2(FP.FromInt(14), FP.FromInt(3)));
        destination[4] = new FPOrientedBox2(
            FPVector2.Zero,
            new FPVector2(FP.FromInt(7), FP.FromInt(7)),
            new FPVector2(FP.FromRatio(181, 256), FP.FromRatio(181, 256)));
    }
}
}
