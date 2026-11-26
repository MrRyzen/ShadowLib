namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    /// <summary>The outcome of a path search.</summary>
    public enum PathStatus
    {
        /// <summary>A path was found and written.</summary>
        Found,

        /// <summary>The start or goal is blocked, or no route connects them.</summary>
        NoPath,

        /// <summary>A path exists but the buffer cannot hold it; the length needed is reported and nothing is written.</summary>
        PathBufferTooSmall,
    }

    /// <summary>
    /// Constants shared by the grid searches. A cost map is one byte per cell in row order
    /// (<c>index = y * width + x</c>): 0 is blocked, 1 to 255 is the cost of entering the cell.
    /// </summary>
    public static class GridPathing
    {
        /// <summary>Cost of an orthogonal step into a cell of cost 1.</summary>
        public const int OrthogonalStep = 10;

        /// <summary>Cost of a diagonal step into a cell of cost 1.</summary>
        public const int DiagonalStep = 14;

        internal const int OrthogonalDirections = 4;
        internal const int AllDirections = 8;

        // two bits per direction (E W N S NE NW SE SW), each holding the offset plus one
        private const int PackedX = 8786;
        private const int PackedY = 2597;

        internal static int DirectionX(int direction) => ((PackedX >> (direction * 2)) & 3) - 1;

        internal static int DirectionY(int direction) => ((PackedY >> (direction * 2)) & 3) - 1;

        internal static int Opposite(int direction) => direction < OrthogonalDirections ? direction ^ 1 : 11 - direction;

        internal static int StepLength(int direction) => direction < OrthogonalDirections ? OrthogonalStep : DiagonalStep;

        internal static bool IsWalkable(ReadOnlySpan<byte> costs, int width, int height, int x, int y) =>
            (uint)x < (uint)width && (uint)y < (uint)height && costs[y * width + x] != 0;

        internal static bool CanStep(ReadOnlySpan<byte> costs, int width, int height, int x, int y, int direction, out int neighbour)
        {
            int nx = x + DirectionX(direction);
            int ny = y + DirectionY(direction);
            neighbour = ny * width + nx;
            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height || costs[neighbour] == 0)
                return false;

            return direction < OrthogonalDirections || (costs[y * width + nx] != 0 && costs[ny * width + x] != 0);
        }

        internal static int Heuristic(int dx, int dy, bool allowDiagonal)
        {
            dx = Math.Abs(dx);
            dy = Math.Abs(dy);
            if (!allowDiagonal)
                return OrthogonalStep * (dx + dy);

            return dx > dy
                ? OrthogonalStep * dx + (DiagonalStep - OrthogonalStep) * dy
                : OrthogonalStep * dy + (DiagonalStep - OrthogonalStep) * dx;
        }

        internal static int RequireMap(int costsLength, int width, int height)
        {
            if (width <= 0)
                throw new ArgumentException("Width must be greater than zero.", "width");
            if (height <= 0)
                throw new ArgumentException("Height must be greater than zero.", "height");
            if ((long)width * height != costsLength)
                throw new ArgumentException("The cost map length must be width times height.", "costs");

            return costsLength;
        }

        internal static void RequireCell(int cell, int cellCount, string paramName)
        {
            if ((uint)cell >= (uint)cellCount)
                throw new ArgumentException("The cell index is outside the map.", paramName);
        }
    }

    internal static class PathScratch
    {
        internal const int Stamps = 0;
        internal const int CostsSoFar = 1;
        internal const int Estimates = 2;
        internal const int Parents = 3;
        internal const int HeapPositions = 4;
        internal const int Heap = 5;

        private const int HeaderLength = 3;
        private const int RegionCount = 6;

        internal static int Length(int cellCount)
        {
            if (cellCount < 0 || cellCount > (int.MaxValue - HeaderLength) / RegionCount)
                throw new ArgumentException("The cell count is negative or too large for one scratch buffer.", "cellCount");

            return HeaderLength + RegionCount * cellCount;
        }

        internal static int Begin(Span<int> scratch, int cellCount, int algorithmId)
        {
            if (scratch.Length < Length(cellCount))
                throw new ArgumentException("The scratch buffer is too short; size it with ScratchLength.", "scratch");

            if (scratch[1] != cellCount || scratch[2] != algorithmId || scratch[0] == int.MaxValue)
            {
                for (int i = 0; i < HeaderLength + cellCount; i++)
                    scratch[i] = 0;
                scratch[1] = cellCount;
                scratch[2] = algorithmId;
            }

            return ++scratch[0];
        }

        internal static Span<int> Region(Span<int> scratch, int cellCount, int region) =>
            scratch.Slice(HeaderLength + region * cellCount, cellCount);
    }
}
