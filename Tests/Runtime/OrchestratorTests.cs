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

        [Test]
        public void SaveLoad_RestoresHeldStreamsInPlace()
        {
            var orchestrator = new Orchestrator(Root);
            var loot = orchestrator.CreateRNG("loot");
            Take(loot, 5);

            byte[] save = orchestrator.Save();
            var expected = Take(loot);
            Take(loot, 100);
            orchestrator.Load(save);

            Assert.AreSame(loot, orchestrator.CreateRNG("loot"));
            CollectionAssert.AreEqual(expected, Take(loot));
        }

        [Test]
        public void Load_IntoOrchestratorWithDifferentRoot_FollowsTheSave()
        {
            var a = new Orchestrator(Root);
            Take(a.CreateRNG("loot"), 3);
            a.Next("chest");
            a.Next("chest");
            byte[] save = a.Save();

            var b = new Orchestrator(999UL);
            var held = b.CreateRNG("loot");
            var stale = b.CreateRNG("not-in-save");
            Take(stale, 4);
            b.Next("chest");
            b.Next("dropped-by-load");
            b.Load(save);

            Assert.AreEqual(Root, b.RootSeed);
            CollectionAssert.AreEqual(Take(a.CreateRNG("loot")), Take(held));
            CollectionAssert.AreEqual(Take(a.Next("chest")), Take(b.Next("chest")));
            CollectionAssert.AreEqual(Take(new Orchestrator(Root).Next("dropped-by-load")), Take(b.Next("dropped-by-load")));
            CollectionAssert.AreEqual(Take(a.Derive("chunk/1/1")), Take(b.Derive("chunk/1/1")));
            CollectionAssert.AreEqual(Take(new Orchestrator(Root).CreateRNG("not-in-save")), Take(stale));
        }

        [Test]
        public void Load_CreatesStreamsThatWereNotYetRequested()
        {
            var a = new Orchestrator(Root);
            Take(a.CreateRNG("loot"), 7);

            var b = new Orchestrator(Root);
            b.Load(a.Save());

            CollectionAssert.AreEqual(Take(a.CreateRNG("loot")), Take(b.CreateRNG("loot")));
        }

        [Test]
        public void Save_IsIndependentOfCreationOrder()
        {
            var a = new Orchestrator(Root);
            a.CreateRNG("a"); a.CreateRNG("b"); a.Next("x"); a.Next("y");

            var b = new Orchestrator(Root);
            b.Next("y"); b.CreateRNG("b"); b.Next("x"); b.CreateRNG("a");

            CollectionAssert.AreEqual(a.Save(), b.Save());
        }

        [Test]
        public void SaveLoad_RoundTripsNonAsciiAndEmptyContexts()
        {
            var a = new Orchestrator(Root);
            Take(a.CreateRNG("战利品/ñ"), 2);
            Take(a.CreateRNG(""), 2);
            a.Next("宝箱");

            var b = new Orchestrator(1UL);
            b.Load(a.Save());

            CollectionAssert.AreEqual(Take(a.CreateRNG("战利品/ñ")), Take(b.CreateRNG("战利品/ñ")));
            CollectionAssert.AreEqual(Take(a.CreateRNG("")), Take(b.CreateRNG("")));
            CollectionAssert.AreEqual(Take(a.Next("宝箱")), Take(b.Next("宝箱")));
        }

        [Test]
        public void Load_RejectsBadData_AndLeavesStateUntouched()
        {
            var orchestrator = new Orchestrator(Root);
            Take(orchestrator.CreateRNG("loot"), 3);
            orchestrator.Next("chest");
            byte[] good = orchestrator.Save();

            byte[] wrongVersion = (byte[])good.Clone();
            wrongVersion[0] = 99;
            byte[] truncated = good.AsSpan(0, good.Length - 3).ToArray();
            byte[] trailing = new byte[good.Length + 1];
            good.CopyTo(trailing, 0);
            byte[] shortState = (byte[])good.Clone();
            shortState[18] = 15; // state length of "loot", after version, root, count and the name
            byte[] negativeCount = (byte[])good.Clone();
            negativeCount[12] = 0xFF; // top byte of the stream count

            foreach (var bad in new[] { Array.Empty<byte>(), wrongVersion, truncated, trailing, shortState, negativeCount, new byte[] { 1, 2, 3 } })
                Assert.Throws<System.IO.InvalidDataException>(() => orchestrator.Load(bad));

            CollectionAssert.AreEqual(good, orchestrator.Save());
        }
    }
}
