namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Authoritative result of the most recently processed shared-path request.
    /// This data belongs to rollback state; the larger waypoint buffers are only
    /// derived caches and can be reconstructed from these keys.
    /// </summary>
    public enum GroupPathStatus : byte
    {
        None = 0,
        Active = 1,
        Unreachable = 2,
    }

    public struct GroupPathState
    {
        public int ResolvedStartIndex;
        public int ResolvedGoalIndex;
        public int ResolvedMapRevision;
        public GroupPathStatus Status;

        public int PendingStartIndex;
        public int PendingGoalIndex;
        public int PendingMapRevision;
        public int PendingSequence;

        public bool HasPending => PendingSequence >= 0;

        public bool IsResolvedFor(int goalIndex, int mapRevision)
        {
            return ResolvedGoalIndex == goalIndex && ResolvedMapRevision == mapRevision;
        }

        public bool IsPendingFor(int goalIndex, int mapRevision)
        {
            return HasPending && PendingGoalIndex == goalIndex && PendingMapRevision == mapRevision;
        }

        public void Queue(int startIndex, int goalIndex, int mapRevision, int sequence)
        {
            PendingStartIndex = startIndex;
            PendingGoalIndex = goalIndex;
            PendingMapRevision = mapRevision;
            PendingSequence = sequence;
        }

        public void ResolveActive(int startIndex, int goalIndex, int mapRevision)
        {
            ResolvedStartIndex = startIndex;
            ResolvedGoalIndex = goalIndex;
            ResolvedMapRevision = mapRevision;
            Status = GroupPathStatus.Active;
            ClearPending();
        }

        public void ResolveUnreachable(int startIndex, int goalIndex, int mapRevision)
        {
            ResolvedStartIndex = startIndex;
            ResolvedGoalIndex = goalIndex;
            ResolvedMapRevision = mapRevision;
            Status = GroupPathStatus.Unreachable;
            ClearPending();
        }

        public void ClearPending()
        {
            PendingStartIndex = -1;
            PendingGoalIndex = -1;
            PendingMapRevision = int.MinValue;
            PendingSequence = -1;
        }

        public static GroupPathState CreateEmpty()
        {
            GroupPathState state = new()
            {
                ResolvedStartIndex = -1,
                ResolvedGoalIndex = -1,
                ResolvedMapRevision = int.MinValue,
                Status = GroupPathStatus.None,
            };
            state.ClearPending();
            return state;
        }
    }
}
