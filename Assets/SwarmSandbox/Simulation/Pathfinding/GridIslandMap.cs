using System;

namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Fixed-storage connected-component labelling for a <see cref="GridMap"/>.
    /// Regions use the same eight-neighbor and diagonal corner rules as A*.
    /// </summary>
    public sealed class GridIslandMap
    {
        public const int NoRegion = -1;

        private readonly GridMap _map;
        private readonly int[] _regionIds;
        private readonly int[] _floodQueue;
        private int _builtRevision;
        private int _regionCount;

        public GridIslandMap(GridMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _regionIds = new int[map.NodeCount];
            _floodQueue = new int[map.NodeCount];
            Rebuild();
        }

        public GridMap Map => _map;

        /// <summary>The map revision represented by the current region labels.</summary>
        public int BuiltRevision
        {
            get
            {
                EnsureUpToDate();
                return _builtRevision;
            }
        }

        public int RegionCount
        {
            get
            {
                EnsureUpToDate();
                return _regionCount;
            }
        }

        /// <summary>
        /// Returns a stable non-negative region id, or <see cref="NoRegion"/> for
        /// an unwalkable cell. Invalid indices are rejected.
        /// </summary>
        public int GetRegionId(int index)
        {
            ValidateIndex(index, nameof(index));
            EnsureUpToDate();
            return _regionIds[index];
        }

        public int GetRegionId(int x, int y)
        {
            return GetRegionId(_map.ToIndex(x, y));
        }

        /// <summary>Returns true only when both walkable cells belong to the same region.</summary>
        public bool AreConnected(int firstIndex, int secondIndex)
        {
            ValidateIndex(firstIndex, nameof(firstIndex));
            ValidateIndex(secondIndex, nameof(secondIndex));
            EnsureUpToDate();

            int firstRegion = _regionIds[firstIndex];
            return firstRegion != NoRegion && firstRegion == _regionIds[secondIndex];
        }

        public bool AreConnected(int firstX, int firstY, int secondX, int secondY)
        {
            return AreConnected(
                _map.ToIndex(firstX, firstY),
                _map.ToIndex(secondX, secondY));
        }

        /// <summary>
        /// Rebuilds every label using preallocated arrays. Region ids are assigned
        /// deterministically in row-major seed order.
        /// </summary>
        public void Rebuild()
        {
            for (int i = 0; i < _regionIds.Length; ++i)
            {
                _regionIds[i] = NoRegion;
            }

            int nextRegion = 0;
            for (int index = 0; index < _regionIds.Length; ++index)
            {
                if (!_map.IsWalkable(index) || _regionIds[index] != NoRegion)
                {
                    continue;
                }

                FloodFill(index, nextRegion);
                ++nextRegion;
            }

            _regionCount = nextRegion;
            _builtRevision = _map.Revision;
        }

        private void EnsureUpToDate()
        {
            if (_builtRevision != _map.Revision)
            {
                Rebuild();
            }
        }

        private void FloodFill(int startIndex, int regionId)
        {
            int head = 0;
            int tail = 0;
            _regionIds[startIndex] = regionId;
            _floodQueue[tail++] = startIndex;

            while (head < tail)
            {
                int current = _floodQueue[head++];
                _map.IndexToCoordinates(current, out int currentX, out int currentY);

                for (int offsetY = -1; offsetY <= 1; ++offsetY)
                {
                    for (int offsetX = -1; offsetX <= 1; ++offsetX)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int nextX = currentX + offsetX;
                        int nextY = currentY + offsetY;
                        if (!_map.TryToIndex(nextX, nextY, out int next) ||
                            !_map.IsWalkable(next) ||
                            _regionIds[next] != NoRegion)
                        {
                            continue;
                        }

                        bool diagonal = offsetX != 0 && offsetY != 0;
                        if (diagonal &&
                            (!_map.IsWalkable(currentX + offsetX, currentY) ||
                             !_map.IsWalkable(currentX, currentY + offsetY)))
                        {
                            continue;
                        }

                        _regionIds[next] = regionId;
                        _floodQueue[tail++] = next;
                    }
                }
            }
        }

        private void ValidateIndex(int index, string parameterName)
        {
            if ((uint)index >= (uint)_map.NodeCount)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
