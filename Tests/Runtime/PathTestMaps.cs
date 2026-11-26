using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    internal sealed class PathMap
    {
        internal byte[] Costs = Array.Empty<byte>();
        internal int Width;
        internal int Height;
        internal int Start = -1;
        internal int Goal = -1;

        internal int CellCount => Width * Height;

        internal int Index(int x, int y) => y * Width + x;

        internal PathMap Flattened()
        {
            var flat = new PathMap { Width = Width, Height = Height, Start = Start, Goal = Goal, Costs = new byte[Costs.Length] };
            for (int i = 0; i < Costs.Length; i++)
                flat.Costs[i] = Costs[i] == 0 ? (byte)0 : (byte)1;
            return flat;
        }
    }

    internal static class PathTestMaps
    {
        private static readonly int[] StepX = { 1, -1, 0, 0, 1, -1, 1, -1 };
        private static readonly int[] StepY = { 0, 0, 1, -1, 1, 1, -1, -1 };

        internal static PathMap Parse(params string[] rows)
        {
            var map = new PathMap { Width = rows[0].Length, Height = rows.Length };
            map.Costs = new byte[map.CellCount];
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    char symbol = rows[y][x];
                    int index = map.Index(x, y);
                    if (symbol == '#')
                        map.Costs[index] = 0;
                    else if (symbol >= '1' && symbol <= '9')
                        map.Costs[index] = (byte)(symbol - '0');
                    else
                        map.Costs[index] = 1;

                    if (symbol == 'S')
                        map.Start = index;
                    if (symbol == 'G')
                        map.Goal = index;
                }
            }

            return map;
        }

        internal static PathMap Random(IRandom rng, int maxSize, int maxBlockedPercent, int maxCost)
        {
            var map = new PathMap { Width = rng.Range(1, maxSize + 1), Height = rng.Range(1, maxSize + 1) };
            map.Costs = new byte[map.CellCount];
            int blockedPercent = rng.Range(0, maxBlockedPercent + 1);
            for (int i = 0; i < map.Costs.Length; i++)
                map.Costs[i] = rng.Range(0, 100) < blockedPercent ? (byte)0 : (byte)rng.Range(1, maxCost + 1);

            map.Start = rng.Range(0, map.CellCount);
            map.Goal = rng.Range(0, map.CellCount);
            return map;
        }

        internal static bool IsLegalStep(PathMap map, int from, int to, bool allowDiagonal, out int stepCost)
        {
            stepCost = 0;
            int fromX = from % map.Width, fromY = from / map.Width;
            int toX = to % map.Width, toY = to / map.Width;
            int dx = toX - fromX, dy = toY - fromY;
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
                return false;
            if (map.Costs[to] == 0)
                return false;

            bool diagonal = dx != 0 && dy != 0;
            if (diagonal && (!allowDiagonal || map.Costs[map.Index(toX, fromY)] == 0 || map.Costs[map.Index(fromX, toY)] == 0))
                return false;

            stepCost = (diagonal ? 14 : 10) * map.Costs[to];
            return true;
        }

        internal static long[] ReferenceDistancesFrom(PathMap map, int start, bool allowDiagonal)
        {
            var distance = new long[map.CellCount];
            var done = new bool[map.CellCount];
            for (int i = 0; i < distance.Length; i++)
                distance[i] = long.MaxValue;
            if (map.Costs[start] == 0)
                return distance;

            distance[start] = 0;
            while (true)
            {
                int current = -1;
                for (int i = 0; i < distance.Length; i++)
                {
                    if (!done[i] && distance[i] != long.MaxValue && (current < 0 || distance[i] < distance[current]))
                        current = i;
                }

                if (current < 0)
                    return distance;

                done[current] = true;
                int x = current % map.Width, y = current / map.Width;
                for (int direction = 0; direction < 8; direction++)
                {
                    int nx = x + StepX[direction], ny = y + StepY[direction];
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                        continue;

                    int neighbour = map.Index(nx, ny);
                    if (IsLegalStep(map, current, neighbour, allowDiagonal, out int stepCost) && distance[current] + stepCost < distance[neighbour])
                        distance[neighbour] = distance[current] + stepCost;
                }
            }
        }

        internal static void AssertValidPath(PathMap map, ReadOnlySpan<int> path, int start, int goal, bool allowDiagonal, long expectedCost, string label)
        {
            Assert.AreEqual(start, path[0], $"{label}: first cell");
            Assert.AreEqual(goal, path[path.Length - 1], $"{label}: last cell");

            long total = 0;
            for (int i = 1; i < path.Length; i++)
            {
                Assert.IsTrue(IsLegalStep(map, path[i - 1], path[i], allowDiagonal, out int stepCost), $"{label}: illegal step {path[i - 1]} -> {path[i]}");
                total += stepCost;
            }

            Assert.AreEqual(expectedCost, total, $"{label}: summed cost");
        }

        internal static string Describe(PathMap map, bool allowDiagonal)
        {
            var text = new System.Text.StringBuilder();
            text.Append($"{map.Width}x{map.Height} start {map.Start} goal {map.Goal} diagonal {allowDiagonal}\n");
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.Index(x, y);
                    text.Append(index == map.Start ? 'S' : index == map.Goal ? 'G' : map.Costs[index] == 0 ? '#' : (char)('0' + Math.Min((int)map.Costs[index], 9)));
                }
                text.Append('\n');
            }
            return text.ToString();
        }
    }
}
