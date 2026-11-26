namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    /// <summary>
    /// Jump Point Search over a byte cost map read as walkable or blocked: every non-zero cell costs 1.
    /// Returns a path as cheap as <see cref="GridAStar"/> would on that uniform map. Much faster than A* on rooms,
    /// corridors and caves; slower than A* on wide-open ground and on short trips across it.
    /// Allocation-free and usable from Burst jobs.
    /// </summary>
    public static class GridJumpPointSearch
    {
        private const int None = -1;

        /// <summary>Gets the scratch length a map of <paramref name="cellCount"/> cells needs.</summary>
        /// <exception cref="ArgumentException">The cell count is negative or too large.</exception>
        public static int ScratchLength(int cellCount) => PathScratch.Length(cellCount);

        /// <summary>Finds a shortest path from <paramref name="start"/> to <paramref name="goal"/>, ignoring cost weights.</summary>
        /// <param name="costs">One byte per cell in row order; 0 is blocked, anything else is walkable.</param>
        /// <param name="width">Map width.</param>
        /// <param name="height">Map height.</param>
        /// <param name="start">Start cell index (<c>y * width + x</c>).</param>
        /// <param name="goal">Goal cell index.</param>
        /// <param name="allowDiagonal">Whether diagonal steps are allowed. A diagonal needs both cells it passes between to be walkable.</param>
        /// <param name="scratch">Working memory of at least <see cref="ScratchLength"/> ints. Zeroed when first created, then reusable by this search and by <see cref="GridAStar"/> on any map size; one search at a time.</param>
        /// <param name="path">Receives every cell from start to goal, not only the jump points. May be empty to ask only for the length and cost.</param>
        /// <param name="pathLength">Cells in the path, start and goal included; the length needed when the buffer is too small; 0 when there is no path.</param>
        /// <param name="pathCost"><see cref="GridPathing.OrthogonalStep"/> per orthogonal step plus <see cref="GridPathing.DiagonalStep"/> per diagonal step.</param>
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

            int open = 0;
            stamps[start] = generation;
            costsSoFar[start] = 0;
            estimates[start] = GridPathing.Heuristic(start % width - goal % width, start / width - goal / width, allowDiagonal);
            parents[start] = start;
            PathHeap.Push(heap, positions, estimates, costsSoFar, ref open, start);

            while (open > 0)
            {
                int current = PathHeap.Pop(heap, positions, estimates, costsSoFar, ref open);
                if (current == goal)
                    return WritePath(parents, width, start, goal, costsSoFar[goal], path, out pathLength, out pathCost);

                int x = current % width;
                int y = current / width;
                int parent = parents[current];
                int dx = Math.Sign(x - parent % width);
                int dy = Math.Sign(y - parent / width);

                if (parent == current)
                {
                    int directions = allowDiagonal ? GridPathing.AllDirections : GridPathing.OrthogonalDirections;
                    for (int direction = 0; direction < directions; direction++)
                    {
                        if (GridPathing.CanStep(costs, width, height, x, y, direction, out _))
                            Visit(costs, width, height, scratch, generation, goal, allowDiagonal, ref open, current, x, y, GridPathing.DirectionX(direction), GridPathing.DirectionY(direction));
                    }
                }
                else if (!allowDiagonal)
                {
                    if (dx != 0)
                    {
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, 0, -1);
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, 0, 1);
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, dx, 0);
                    }
                    else
                    {
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, -1, 0);
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, 1, 0);
                        Visit(costs, width, height, scratch, generation, goal, false, ref open, current, x, y, 0, dy);
                    }
                }
                else if (dx != 0 && dy != 0)
                {
                    bool vertical = GridPathing.IsWalkable(costs, width, height, x, y + dy);
                    bool horizontal = GridPathing.IsWalkable(costs, width, height, x + dx, y);
                    if (vertical)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 0, dy);
                    if (horizontal)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, dx, 0);
                    if (vertical && horizontal)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, dx, dy);
                }
                else if (dx != 0)
                {
                    bool ahead = GridPathing.IsWalkable(costs, width, height, x + dx, y);
                    bool up = GridPathing.IsWalkable(costs, width, height, x, y + 1);
                    bool down = GridPathing.IsWalkable(costs, width, height, x, y - 1);
                    if (ahead)
                    {
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, dx, 0);
                        if (up)
                            Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, dx, 1);
                        if (down)
                            Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, dx, -1);
                    }

                    if (up)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 0, 1);
                    if (down)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 0, -1);
                }
                else
                {
                    bool ahead = GridPathing.IsWalkable(costs, width, height, x, y + dy);
                    bool right = GridPathing.IsWalkable(costs, width, height, x + 1, y);
                    bool left = GridPathing.IsWalkable(costs, width, height, x - 1, y);
                    if (ahead)
                    {
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 0, dy);
                        if (right)
                            Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 1, dy);
                        if (left)
                            Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, -1, dy);
                    }

                    if (right)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, 1, 0);
                    if (left)
                        Visit(costs, width, height, scratch, generation, goal, true, ref open, current, x, y, -1, 0);
                }
            }

            return PathStatus.NoPath;
        }

        private static void Visit(
            ReadOnlySpan<byte> costs, int width, int height, Span<int> scratch, int generation,
            int goal, bool allowDiagonal, ref int open, int current, int x, int y, int dx, int dy)
        {
            int jumpPoint;
            if (!allowDiagonal)
                jumpPoint = dx != 0 ? ScanRow(costs, width, height, goal, x + dx, y, dx) : ScanColumnWithRows(costs, width, height, goal, x, y + dy, dy);
            else if (dx != 0 && dy != 0)
                jumpPoint = ScanDiagonal(costs, width, height, goal, x + dx, y + dy, dx, dy);
            else
                jumpPoint = ScanStraight(costs, width, height, goal, x + dx, y + dy, dx, dy);

            if (jumpPoint == None)
                return;

            int cellCount = costs.Length;
            var stamps = PathScratch.Region(scratch, cellCount, PathScratch.Stamps);
            var costsSoFar = PathScratch.Region(scratch, cellCount, PathScratch.CostsSoFar);
            var estimates = PathScratch.Region(scratch, cellCount, PathScratch.Estimates);
            var parents = PathScratch.Region(scratch, cellCount, PathScratch.Parents);
            var positions = PathScratch.Region(scratch, cellCount, PathScratch.HeapPositions);
            var heap = PathScratch.Region(scratch, cellCount, PathScratch.Heap);

            bool seen = stamps[jumpPoint] == generation;
            if (seen && positions[jumpPoint] == PathHeap.Closed)
                return;

            int jumpX = jumpPoint % width;
            int jumpY = jumpPoint / width;
            long steps = Math.Max(Math.Abs(jumpX - x), Math.Abs(jumpY - y));
            long stepLength = dx != 0 && dy != 0 ? GridPathing.DiagonalStep : GridPathing.OrthogonalStep;
            long remaining = GridPathing.Heuristic(jumpX - goal % width, jumpY - goal / width, allowDiagonal);
            long cost = costsSoFar[current] + steps * stepLength;
            if (cost + remaining > int.MaxValue || (seen && cost >= costsSoFar[jumpPoint]))
                return;

            costsSoFar[jumpPoint] = (int)cost;
            estimates[jumpPoint] = (int)(cost + remaining);
            parents[jumpPoint] = current;
            if (seen)
            {
                PathHeap.Improve(heap, positions, estimates, costsSoFar, jumpPoint);
            }
            else
            {
                stamps[jumpPoint] = generation;
                PathHeap.Push(heap, positions, estimates, costsSoFar, ref open, jumpPoint);
            }
        }

        private static int ScanStraight(ReadOnlySpan<byte> costs, int width, int height, int goal, int x, int y, int dx, int dy)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                return None;

            int cell = y * width + x;
            if (dx != 0)
            {
                bool hasBelow = y > 0;
                bool hasAbove = y + 1 < height;
                for (int remaining = dx > 0 ? width - x : x + 1; remaining > 0; remaining--, cell += dx)
                {
                    if (costs[cell] == 0)
                        return None;
                    if (cell == goal
                        || (hasBelow && costs[cell - width] != 0 && costs[cell - width - dx] == 0)
                        || (hasAbove && costs[cell + width] != 0 && costs[cell + width - dx] == 0))
                        return cell;
                }

                return None;
            }

            bool hasLeft = x > 0;
            bool hasRight = x + 1 < width;
            int stride = dy * width;
            for (int remaining = dy > 0 ? height - y : y + 1; remaining > 0; remaining--, cell += stride)
            {
                if (costs[cell] == 0)
                    return None;
                if (cell == goal
                    || (hasLeft && costs[cell - 1] != 0 && costs[cell - 1 - stride] == 0)
                    || (hasRight && costs[cell + 1] != 0 && costs[cell + 1 - stride] == 0))
                    return cell;
            }

            return None;
        }

        private static int ScanDiagonal(ReadOnlySpan<byte> costs, int width, int height, int goal, int x, int y, int dx, int dy)
        {
            while (GridPathing.IsWalkable(costs, width, height, x, y))
            {
                int cell = y * width + x;
                if (cell == goal
                    || ScanStraight(costs, width, height, goal, x + dx, y, dx, 0) != None
                    || ScanStraight(costs, width, height, goal, x, y + dy, 0, dy) != None)
                    return cell;

                if (!GridPathing.IsWalkable(costs, width, height, x + dx, y) || !GridPathing.IsWalkable(costs, width, height, x, y + dy))
                    return None;

                x += dx;
                y += dy;
            }

            return None;
        }

        private static int ScanRow(ReadOnlySpan<byte> costs, int width, int height, int goal, int x, int y, int dx) =>
            ScanStraight(costs, width, height, goal, x, y, dx, 0);

        private static int ScanColumnWithRows(ReadOnlySpan<byte> costs, int width, int height, int goal, int x, int y, int dy)
        {
            while (GridPathing.IsWalkable(costs, width, height, x, y))
            {
                int cell = y * width + x;
                if (cell == goal
                    || ScanRow(costs, width, height, goal, x + 1, y, 1) != None
                    || ScanRow(costs, width, height, goal, x - 1, y, -1) != None)
                    return cell;

                y += dy;
            }

            return None;
        }

        private static PathStatus WritePath(
            ReadOnlySpan<int> parents, int width, int start, int goal, int cost, Span<int> path, out int pathLength, out int pathCost)
        {
            int length = 1;
            for (int cell = goal; cell != start; cell = parents[cell])
                length += SegmentSteps(cell, parents[cell], width);

            pathLength = length;
            pathCost = cost;
            if (path.Length < length)
                return PathStatus.PathBufferTooSmall;

            int slot = length - 1;
            for (int cell = goal; cell != start; cell = parents[cell])
            {
                int parent = parents[cell];
                int towardParent = Math.Sign(parent % width - cell % width) + Math.Sign(parent / width - cell / width) * width;
                for (int at = cell; at != parent; at += towardParent)
                    path[slot--] = at;
            }

            path[0] = start;
            return PathStatus.Found;
        }

        private static int SegmentSteps(int cell, int parent, int width) =>
            Math.Max(Math.Abs(cell % width - parent % width), Math.Abs(cell / width - parent / width));
    }
}
