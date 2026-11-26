using System;
using System.Diagnostics;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class GridPathfindingBenchmark
    {
        private const int Size = 512;
        private const int Queries = 200;

        [Test, Explicit("Prints timings; asserts nothing.")]
        public void PrintTimings([Values("open", "noise15", "noise30", "rooms", "caves")] string layout)
        {
            var rng = new Xoshiro128StarStar(2026UL);
            var costs = new byte[Size * Size];
            costs.AsSpan().Fill(1);
            if (layout == "noise15" || layout == "noise30")
            {
                int blockedPercent = layout == "noise15" ? 15 : 30;
                for (int i = 0; i < costs.Length; i++)
                    costs[i] = rng.Range(0, 100) < blockedPercent ? (byte)0 : (byte)1;
            }
            else if (layout == "rooms")
            {
                const int room = 32;
                for (int line = room; line < Size; line += room)
                {
                    for (int i = 0; i < Size; i++)
                    {
                        costs[line * Size + i] = 0;
                        costs[i * Size + line] = 0;
                    }
                }

                for (int line = room; line < Size; line += room)
                {
                    for (int segment = 0; segment < Size; segment += room)
                    {
                        int door = segment + rng.Range(1, room);
                        if (door % room == 0 || door >= Size)
                            continue;

                        costs[line * Size + door] = 1;
                        costs[door * Size + line] = 1;
                    }
                }
            }
            else if (layout == "caves")
            {
                var cave = new FieldGrid<bool>(Size, Size, 1f);
                CellularAutomata.Randomize(cave, rng, 0.42f);
                new CellularAutomata(5, 4).Step(cave, 5);
                var walls = cave.AsSpan();
                for (int i = 0; i < costs.Length; i++)
                    costs[i] = walls[i] ? (byte)0 : (byte)1;
            }

            var starts = new int[Queries];
            var goals = new int[Queries];
            for (int i = 0; i < Queries; i++)
            {
                do starts[i] = rng.Range(0, costs.Length); while (costs[starts[i]] == 0);
                do goals[i] = rng.Range(0, costs.Length); while (costs[goals[i]] == 0);
            }

            var scratch = new int[GridAStar.ScratchLength(costs.Length)];
            var path = new int[costs.Length];
            var distances = new int[costs.Length];
            var directions = new byte[costs.Length];
            var flowScratch = new int[GridFlowField.ScratchLength(costs.Length)];

            foreach (bool allowDiagonal in new[] { false, true })
            {
                GridAStar.FindPath(costs, Size, Size, starts[0], goals[0], allowDiagonal, scratch, path, out _, out _);
                var aStar = Stopwatch.StartNew();
                int found = 0;
                for (int i = 0; i < Queries; i++)
                {
                    if (GridAStar.FindPath(costs, Size, Size, starts[i], goals[i], allowDiagonal, scratch, path, out _, out _) == PathStatus.Found)
                        found++;
                }
                aStar.Stop();

                GridJumpPointSearch.FindPath(costs, Size, Size, starts[0], goals[0], allowDiagonal, scratch, path, out _, out _);
                var jump = Stopwatch.StartNew();
                for (int i = 0; i < Queries; i++)
                    GridJumpPointSearch.FindPath(costs, Size, Size, starts[i], goals[i], allowDiagonal, scratch, path, out _, out _);
                jump.Stop();

                var flow = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                    GridFlowField.Build(costs, Size, Size, goals.AsSpan(i, 1), allowDiagonal, flowScratch, distances, directions);
                flow.Stop();

                TestContext.Out.WriteLine(
                    $"{Size}x{Size} {layout}, diagonal {allowDiagonal}: " +
                    $"A* {aStar.Elapsed.TotalMilliseconds / Queries:F3} ms/query, " +
                    $"JPS {jump.Elapsed.TotalMilliseconds / Queries:F3} ms/query, " +
                    $"flow field {flow.Elapsed.TotalMilliseconds / 10:F2} ms/build ({found}/{Queries} reachable)");
            }
        }
    }
}
