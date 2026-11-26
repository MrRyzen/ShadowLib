using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class GridFlowFieldTests
    {
        private sealed class Field
        {
            internal int[] Distances = Array.Empty<int>();
            internal byte[] Directions = Array.Empty<byte>();
        }

        private static Field Build(PathMap map, bool allowDiagonal, params int[] goals)
        {
            var field = new Field { Distances = new int[map.CellCount], Directions = new byte[map.CellCount] };
            GridFlowField.Build(
                map.Costs, map.Width, map.Height, goals, allowDiagonal,
                new int[GridFlowField.ScratchLength(map.CellCount)], field.Distances, field.Directions);
            return field;
        }

        [Test]
        public void Corridor_PointsEveryCellAtTheGoal_WithRisingDistances()
        {
            var map = PathTestMaps.Parse("...G.");

            var field = Build(map, false, 3);

            CollectionAssert.AreEqual(new[] { 30, 20, 10, 0, 10 }, field.Distances);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, GridFlowField.NoDirection, 1 }, field.Directions);
        }

        [Test]
        public void DirectionCodes_MapToSteps()
        {
            var map = PathTestMaps.Parse(
                "...",
                ".G.",
                "...");

            var field = Build(map, true, 4);

            for (int cell = 0; cell < 9; cell++)
            {
                if (cell == 4)
                    continue;

                byte direction = field.Directions[cell];
                Assert.AreEqual(4, GridFlowField.NextCell(cell, 3, direction), $"cell {cell}");
                Assert.AreEqual(1 - cell % 3, GridFlowField.StepX(direction), $"cell {cell}");
                Assert.AreEqual(1 - cell / 3, GridFlowField.StepY(direction), $"cell {cell}");
            }

            Assert.AreEqual(4, GridFlowField.NextCell(4, 3, GridFlowField.NoDirection));
            Assert.AreEqual((0, 0), (GridFlowField.StepX(GridFlowField.NoDirection), GridFlowField.StepY(GridFlowField.NoDirection)));
            CollectionAssert.AreEqual(new byte[] { 4, 2, 5, 0, 255, 1, 6, 3, 7 }, field.Directions);
        }

        [Test]
        public void BlockedAndUnreachableCells_HaveNoDirection_AndMaxDistance()
        {
            var map = PathTestMaps.Parse("G.#.");

            var field = Build(map, true, 0);

            CollectionAssert.AreEqual(new[] { 0, 10, int.MaxValue, int.MaxValue }, field.Distances);
            CollectionAssert.AreEqual(new byte[] { 255, 1, 255, 255 }, field.Directions);
        }

        [Test]
        public void EnteringAGoalIsPaid_LeavingTheStartCellIsNot()
        {
            var map = PathTestMaps.Parse("9.5");

            var field = Build(map, false, 2);

            CollectionAssert.AreEqual(new[] { 60, 50, 0 }, field.Distances);
        }

        [Test]
        public void TwoGoals_EachCellFlowsToTheCheaperOne()
        {
            var map = PathTestMaps.Parse("G.....G");

            var field = Build(map, false, 0, 6);

            CollectionAssert.AreEqual(new[] { 0, 10, 20, 30, 20, 10, 0 }, field.Distances);
            Assert.AreEqual(1, field.Directions[1]);
            Assert.AreEqual(0, field.Directions[5]);
        }

        [Test]
        public void DuplicateBlockedAndMissingGoals_AreHandled()
        {
            var map = PathTestMaps.Parse("..#.");

            var duplicated = Build(map, false, 0, 0, 2);
            var none = Build(map, false);

            CollectionAssert.AreEqual(new[] { 0, 10, int.MaxValue, int.MaxValue }, duplicated.Distances);
            CollectionAssert.AreEqual(new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue }, none.Distances);
            CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255 }, none.Directions);
        }

        [Test]
        public void Diagonal_NeverSqueezesPastACorner()
        {
            var map = PathTestMaps.Parse(
                ".#",
                ".G");

            var field = Build(map, true, 3);

            Assert.AreEqual(20, field.Distances[0]);
            Assert.AreEqual(2, field.Directions[0]);
        }

        [Test]
        public void DistanceBeyondIntRange_IsUnreachable_NotAWrappedNumber()
        {
            var map = new PathMap { Width = 842_152, Height = 1, Costs = new byte[842_152] };
            map.Costs.AsSpan().Fill(255);

            var field = Build(map, false, 0);

            Assert.AreEqual(2_147_482_500, field.Distances[842_150]);
            Assert.AreEqual(1, field.Directions[842_150]);
            Assert.AreEqual(int.MaxValue, field.Distances[842_151]);
            Assert.AreEqual(GridFlowField.NoDirection, field.Directions[842_151]);
        }

        [Test]
        public void RandomMaps_DistancesMatchAStar_AndFollowingDirectionsArrivesAtThatCost([Values(false, true)] bool allowDiagonal)
        {
            var rng = new Xoshiro128StarStar(allowDiagonal ? 601UL : 600UL);
            for (int i = 0; i < 100; i++)
            {
                var map = PathTestMaps.Random(rng, 14, 40, 9);
                var goals = new int[rng.Range(1, 4)];
                for (int g = 0; g < goals.Length; g++)
                    goals[g] = rng.Range(0, map.CellCount);

                var field = Build(map, allowDiagonal, goals);
                var scratch = new int[GridAStar.ScratchLength(map.CellCount)];
                string label = PathTestMaps.Describe(map, allowDiagonal) + "goals " + string.Join(",", goals);

                for (int cell = 0; cell < map.CellCount; cell++)
                {
                    int expected = int.MaxValue;
                    foreach (int goal in goals)
                    {
                        var status = GridAStar.FindPath(map.Costs, map.Width, map.Height, cell, goal, allowDiagonal, scratch, Span<int>.Empty, out _, out int cost);
                        if (status != PathStatus.NoPath)
                            expected = Math.Min(expected, cost);
                    }

                    Assert.AreEqual(expected, field.Distances[cell], $"{label}\ncell {cell}");
                    if (expected == int.MaxValue || expected == 0)
                    {
                        Assert.AreEqual(GridFlowField.NoDirection, field.Directions[cell], $"{label}\ncell {cell}");
                        continue;
                    }

                    long walked = 0;
                    int at = cell;
                    for (int steps = 0; field.Directions[at] != GridFlowField.NoDirection; steps++)
                    {
                        Assert.Less(steps, map.CellCount, $"{label}\ncell {cell} loops");
                        int next = GridFlowField.NextCell(at, map.Width, field.Directions[at]);
                        Assert.IsTrue(PathTestMaps.IsLegalStep(map, at, next, allowDiagonal, out int stepCost), $"{label}\nillegal {at} -> {next}");
                        walked += stepCost;
                        at = next;
                    }

                    CollectionAssert.Contains(goals, at, $"{label}\ncell {cell}");
                    Assert.AreEqual(expected, walked, $"{label}\ncell {cell}");
                }
            }
        }

        [Test]
        public void Build_DoesNotAllocate_AndOverwritesAnOldField()
        {
            var map = PathTestMaps.Random(new Xoshiro128StarStar(9UL), 24, 20, 5);
            var scratch = new int[GridFlowField.ScratchLength(map.CellCount)];
            var distances = new int[map.CellCount];
            var directions = new byte[map.CellCount];
            var goals = new[] { map.Goal };
            var other = new[] { map.Start };
            GridFlowField.Build(map.Costs, map.Width, map.Height, other, true, scratch, distances, directions);

            long before = GC.GetAllocatedBytesForCurrentThread();
            GridFlowField.Build(map.Costs, map.Width, map.Height, goals, true, scratch, distances, directions);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            var fresh = Build(map, true, map.Goal);
            Assert.AreEqual(0L, allocated);
            CollectionAssert.AreEqual(fresh.Distances, distances);
            CollectionAssert.AreEqual(fresh.Directions, directions);
        }

        [Test]
        public void RejectsBadArguments()
        {
            var costs = new byte[6];
            var scratch = new int[GridFlowField.ScratchLength(6)];
            var distances = new int[6];
            var directions = new byte[6];
            var goals = new[] { 0 };

            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 4, 2, goals, false, scratch, distances, directions));
            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 3, 2, new[] { 6 }, false, scratch, distances, directions));
            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 3, 2, new[] { -1 }, false, scratch, distances, directions));
            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 3, 2, goals, false, new int[11], distances, directions));
            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 3, 2, goals, false, scratch, new int[5], directions));
            Assert.Throws<ArgumentException>(() => GridFlowField.Build(costs, 3, 2, goals, false, scratch, distances, new byte[5]));
            Assert.Throws<ArgumentException>(() => GridFlowField.ScratchLength(-1));
            Assert.Throws<ArgumentException>(() => GridFlowField.NextCell(0, 3, 8));
        }
    }
}
