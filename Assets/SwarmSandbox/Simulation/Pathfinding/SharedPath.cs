using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Reusable immutable-by-convention path storage. Many agents may retain the
    /// same instance, avoiding one path allocation per agent or per request.
    /// </summary>
    public sealed class SharedPath
    {
        public SharedPath(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            NodeIndices = new int[capacity];
            Waypoints = new FPVector2[capacity];
            StartIndex = -1;
            GoalIndex = -1;
            MapRevision = int.MinValue;
        }

        public int[] NodeIndices { get; }

        public FPVector2[] Waypoints { get; }

        public int Capacity => NodeIndices.Length;

        public int Count { get; internal set; }

        public int StartIndex { get; internal set; }

        public int GoalIndex { get; internal set; }

        public int MapRevision { get; internal set; }

        public bool IsReusableFor(GridMap map, int startIndex, int goalIndex)
        {
            return map != null && Count > 0 && StartIndex == startIndex && GoalIndex == goalIndex && MapRevision == map.Revision;
        }

        internal void Invalidate()
        {
            Count = 0;
            StartIndex = -1;
            GoalIndex = -1;
            MapRevision = int.MinValue;
        }
    }
}
