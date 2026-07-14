using SwarmECS.FixedPoint;

namespace SwarmECS.HotUpdate
{
    /// <summary>
    /// Deliberately isolated hot-update assembly. HybridCLR can replace this tuning
    /// policy without rebuilding the native IL2CPP player.
    /// </summary>
    public static class HotUpdateEntry
    {
        public const string Version = "1.0.0";

        public static AvoidanceTuning ResolveTuning(int profile)
        {
            switch (profile)
            {
                case 1:
                    return new AvoidanceTuning(FP.FromInt(5), 16, FP.FromRatio(5, 2));
                case 2:
                    return new AvoidanceTuning(FP.FromInt(3), 8, FP.FromRatio(3, 2));
                default:
                    return new AvoidanceTuning(FP.FromInt(4), 12, FP.FromInt(2));
            }
        }
    }

    public readonly struct AvoidanceTuning
    {
        public AvoidanceTuning(FP neighborDistance, int maxNeighbors, FP timeHorizon)
        {
            NeighborDistance = neighborDistance;
            MaxNeighbors = maxNeighbors;
            TimeHorizon = timeHorizon;
        }

        public FP NeighborDistance { get; }

        public int MaxNeighbors { get; }

        public FP TimeHorizon { get; }
    }
}
