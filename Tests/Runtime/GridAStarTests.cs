using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class GridAStarTests
    {
        private static PathStatus Find(PathMap map, bool allowDiagonal, out int[] path, out int cost)
        {
            var buffer = new int[map.CellCount];
            var status = GridAStar.FindPath(
                map.Costs, map.Width, map.Height, map.Start, map.Goal, allowDiagonal,
                new int[GridAStar.ScratchLength(map.CellCount)], buffer, out int length, out cost);
            path = status == PathStatus.Found ? buffer.AsSpan(0, length).ToArray() : Array.Empty<int>();
            return status;
        }

        [Test]
        public void StraightLine_IsFound_WithOneStepCostPerCell()
        {
            var map = PathTestMaps.Parse("S...G");

            Assert.AreEqual(PathStatus.Found, Find(map, false, out int[] path, out int cost));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, path);
            Assert.AreEqual(4 * GridPathing.OrthogonalStep, cost);
        }

        [Test]
        public void WallWithOneGap_IsCrossedThroughTheGap()
        {
            var map = PathTestMaps.Parse(
                "S.#..",
                "..#..",
                "....G",
                "..#..");

            Assert.AreEqual(PathStatus.Found, Find(map, false, out int[] path, out int cost));
            CollectionAssert.Contains(path, map.Index(2, 2));
            Assert.AreEqual(6 * GridPathing.OrthogonalStep, cost);
        }

        [Test]
        public void EnclosedGoal_BlockedStart_BlockedGoal_AreNoPath()
        {
            var enclosed = PathTestMaps.Parse(
                "S.#G",
                "..##");
            var blocked = PathTestMaps.Parse("S.#");

            Assert.AreEqual(PathStatus.NoPath, Find(enclosed, true, out _, out int cost));
            Assert.AreEqual(0, cost);

            blocked.Goal = 2;
            Assert.AreEqual(PathStatus.NoPath, Find(blocked, false, out _, out _));

            blocked.Start = 2;
            blocked.Goal = 0;
            Assert.AreEqual(PathStatus.NoPath, Find(blocked, false, out _, out _));

            blocked.Goal = 2;
            Assert.AreEqual(PathStatus.NoPath, Find(blocked, false, out _, out _));
        }

        [Test]
        public void StartEqualToGoal_IsFound_WithOneCellAndNoCost()
        {
            var map = PathTestMaps.Parse("...");
            map.Start = map.Goal = 1;

            Assert.AreEqual(PathStatus.Found, Find(map, false, out int[] path, out int cost));
            CollectionAssert.AreEqual(new[] { 1 }, path);
            Assert.AreEqual(0, cost);
        }

        [Test]
        public void SingleCellMap_Works()
        {
            var map = PathTestMaps.Parse("S");
            map.Goal = 0;

            Assert.AreEqual(PathStatus.Found, Find(map, true, out int[] path, out _));
            CollectionAssert.AreEqual(new[] { 0 }, path);
        }

        [Test]
        public void WeightedTerrain_TakesTheRoadWhenCheaper_AndTheMudWhenTheRoadIsLonger()
        {
            var cheapRoad = PathTestMaps.Parse(
                "S9G",
                "...");
            var shortMud = PathTestMaps.Parse(
                "S2G",
                "...");

            Find(cheapRoad, false, out int[] around, out int aroundCost);
            Find(shortMud, false, out int[] through, out int throughCost);

            Assert.AreEqual(4 * 10, aroundCost);
            Assert.AreEqual(5, around.Length);
            Assert.AreEqual(2 * 10 + 10, throughCost);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, through);
        }

        [Test]
        public void Diagonal_IsUsedOnlyWhenAllowed_AndNeverSqueezesPastACorner()
        {
            var open = PathTestMaps.Parse(
                "S.",
                ".G");
            var corner = PathTestMaps.Parse(
                "S#",
                ".G");
            var pinched = PathTestMaps.Parse(
                "S#",
                "#G");

            Find(open, true, out int[] diagonal, out int diagonalCost);
            Find(open, false, out _, out int straightCost);
            Find(corner, true, out int[] aroundCorner, out int cornerCost);

            CollectionAssert.AreEqual(new[] { 0, 3 }, diagonal);
            Assert.AreEqual(GridPathing.DiagonalStep, diagonalCost);
            Assert.AreEqual(2 * GridPathing.OrthogonalStep, straightCost);
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, aroundCorner);
            Assert.AreEqual(2 * GridPathing.OrthogonalStep, cornerCost);
            Assert.AreEqual(PathStatus.NoPath, Find(pinched, true, out _, out _));
        }

        [Test]
        public void RandomMaps_PathsAreLegal_AndAsCheapAsPlainDijkstra([Values(false, true)] bool allowDiagonal)
        {
            var rng = new Xoshiro128StarStar(allowDiagonal ? 501UL : 500UL);
            int found = 0;
            for (int i = 0; i < 300; i++)
            {
                var map = PathTestMaps.Random(rng, 24, 45, 9);
                long expected = PathTestMaps.ReferenceDistancesFrom(map, map.Start, allowDiagonal)[map.Goal];
                var status = Find(map, allowDiagonal, out int[] path, out int cost);
                string label = PathTestMaps.Describe(map, allowDiagonal);

                if (expected == long.MaxValue || map.Costs[map.Goal] == 0)
                {
                    Assert.AreEqual(PathStatus.NoPath, status, label);
                    continue;
                }

                found++;
                Assert.AreEqual(PathStatus.Found, status, label);
                Assert.AreEqual(expected, cost, label);
                PathTestMaps.AssertValidPath(map, path, map.Start, map.Goal, allowDiagonal, expected, label);
            }

            Assert.Greater(found, 100);
        }

        [Test]
        public void RandomMaps_CheapTerrainAndManyGoals_StayAsCheapAsPlainDijkstra([Values(false, true)] bool allowDiagonal)
        {
            var rng = new Xoshiro128StarStar(allowDiagonal ? 511UL : 510UL);
            int found = 0;
            for (int i = 0; i < 150; i++)
            {
                var map = PathTestMaps.Random(rng, 28, 55, 2);
                long[] expected = PathTestMaps.ReferenceDistancesFrom(map, map.Start, allowDiagonal);
                var scratch = new int[GridAStar.ScratchLength(map.CellCount)];
                for (int query = 0; query < 12; query++)
                {
                    int goal = rng.Range(0, map.CellCount);
                    var status = GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Start, goal, allowDiagonal, scratch, Span<int>.Empty, out _, out int cost);
                    if (expected[goal] == long.MaxValue || map.Costs[map.Start] == 0)
                    {
                        Assert.AreEqual(PathStatus.NoPath, status, PathTestMaps.Describe(map, allowDiagonal) + "goal " + goal);
                        continue;
                    }

                    found++;
                    Assert.AreEqual(expected[goal], cost, PathTestMaps.Describe(map, allowDiagonal) + "goal " + goal);
                }
            }

            Assert.Greater(found, 300);
        }

        [Test]
        public void OpenGround_TieBreakIsPinned([Values(false, true)] bool allowDiagonal)
        {
            var map = PathTestMaps.Parse(
                "S.....",
                "......",
                "......",
                "......",
                "......",
                ".....G");
            if (allowDiagonal)
                map.Goal = map.Index(5, 2);

            Assert.AreEqual(PathStatus.Found, Find(map, allowDiagonal, out int[] path, out _));
            CollectionAssert.AreEqual(
                allowDiagonal ? new[] { 0, 7, 14, 15, 16, 17 } : new[] { 0, 1, 2, 3, 4, 5, 11, 17, 23, 29, 35 },
                path);
        }

        [Test]
        public void PathBuffer_TooSmallOrEmpty_ReportsTheLengthNeeded_AndIsLeftUntouched()
        {
            var map = PathTestMaps.Parse("S...G");
            var scratch = new int[GridAStar.ScratchLength(5)];
            var small = new[] { -7, -7, -7, -7 };

            var status = GridAStar.FindPath(map.Costs, 5, 1, 0, 4, false, scratch, small, out int length, out int cost);
            var emptyStatus = GridAStar.FindPath(map.Costs, 5, 1, 0, 4, false, scratch, Span<int>.Empty, out int emptyLength, out int emptyCost);

            Assert.AreEqual(PathStatus.PathBufferTooSmall, status);
            Assert.AreEqual((5, 40), (length, cost));
            CollectionAssert.AreEqual(new[] { -7, -7, -7, -7 }, small);
            Assert.AreEqual(PathStatus.PathBufferTooSmall, emptyStatus);
            Assert.AreEqual((5, 40), (emptyLength, emptyCost));
        }

        [Test]
        public void Scratch_ReusedAcrossQueriesAndMapSizes_AnswersLikeAFreshOne()
        {
            var rng = new Xoshiro128StarStar(77UL);
            var shared = new int[GridAStar.ScratchLength(24 * 24)];
            for (int i = 0; i < 500; i++)
            {
                var map = PathTestMaps.Random(rng, 24, 40, 5);
                var sharedPath = new int[map.CellCount];
                var freshPath = new int[map.CellCount];

                var sharedStatus = GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, shared, sharedPath, out int sharedLength, out int sharedCost);
                var freshStatus = GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, new int[GridAStar.ScratchLength(map.CellCount)], freshPath, out int freshLength, out int freshCost);

                Assert.AreEqual(freshStatus, sharedStatus, $"query {i}");
                Assert.AreEqual((freshLength, freshCost), (sharedLength, sharedCost), $"query {i}");
                CollectionAssert.AreEqual(freshPath, sharedPath, $"query {i}");
            }
        }

        [Test]
        public void RouteCostBeyondIntRange_IsNoPath_NotAWrappedNumber()
        {
            var fits = new byte[842_151];
            var overflows = new byte[842_152];
            fits.AsSpan().Fill(255);
            overflows.AsSpan().Fill(255);
            var scratch = new int[GridAStar.ScratchLength(overflows.Length)];

            var fitsStatus = GridAStar.FindPath(fits, fits.Length, 1, 0, fits.Length - 1, false, scratch, Span<int>.Empty, out _, out int fitsCost);
            var overflowStatus = GridAStar.FindPath(overflows, overflows.Length, 1, 0, overflows.Length - 1, false, scratch, Span<int>.Empty, out _, out int overflowCost);

            Assert.AreEqual(PathStatus.PathBufferTooSmall, fitsStatus);
            Assert.AreEqual(2_147_482_500, fitsCost);
            Assert.AreEqual(PathStatus.NoPath, overflowStatus);
            Assert.AreEqual(0, overflowCost);
        }

        [Test]
        public void FindPath_DoesNotAllocate()
        {
            var map = PathTestMaps.Random(new Xoshiro128StarStar(9UL), 24, 20, 5);
            var scratch = new int[GridAStar.ScratchLength(map.CellCount)];
            var path = new int[map.CellCount];
            GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, scratch, path, out _, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, scratch, path, out _, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void Path_IsPinned()
        {
            var map = PathTestMaps.Parse(
                "S..#....",
                ".#.#.##.",
                ".#...#..",
                ".####.#.",
                "......#G");

            Assert.AreEqual(PathStatus.Found, Find(map, true, out int[] path, out int cost));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 10, 18, 19, 20, 12, 4, 5, 6, 7, 15, 23, 31, 39 }, path);
            Assert.AreEqual(150, cost);
        }

        [Test]
        public void RejectsBadArguments()
        {
            var costs = new byte[6];
            var scratch = new int[GridAStar.ScratchLength(6)];
            var path = new int[6];

            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 0, 6, 0, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 3, 0, 0, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 4, 2, 0, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 3, 2, -1, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 3, 2, 0, 6, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(costs, 3, 2, 0, 1, false, new int[scratch.Length - 1], path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.FindPath(new byte[0], int.MaxValue / 14, 1, 0, 0, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridAStar.ScratchLength(-1));
            Assert.Throws<ArgumentException>(() => GridAStar.ScratchLength(int.MaxValue / 2));
        }
    }
}
