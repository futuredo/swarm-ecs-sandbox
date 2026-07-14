using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Pathfinding
{
    /// <summary>
    /// Dense row-major navigation grid. Walkability and integer traversal
    /// penalties are deliberately independent of Unity navigation APIs.
    /// </summary>
    public sealed class GridMap
    {
        private readonly bool[] _walkable;
        private readonly int[] _penalty;

        public GridMap(int width, int height)
            : this(width, height, FP.One, FPVector2.Zero)
        {
        }

        public GridMap(int width, int height, FP cellSize, FPVector2 origin)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (cellSize <= FP.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            long nodeCount = (long)width * height;
            if (nodeCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Grid node count exceeds the supported array size.");
            }

            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            _walkable = new bool[(int)nodeCount];
            _penalty = new int[(int)nodeCount];

            for (int i = 0; i < _walkable.Length; ++i)
            {
                _walkable[i] = true;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public int NodeCount => _walkable.Length;

        public FP CellSize { get; }

        public FPVector2 Origin { get; }

        /// <summary>Changes whenever walkability or a traversal penalty changes.</summary>
        public int Revision { get; private set; }

        public bool IsInside(int x, int y)
        {
            return (uint)x < (uint)Width && (uint)y < (uint)Height;
        }

        public int ToIndex(int x, int y)
        {
            if (!IsInside(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Grid coordinates are outside the map.");
            }

            return y * Width + x;
        }

        public bool TryToIndex(int x, int y, out int index)
        {
            if (!IsInside(x, y))
            {
                index = -1;
                return false;
            }

            index = y * Width + x;
            return true;
        }

        public void IndexToCoordinates(int index, out int x, out int y)
        {
            if ((uint)index >= (uint)NodeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            y = index / Width;
            x = index - y * Width;
        }

        public bool IsWalkable(int x, int y)
        {
            return IsInside(x, y) && _walkable[y * Width + x];
        }

        public bool IsWalkable(int index)
        {
            return (uint)index < (uint)NodeCount && _walkable[index];
        }

        public void SetWalkable(int x, int y, bool walkable)
        {
            int index = ToIndex(x, y);
            if (_walkable[index] == walkable)
            {
                return;
            }

            _walkable[index] = walkable;
            unchecked
            {
                ++Revision;
            }
        }

        public int GetPenalty(int x, int y)
        {
            return _penalty[ToIndex(x, y)];
        }

        public int GetPenalty(int index)
        {
            if ((uint)index >= (uint)NodeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _penalty[index];
        }

        public void SetPenalty(int x, int y, int penalty)
        {
            if (penalty < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(penalty), "A* penalties must be non-negative.");
            }

            int index = ToIndex(x, y);
            if (_penalty[index] == penalty)
            {
                return;
            }

            _penalty[index] = penalty;
            unchecked
            {
                ++Revision;
            }
        }

        public FPVector2 CellCenter(int x, int y)
        {
            if (!IsInside(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            FP half = FP.FromRatio(1, 2);
            FP worldX = Origin.X + (FP.FromInt(x) + half) * CellSize;
            FP worldY = Origin.Y + (FP.FromInt(y) + half) * CellSize;
            return new FPVector2(worldX, worldY);
        }

        public FPVector2 CellCenter(int index)
        {
            IndexToCoordinates(index, out int x, out int y);
            return CellCenter(x, y);
        }

        public bool TryWorldToCell(FPVector2 worldPosition, out int x, out int y)
        {
            long localXRaw = (long)worldPosition.X.Raw - Origin.X.Raw;
            long localYRaw = (long)worldPosition.Y.Raw - Origin.Y.Raw;
            long cellX = FloorDivide(localXRaw, CellSize.Raw);
            long cellY = FloorDivide(localYRaw, CellSize.Raw);
            if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height)
            {
                x = -1;
                y = -1;
                return false;
            }

            x = (int)cellX;
            y = (int)cellY;
            return true;
        }

        private static long FloorDivide(long numerator, int positiveDenominator)
        {
            long quotient = numerator / positiveDenominator;
            long remainder = numerator % positiveDenominator;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }
}
