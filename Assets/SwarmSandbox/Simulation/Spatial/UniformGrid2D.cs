using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Spatial
{
    /// <summary>
    /// Fixed-capacity spatial hash grid. Build and query reuse all storage and
    /// therefore allocate no managed memory after construction.
    /// </summary>
    public sealed class UniformGrid2D
    {
        private readonly int _capacity;
        private readonly FP _cellSize;
        private readonly int _cellSizeRaw;
        private readonly int _bucketMask;
        private readonly int[] _cellX;
        private readonly int[] _cellY;
        private readonly int[] _cellHead;
        private readonly int[] _bucketStamp;
        private readonly int[] _nextEntity;
        private readonly int[] _queryEntities;
        private readonly ulong[] _queryDistances;

        private FPVector2[] _positions;
        private int _entityCount;
        private int _generation;

        public UniformGrid2D(int capacity, FP cellSize)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (cellSize <= FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            _capacity = capacity;
            _cellSize = cellSize;
            _cellSizeRaw = cellSize.Raw;

            int bucketCount = 1;
            long requestedBuckets = (long)capacity * 2L;
            while (bucketCount < requestedBuckets)
            {
                if (bucketCount > (1 << 29))
                {
                    throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity is too large for the grid hash table.");
                }

                bucketCount <<= 1;
            }

            _bucketMask = bucketCount - 1;
            _cellX = new int[bucketCount];
            _cellY = new int[bucketCount];
            _cellHead = new int[bucketCount];
            _bucketStamp = new int[bucketCount];
            _nextEntity = new int[capacity];
            _queryEntities = new int[capacity];
            _queryDistances = new ulong[capacity];
        }

        public int Capacity => _capacity;

        public int EntityCount => _entityCount;

        public FP CellSize => _cellSize;

        /// <summary>
        /// Rebuilds the hash grid. Entity ids are their indices in positions.
        /// The caller must keep the positions array alive until all queries finish.
        /// </summary>
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

            AdvanceGeneration();
            _positions = positions;
            _entityCount = count;

            // Descending insertion makes each per-cell linked list entity-id ascending.
            for (int entityId = count - 1; entityId >= 0; --entityId)
            {
                int x = ToCellCoordinate(positions[entityId].X.Raw);
                int y = ToCellCoordinate(positions[entityId].Y.Raw);
                int slot = FindOrCreateCell(x, y);
                _nextEntity[entityId] = _cellHead[slot];
                _cellHead[slot] = entityId;
            }
        }

        /// <summary>
        /// Returns at most result.Length ids inside the inclusive radius. Results
        /// are ordered by squared distance, then entity id, on every platform.
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
            if (_positions == null || _entityCount == 0 || result.Length == 0)
            {
                return;
            }

            QueryRadiusCore(
                center,
                radius,
                result.Length,
                _queryEntities,
                _queryDistances,
                out count);
            Array.Copy(_queryEntities, 0, result, 0, count);
        }

        /// <summary>
        /// Thread-safe radius query using scratch storage owned exclusively by the
        /// caller. The first <paramref name="count"/> entries in
        /// <paramref name="entityScratch"/> are the nearest matches, ordered by
        /// squared distance and then entity id. Both scratch arrays only need
        /// <paramref name="maxResults"/> entries. Build must not run concurrently.
        /// </summary>
        public void QueryRadius(
            FPVector2 center,
            FP radius,
            int maxResults,
            int[] entityScratch,
            ulong[] distanceScratch,
            out int count)
        {
            if (entityScratch == null)
            {
                throw new ArgumentNullException(nameof(entityScratch));
            }

            if (distanceScratch == null)
            {
                throw new ArgumentNullException(nameof(distanceScratch));
            }

            if (radius < FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            if (maxResults < 0 || maxResults > entityScratch.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults));
            }

            count = 0;
            if (_positions == null || _entityCount == 0 || maxResults == 0)
            {
                return;
            }

            if (distanceScratch.Length < maxResults)
            {
                throw new ArgumentException(
                    "Distance scratch must hold maxResults entries.",
                    nameof(distanceScratch));
            }

            QueryRadiusCore(
                center,
                radius,
                maxResults,
                entityScratch,
                distanceScratch,
                out count);
        }

        /// <summary>
        /// Thread-safe caller-scratch overload that copies the nearest matches to a
        /// compact result buffer. Build must not run concurrently with this query.
        /// </summary>
        public void QueryRadius(
            FPVector2 center,
            FP radius,
            int[] result,
            int[] entityScratch,
            ulong[] distanceScratch,
            out int count)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            QueryRadius(
                center,
                radius,
                result.Length,
                entityScratch,
                distanceScratch,
                out count);

            if (!ReferenceEquals(result, entityScratch))
            {
                Array.Copy(entityScratch, 0, result, 0, count);
            }
        }

        private void QueryRadiusCore(
            FPVector2 center,
            FP radius,
            int maxResults,
            int[] entityScratch,
            ulong[] distanceScratch,
            out int count)
        {

            ulong radiusSquared = SpatialQueryDistance.Square(radius.Raw);
            int minX = ToCellCoordinate((center.X - radius).Raw);
            int maxX = ToCellCoordinate((center.X + radius).Raw);
            int minY = ToCellCoordinate((center.Y - radius).Raw);
            int maxY = ToCellCoordinate((center.Y + radius).Raw);
            int selectedCount = 0;

            int y = minY;
            while (true)
            {
                int x = minX;
                while (true)
                {
                    int slot = FindCell(x, y);
                    if (slot >= 0)
                    {
                        int entityId = _cellHead[slot];
                        while (entityId >= 0)
                        {
                            ulong distanceSquared = SpatialQueryDistance.Squared(
                                _positions[entityId],
                                center);
                            if (distanceSquared <= radiusSquared)
                            {
                                InsertNearestCandidate(
                                    entityId,
                                    distanceSquared,
                                    maxResults,
                                    entityScratch,
                                    distanceScratch,
                                    ref selectedCount);
                            }

                            entityId = _nextEntity[entityId];
                        }
                    }

                    // Equality is checked before incrementing so int.MaxValue is
                    // processed once and never wraps back to int.MinValue.
                    if (x == maxX)
                    {
                        break;
                    }

                    ++x;
                }

                if (y == maxY)
                {
                    break;
                }

                ++y;
            }

            count = selectedCount;
        }

        private static void InsertNearestCandidate(
            int entityId,
            ulong distanceSquared,
            int maxResults,
            int[] entityScratch,
            ulong[] distanceScratch,
            ref int selectedCount)
        {
            int insertion;
            if (selectedCount < maxResults)
            {
                insertion = selectedCount;
                ++selectedCount;
            }
            else
            {
                insertion = maxResults - 1;
                if (!ComesBefore(
                    distanceSquared,
                    entityId,
                    distanceScratch[insertion],
                    entityScratch[insertion]))
                {
                    return;
                }
            }

            while (insertion > 0 && ComesBefore(
                distanceSquared,
                entityId,
                distanceScratch[insertion - 1],
                entityScratch[insertion - 1]))
            {
                distanceScratch[insertion] = distanceScratch[insertion - 1];
                entityScratch[insertion] = entityScratch[insertion - 1];
                --insertion;
            }

            distanceScratch[insertion] = distanceSquared;
            entityScratch[insertion] = entityId;
        }

        private static bool ComesBefore(
            ulong leftDistance,
            int leftEntity,
            ulong rightDistance,
            int rightEntity)
        {
            return leftDistance < rightDistance ||
                (leftDistance == rightDistance && leftEntity < rightEntity);
        }

        private void AdvanceGeneration()
        {
            ++_generation;
            if (_generation != 0)
            {
                return;
            }

            Array.Clear(_bucketStamp, 0, _bucketStamp.Length);
            _generation = 1;
        }

        private int ToCellCoordinate(int rawCoordinate)
        {
            int quotient = rawCoordinate / _cellSizeRaw;
            int remainder = rawCoordinate % _cellSizeRaw;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private int FindOrCreateCell(int x, int y)
        {
            int slot = Hash(x, y) & _bucketMask;
            while (_bucketStamp[slot] == _generation)
            {
                if (_cellX[slot] == x && _cellY[slot] == y)
                {
                    return slot;
                }

                slot = (slot + 1) & _bucketMask;
            }

            _bucketStamp[slot] = _generation;
            _cellX[slot] = x;
            _cellY[slot] = y;
            _cellHead[slot] = -1;
            return slot;
        }

        private int FindCell(int x, int y)
        {
            int slot = Hash(x, y) & _bucketMask;
            while (_bucketStamp[slot] == _generation)
            {
                if (_cellX[slot] == x && _cellY[slot] == y)
                {
                    return slot;
                }

                slot = (slot + 1) & _bucketMask;
            }

            return -1;
        }

        private static int Hash(int x, int y)
        {
            unchecked
            {
                uint hash = (uint)x * 0x8da6b343u;
                hash ^= (uint)y * 0xd8163841u;
                hash ^= hash >> 13;
                hash *= 0x85ebca6bu;
                return (int)(hash ^ (hash >> 16));
            }
        }
    }
}
