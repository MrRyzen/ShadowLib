using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial.Pathfinding;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class GridJumpPointSearchTests
    {
        private static PathStatus Find(PathMap map, bool allowDiagonal, out int[] path, out int cost)
        {
            var buffer = new int[map.CellCount];
            var status = GridJumpPointSearch.FindPath(
                map.Costs, map.Width, map.Height, map.Start, map.Goal, allowDiagonal,
                new int[GridJumpPointSearch.ScratchLength(map.CellCount)], buffer, out int length, out cost);
            path = status == PathStatus.Found ? buffer.AsSpan(0, length).ToArray() : Array.Empty<int>();
            return status;
        }

        private static void AssertMatchesAStar(PathMap map, bool allowDiagonal)
        {
            var flat = map.Flattened();
            string label = PathTestMaps.Describe(map, allowDiagonal);
            var expectedStatus = GridAStar.FindPath(
                flat.Costs, flat.Width, flat.Height, flat.Start, flat.Goal, allowDiagonal,
                new int[GridAStar.ScratchLength(flat.CellCount)], new int[flat.CellCount], out int expectedLength, out int expectedCost);

            var status = Find(map, allowDiagonal, out int[] path, out int cost);

            Assert.AreEqual(expectedStatus, status, label);
            Assert.AreEqual(expectedCost, cost, label);
            if (status != PathStatus.Found)
                return;

            PathTestMaps.AssertValidPath(flat, path, map.Start, map.Goal, allowDiagonal, expectedCost, label);
            if (!allowDiagonal)
                Assert.AreEqual(expectedLength, path.Length, label);
        }

        [Test]
        public void RandomMaps_StatusAndCostMatchAStarOnTheUniformMap([Values(false, true)] bool allowDiagonal)
        {
            var rng = new Xoshiro128StarStar(allowDiagonal ? 701UL : 700UL);
            for (int i = 0; i < 2000; i++)
                AssertMatchesAStar(PathTestMaps.Random(rng, 32, 45, 9), allowDiagonal);
        }

        [Test]
        public void HandBuiltMaps_MatchAStar([Values(false, true)] bool allowDiagonal)
        {
            var single = PathTestMaps.Parse("S");
            single.Goal = 0;
            AssertMatchesAStar(single, allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse("SG"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse("S.", ".G"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse("S#", "#G"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse("S#", ".G"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse(
                "S#.....",
                ".#.###.",
                ".#.#G#.",
                ".#.#.#.",
                ".#...#.",
                ".#####.",
                "......."), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse(
                "S..#....",
                ".#.#.##.",
                ".#...#..",
                ".####.#.",
                "......#G"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse(
                "S.......",
                "######.#",
                "........",
                "#.######",
                ".......G"), allowDiagonal);
            AssertMatchesAStar(PathTestMaps.Parse(
                "..........",
                ".S........",
                "..........",
                "........G.",
                ".........."), allowDiagonal);

            var same = PathTestMaps.Parse("...");
            same.Start = same.Goal = 1;
            AssertMatchesAStar(same, allowDiagonal);

            var blockedGoal = PathTestMaps.Parse("S.#");
            blockedGoal.Goal = 2;
            AssertMatchesAStar(blockedGoal, allowDiagonal);
        }

        [Test]
        public void WeightsAreIgnored_AnyNonZeroCellCostsOne()
        {
            var map = PathTestMaps.Parse("S9G", "...");

            Assert.AreEqual(PathStatus.Found, Find(map, false, out int[] path, out int cost));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, path);
            Assert.AreEqual(2 * GridPathing.OrthogonalStep, cost);
        }

        [Test]
        public void LongOpenRun_ReturnsEveryCell_NotJustJumpPoints()
        {
            var map = PathTestMaps.Parse("S........G");

            Assert.AreEqual(PathStatus.Found, Find(map, true, out int[] path, out _));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, path);
        }

        [Test]
        public void PathBuffer_TooSmallOrEmpty_ReportsTheLengthNeeded_AndIsLeftUntouched()
        {
            var map = PathTestMaps.Parse("S...G");
            var scratch = new int[GridJumpPointSearch.ScratchLength(5)];
            var small = new[] { -7, -7, -7, -7 };

            var status = GridJumpPointSearch.FindPath(map.Costs, 5, 1, 0, 4, true, scratch, small, out int length, out int cost);
            var emptyStatus = GridJumpPointSearch.FindPath(map.Costs, 5, 1, 0, 4, true, scratch, Span<int>.Empty, out int emptyLength, out _);

            Assert.AreEqual(PathStatus.PathBufferTooSmall, status);
            Assert.AreEqual((5, 40), (length, cost));
            CollectionAssert.AreEqual(new[] { -7, -7, -7, -7 }, small);
            Assert.AreEqual(PathStatus.PathBufferTooSmall, emptyStatus);
            Assert.AreEqual(5, emptyLength);
        }

        [Test]
        public void Scratch_SharedWithAStarAndReusedAcrossMaps_AnswersLikeAFreshOne()
        {
            var rng = new Xoshiro128StarStar(78UL);
            var shared = new int[GridJumpPointSearch.ScratchLength(24 * 24)];
            for (int i = 0; i < 400; i++)
            {
                var map = PathTestMaps.Random(rng, 24, 40, 1);
                var sharedPath = new int[map.CellCount];
                var freshPath = new int[map.CellCount];
                if (i % 3 == 0)
                    GridAStar.FindPath(map.Costs, map.Width, map.Height, map.Goal, map.Start, true, shared, Span<int>.Empty, out _, out _);

                var sharedStatus = GridJumpPointSearch.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, shared, sharedPath, out int sharedLength, out int sharedCost);
                var freshStatus = GridJumpPointSearch.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, true, new int[GridJumpPointSearch.ScratchLength(map.CellCount)], freshPath, out int freshLength, out int freshCost);

                Assert.AreEqual(freshStatus, sharedStatus, $"query {i}");
                Assert.AreEqual((freshLength, freshCost), (sharedLength, sharedCost), $"query {i}");
                CollectionAssert.AreEqual(freshPath, sharedPath, $"query {i}");
            }
        }

        [Test]
        public void FindPath_DoesNotAllocate([Values(false, true)] bool allowDiagonal)
        {
            var map = PathTestMaps.Random(new Xoshiro128StarStar(9UL), 24, 20, 1);
            var scratch = new int[GridJumpPointSearch.ScratchLength(map.CellCount)];
            var path = new int[map.CellCount];
            GridJumpPointSearch.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, allowDiagonal, scratch, path, out _, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                GridJumpPointSearch.FindPath(map.Costs, map.Width, map.Height, map.Start, map.Goal, allowDiagonal, scratch, path, out _, out _);
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
                allowDiagonal ? new[] { 0, 7, 14, 15, 16, 17 } : new[] { 0, 6, 12, 18, 24, 30, 31, 32, 33, 34, 35 },
                path);
        }

        [Test]
        public void RejectsBadArguments()
        {
            var costs = new byte[6];
            var scratch = new int[GridJumpPointSearch.ScratchLength(6)];
            var path = new int[6];

            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.FindPath(costs, 0, 6, 0, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.FindPath(costs, 4, 2, 0, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.FindPath(costs, 3, 2, -1, 1, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.FindPath(costs, 3, 2, 0, 6, false, scratch, path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.FindPath(costs, 3, 2, 0, 1, false, new int[scratch.Length - 1], path, out _, out _));
            Assert.Throws<ArgumentException>(() => GridJumpPointSearch.ScratchLength(-1));
        }
    }
}
