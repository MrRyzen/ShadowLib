using System;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class PolycubeGenerateTests
    {
        private static readonly float[] Compactness = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        private static readonly int[] CellCounts = { 1, 2, 7, 30 };

        [Test]
        public void Generate_HasExactCellCount_Distinct_Connected_WithinLimits()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                foreach (float compactness in Compactness)
                {
                    foreach (int cellCount in CellCounts)
                    {
                        var shape = Polycube.Generate(new Xoshiro128StarStar(seed), cellCount, compactness, 4, 4, 2);
                        string label = $"seed {seed}, compactness {compactness}, cells {cellCount}";

                        ShapeChecks.AssertDistinctAndConnected(shape.Offsets, cellCount, label);
                        Assert.LessOrEqual(shape.Width, 4, label);
                        Assert.LessOrEqual(shape.Height, 4, label);
                        Assert.LessOrEqual(shape.Depth, 2, label);
                    }
                }
            }
        }

        [Test]
        public void Generate_DrawsOncePerAddedCell()
        {
            var used = new Xoshiro128StarStar(77UL);
            Polycube.Generate(used, 9, 0.5f);

            var reference = new Xoshiro128StarStar(77UL);
            for (int i = 0; i < 8; i++)
                reference.NextFloat();

            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_SingleCell_DrawsNothing()
        {
            var used = new Xoshiro128StarStar(77UL);
            Polycube.Generate(used, 1, 0.5f);

            Assert.AreEqual(new Xoshiro128StarStar(77UL).NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_CellCountEqualToLimitVolume_FillsTheBox([Values(0f, 1f)] float compactness)
        {
            var shape = Polycube.Generate(new Xoshiro128StarStar(5UL), 24, compactness, 4, 3, 2);

            ShapeChecks.AssertDistinctAndConnected(shape.Offsets, 24, "full box");
            Assert.AreEqual((4, 3, 2), (shape.Width, shape.Height, shape.Depth));
        }

        [Test]
        public void Generate_UnitLimits_SingleCell()
        {
            var shape = Polycube.Generate(new Xoshiro128StarStar(5UL), 1, 0.5f, 1, 1, 1);

            Assert.AreEqual((1, 1, 1, 1), (shape.Width, shape.Height, shape.Depth, shape.CellCount));
        }

        [Test]
        public void Generate_DefaultLimits_DoNotOverflow()
        {
            var shape = Polycube.Generate(new Xoshiro128StarStar(5UL), 50, 0f);

            ShapeChecks.AssertDistinctAndConnected(shape.Offsets, 50, "unbounded");
        }

        [Test]
        public void Generate_HighestDraw_PicksAnAllowedCell()
        {
            var shape = Polycube.Generate(new ShapeChecks.ConstantRandom(0.99999994f), 12, 0.3f, 3, 2, 2);

            ShapeChecks.AssertDistinctAndConnected(shape.Offsets, 12, "highest draw");
            Assert.AreEqual((3, 2, 2), (shape.Width, shape.Height, shape.Depth));
        }

        [Test]
        public void Generate_CompactnessOne_IsTighterThanZero()
        {
            long blocky = 0;
            long stringy = 0;
            for (ulong seed = 1; seed <= 50; seed++)
            {
                blocky += BoxVolume(Polycube.Generate(new Xoshiro128StarStar(seed), 12, 1f));
                stringy += BoxVolume(Polycube.Generate(new Xoshiro128StarStar(seed), 12, 0f));
            }

            Assert.Less(blocky, stringy);
        }

        [Test]
        public void Generate_RejectsBadArguments()
        {
            var rng = new Xoshiro128StarStar(1UL);

            Assert.Throws<ArgumentNullException>(() => Polycube.Generate(null!, 4, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 0, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, -1, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, -0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, 1.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, 0.5f, maxWidth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, 0.5f, maxHeight: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 4, 0.5f, maxDepth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.Generate(rng, 25, 0.5f, 4, 3, 2));
        }

        [Test]
        public void Generate_Seed123_ShapeIsPinned()
        {
            var expected = new[]
            {
                (1, 1, 0), (1, 2, 0), (0, 2, 0), (1, 0, 0), (1, 2, 1), (1, 3, 1), (0, 3, 0), (1, 1, 1),
            };

            CollectionAssert.AreEqual(expected, Polycube.Generate(new Xoshiro128StarStar(123UL), 8, 0.5f).Offsets.ToArray());
        }

        private static int BoxVolume(Polycube shape) => shape.Width * shape.Height * shape.Depth;
    }
}
