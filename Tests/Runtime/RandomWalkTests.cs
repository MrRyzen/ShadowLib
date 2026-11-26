using System;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class RandomWalkTests
    {
        [Test]
        public void Generate_StartsAtStart_StaysInBounds_TakesUnitSteps()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var path = RandomWalk.Generate(new Xoshiro128StarStar(seed), 200, 5, 4, (2, 1), DirectionBias.Uniform);

                Assert.AreEqual(200, path.Length);
                Assert.AreEqual((2, 1), path[0]);
                for (int i = 0; i < path.Length; i++)
                {
                    Assert.That(path[i].X, Is.InRange(0, 4), $"seed {seed}, step {i}");
                    Assert.That(path[i].Y, Is.InRange(0, 3), $"seed {seed}, step {i}");
                    if (i > 0)
                        Assert.AreEqual(1, Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y), $"seed {seed}, step {i}");
                }
            }
        }

        [Test]
        public void Generate_DrawsOncePerStepTaken()
        {
            var used = new Xoshiro128StarStar(9UL);
            RandomWalk.Generate(used, 11, 6, 6, (3, 3), DirectionBias.Uniform);

            var reference = new Xoshiro128StarStar(9UL);
            for (int i = 0; i < 10; i++)
                reference.NextFloat();

            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_RightOnly_WalksStraightAndStopsAtTheWall()
        {
            var used = new Xoshiro128StarStar(9UL);
            var path = RandomWalk.Generate(used, 10, 5, 3, (1, 2), new DirectionBias(1f, 0f, 0f, 0f));

            CollectionAssert.AreEqual(new[] { (1, 2), (2, 2), (3, 2), (4, 2) }, path);

            var reference = new Xoshiro128StarStar(9UL);
            for (int i = 0; i < 3; i++)
                reference.NextFloat();
            Assert.AreEqual(reference.NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_ZeroWeightDirection_IsNeverTaken()
        {
            var path = RandomWalk.Generate(new Xoshiro128StarStar(4UL), 300, 400, 7, (0, 3), new DirectionBias(0.6f, 0f, 0.2f, 0.2f));

            Assert.AreEqual(300, path.Length);
            for (int i = 1; i < path.Length; i++)
                Assert.GreaterOrEqual(path[i].X, path[i - 1].X);
        }

        [Test]
        public void Generate_UpIsPositiveY_DownIsNegativeY()
        {
            var up = RandomWalk.Generate(new Xoshiro128StarStar(1UL), 2, 3, 3, (1, 1), new DirectionBias(0f, 0f, 1f, 0f));
            var down = RandomWalk.Generate(new Xoshiro128StarStar(1UL), 2, 3, 3, (1, 1), new DirectionBias(0f, 0f, 0f, 1f));
            var left = RandomWalk.Generate(new Xoshiro128StarStar(1UL), 2, 3, 3, (1, 1), new DirectionBias(0f, 1f, 0f, 0f));

            Assert.AreEqual((1, 2), up[1]);
            Assert.AreEqual((1, 0), down[1]);
            Assert.AreEqual((0, 1), left[1]);
        }

        [Test]
        public void Generate_HighestDraw_PicksAnAllowedDirection()
        {
            var path = RandomWalk.Generate(new ShapeChecks.ConstantRandom(0.99999994f), 2, 3, 3, (1, 0), DirectionBias.Uniform);

            Assert.AreEqual((1, 1), path[1]);
        }

        [Test]
        public void Generate_LowestDraw_SkipsABlockedDirection()
        {
            var path = RandomWalk.Generate(new ShapeChecks.ConstantRandom(0f), 2, 3, 3, (2, 1), DirectionBias.Uniform);

            Assert.AreEqual((1, 1), path[1]);
        }

        [Test]
        public void Generate_BadArguments_ThrowBeforeThePathIsAllocated()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                RandomWalk.Generate(null!, 4_000_000, 4, 4, (0, 0), DirectionBias.Uniform);
            }
            catch (ArgumentNullException)
            {
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Less(allocated, 1_000_000L);
        }

        [Test]
        public void Generate_SingleCellGrid_ReturnsOnlyTheStart_WithoutDrawing()
        {
            var used = new Xoshiro128StarStar(9UL);
            var path = RandomWalk.Generate(used, 5, 1, 1, (0, 0), DirectionBias.Uniform);

            CollectionAssert.AreEqual(new[] { (0, 0) }, path);
            Assert.AreEqual(new Xoshiro128StarStar(9UL).NextUInt(), used.NextUInt());
        }

        [Test]
        public void Generate_SpanForm_MatchesArrayForm_AndEmptySpanWritesNothing()
        {
            var expected = RandomWalk.Generate(new Xoshiro128StarStar(21UL), 40, 8, 6, (0, 0), DirectionBias.Uniform);
            var buffer = new (int X, int Y)[40];

            int written = RandomWalk.Generate(new Xoshiro128StarStar(21UL), buffer, 8, 6, (0, 0), DirectionBias.Uniform);

            Assert.AreEqual(40, written);
            CollectionAssert.AreEqual(expected, buffer);
            Assert.AreEqual(0, RandomWalk.Generate(new Xoshiro128StarStar(21UL), Span<(int X, int Y)>.Empty, 8, 6, (0, 0), DirectionBias.Uniform));
        }

        [Test]
        public void Generate_Seed123_PathIsPinned()
        {
            var expected = new[]
            {
                (2, 2), (3, 2), (3, 3), (4, 3), (4, 2), (4, 1), (4, 2), (3, 2),
                (3, 3), (2, 3), (2, 2), (3, 2), (2, 2), (2, 1), (3, 1), (4, 1),
            };

            var path = RandomWalk.Generate(new Xoshiro128StarStar(123UL), 16, 5, 4, (2, 2), new DirectionBias(0.4f, 0.1f, 0.3f, 0.2f));

            CollectionAssert.AreEqual(expected, path);
        }

        [Test]
        public void Generate_RejectsBadArguments()
        {
            var rng = new Xoshiro128StarStar(1UL);
            var uniform = DirectionBias.Uniform;

            Assert.Throws<ArgumentNullException>(() => RandomWalk.Generate(null!, 5, 4, 4, (0, 0), uniform));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomWalk.Generate(rng, 0, 4, 4, (0, 0), uniform));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomWalk.Generate(rng, 5, 0, 4, (0, 0), uniform));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomWalk.Generate(rng, 5, 4, 0, (0, 0), uniform));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomWalk.Generate(rng, 5, 4, 4, (4, 0), uniform));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomWalk.Generate(rng, 5, 4, 4, (0, -1), uniform));
            Assert.Throws<ArgumentException>(() => RandomWalk.Generate(rng, 5, 4, 4, (0, 0), default));
        }

        [Test]
        public void DirectionBias_RejectsBadWeights()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionBias(-1f, 1f, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionBias(1f, float.NaN, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionBias(1f, 1f, float.PositiveInfinity, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionBias(1f, 1f, 1f, -0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectionBias(0f, 0f, 0f, 0f));
        }
    }
}
