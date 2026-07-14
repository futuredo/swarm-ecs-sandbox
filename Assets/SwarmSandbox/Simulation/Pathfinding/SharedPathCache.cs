using System;

namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Fixed-capacity, allocation-free cache for shared A* results. Entries are
    /// replaced round-robin so eviction is deterministic and constant time.
    /// </summary>
    public sealed class SharedPathCache
    {
        private readonly SharedPath[] _entries;
        private int _replacementCursor;

        public SharedPathCache(int capacity, int pathNodeCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entries = new SharedPath[capacity];
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = new SharedPath(pathNodeCapacity);
            }
        }

        public int Capacity => _entries.Length;

        public bool TryCopyTo(int startIndex, int goalIndex, int mapRevision, SharedPath destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                SharedPath entry = _entries[i];
                if (entry.Count <= 0 ||
                    entry.StartIndex != startIndex ||
                    entry.GoalIndex != goalIndex ||
                    entry.MapRevision != mapRevision)
                {
                    continue;
                }

                Copy(entry, destination);
                return true;
            }

            return false;
        }

        public void Store(SharedPath source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                SharedPath entry = _entries[i];
                if (entry.StartIndex == source.StartIndex &&
                    entry.GoalIndex == source.GoalIndex &&
                    entry.MapRevision == source.MapRevision)
                {
                    Copy(source, entry);
                    return;
                }
            }

            SharedPath replacement = _entries[_replacementCursor];
            Copy(source, replacement);
            _replacementCursor++;
            if (_replacementCursor == _entries.Length)
            {
                _replacementCursor = 0;
            }
        }

        private static void Copy(SharedPath source, SharedPath destination)
        {
            if (destination.Capacity < source.Count)
            {
                throw new ArgumentException("Destination path storage is too small.", nameof(destination));
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination.NodeIndices[i] = source.NodeIndices[i];
                destination.Waypoints[i] = source.Waypoints[i];
            }

            destination.Count = source.Count;
            destination.StartIndex = source.StartIndex;
            destination.GoalIndex = source.GoalIndex;
            destination.MapRevision = source.MapRevision;
        }
    }
}
