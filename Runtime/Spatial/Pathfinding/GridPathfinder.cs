namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    /// <summary>
    /// Pathfinding for a <see cref="Grid2D{TCell, TOccupant}"/> in (x, y) terms: owns a cost map and all working memory,
    /// so searches do not allocate after their first use. Not thread-safe; for parallel searches call
    /// <see cref="GridAStar"/>, <see cref="GridJumpPointSearch"/> or <see cref="GridFlowField"/> with one scratch per worker.
    /// </summary>
    public sealed class GridPathfinder
    {
        private readonly byte[] _costs;
        private readonly int[] _pathCells;
        private int[] _goalCells = Array.Empty<int>();
        private int[]? _searchScratch;
        private int[]? _flowScratch;
        private int[]? _flowDistances;
        private byte[]? _flowDirections;
        private bool _flowIsCurrent;

        /// <summary>Creates a pathfinder for a map of the given size with every cell walkable at cost 1.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the map is too large.</exception>
        public GridPathfinder(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
            if ((long)width * height > MaxCellCount)
                throw new ArgumentOutOfRangeException(nameof(width), "The map has too many cells.");

            Width = width;
            Height = height;
            _costs = new byte[width * height];
            _costs.AsSpan().Fill(1);
            _pathCells = new int[_costs.Length];
        }

        private const int MaxArrayLength = 0x7FFFFFC7;
        private const int MaxCellCount = (MaxArrayLength - 2) / 6;

        /// <summary>Gets the map width.</summary>
        public int Width { get; }

        /// <summary>Gets the map height.</summary>
        public int Height { get; }

        /// <summary>
        /// Gets the cost map, one byte per cell in row order (<c>y * Width + x</c>): 0 is blocked,
        /// 1 to 255 is the cost of entering the cell.
        /// </summary>
        public ReadOnlySpan<byte> Costs => _costs;

        /// <summary>
        /// Gets the cost map for bulk editing and marks the flow field as out of date. Ask for it again for each
        /// round of edits: writes through a span kept from before <see cref="BuildFlowField"/> are not noticed.
        /// </summary>
        public Span<byte> EditCosts()
        {
            _flowIsCurrent = false;
            return _costs;
        }

        /// <summary>Sets one cell's cost; 0 blocks it. Marks the flow field as out of date.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The cell is outside the map.</exception>
        public void SetCost(int x, int y, byte cost)
        {
            _costs[IndexOf(x, y, nameof(x), nameof(y))] = cost;
            _flowIsCurrent = false;
        }

        /// <summary>
        /// Copies costs from a grid, asking <paramref name="costOf"/> for each cell (return 0 for blocked).
        /// The pathfinder does not watch the grid afterwards: call this again, or <see cref="SetCost"/>, when it changes.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The grid is not the same size as this pathfinder.</exception>
        public void Bake<TCell, TOccupant>(Grid2D<TCell, TOccupant> grid, Func<TCell, byte> costOf)
            where TCell : class, ICell<TOccupant>
            where TOccupant : notnull
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (costOf == null)
                throw new ArgumentNullException(nameof(costOf));
            if (grid.Width != Width || grid.Height != Height)
                throw new ArgumentException("The grid is not the same size as the pathfinder.", nameof(grid));

            _flowIsCurrent = false;
            for (int y = 0; y < Height; y++)
            {
                var row = grid.GetRow(y);
                for (int x = 0; x < Width; x++)
                    _costs[y * Width + x] = costOf(row[x]);
            }
        }

        /// <summary>Finds a cheapest path between two cells.</summary>
        /// <param name="start">The start cell.</param>
        /// <param name="goal">The goal cell.</param>
        /// <param name="path">Receives every cell from start to goal. May be empty to ask only for the length and cost.</param>
        /// <param name="pathLength">Cells in the path; the length needed when the buffer is too small; 0 when there is no path.</param>
        /// <param name="pathCost">Sum of step costs (see <see cref="GridPathing"/>).</param>
        /// <param name="allowDiagonal">Whether diagonal steps are allowed.</param>
        /// <param name="uniformCost">True to ignore cost weights and use Jump Point Search: faster on rooms, corridors and caves, slower on wide-open ground.</param>
        /// <exception cref="ArgumentOutOfRangeException">The start or goal is outside the map.</exception>
        public PathStatus FindPath(
            (int X, int Y) start, (int X, int Y) goal, Span<(int X, int Y)> path,
            out int pathLength, out int pathCost, bool allowDiagonal = false, bool uniformCost = false)
        {
            int startCell = IndexOf(start.X, start.Y, nameof(start), nameof(start));
            int goalCell = IndexOf(goal.X, goal.Y, nameof(goal), nameof(goal));
            _searchScratch ??= new int[GridAStar.ScratchLength(_costs.Length)];

            var status = uniformCost
                ? GridJumpPointSearch.FindPath(_costs, Width, Height, startCell, goalCell, allowDiagonal, _searchScratch, _pathCells, out pathLength, out pathCost)
                : GridAStar.FindPath(_costs, Width, Height, startCell, goalCell, allowDiagonal, _searchScratch, _pathCells, out pathLength, out pathCost);
            if (status != PathStatus.Found)
                return status;
            if (path.Length < pathLength)
                return PathStatus.PathBufferTooSmall;

            for (int i = 0; i < pathLength; i++)
                path[i] = (_pathCells[i] % Width, _pathCells[i] / Width);

            return PathStatus.Found;
        }

        /// <summary>Builds the flow field toward the given goals, replacing any earlier one.</summary>
        /// <param name="goals">Goal cells. Blocked goals are ignored.</param>
        /// <param name="allowDiagonal">Whether diagonal steps are allowed.</param>
        /// <exception cref="ArgumentOutOfRangeException">A goal is outside the map.</exception>
        public void BuildFlowField(ReadOnlySpan<(int X, int Y)> goals, bool allowDiagonal = false)
        {
            if (_goalCells.Length < goals.Length)
                _goalCells = new int[goals.Length];
            for (int i = 0; i < goals.Length; i++)
                _goalCells[i] = IndexOf(goals[i].X, goals[i].Y, nameof(goals), nameof(goals));

            _flowScratch ??= new int[GridFlowField.ScratchLength(_costs.Length)];
            _flowDistances ??= new int[_costs.Length];
            _flowDirections ??= new byte[_costs.Length];
            GridFlowField.Build(
                _costs, Width, Height, _goalCells.AsSpan(0, goals.Length), allowDiagonal,
                _flowScratch, _flowDistances, _flowDirections);
            _flowIsCurrent = true;
        }

        /// <summary>Gets the step an agent on a cell should take toward the nearest goal.</summary>
        /// <returns>False, with a zero step, on a goal, a blocked or cut-off cell, or when no current flow field exists.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The cell is outside the map.</exception>
        public bool TryGetFlowStep(int x, int y, out int stepX, out int stepY)
        {
            int cell = IndexOf(x, y, nameof(x), nameof(y));
            byte direction = _flowIsCurrent ? _flowDirections![cell] : GridFlowField.NoDirection;
            stepX = GridFlowField.StepX(direction);
            stepY = GridFlowField.StepY(direction);
            return direction != GridFlowField.NoDirection;
        }

        /// <summary>Gets the cost of walking from a cell to the nearest goal, or <see cref="int.MaxValue"/> when it cannot or no current flow field exists.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The cell is outside the map.</exception>
        public int GetFlowDistance(int x, int y)
        {
            int cell = IndexOf(x, y, nameof(x), nameof(y));
            return _flowIsCurrent ? _flowDistances![cell] : int.MaxValue;
        }

        private int IndexOf(int x, int y, string xName, string yName)
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(xName, $"Cell ({x}, {y}) is outside the {Width}x{Height} map.");
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(yName, $"Cell ({x}, {y}) is outside the {Width}x{Height} map.");

            return y * Width + x;
        }
    }
}
