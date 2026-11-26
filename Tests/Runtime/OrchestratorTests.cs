using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ShadowLib.RNG;
using ShadowLib.RNG.Sources;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class OrchestratorTests
    {
        private const ulong Root = 0xC0FFEEUL;

        private static uint[] Take(IRandom rng, int count = 8)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++) values[i] = rng.NextUInt();
            return values;
        }

        [Test]
        public void CreateRNG_SameContext_ReturnsSameInstance()
        {
            var orchestrator = new Orchestrator(Root);
            Assert.AreSame(orchestrator.CreateRNG("loot"), orchestrator.CreateRNG("loot"));
        }

        [Test]
        public void CreateRNG_DifferentContexts_ProduceDifferentStreams()
        {
            var orchestrator = new Orchestrator(Root);
            CollectionAssert.AreNotEqual(Take(orchestrator.CreateRNG("loot")), Take(orchestrator.CreateRNG("ai")));
        }

        [Test]
        public void CreateRNG_SeedIsUnchangedFromLegacyDerivation()
        {
            var orchestrator = new Orchestrator(Root);
            Assert.AreEqual(Root, orchestrator.RootSeed);
            Assert.AreEqual(Seeding.DeriveSeed64(Root, "loot"), orchestrator.GetSeedForContext("loot"));
            CollectionAssert.AreEqual(
                Take(new Xoshiro128StarStar(Seeding.DeriveSeed64(Root, "loot"))),
                Take(orchestrator.CreateRNG("loot")));
        }

        [Test]
        public void TwoOrchestrators_SameRoot_AreIndependentAndIdentical()
        {
            var a = new Orchestrator(Root);
            var b = new Orchestrator(Root);
            Assert.AreNotSame(a.CreateRNG("x"), b.CreateRNG("x"));
            CollectionAssert.AreEqual(Take(a.CreateRNG("x")), Take(b.CreateRNG("x")));
        }

        [Test]
        public void Derive_ReturnsFreshIdenticalStreamEachCall()
        {
            var orchestrator = new Orchestrator(Root);
            var first = orchestrator.Derive("chunk/3/4");
            var second = orchestrator.Derive("chunk/3/4");

            Assert.AreNotSame(first, second);
            CollectionAssert.AreEqual(Take(first), Take(second));
            CollectionAssert.AreEqual(
                Take(new Xoshiro128StarStar(orchestrator.DeriveSeed("chunk/3/4"))),
                Take(orchestrator.Derive("chunk/3/4")));
            CollectionAssert.AreNotEqual(Take(orchestrator.Derive("chunk/3/4")), Take(orchestrator.Derive("chunk/4/3")));
        }

        [Test]
        public void Next_AdvancesSeries_AndReplaysFromSameRoot()
        {
            var a = new Orchestrator(Root);
            var b = new Orchestrator(Root);

            var a0 = Take(a.Next("chest"));
            var a1 = Take(a.Next("chest"));

            CollectionAssert.AreNotEqual(a0, a1);
            CollectionAssert.AreEqual(a0, Take(b.Next("chest")));
            CollectionAssert.AreEqual(a1, Take(new Xoshiro128StarStar(b.NextSeed("chest"))));
        }

        [Test]
        public void Next_ContextsHaveSeparateCounters()
        {
            var a = new Orchestrator(Root);
            var b = new Orchestrator(Root);
            a.Next("other");
            a.Next("other");
            CollectionAssert.AreEqual(Take(b.Next("chest")), Take(a.Next("chest")));
        }

        [Test]
        public void StreamKinds_WithSameContext_DoNotCollide()
        {
            var orchestrator = new Orchestrator(Root);
            var seeds = new[]
            {
                orchestrator.GetSeedForContext("loot"),
                orchestrator.DeriveSeed("loot"),
                orchestrator.NextSeed("loot"),
                orchestrator.Fork("loot").RootSeed,
            };
            CollectionAssert.AllItemsAreUnique(seeds);
        }

        [Test]
        public void Fork_IsDeterministicAndIndependentOfParent()
        {
            var orchestrator = new Orchestrator(Root);
            Assert.AreEqual(orchestrator.Fork("world/2").RootSeed, new Orchestrator(Root).Fork("world/2").RootSeed);
            Assert.AreNotEqual(orchestrator.RootSeed, orchestrator.Fork("world/2").RootSeed);
            Assert.AreNotEqual(orchestrator.Fork("world/1").RootSeed, orchestrator.Fork("world/2").RootSeed);
        }

        [Test]
        public void NullContext_Throws()
        {
            var orchestrator = new Orchestrator(Root);
            Assert.Throws<ArgumentNullException>(() => orchestrator.CreateRNG(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.GetSeedForContext(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.Derive(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.DeriveSeed(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.Next(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.NextSeed(null!));
            Assert.Throws<ArgumentNullException>(() => orchestrator.Fork(null!));
        }

        [Test]
        public void EmptyContext_IsAllowed()
        {
            var orchestrator = new Orchestrator(Root);
            Assert.DoesNotThrow(() =>
            {
                orchestrator.CreateRNG("");
                orchestrator.Derive("");
                orchestrator.Next("");
            });
        }

        [Test]
        public void ConcurrentCreateAndNext_DoNotCorruptState()
        {
            var orchestrator = new Orchestrator(Root);
            var seeds = new ConcurrentBag<ulong>();

            Parallel.For(0, 2000, i =>
            {
                orchestrator.CreateRNG("s" + (i % 50));
                seeds.Add(orchestrator.NextSeed("n"));
            });

            Assert.AreEqual(2000, new HashSet<ulong>(seeds).Count);

            var expected = new Orchestrator(Root);
            for (int i = 0; i < 2000; i++) expected.NextSeed("n");
            Assert.AreEqual(expected.NextSeed("n"), orchestrator.NextSeed("n"));
        }
    }
}
