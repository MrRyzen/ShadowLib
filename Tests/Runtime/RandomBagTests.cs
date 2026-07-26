using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class RandomBagTests
    {
        private static readonly string[] Pieces = { "I", "O", "T", "S", "Z", "L", "J" };

        [Test]
        public void Sample_DrawsEachItemExactlyOncePerCycle()
        {
            var bag = new RandomBag<string>(Pieces, new Xoshiro128StarStar(123UL));

            for (int cycle = 0; cycle < 3; cycle++)
            {
                var drawn = new HashSet<string>();
                for (int i = 0; i < Pieces.Length; i++)
                    Assert.IsTrue(drawn.Add(bag.Sample()), $"Duplicate within cycle {cycle}");
                CollectionAssert.AreEquivalent(Pieces, drawn);
            }
        }

        [Test]
        public void Sample_AutoRefills_WhenCycleExhausted()
        {
            var bag = new RandomBag<string>(Pieces, new Xoshiro128StarStar(1UL));

            for (int i = 0; i < Pieces.Length * 4; i++)
                Assert.DoesNotThrow(() => bag.Sample());
            Assert.IsTrue(bag.HasNext);
        }

        [Test]
        public void Sample_Throws_WhenAutoRefillDisabledAndEmpty()
        {
            var bag = new RandomBag<string>(Pieces, new Xoshiro128StarStar(1UL), autoRefill: false);

            for (int i = 0; i < Pieces.Length; i++)
                bag.Sample();

            Assert.IsFalse(bag.HasNext);
            Assert.Throws<InvalidOperationException>(() => bag.Sample());
        }

        [Test]
        public void Refill_RestartsDrainedBag_WhenAutoRefillDisabled()
        {
            var bag = new RandomBag<string>(Pieces, new Xoshiro128StarStar(7UL), autoRefill: false);

            for (int i = 0; i < Pieces.Length; i++)
                bag.Sample();

            bag.Refill();
            Assert.AreEqual(Pieces.Length, bag.Count);
            CollectionAssert.Contains(Pieces, bag.Sample());
        }

        [Test]
        public void Add_ItemJoinsCurrentCycleAndFutureRefills()
        {
            var bag = new RandomBag<string>(new[] { "a", "b" }, new Xoshiro128StarStar(5UL));
            bag.Add("c");

            var firstCycle = new HashSet<string> { bag.Sample(), bag.Sample(), bag.Sample() };
            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, firstCycle);

            var secondCycle = new HashSet<string> { bag.Sample(), bag.Sample(), bag.Sample() };
            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, secondCycle);
        }

        [Test]
        public void Sample_IsDeterministic_ForSameSeed()
        {
            var a = new RandomBag<string>(Pieces, new Xoshiro128StarStar(99UL));
            var b = new RandomBag<string>(Pieces, new Xoshiro128StarStar(99UL));

            for (int i = 0; i < Pieces.Length * 3; i++)
                Assert.AreEqual(a.Sample(), b.Sample());
        }

        [Test]
        public void Peek_RefillsAndDoesNotRemove()
        {
            var bag = new RandomBag<string>(Pieces, new Xoshiro128StarStar(3UL));

            for (int i = 0; i < Pieces.Length; i++)
                bag.Sample();

            string peeked = bag.Peek();               // triggers refill
            Assert.AreEqual(Pieces.Length, bag.Count);
            Assert.AreEqual(peeked, bag.Sample());
        }

        [Test]
        public void EmptyBag_HasNextFalse_EvenWithAutoRefill()
        {
            var bag = new RandomBag<string>(Array.Empty<string>(), new Xoshiro128StarStar(1UL));
            Assert.IsFalse(bag.HasNext);
            Assert.Throws<InvalidOperationException>(() => bag.Sample());
        }
    }
}
