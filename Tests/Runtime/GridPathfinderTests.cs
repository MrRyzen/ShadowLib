using System;
using NUnit.Framework;
using ShadowLib.Spatial;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class GridPathfinderTests
    {
        private sealed class TerrainCell : ICell<int>
        {
            public int X { get; private set; }
            public int Y { get; private set; }
            public int Occupant { get; set; }
            public bool IsOccupied { get; private set; }
            public byte Terrain = 1;

            public void Initialize(int x, int y)
            {
                X = x;
                Y = y;
            }

            public bool Contains(int item) => IsOccupied && Occupant == item;

            public void Clear() => IsOccupied = false;

            public void Occupy(int item)
            {
                Occupant = item;
                IsOccupied = true;
            }
        }

        private static GridPathfinder FromRows(params string[] rows)
        {
            var map = PathTestMaps.Parse(rows);
            var pathfinder = new GridPathfinder(map.Width, map.Height);
            map.Costs.CopyTo(pathfinder.EditCosts());
            return pathfinder;
        }

        [Test]
        public void NewPathfinder_IsFullyWalkableAtCostOne()
        {
            var pathfinder = new GridPathfinder(4, 3);
            var path = new (int X, int Y)[12];

            var status = pathfinder.FindPath((0, 0), (3, 2), path, out int length, out int cost);

            Assert.AreEqual((4, 3), (pathfinder.Width, pathfinder.Height));
            Assert.AreEqual(PathStatus.Found, status);
            Assert.AreEqual((6, 50), (length, cost));
            Assert.AreEqual((0, 0), path[0]);
            Assert.AreEqual((3, 2), path[5]);
        }

        [Test]
        public void Bake_ReadsCostsFromTheGridCells()
        {
            var grid = new Grid2D<TerrainCell, int>(3, 2, 1f, (x, y) => new TerrainCell());
            grid[1, 0].Terrain = 0;
            grid[1, 1].Terrain = 7;
            var pathfinder = new GridPathfinder(3, 2);

            pathfinder.Bake(grid, cell => cell.Terrain);

            CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 1, 7, 1 }, pathfinder.Costs.ToArray());
        }

        [Test]
        public void Bake_CanTreatOccupiedCellsAsBlocked()
        {
            var grid = new Grid2D<TerrainCell, int>(3, 1, 1f, (x, y) => new TerrainCell());
            grid.Occupy(1, 0, 42, Polyomino.Single);
            var pathfinder = new GridPathfinder(3, 1);

            pathfinder.Bake(grid, cell => cell.IsOccupied ? (byte)0 : (byte)1);

            Assert.AreEqual(PathStatus.NoPath, pathfinder.FindPath((0, 0), (2, 0), Span<(int X, int Y)>.Empty, out _, out _));
        }

        [Test]
        public void FindPath_ReturnsCoordinates_ForBothAlgorithms()
        {
            var pathfinder = FromRows(
                "..#",
                "..#",
                "...");
            var weighted = new (int X, int Y)[9];
            var uniform = new (int X, int Y)[9];

            var weightedStatus = pathfinder.FindPath((0, 0), (2, 2), weighted, out int weightedLength, out int weightedCost, allowDiagonal: true);
            var uniformStatus = pathfinder.FindPath((0, 0), (2, 2), uniform, out int uniformLength, out int uniformCost, allowDiagonal: true, uniformCost: true);

            Assert.AreEqual(PathStatus.Found, weightedStatus);
            Assert.AreEqual(PathStatus.Found, uniformStatus);
            Assert.AreEqual((4, 34), (weightedLength, weightedCost));
            Assert.AreEqual((4, 34), (uniformLength, uniformCost));
            Assert.AreEqual((0, 0), weighted[0]);
            Assert.AreEqual((2, 2), weighted[3]);
            Assert.AreEqual((2, 2), uniform[3]);
        }

        [Test]
        public void FindPath_UniformCost_IgnoresWeights()
        {
            var pathfinder = FromRows("S9G", "...");

            pathfinder.FindPath((0, 0), (2, 0), Span<(int X, int Y)>.Empty, out _, out int weighted);
            pathfinder.FindPath((0, 0), (2, 0), Span<(int X, int Y)>.Empty, out _, out int uniform, uniformCost: true);

            Assert.AreEqual(40, weighted);
            Assert.AreEqual(20, uniform);
        }

        [Test]
        public void FindPath_BufferTooSmall_ReportsLength_AndLeavesTheBufferUntouched()
        {
            var pathfinder = new GridPathfinder(5, 1);
            var small = new[] { (-7, -7), (-7, -7) };

            var status = pathfinder.FindPath((0, 0), (4, 0), small, out int length, out int cost);

            Assert.AreEqual(PathStatus.PathBufferTooSmall, status);
            Assert.AreEqual((5, 40), (length, cost));
            CollectionAssert.AreEqual(new[] { (-7, -7), (-7, -7) }, small);
        }

        [Test]
        public void FlowField_GivesStepsAndDistances_AndGoesStaleWhenCostsChange()
        {
            var pathfinder = new GridPathfinder(4, 1);
            Assert.IsFalse(pathfinder.TryGetFlowStep(0, 0, out _, out _));
            Assert.AreEqual(int.MaxValue, pathfinder.GetFlowDistance(0, 0));

            pathfinder.BuildFlowField(new[] { (3, 0) });

            Assert.IsTrue(pathfinder.TryGetFlowStep(0, 0, out int stepX, out int stepY));
            Assert.AreEqual((1, 0), (stepX, stepY));
            Assert.AreEqual(30, pathfinder.GetFlowDistance(0, 0));
            Assert.IsFalse(pathfinder.TryGetFlowStep(3, 0, out stepX, out stepY));
            Assert.AreEqual((0, 0), (stepX, stepY));
            Assert.AreEqual(0, pathfinder.GetFlowDistance(3, 0));

            pathfinder.SetCost(1, 0, 0);

            Assert.IsFalse(pathfinder.TryGetFlowStep(0, 0, out _, out _));
            Assert.AreEqual(int.MaxValue, pathfinder.GetFlowDistance(0, 0));

            pathfinder.BuildFlowField(new[] { (3, 0) });

            Assert.IsFalse(pathfinder.TryGetFlowStep(0, 0, out _, out _));
            Assert.AreEqual(10, pathfinder.GetFlowDistance(2, 0));
        }

        [Test]
        public void FlowField_GoesStaleOnBakeAndEditCosts_ButNotOnReadingCosts()
        {
            var grid = new Grid2D<TerrainCell, int>(4, 1, 1f, (x, y) => new TerrainCell());
            var pathfinder = new GridPathfinder(4, 1);
            var goal = new[] { (3, 0) };

            pathfinder.BuildFlowField(goal);
            Assert.AreEqual(4, pathfinder.Costs.Length);
            Assert.IsTrue(pathfinder.TryGetFlowStep(0, 0, out _, out _));

            pathfinder.EditCosts()[1] = 3;
            Assert.IsFalse(pathfinder.TryGetFlowStep(0, 0, out _, out _));

            pathfinder.BuildFlowField(goal);
            Assert.AreEqual(50, pathfinder.GetFlowDistance(0, 0));

            pathfinder.Bake(grid, cell => cell.Terrain);
            Assert.IsFalse(pathfinder.TryGetFlowStep(0, 0, out _, out _));
        }

        [Test]
        public void Bake_ThatThrowsPartWay_StillMarksTheFlowFieldStale()
        {
            var grid = new Grid2D<TerrainCell, int>(4, 1, 1f, (x, y) => new TerrainCell());
            var pathfinder = new GridPathfinder(4, 1);
            pathfinder.BuildFlowField(new[] { (3, 0) });

            Assert.Throws<InvalidOperationException>(() => pathfinder.Bake(grid, cell => cell.X == 2 ? throw new InvalidOperationException() : (byte)0));

            Assert.IsFalse(pathfinder.TryGetFlowStep(3, 0, out _, out _));
            Assert.AreEqual(int.MaxValue, pathfinder.GetFlowDistance(0, 0));
        }

        [Test]
        public void MixingBothSearchesOnOnePathfinder_KeepsAnsweringCorrectly()
        {
            var pathfinder = FromRows(
                "....#...",
                ".##.#.#.",
                ".#....#.",
                ".#.##.#.",
                "...#....");

            for (int i = 0; i < 6; i++)
            {
                var weighted = pathfinder.FindPath((0, 0), (7, 4), Span<(int X, int Y)>.Empty, out _, out int weightedCost, allowDiagonal: i % 2 == 0);
                var uniform = pathfinder.FindPath((0, 0), (7, 4), Span<(int X, int Y)>.Empty, out _, out int uniformCost, allowDiagonal: i % 2 == 0, uniformCost: true);

                Assert.AreEqual(PathStatus.PathBufferTooSmall, weighted);
                Assert.AreEqual(PathStatus.PathBufferTooSmall, uniform);
                Assert.AreEqual(weightedCost, uniformCost, $"round {i}");
            }
        }

        [Test]
        public void BadCoordinate_NamesTheRightArgument()
        {
            var pathfinder = new GridPathfinder(3, 2);

            Assert.AreEqual("x", Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.SetCost(3, 0, 1))!.ParamName);
            Assert.AreEqual("y", Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.SetCost(0, 2, 1))!.ParamName);
        }

        [Test]
        public void AfterWarmUp_NothingAllocates()
        {
            var pathfinder = new GridPathfinder(16, 16);
            var path = new (int X, int Y)[256];
            var goals = new[] { (15, 15), (0, 15) };
            pathfinder.FindPath((0, 0), (15, 15), path, out _, out _, allowDiagonal: true);
            pathfinder.FindPath((0, 0), (15, 15), path, out _, out _, allowDiagonal: true, uniformCost: true);
            pathfinder.BuildFlowField(goals, allowDiagonal: true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            pathfinder.FindPath((3, 0), (15, 9), path, out _, out _, allowDiagonal: true);
            pathfinder.FindPath((3, 0), (15, 9), path, out _, out _, allowDiagonal: true, uniformCost: true);
            pathfinder.BuildFlowField(goals, allowDiagonal: true);
            pathfinder.TryGetFlowStep(4, 4, out _, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void RejectsBadArguments()
        {
            var pathfinder = new GridPathfinder(3, 2);
            var path = new (int X, int Y)[6];
            var wrongSize = new Grid2D<TerrainCell, int>(2, 2, 1f, (x, y) => new TerrainCell());

            Assert.Throws<ArgumentOutOfRangeException>(() => new GridPathfinder(0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridPathfinder(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridPathfinder(int.MaxValue, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.SetCost(3, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.SetCost(0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath((3, 0), (0, 0), path, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath((0, 0), (0, 2), path, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.BuildFlowField(new[] { (3, 0) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.TryGetFlowStep(-1, 0, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.GetFlowDistance(0, 2));
            Assert.Throws<ArgumentNullException>(() => pathfinder.Bake<TerrainCell, int>(null!, cell => 1));
            Assert.Throws<ArgumentNullException>(() => pathfinder.Bake(wrongSize, null!));
            Assert.Throws<ArgumentException>(() => pathfinder.Bake(wrongSize, cell => 1));
        }
    }
}
