namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    /// <summary>
    /// Builds a flow field over a byte cost map: for every cell, the cost of walking to the nearest goal and the
    /// first step of that walk. One field serves any number of agents heading for the same goals.
    /// Allocation-free and usable from Burst jobs.
    /// </summary>
    public static class GridFlowField
    {
        /// <summary>The direction stored on goals, blocked cells and cells that cannot reach a goal.</summary>
        public const byte NoDirection = 255;

        /// <summary>Gets the scratch length a map of <paramref name="cellCount"/> cells needs.</summary>
        /// <exception cref="ArgumentException">The cell count is negative or too large.</exception>
        public static int ScratchLength(int cellCount)
        {
            if (cellCount < 0 || cellCount > int.MaxValue / 2)
                throw new ArgumentException("The cell count is negative or too large for one scratch buffer.", "cellCount");

            return 2 * cellCount;
        }

        /// <summary>Fills <paramref name="distances"/> and <paramref name="directions"/> for the given goals.</summary>
        /// <param name="costs">One byte per cell in row order; 0 is blocked, otherwise the cost of entering the cell.</param>
        /// <param name="width">Map width.</param>
        /// <param name="height">Map height.</param>
        /// <param name="goals">Goal cell indices. Blocked goals and repeats are ignored; with none, every cell is unreachable.</param>
        /// <param name="allowDiagonal">Whether diagonal steps are allowed. A diagonal needs both cells it passes between to be walkable.</param>
        /// <param name="scratch">Working memory of at least <see cref="ScratchLength"/> ints. Its contents do not matter.</param>
        /// <param name="distances">Per cell, the cost of the cheapest walk to a goal (entering a goal is paid, the cell walked from is not); <see cref="int.MaxValue"/> when there is none.</param>
        /// <param name="directions">Per cell, the step to take: 0 E (+X), 1 W, 2 N (+Y), 3 S, 4 NE, 5 NW, 6 SE, 7 SW, or <see cref="NoDirection"/>.</param>
        /// <exception cref="ArgumentException">The map size, a goal index or a buffer length is invalid.</exception>
        public static void Build(
            ReadOnlySpan<byte> costs, int width, int height,
            ReadOnlySpan<int> goals, bool allowDiagonal,
            Span<int> scratch, Span<int> distances, Span<byte> directions)
        {
            int cellCount = GridPathing.RequireMap(costs.Length, width, height);
            if (scratch.Length < ScratchLength(cellCount))
                throw new ArgumentException("The scratch buffer is too short; size it with ScratchLength.", "scratch");
            if (distances.Length < cellCount)
                throw new ArgumentException("The distance buffer is shorter than the map.", "distances");
            if (directions.Length < cellCount)
                throw new ArgumentException("The direction buffer is shorter than the map.", "directions");
            for (int i = 0; i < goals.Length; i++)
                GridPathing.RequireCell(goals[i], cellCount, "goals");

            distances = distances.Slice(0, cellCount);
            directions = directions.Slice(0, cellCount);
            // a loop, not Span.Fill: Burst 2.0.0 compiles Span<byte>.Fill to writes four bytes wide
            for (int i = 0; i < cellCount; i++)
            {
                distances[i] = int.MaxValue;
                directions[i] = NoDirection;
            }

            var positions = scratch.Slice(0, cellCount);
            var heap = scratch.Slice(cellCount, cellCount);
            int open = 0;
            for (int i = 0; i < goals.Length; i++)
            {
                int goal = goals[i];
                if (costs[goal] == 0 || distances[goal] == 0)
                    continue;

                distances[goal] = 0;
                PathHeap.Push(heap, positions, distances, distances, ref open, goal);
            }

            // ponytail: binary heap; a bucket queue would build low-cost maps several times faster
            int directionCount = allowDiagonal ? GridPathing.AllDirections : GridPathing.OrthogonalDirections;
            while (open > 0)
            {
                int current = PathHeap.Pop(heap, positions, distances, distances, ref open);
                int x = current % width;
                int y = current / width;
                int currentDistance = distances[current];
                int enterCurrent = costs[current];
                for (int direction = 0; direction < directionCount; direction++)
                {
                    if (!GridPathing.CanStep(costs, width, height, x, y, direction, out int from))
                        continue;

                    bool seen = distances[from] != int.MaxValue;
                    if (seen && positions[from] == PathHeap.Closed)
                        continue;

                    int step = GridPathing.StepLength(direction) * enterCurrent;
                    if (currentDistance >= int.MaxValue - step)
                        continue;

                    int distance = currentDistance + step;
                    if (distance >= distances[from])
                        continue;

                    distances[from] = distance;
                    directions[from] = (byte)GridPathing.Opposite(direction);
                    if (seen)
                        PathHeap.Improve(heap, positions, distances, distances, from);
                    else
                        PathHeap.Push(heap, positions, distances, distances, ref open, from);
                }
            }
        }

        /// <summary>Gets the cell a direction leads to, or <paramref name="cell"/> itself for <see cref="NoDirection"/>.</summary>
        /// <exception cref="ArgumentException">The direction is not 0 to 7 or <see cref="NoDirection"/>.</exception>
        public static int NextCell(int cell, int width, byte direction) =>
            cell + StepX(direction) + StepY(direction) * width;

        /// <summary>Gets the X part (-1, 0 or 1) of a direction; 0 for <see cref="NoDirection"/>.</summary>
        /// <exception cref="ArgumentException">The direction is not 0 to 7 or <see cref="NoDirection"/>.</exception>
        public static int StepX(byte direction) =>
            direction == NoDirection ? 0 : GridPathing.DirectionX(RequireDirection(direction));

        /// <summary>Gets the Y part (-1, 0 or 1) of a direction; 0 for <see cref="NoDirection"/>.</summary>
        /// <exception cref="ArgumentException">The direction is not 0 to 7 or <see cref="NoDirection"/>.</exception>
        public static int StepY(byte direction) =>
            direction == NoDirection ? 0 : GridPathing.DirectionY(RequireDirection(direction));

        private static int RequireDirection(byte direction)
        {
            if (direction >= GridPathing.AllDirections)
                throw new ArgumentException("A direction must be 0 to 7 or NoDirection.", "direction");

            return direction;
        }
    }
}
