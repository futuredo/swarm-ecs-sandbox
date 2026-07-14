using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Spatial
{
    /// <summary>
    /// Allocation-free deterministic sorting for spatial query results.
    /// Distances are ordered first and entity ids break exact-distance ties.
    /// </summary>
    internal static class SpatialQuerySort
    {
        public static void Sort(FP[] distances, int[] entityIds, int count)
        {
            if (count < 2)
            {
                return;
            }

            for (int root = (count >> 1) - 1; root >= 0; --root)
            {
                SiftDown(distances, entityIds, root, count);
            }

            for (int end = count - 1; end > 0; --end)
            {
                Swap(distances, entityIds, 0, end);
                SiftDown(distances, entityIds, 0, end);
            }
        }

        private static void SiftDown(FP[] distances, int[] entityIds, int root, int count)
        {
            while (true)
            {
                int child = (root << 1) + 1;
                if (child >= count)
                {
                    return;
                }

                int right = child + 1;
                if (right < count && IsGreater(distances[right], entityIds[right], distances[child], entityIds[child]))
                {
                    child = right;
                }

                if (!IsGreater(distances[child], entityIds[child], distances[root], entityIds[root]))
                {
                    return;
                }

                Swap(distances, entityIds, root, child);
                root = child;
            }
        }

        private static bool IsGreater(FP leftDistance, int leftId, FP rightDistance, int rightId)
        {
            return leftDistance > rightDistance || (leftDistance == rightDistance && leftId > rightId);
        }

        private static void Swap(FP[] distances, int[] entityIds, int left, int right)
        {
            FP distance = distances[left];
            distances[left] = distances[right];
            distances[right] = distance;

            int entity = entityIds[left];
            entityIds[left] = entityIds[right];
            entityIds[right] = entity;
        }
    }
}
