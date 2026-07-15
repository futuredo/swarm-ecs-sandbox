using System;
using SwarmECS.Simulation.Collision;

namespace SwarmECS.Simulation.Spatial
{
    /// <summary>
    /// Caller-owned storage for allocation-free, re-entrant static-obstacle queries.
    /// One scratch instance must not be used by two execution lanes concurrently.
    /// </summary>
    public sealed class StaticObstacleQueryScratch
    {
        internal StaticObstacleQueryScratch(int nodeCapacity, int obstacleCapacity)
        {
            NodeStack = new int[nodeCapacity];
            ObstacleIds = new int[obstacleCapacity];
        }

        internal int[] NodeStack { get; }

        public int[] ObstacleIds { get; }

        public int Capacity => ObstacleIds.Length;
    }

    /// <summary>
    /// Immutable fixed-capacity AABB BVH for static OBBs. Construction allocates all
    /// nodes once; queries only mutate caller-owned scratch storage. Query results are
    /// ordered by stable obstacle id, independently of tree traversal order.
    /// </summary>
    public sealed class StaticObstacleBvh2D
    {
        private struct Node
        {
            public FPAabb2 Bounds;
            public int Left;
            public int Right;
            public int ObstacleId;
        }

        private readonly FPAabb2[] _obstacleBounds;
        private readonly Node[] _nodes;
        private readonly int[] _buildIndices;
        private readonly int _root;
        private int _nodeCount;

        public StaticObstacleBvh2D(FPOrientedBox2[] obstacles)
        {
            if (obstacles == null)
            {
                throw new ArgumentNullException(nameof(obstacles));
            }

            long requestedNodes = obstacles.Length == 0 ? 0L : ((long)obstacles.Length * 2L) - 1L;
            if (requestedNodes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacles), "Obstacle count exceeds BVH node capacity.");
            }

            _obstacleBounds = new FPAabb2[obstacles.Length];
            _buildIndices = new int[obstacles.Length];
            _nodes = new Node[(int)requestedNodes];

            for (int i = 0; i < obstacles.Length; ++i)
            {
                FPOrientedBox2 obstacle = obstacles[i];
                _obstacleBounds[i] = FPAabb2.FromOrientedBox(in obstacle);
                _buildIndices[i] = i;
            }

            _root = obstacles.Length == 0 ? -1 : BuildNode(0, obstacles.Length);
        }

        public int ObstacleCount => _obstacleBounds.Length;

        public int NodeCount => _nodeCount;

        public StaticObstacleQueryScratch CreateScratch()
        {
            return new StaticObstacleQueryScratch(_nodes.Length, _obstacleBounds.Length);
        }

        public FPAabb2 GetObstacleBounds(int obstacleId)
        {
            if ((uint)obstacleId >= (uint)_obstacleBounds.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(obstacleId));
            }

            return _obstacleBounds[obstacleId];
        }

        /// <summary>
        /// Exposes immutable node geometry for presentation-only BVH visualization.
        /// Traversal and simulation behavior remain private to the broadphase.
        /// </summary>
        public bool TryGetNodeDiagnostic(
            int nodeIndex,
            out FPAabb2 bounds,
            out int left,
            out int right,
            out int obstacleId)
        {
            if ((uint)nodeIndex >= (uint)_nodeCount)
            {
                bounds = default;
                left = -1;
                right = -1;
                obstacleId = -1;
                return false;
            }

            Node node = _nodes[nodeIndex];
            bounds = node.Bounds;
            left = node.Left;
            right = node.Right;
            obstacleId = node.ObstacleId;
            return true;
        }

        public void QueryAabb(
            in FPAabb2 query,
            StaticObstacleQueryScratch scratch,
            out int count)
        {
            if (scratch == null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            if (scratch.NodeStack.Length < _nodeCount || scratch.ObstacleIds.Length < _obstacleBounds.Length)
            {
                throw new ArgumentException("Query scratch is smaller than this BVH's fixed capacity.", nameof(scratch));
            }

            count = 0;
            if (_root < 0)
            {
                return;
            }

            int stackCount = 0;
            scratch.NodeStack[stackCount++] = _root;
            while (stackCount > 0)
            {
                Node node = _nodes[scratch.NodeStack[--stackCount]];
                if (!node.Bounds.Overlaps(in query))
                {
                    continue;
                }

                if (node.ObstacleId >= 0)
                {
                    scratch.ObstacleIds[count++] = node.ObstacleId;
                    continue;
                }

                // Push right first so the deterministic build's left node is visited
                // first. Results are sorted anyway, so correctness never depends on it.
                scratch.NodeStack[stackCount++] = node.Right;
                scratch.NodeStack[stackCount++] = node.Left;
            }

            HeapSortAscending(scratch.ObstacleIds, count);
        }

        private int BuildNode(int start, int count)
        {
            FPAabb2 bounds = _obstacleBounds[_buildIndices[start]];
            for (int i = 1; i < count; ++i)
            {
                FPAabb2 candidate = _obstacleBounds[_buildIndices[start + i]];
                bounds = FPAabb2.Merge(in bounds, in candidate);
            }

            int nodeIndex = _nodeCount++;
            if (count == 1)
            {
                _nodes[nodeIndex] = new Node
                {
                    Bounds = bounds,
                    Left = -1,
                    Right = -1,
                    ObstacleId = _buildIndices[start],
                };
                return nodeIndex;
            }

            int splitAxis = bounds.ExtentRaw(0) >= bounds.ExtentRaw(1) ? 0 : 1;
            StableSortRange(start, count, splitAxis);
            int leftCount = count / 2;
            int left = BuildNode(start, leftCount);
            int right = BuildNode(start + leftCount, count - leftCount);
            _nodes[nodeIndex] = new Node
            {
                Bounds = bounds,
                Left = left,
                Right = right,
                ObstacleId = -1,
            };
            return nodeIndex;
        }

        private void StableSortRange(int start, int count, int axis)
        {
            for (int i = start + 1; i < start + count; ++i)
            {
                int obstacleId = _buildIndices[i];
                int insert = i;
                while (insert > start && CompareBuildOrder(obstacleId, _buildIndices[insert - 1], axis) < 0)
                {
                    _buildIndices[insert] = _buildIndices[insert - 1];
                    --insert;
                }

                _buildIndices[insert] = obstacleId;
            }
        }

        private int CompareBuildOrder(int leftId, int rightId, int axis)
        {
            long leftCenter = _obstacleBounds[leftId].CenterRaw(axis);
            long rightCenter = _obstacleBounds[rightId].CenterRaw(axis);
            if (leftCenter != rightCenter)
            {
                return leftCenter < rightCenter ? -1 : 1;
            }

            return leftId.CompareTo(rightId);
        }

        private static void HeapSortAscending(int[] values, int count)
        {
            for (int root = (count / 2) - 1; root >= 0; --root)
            {
                SiftDownMaxHeap(values, root, count);
            }

            for (int end = count - 1; end > 0; --end)
            {
                int swap = values[0];
                values[0] = values[end];
                values[end] = swap;
                SiftDownMaxHeap(values, 0, end);
            }
        }

        private static void SiftDownMaxHeap(int[] values, int root, int count)
        {
            while (true)
            {
                int left = (root * 2) + 1;
                if (left >= count)
                {
                    return;
                }

                int largest = left;
                int right = left + 1;
                if (right < count && values[right] > values[left])
                {
                    largest = right;
                }

                if (values[root] >= values[largest])
                {
                    return;
                }

                int swap = values[root];
                values[root] = values[largest];
                values[largest] = swap;
                root = largest;
            }
        }
    }
}
