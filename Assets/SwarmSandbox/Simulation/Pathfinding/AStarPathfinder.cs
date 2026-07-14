using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Allocation-free deterministic A* over an eight-connected GridMap.
    /// Diagonal movement cannot squeeze through blocked cardinal corners.
    /// </summary>
    public sealed class AStarPathfinder
    {
        public const int CardinalCost = 10;
        public const int DiagonalCost = 14;

        private readonly GridMap _map;
        private readonly long[] _gCost;
        private readonly int[] _parent;
        private readonly int[] _visitedStamp;
        private readonly int[] _closedStamp;
        private readonly int[] _heapStamp;
        private readonly int[] _heapPosition;
        private readonly int[] _heap;
        private readonly int[] _reversePath;

        private int _searchStamp;
        private int _heapCount;
        private int _goalIndex;
        private int _pathLength;

        public AStarPathfinder(GridMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            int count = map.NodeCount;
            _gCost = new long[count];
            _parent = new int[count];
            _visitedStamp = new int[count];
            _closedStamp = new int[count];
            _heapStamp = new int[count];
            _heapPosition = new int[count];
            _heap = new int[count];
            _reversePath = new int[count];
        }

        public GridMap Map => _map;

        public bool FindPath(int startX, int startY, int goalX, int goalY, int[] nodeBuffer, out int count)
        {
            if (nodeBuffer == null)
            {
                throw new ArgumentNullException(nameof(nodeBuffer));
            }

            if (!_map.TryToIndex(startX, startY, out int start) || !_map.TryToIndex(goalX, goalY, out int goal))
            {
                count = 0;
                return false;
            }

            return FindPath(start, goal, nodeBuffer, out count);
        }

        public bool FindPath(int startIndex, int goalIndex, int[] nodeBuffer, out int count)
        {
            if (nodeBuffer == null)
            {
                throw new ArgumentNullException(nameof(nodeBuffer));
            }

            if (!Search(startIndex, goalIndex))
            {
                count = 0;
                return false;
            }

            count = _pathLength;
            if (nodeBuffer.Length < _pathLength)
            {
                return false;
            }

            CopyNodes(nodeBuffer);
            return true;
        }

        public bool FindWaypointPath(int startX, int startY, int goalX, int goalY, FPVector2[] waypointBuffer, out int count)
        {
            if (!_map.TryToIndex(startX, startY, out int start) || !_map.TryToIndex(goalX, goalY, out int goal))
            {
                count = 0;
                return false;
            }

            return FindWaypointPath(start, goal, waypointBuffer, out count);
        }

        public bool FindWaypointPath(int startIndex, int goalIndex, FPVector2[] waypointBuffer, out int count)
        {
            if (waypointBuffer == null)
            {
                throw new ArgumentNullException(nameof(waypointBuffer));
            }

            if (!Search(startIndex, goalIndex))
            {
                count = 0;
                return false;
            }

            count = _pathLength;
            if (waypointBuffer.Length < _pathLength)
            {
                return false;
            }

            for (int i = 0; i < _pathLength; ++i)
            {
                waypointBuffer[i] = _map.CellCenter(_reversePath[_pathLength - 1 - i]);
            }

            return true;
        }

        /// <summary>
        /// Fills reusable shared storage. If the map revision and endpoints match,
        /// an existing shared path is returned without running A* again.
        /// </summary>
        public bool FindSharedPath(int startIndex, int goalIndex, SharedPath sharedPath)
        {
            if (sharedPath == null)
            {
                throw new ArgumentNullException(nameof(sharedPath));
            }

            if (sharedPath.IsReusableFor(_map, startIndex, goalIndex))
            {
                return true;
            }

            sharedPath.Invalidate();
            if (!Search(startIndex, goalIndex) || sharedPath.Capacity < _pathLength)
            {
                return false;
            }

            for (int i = 0; i < _pathLength; ++i)
            {
                int node = _reversePath[_pathLength - 1 - i];
                sharedPath.NodeIndices[i] = node;
                sharedPath.Waypoints[i] = _map.CellCenter(node);
            }

            sharedPath.Count = _pathLength;
            sharedPath.StartIndex = startIndex;
            sharedPath.GoalIndex = goalIndex;
            sharedPath.MapRevision = _map.Revision;
            return true;
        }

        public bool FindSharedPath(int startX, int startY, int goalX, int goalY, SharedPath sharedPath)
        {
            if (!_map.TryToIndex(startX, startY, out int start) || !_map.TryToIndex(goalX, goalY, out int goal))
            {
                sharedPath?.Invalidate();
                return false;
            }

            return FindSharedPath(start, goal, sharedPath);
        }

        private bool Search(int startIndex, int goalIndex)
        {
            _pathLength = 0;
            if ((uint)startIndex >= (uint)_map.NodeCount || (uint)goalIndex >= (uint)_map.NodeCount)
            {
                return false;
            }

            if (!_map.IsWalkable(startIndex) || !_map.IsWalkable(goalIndex))
            {
                return false;
            }

            AdvanceSearchStamp();
            _goalIndex = goalIndex;
            _heapCount = 0;
            _visitedStamp[startIndex] = _searchStamp;
            _gCost[startIndex] = 0;
            _parent[startIndex] = -1;
            Push(startIndex);

            while (_heapCount > 0)
            {
                int current = Pop();
                if (_closedStamp[current] == _searchStamp)
                {
                    continue;
                }

                if (current == goalIndex)
                {
                    return Reconstruct(startIndex, goalIndex);
                }

                _closedStamp[current] = _searchStamp;
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
                        if (!_map.TryToIndex(nextX, nextY, out int next) || !_map.IsWalkable(next))
                        {
                            continue;
                        }

                        bool diagonal = offsetX != 0 && offsetY != 0;
                        if (diagonal && (!_map.IsWalkable(currentX + offsetX, currentY) || !_map.IsWalkable(currentX, currentY + offsetY)))
                        {
                            continue;
                        }

                        if (_closedStamp[next] == _searchStamp)
                        {
                            continue;
                        }

                        long tentative = _gCost[current] + (diagonal ? DiagonalCost : CardinalCost) + _map.GetPenalty(next);
                        if (_visitedStamp[next] == _searchStamp && tentative >= _gCost[next])
                        {
                            continue;
                        }

                        _visitedStamp[next] = _searchStamp;
                        _gCost[next] = tentative;
                        _parent[next] = current;
                        AddOrUpdate(next);
                    }
                }
            }

            return false;
        }

        private bool Reconstruct(int startIndex, int goalIndex)
        {
            int node = goalIndex;
            int length = 0;
            while (node >= 0 && length < _reversePath.Length)
            {
                _reversePath[length++] = node;
                if (node == startIndex)
                {
                    _pathLength = length;
                    return true;
                }

                node = _parent[node];
            }

            _pathLength = 0;
            return false;
        }

        private void CopyNodes(int[] destination)
        {
            for (int i = 0; i < _pathLength; ++i)
            {
                destination[i] = _reversePath[_pathLength - 1 - i];
            }
        }

        private void AdvanceSearchStamp()
        {
            unchecked
            {
                ++_searchStamp;
            }

            if (_searchStamp != 0)
            {
                return;
            }

            Array.Clear(_visitedStamp, 0, _visitedStamp.Length);
            Array.Clear(_closedStamp, 0, _closedStamp.Length);
            Array.Clear(_heapStamp, 0, _heapStamp.Length);
            _searchStamp = 1;
        }

        private void AddOrUpdate(int node)
        {
            if (_heapStamp[node] == _searchStamp && _heapPosition[node] >= 0)
            {
                SiftUp(_heapPosition[node]);
                return;
            }

            Push(node);
        }

        private void Push(int node)
        {
            int position = _heapCount++;
            _heap[position] = node;
            _heapPosition[node] = position;
            _heapStamp[node] = _searchStamp;
            SiftUp(position);
        }

        private int Pop()
        {
            int result = _heap[0];
            --_heapCount;
            if (_heapCount > 0)
            {
                int tail = _heap[_heapCount];
                _heap[0] = tail;
                _heapPosition[tail] = 0;
                SiftDown(0);
            }

            _heapPosition[result] = -1;
            return result;
        }

        private void SiftUp(int position)
        {
            while (position > 0)
            {
                int parent = (position - 1) >> 1;
                if (!HasHigherPriority(_heap[position], _heap[parent]))
                {
                    return;
                }

                SwapHeap(position, parent);
                position = parent;
            }
        }

        private void SiftDown(int position)
        {
            while (true)
            {
                int left = (position << 1) + 1;
                if (left >= _heapCount)
                {
                    return;
                }

                int best = left;
                int right = left + 1;
                if (right < _heapCount && HasHigherPriority(_heap[right], _heap[left]))
                {
                    best = right;
                }

                if (!HasHigherPriority(_heap[best], _heap[position]))
                {
                    return;
                }

                SwapHeap(position, best);
                position = best;
            }
        }

        private bool HasHigherPriority(int left, int right)
        {
            long leftHeuristic = Heuristic(left, _goalIndex);
            long rightHeuristic = Heuristic(right, _goalIndex);
            long leftF = _gCost[left] + leftHeuristic;
            long rightF = _gCost[right] + rightHeuristic;
            if (leftF != rightF)
            {
                return leftF < rightF;
            }

            if (leftHeuristic != rightHeuristic)
            {
                return leftHeuristic < rightHeuristic;
            }

            return left < right;
        }

        private long Heuristic(int from, int to)
        {
            _map.IndexToCoordinates(from, out int fromX, out int fromY);
            _map.IndexToCoordinates(to, out int toX, out int toY);
            int deltaX = fromX > toX ? fromX - toX : toX - fromX;
            int deltaY = fromY > toY ? fromY - toY : toY - fromY;
            int diagonal = deltaX < deltaY ? deltaX : deltaY;
            int straight = (deltaX > deltaY ? deltaX : deltaY) - diagonal;
            return (long)diagonal * DiagonalCost + (long)straight * CardinalCost;
        }

        private void SwapHeap(int left, int right)
        {
            int value = _heap[left];
            _heap[left] = _heap[right];
            _heap[right] = value;
            _heapPosition[_heap[left]] = left;
            _heapPosition[_heap[right]] = right;
        }
    }
}
