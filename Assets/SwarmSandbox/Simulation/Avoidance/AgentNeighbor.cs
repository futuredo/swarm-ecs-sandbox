using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Avoidance
{
    /// <summary>
    /// The portion of another agent's state required by the ORCA solver.
    /// RelativePosition is otherPosition - thisAgentPosition.
    /// </summary>
    public readonly struct AgentNeighbor
    {
        public readonly int StableId;
        public readonly FPVector2 RelativePosition;
        public readonly FPVector2 Velocity;
        public readonly FP Radius;

        public AgentNeighbor(
            int stableId,
            FPVector2 relativePosition,
            FPVector2 velocity,
            FP radius)
        {
            StableId = stableId;
            RelativePosition = relativePosition;
            Velocity = velocity;
            Radius = radius;
        }
    }

    /// <summary>
    /// An oriented ORCA line. Feasible velocities are on the right-hand side:
    /// Det(Direction, Point - velocity) &lt;= 0.
    /// </summary>
    public struct OrcaLine
    {
        public FPVector2 Point;
        public FPVector2 Direction;
        public int NeighborId;

        internal int SourceOrder;
    }
}
