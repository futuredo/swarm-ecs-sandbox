using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Spatial
{
    /// <summary>
    /// A deterministic fixed-capacity 2D kd-tree. Nodes are stored in parallel
    /// arrays, avoiding object graphs and preserving cache-friendly traversal.
    /// </summary>
    public sealed class DataOrientedKdTree2D
    {
        private readonly int _capacity;
        private readonly int[] _buildIndices;
        private readonly int[] _entityAtNode;
        private readonly int[] _leftNode;
        private readonly int[] _rightNode;
        private readonly byte[] _splitAxis;
        private readonly int[] _queryEntities;
        private readonly FP[] _queryDistances;

        private FPVector2[] _positions;
        private int _entityCount;
        private int _nodeCount;
        private int _rootNode = -1;
        private int _radiusMatchCount;
        private FPVector2 _queryCenter;
        private FP _queryRadiusSquared;
        private int _knnLimit;
        private int _knnCount;

        public DataOrientedKdTree2D(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
            _buildIndices = new int[capacity];
            _entityAtNode = new int[capacity];
            _leftNode = new int[capacity];
            _rightNode = new int[capacity];
            _splitAxis = new byte[capacity];
            _queryEntities = new int[capacity];
            _queryDistances = new FP[capacity];
        }

        public int Capacity => _capacity;

        public int EntityCount => _entityCount;

        public void Build(FPVector2[] positions, int count)
        {
            if (positions == null)
            {
                throw new ArgumentNullException(nameof(positions));
            }

            if (count < 0 || count > positions.Length || count > _capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _positions = positions;
            _entityCount = count;
            _nodeCount = 0;

            for (int i = 0; i < count; ++i)
            {
                _buildIndices[i] = i;
            }

            _rootNode = count == 0 ? -1 : BuildRange(0, count - 1, 0);
        }

        /// <summary>
        /// Inclusive radius query ordered by squared distance, then entity id.
        /// </summary>
        public void QueryRadius(FPVector2 center, FP radius, int[] result, out int count)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (radius < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            count = 0;
            if (_rootNode < 0 || result.Length == 0)
            {
                return;
            }

            _queryCenter = center;
            _queryRadiusSquared = radius * radius;
            _radiusMatchCount = 0;
            RadiusSearch(_rootNode);

            SpatialQuerySort.Sort(_queryDistances, _queryEntities, _radiusMatchCount);
            count = _radiusMatchCount < result.Length ? _radiusMatchCount : result.Length;
            Array.Copy(_queryEntities, 0, result, 0, count);
        }

        /// <summary>
        /// Finds the nearest k entities. Exact-distance ties are resolved by id.
        /// </summary>
        public void QueryKNearest(FPVector2 center, int k, int[] result, out int count)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (k < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(k));
            }

            count = 0;
            if (_rootNode < 0 || k == 0 || result.Length == 0)
            {
                return;
            }

            _queryCenter = center;
            _knnLimit = k;
            if (_knnLimit > result.Length)
            {
                _knnLimit = result.Length;
            }

            if (_knnLimit > _entityCount)
            {
                _knnLimit = _entityCount;
            }

            _knnCount = 0;
            KNearestSearch(_rootNode);
            count = _knnCount;
            Array.Copy(_queryEntities, 0, result, 0, count);
        }

        private int BuildRange(int left, int right, int depth)
        {
            if (left > right)
            {
                return -1;
            }

            int median = left + ((right - left) >> 1);
            int axis = depth & 1;
            QuickSelect(left, right, median, axis);

            int node = _nodeCount++;
            _entityAtNode[node] = _buildIndices[median];
            _splitAxis[node] = (byte)axis;
            _leftNode[node] = BuildRange(left, median - 1, depth + 1);
            _rightNode[node] = BuildRange(median + 1, right, depth + 1);
            return node;
        }

        private void QuickSelect(int left, int right, int target, int axis)
        {
            while (left < right)
            {
                int pivot = left + ((right - left) >> 1);
                pivot = Partition(left, right, pivot, axis);
                if (target == pivot)
                {
                    return;
                }

                if (target < pivot)
                {
                    right = pivot - 1;
                }
                else
                {
                    left = pivot + 1;
                }
            }
        }

        private int Partition(int left, int right, int pivot, int axis)
        {
            int pivotEntity = _buildIndices[pivot];
            SwapBuildIndices(pivot, right);
            int store = left;

            for (int i = left; i < right; ++i)
            {
                if (CompareEntities(_buildIndices[i], pivotEntity, axis) < 0)
                {
                    SwapBuildIndices(store, i);
                    ++store;
                }
            }

            SwapBuildIndices(store, right);
            return store;
        }

        private int CompareEntities(int leftEntity, int rightEntity, int axis)
        {
            FPVector2 left = _positions[leftEntity];
            FPVector2 right = _positions[rightEntity];
            FP leftPrimary = axis == 0 ? left.X : left.Y;
            FP rightPrimary = axis == 0 ? right.X : right.Y;
            if (leftPrimary < rightPrimary)
            {
                return -1;
            }

            if (leftPrimary > rightPrimary)
            {
                return 1;
            }

            FP leftSecondary = axis == 0 ? left.Y : left.X;
            FP rightSecondary = axis == 0 ? right.Y : right.X;
            if (leftSecondary < rightSecondary)
            {
                return -1;
            }

            if (leftSecondary > rightSecondary)
            {
                return 1;
            }

            return leftEntity < rightEntity ? -1 : leftEntity > rightEntity ? 1 : 0;
        }

        private void SwapBuildIndices(int left, int right)
        {
            int value = _buildIndices[left];
            _buildIndices[left] = _buildIndices[right];
            _buildIndices[right] = value;
        }

        private void RadiusSearch(int node)
        {
            if (node < 0)
            {
                return;
            }

            int entity = _entityAtNode[node];
            FPVector2 position = _positions[entity];
            FP distanceSquared = (position - _queryCenter).SqrMagnitude;
            if (distanceSquared <= _queryRadiusSquared)
            {
                _queryEntities[_radiusMatchCount] = entity;
                _queryDistances[_radiusMatchCount] = distanceSquared;
                ++_radiusMatchCount;
            }

            FP delta = _splitAxis[node] == 0 ? _queryCenter.X - position.X : _queryCenter.Y - position.Y;
            int near = delta <= FP.Zero ? _leftNode[node] : _rightNode[node];
            int far = delta <= FP.Zero ? _rightNode[node] : _leftNode[node];
            RadiusSearch(near);
            if (delta * delta <= _queryRadiusSquared)
            {
                RadiusSearch(far);
            }
        }

        private void KNearestSearch(int node)
        {
            if (node < 0)
            {
                return;
            }

            int entity = _entityAtNode[node];
            FPVector2 position = _positions[entity];
            FP distanceSquared = (position - _queryCenter).SqrMagnitude;
            InsertKnnCandidate(entity, distanceSquared);

            FP delta = _splitAxis[node] == 0 ? _queryCenter.X - position.X : _queryCenter.Y - position.Y;
            int near = delta <= FP.Zero ? _leftNode[node] : _rightNode[node];
            int far = delta <= FP.Zero ? _rightNode[node] : _leftNode[node];
            KNearestSearch(near);

            FP worstDistance = _knnCount < _knnLimit ? FP.FromRaw(int.MaxValue) : _queryDistances[_knnCount - 1];
            if (delta * delta <= worstDistance)
            {
                KNearestSearch(far);
            }
        }

        private void InsertKnnCandidate(int entity, FP distanceSquared)
        {
            int insertion = _knnCount;
            while (insertion > 0 && ComesBefore(distanceSquared, entity, _queryDistances[insertion - 1], _queryEntities[insertion - 1]))
            {
                --insertion;
            }

            if (insertion >= _knnLimit)
            {
                return;
            }

            int newCount = _knnCount < _knnLimit ? _knnCount + 1 : _knnCount;
            for (int i = newCount - 1; i > insertion; --i)
            {
                _queryDistances[i] = _queryDistances[i - 1];
                _queryEntities[i] = _queryEntities[i - 1];
            }

            _queryDistances[insertion] = distanceSquared;
            _queryEntities[insertion] = entity;
            _knnCount = newCount;
        }

        private static bool ComesBefore(FP leftDistance, int leftEntity, FP rightDistance, int rightEntity)
        {
            return leftDistance < rightDistance || (leftDistance == rightDistance && leftEntity < rightEntity);
        }
    }
}
