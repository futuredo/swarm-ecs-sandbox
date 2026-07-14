using System;
using NUnit.Framework;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Tests.EditMode
{
    public sealed class GridIslandMapTests
    {
        [Test]
        public void Regions_UseDeterministicRowMajorIds()
        {
            GridMap map = new(5, 1);
            map.SetWalkable(1, 0, false);
            map.SetWalkable(3, 0, false);
            GridIslandMap islands = new(map);

            Assert.That(islands.RegionCount, Is.EqualTo(3));
            Assert.That(islands.GetRegionId(0, 0), Is.EqualTo(0));
            Assert.That(islands.GetRegionId(1, 0), Is.EqualTo(GridIslandMap.NoRegion));
            Assert.That(islands.GetRegionId(2, 0), Is.EqualTo(1));
            Assert.That(islands.GetRegionId(4, 0), Is.EqualTo(2));
        }

        [Test]
        public void DiagonalCells_AreDisconnectedWhenCardinalCornersAreBlocked()
        {
            GridMap map = new(2, 2);
            map.SetWalkable(1, 0, false);
            map.SetWalkable(0, 1, false);
            GridIslandMap islands = new(map);

            Assert.That(islands.RegionCount, Is.EqualTo(2));
            Assert.That(islands.AreConnected(0, 0, 1, 1), Is.False);
            Assert.That(islands.GetRegionId(1, 0), Is.EqualTo(GridIslandMap.NoRegion));
        }

        [Test]
        public void DiagonalCells_AreConnectedWhenBothCardinalCornersAreOpen()
        {
            GridMap map = new(2, 2);
            GridIslandMap islands = new(map);

            Assert.That(islands.RegionCount, Is.EqualTo(1));
            Assert.That(islands.AreConnected(0, 0, 1, 1), Is.True);
        }

        [Test]
        public void RevisionChange_LazilyRebuildsAndMergesRegions()
        {
            GridMap map = new(3, 1);
            map.SetWalkable(1, 0, false);
            GridIslandMap islands = new(map);
            int previousRevision = islands.BuiltRevision;

            Assert.That(islands.RegionCount, Is.EqualTo(2));
            Assert.That(islands.AreConnected(0, 0, 2, 0), Is.False);

            map.SetWalkable(1, 0, true);

            Assert.That(map.Revision, Is.Not.EqualTo(previousRevision));
            Assert.That(islands.RegionCount, Is.EqualTo(1));
            Assert.That(islands.BuiltRevision, Is.EqualTo(map.Revision));
            Assert.That(islands.AreConnected(0, 0, 2, 0), Is.True);
        }

        [Test]
        public void RebuildAfterWarmup_AllocatesNoManagedBytes()
        {
            GridMap map = new(32, 32);
            GridIslandMap islands = new(map);
            for (int i = 0; i < 4; ++i)
            {
                map.SetWalkable(15, 15, (i & 1) == 0);
                islands.Rebuild();
            }

            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; ++i)
            {
                map.SetWalkable(15, 15, (i & 1) == 0);
                islands.Rebuild();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Queries_RejectCoordinatesOutsideTheMap()
        {
            GridIslandMap islands = new(new GridMap(2, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() => islands.GetRegionId(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => islands.AreConnected(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => islands.GetRegionId(2, 0));
        }
    }
}
