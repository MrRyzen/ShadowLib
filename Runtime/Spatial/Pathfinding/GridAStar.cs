namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    /// <summary>
    /// A* over a byte cost map: the cheapest path between two cells on weighted terrain.
    /// Allocation-free and usable from Burst jobs; all working memory is the caller's scratch buffer.
    /// </summary>
    public static class GridAStar
    {

        /// <summary>Gets the scratch length a map of <paramref name="cellCount"/> cells needs.</summary>
        /// <exception cref="ArgumentException">The cell count is negative or too large.</exception>
        public static int ScratchLength(int cellCount) => PathScratch.Length(cellCount);

        /// <summary>Finds a cheapest path from <paramref name="start"/> to <paramref name="goal"/>.</summary>
        /// <param name="costs">One byte per cell in row order; 0 is blocked, otherwise the cost of entering the cell.</param>
        /// <param name="width">Map width.</param>
        /// <param name="height">Map height.</param>
        /// <param name="start">Start cell index (<c>y * width + x</c>).</param>
        /// <param name="goal">Goal cell index.</param>
        /// <param name="allowDiagonal">Whether diagonal steps are allowed. A diagonal needs both cells it passes between to be walkable.</param>
        /// <param name="scratch">Working memory of at least <see cref="ScratchLength"/> ints. Zeroed when first created, then reusable by this search and by <see cref="GridJumpPointSearch"/> on any map size; one search at a time.</param>
        /// <param name="path">Receives every cell from start to goal. May be empty to ask only for the length and cost.</param>
        /// <param name="pathLength">Cells in the path, start and goal included; the length needed when the buffer is too small; 0 when there is no path.</param>
        /// <param name="pathCost">Sum of step costs: <see cref="GridPathing.OrthogonalStep"/> or <see cref="GridPathing.DiagonalStep"/> times the cost of each cell entered.</param>
        /// <exception cref="ArgumentException">The map size, a cell index or the scratch length is invalid.</exception>
        public static PathStatus FindPath(
            ReadOnlySpan<byte> costs, int width, int height,
            int start, int goal, bool allowDiagonal,
            Span<int> scratch, Span<int> path,
            out int pathLength, out int pathCost)
        {
            pathLength = 0;
            pathCost = 0;
            int cellCount = GridPathing.RequireMap(costs.Length, width, height);
            GridPathing.RequireCell(start, cellCount, "start");
            GridPathing.RequireCell(goal, cellCount, "goal");
            int generation = PathScratch.Begin(scratch, cellCount);
            if (costs[start] == 0 || costs[goal] == 0)
                return PathStatus.NoPath;

            var stamps = PathScratch.Region(scratch, cellCount, PathScratch.Stamps);
            var costsSoFar = PathScratch.Region(scratch, cellCount, PathScratch.CostsSoFar);
            var estimates = PathScratch.Region(scratch, cellCount, PathScratch.Estimates);
            var parents = PathScratch.Region(scratch, cellCount, PathScratch.Parents);
            var positions = PathScratch.Region(scratch, cellCount, PathScratch.HeapPositions);
            var heap = PathScratch.Region(scratch, cellCount, PathScratch.Heap);

            int goalX = goal % width;
            int goalY = goal / width;
            int directions = allowDiagonal ? GridPathing.AllDirections : GridPathing.OrthogonalDirections;
            int open = 0;

            stamps[start] = generation;
            costsSoFar[start] = 0;
            estimates[start] = GridPathing.Heuristic(start % width - goalX, start / width - goalY, allowDiagonal);
            parents[start] = start;
            PathHeap.Push(heap, positions, estimates, costsSoFar, ref open, start);

            while (open > 0)
            {
                int current = PathHeap.Pop(heap, positions, estimates, costsSoFar, ref open);
                if (current == goal)
                    return WritePath(parents, start, goal, costsSoFar[goal], path, out pathLength, out pathCost);

                int x = current % width;
                int y = current / width;
                int currentCost = costsSoFar[current];
                for (int direction = 0; direction < directions; direction++)
                {
                    if (!GridPathing.CanStep(costs, width, height, x, y, direction, out int neighbour))
                        continue;

                    bool seen = stamps[neighbour] == generation;
                    if (seen && positions[neighbour] == PathHeap.Closed)
                        continue;

                    int step = GridPathing.StepLength(direction) * costs[neighbour];
                    if (currentCost > int.MaxValue - step)
                        continue;

                    int cost = currentCost + step;
                    if (seen && cost >= costsSoFar[neighbour])
                        continue;

                    int remaining = GridPathing.Heuristic(
                        x + GridPathing.DirectionX(direction) - goalX, y + GridPathing.DirectionY(direction) - goalY, allowDiagonal);
                    if (cost > int.MaxValue - remaining)
                        continue;

                    costsSoFar[neighbour] = cost;
                    estimates[neighbour] = cost + remaining;
                    parents[neighbour] = current;
                    if (seen)
                    {
                        PathHeap.Improve(heap, positions, estimates, costsSoFar, neighbour);
                    }
                    else
                    {
                        stamps[neighbour] = generation;
                        PathHeap.Push(heap, positions, estimates, costsSoFar, ref open, neighbour);
                    }
                }
            }

            return PathStatus.NoPath;
        }

        private static PathStatus WritePath(
            ReadOnlySpan<int> parents, int start, int goal, int cost, Span<int> path, out int pathLength, out int pathCost)
        {
            int length = 1;
            for (int cell = goal; cell != start; cell = parents[cell])
                length++;

            pathLength = length;
            pathCost = cost;
            if (path.Length < length)
                return PathStatus.PathBufferTooSmall;

            int slot = length - 1;
            for (int cell = goal; cell != start; cell = parents[cell])
                path[slot--] = cell;
            path[0] = start;
            return PathStatus.Found;
        }
    }
}
