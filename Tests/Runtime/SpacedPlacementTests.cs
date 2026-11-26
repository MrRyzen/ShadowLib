using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class SpacedPlacementTests
    {
        private static readonly Func<(int X, int Y), (int X, int Y), float> Manhattan =
            (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static List<(int X, int Y)> GridPoints(int width, int height)
        {
            var points = new List<(int X, int Y)>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    points.Add((x, y));
            return points;
        }

        [Test]
        public void Place_ResultsAreDistinctCandidates_AtLeastMinDistanceApart()
        {
            var candidates = GridPoints(8, 6);
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var placed = SpacedPlacement.Place(new Xoshiro128StarStar(seed), candidates, 5, 3f, Manhattan);

                Assert.That(placed.Length, Is.InRange(1, 5), $"seed {seed}");
                for (int i = 0; i < placed.Length; i++)
                {
                    CollectionAssert.Contains(candidates, placed[i]);
                    for (int j = 0; j < i; j++)
                        Assert.GreaterOrEqual(Manhattan(placed[i], placed[j]), 3f, $"seed {seed}: {placed[i]} vs {placed[j]}");
                }
            }
        }

        [Test]
        public void Place_ZeroMinDistance_ReturnsExactlyCountDifferentCandidates()
        {
            var placed = SpacedPlacement.Place(new Xoshiro128StarStar(3UL), GridPoints(4, 4), 16, 0f, Manhattan);

            Assert.AreEqual(16, placed.Length);
            CollectionAssert.AllItemsAreUnique(placed);
        }

        [Test]
        public void Place_DistanceExactlyAtMinimum_IsAccepted()
        {
            var placed = SpacedPlacement.Place(new Xoshiro128StarStar(3UL), new[] { (0, 0), (2, 0) }, 2, 2f, Manhattan);

            Assert.AreEqual(2, placed.Length);
        }

        [Test]
        public void Place_WhenSpacingCannotBeMet_ReturnsFewer()
        {
            var placed = SpacedPlacement.Place(new Xoshiro128StarStar(3UL), GridPoints(10, 1), 4, 100f, Manhattan);

            Assert.AreEqual(1, placed.Length);
        }

        [Test]
        public void Place_CountAboveCandidates_ReturnsWhatFits()
        {
            var placed = SpacedPlacement.Place(new Xoshiro128StarStar(3UL), GridPoints(3, 1), 10, 1f, Manhattan);

            Assert.AreEqual(3, placed.Length);
        }

        [Test]
        public void Place_DrawsOneShuffleOfTheCandidates()
        {
            var used = new Xoshiro128StarStar(11UL);
            SpacedPlacement.Place(used, GridPoints(5, 5), 3, 2f, Manhattan);

            var reference = new Xoshiro128StarStar(11UL);
            RandomUtils.Shuffle(new int[25], reference);

            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Place_NothingToPlace_DrawsNothing()
        {
            var used = new Xoshiro128StarStar(11UL);

            Assert.IsEmpty(SpacedPlacement.Place(used, GridPoints(5, 5), 0, 2f, Manhattan));
            Assert.IsEmpty(SpacedPlacement.Place(used, new List<(int X, int Y)>(), 3, 2f, Manhattan));
            Assert.AreEqual(new Xoshiro128StarStar(11UL).NextUInt(), used.NextUInt());
        }

        [Test]
        public void Place_Seed123_ResultIsPinned()
        {
            var placed = SpacedPlacement.Place(new Xoshiro128StarStar(123UL), GridPoints(8, 6), 4, 4f, Manhattan);

            CollectionAssert.AreEqual(new[] { (1, 5), (3, 0), (7, 3), (5, 5) }, placed);
        }

        [Test]
        public void Place_RejectsBadArguments()
        {
            var rng = new Xoshiro128StarStar(1UL);
            var candidates = GridPoints(3, 3);

            Assert.Throws<ArgumentNullException>(() => SpacedPlacement.Place(null!, candidates, 2, 1f, Manhattan));
            Assert.Throws<ArgumentNullException>(() => SpacedPlacement.Place<(int X, int Y)>(rng, null!, 2, 1f, Manhattan));
            Assert.Throws<ArgumentNullException>(() => SpacedPlacement.Place(rng, candidates, 2, 1f, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpacedPlacement.Place(rng, candidates, -1, 1f, Manhattan));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpacedPlacement.Place(rng, candidates, 2, -1f, Manhattan));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpacedPlacement.Place(rng, candidates, 2, float.NaN, Manhattan));
        }
    }
}
