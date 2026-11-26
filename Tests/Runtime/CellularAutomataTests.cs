using System;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class CellularAutomataTests
    {
        private static FieldGrid<bool> Grid(params string[] rowsTopToBottom)
        {
            var grid = new FieldGrid<bool>(rowsTopToBottom[0].Length, rowsTopToBottom.Length, 1f);
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                    grid[x, y] = rowsTopToBottom[y][x] == '#';
            return grid;
        }

        private static string[] Rows(FieldGrid<bool> grid)
        {
            var rows = new string[grid.Height];
            for (int y = 0; y < grid.Height; y++)
            {
                var row = new char[grid.Width];
                for (int x = 0; x < grid.Width; x++)
                    row[x] = grid[x, y] ? '#' : '.';
                rows[y] = new string(row);
            }
            return rows;
        }

        [Test]
        public void Step_DeadCellWithEnoughNeighbours_IsBorn()
        {
            var grid = Grid(
                "###",
                "#.#",
                "...");

            new CellularAutomata(birthRule: 5, surviveRule: 9, edgesAlive: false).Step(grid);

            CollectionAssert.AreEqual(new[] { "...", ".#.", "..." }, Rows(grid));
        }

        [Test]
        public void Step_LiveCellBelowSurviveRule_Dies_AndAtOrAboveIt_Stays()
        {
            var grid = Grid(
                "###",
                "###",
                "###");

            new CellularAutomata(birthRule: 9, surviveRule: 4, edgesAlive: false).Step(grid);

            CollectionAssert.AreEqual(new[] { ".#.", "###", ".#." }, Rows(grid));
        }

        [Test]
        public void Step_UpdatesAllCellsFromTheSameGeneration()
        {
            var grid = Grid(
                "##.",
                "#..",
                "...");

            new CellularAutomata(birthRule: 3, surviveRule: 9, edgesAlive: false).Step(grid);

            CollectionAssert.AreEqual(new[] { "...", ".#.", "..." }, Rows(grid));
        }

        [Test]
        public void Step_EdgesAlive_CountsOutsideAsAlive()
        {
            var walled = Grid(".");
            var open = Grid(".");

            new CellularAutomata(birthRule: 5, surviveRule: 4).Step(walled);
            new CellularAutomata(birthRule: 5, surviveRule: 4, edgesAlive: false).Step(open);

            Assert.IsTrue(walled[0, 0]);
            Assert.IsFalse(open[0, 0]);
        }

        [Test]
        public void Step_Iterations_EqualRepeatedSingleSteps()
        {
            var rules = new CellularAutomata(5, 4);
            var together = new FieldGrid<bool>(20, 12, 1f);
            CellularAutomata.Randomize(together, new Xoshiro128StarStar(8UL), 0.45f);
            var oneByOne = new FieldGrid<bool>(20, 12, 1f);
            oneByOne.CopyFrom(together.AsReadOnlySpan());

            rules.Step(together, iterations: 4);
            for (int i = 0; i < 4; i++)
                rules.Step(oneByOne);

            CollectionAssert.AreEqual(Rows(oneByOne), Rows(together));
        }

        [Test]
        public void Step_ZeroIterations_ChangesNothing()
        {
            var grid = Grid("#.#", ".#.");

            new CellularAutomata(5, 4).Step(grid, iterations: 0);

            CollectionAssert.AreEqual(new[] { "#.#", ".#." }, Rows(grid));
        }

        [Test]
        public void Step_DoesNotAllocateAfterTheFirstCall()
        {
            var rules = new CellularAutomata(5, 4);
            var grid = new FieldGrid<bool>(16, 16, 1f);
            CellularAutomata.Randomize(grid, new Xoshiro128StarStar(8UL), 0.5f);
            rules.Step(grid);

            long before = GC.GetAllocatedBytesForCurrentThread();
            rules.Step(grid, iterations: 3);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void Randomize_DrawsOncePerCell_AndFollowsTheChance()
        {
            var used = new Xoshiro128StarStar(8UL);
            var grid = new FieldGrid<bool>(6, 5, 1f);
            CellularAutomata.Randomize(grid, used, 0.4f);

            var reference = new Xoshiro128StarStar(8UL);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 6; x++)
                    Assert.AreEqual(reference.NextFloat() < 0.4f, grid[x, y], $"({x}, {y})");
            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Randomize_ChanceZeroAndOne_FillNothingAndEverything()
        {
            var grid = new FieldGrid<bool>(6, 5, 1f);

            CellularAutomata.Randomize(grid, new Xoshiro128StarStar(8UL), 1f);
            Assert.AreEqual(-1, grid.FindIndex(alive => !alive));

            CellularAutomata.Randomize(grid, new Xoshiro128StarStar(8UL), 0f);
            Assert.AreEqual(-1, grid.FindIndex(alive => alive));
        }

        [Test]
        public void RejectsBadArguments()
        {
            var grid = new FieldGrid<bool>(3, 3, 1f);
            var rng = new Xoshiro128StarStar(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => new CellularAutomata(-1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellularAutomata(10, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellularAutomata(5, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellularAutomata(5, 10));
            Assert.Throws<ArgumentNullException>(() => new CellularAutomata(5, 4).Step(null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellularAutomata(5, 4).Step(grid, iterations: -1));
            Assert.Throws<ArgumentNullException>(() => CellularAutomata.Randomize(null!, rng, 0.5f));
            Assert.Throws<ArgumentNullException>(() => CellularAutomata.Randomize(grid, null!, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CellularAutomata.Randomize(grid, rng, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CellularAutomata.Randomize(grid, rng, 1.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CellularAutomata.Randomize(grid, rng, float.NaN));
        }
    }
}
