namespace SwarmECS.Simulation
{

/// <summary>
/// One deterministic fixed tick. The caller owns tick advancement and snapshot timing.
/// </summary>
public interface IDeterministicSimulation
{
    void Step(SwarmWorld world);
}
}
