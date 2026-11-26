using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class PolyominoGenerateTests
    {
        private static readonly float[] Compactness = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        private static readonly int[] CellCounts = { 1, 2, 7, 12 };

        [Test]
        public void Generate_HasExactCellCount_Distinct_Connected_WithinLimits()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                foreach (float compactness in Compactness)
                {
                    foreach (int cellCount in CellCounts)
                    {
                        var shape = Polyomino.Generate(new Xoshiro128StarStar(seed), cellCount, compactness, 4, 3);
                        string label = $"seed {seed}, compactness {compactness}, cells {cellCount}";

                        ShapeChecks.AssertDistinctAndConnected(Polycube.FromPolyomino(shape).Offsets, cellCount, label);
                        Assert.LessOrEqual(shape.Width, 4, label);
                        Assert.LessOrEqual(shape.Height, 3, label);
                    }
                }
            }
        }

        [Test]
        public void Generate_DrawsOncePerAddedCell()
        {
            var used = new Xoshiro128StarStar(77UL);
            Polyomino.Generate(used, 9, 0.5f);

            var reference = new Xoshiro128StarStar(77UL);
            for (int i = 0; i < 8; i++)
                reference.NextFloat();

            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_CellCountEqualToLimitArea_FillsTheRect([Values(0f, 1f)] float compactness)
        {
            var shape = Polyomino.Generate(new Xoshiro128StarStar(5UL), 12, compactness, 4, 3);

            Assert.AreEqual((4, 3, 12), (shape.Width, shape.Height, shape.CellCount));
        }

        [Test]
        public void Generate_CompactnessOne_IsTighterThanZero()
        {
            long blocky = 0;
            long stringy = 0;
            for (ulong seed = 1; seed <= 50; seed++)
            {
                blocky += BoxArea(Polyomino.Generate(new Xoshiro128StarStar(seed), 12, 1f));
                stringy += BoxArea(Polyomino.Generate(new Xoshiro128StarStar(seed), 12, 0f));
            }

            Assert.Less(blocky, stringy);
        }

        [Test]
        public void Generate_RejectsBadArguments()
        {
            var rng = new Xoshiro128StarStar(1UL);

            Assert.Throws<ArgumentNullException>(() => Polyomino.Generate(null!, 4, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polyomino.Generate(rng, 0, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polyomino.Generate(rng, 4, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polyomino.Generate(rng, 4, 0.5f, maxWidth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polyomino.Generate(rng, 4, 0.5f, maxHeight: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polyomino.Generate(rng, 13, 0.5f, 4, 3));
        }

        [Test]
        public void Generate_Seed123_ShapeIsPinned()
        {
            var expected = new[] { (2, 2), (1, 2), (0, 2), (2, 3), (1, 1), (1, 0), (0, 3), (2, 1) };

            CollectionAssert.AreEqual(expected, Polyomino.Generate(new Xoshiro128StarStar(123UL), 8, 0.5f).Offsets.ToArray());
        }

        private static int BoxArea(Polyomino shape) => shape.Width * shape.Height;
    }
}
