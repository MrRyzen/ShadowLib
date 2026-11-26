using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class DynamicWeightTableTests
    {
        private static readonly string[] Letters = { "A", "B", "C", "D" };

        private static DynamicWeightTable<string> PlainTable()
        {
            var table = new DynamicWeightTable<string>();
            table.Add("A", 1f);
            table.Add("B", 2f);
            table.Add("C", 3f);
            table.Add("D", 4f);
            return table;
        }

        [Test]
        public void Sample_PlainTable_SequenceIsPinned()
        {
            var table = PlainTable();
            var rng = new Xoshiro128StarStar(123UL);
            var picks = new System.Text.StringBuilder();
            for (int i = 0; i < 48; i++) picks.Append(table.Sample(rng));
            Assert.AreEqual("CDBCDCBDDDBCDBCBBBBBDDBABCCBBCCCDAABDDCDBDCDCCDD", picks.ToString());
        }

        [Test]
        public void Add_Duplicate_Throws()
        {
            var t = PlainTable();
            Assert.Throws<ArgumentException>(() => t.Add("A", 1f));
        }

        [Test]
        public void SetWeight_ChangesDesignedWeight()
        {
            var t = PlainTable();
            Assert.IsTrue(t.SetWeight("A", 10f));
            Assert.AreEqual(10f, t.GetWeight("A"));
            Assert.AreEqual(19f, t.TotalWeight);
            Assert.IsFalse(t.SetWeight("Z", 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetWeight("A", 0f));
        }

        [Test]
        public void Disabled_ItemIsNeverPicked_AndWeighsZero()
        {
            var t = PlainTable();
            t.SetEnabled("D", false);
            Assert.IsFalse(t.IsEnabled("D"));
            Assert.AreEqual(6f, t.TotalWeight);
            Assert.AreEqual(0f, t.GetProbability("D"));
            var rng = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 100_000; i++) Assert.AreNotEqual("D", t.Sample(rng));
        }

        [Test]
        public void GetProbability_SumsToOne()
        {
            var t = PlainTable();
            float sum = t.GetProbability("A") + t.GetProbability("B") + t.GetProbability("C") + t.GetProbability("D");
            Assert.AreEqual(1f, sum, 1e-6f);
            Assert.AreEqual(0.4f, t.GetProbability("D"), 1e-6f);
            Assert.AreEqual(0f, t.GetProbability("Z"));
        }

        [Test]
        public void NothingPickable_SampleThrows_TrySampleFalse_NoDraw()
        {
            var empty = new DynamicWeightTable<string>();
            var allOff = PlainTable();
            foreach (var s in Letters) allOff.SetEnabled(s, false);
            var rng = new Xoshiro128StarStar(123UL);
            var reference = new Xoshiro128StarStar(123UL);

            Assert.Throws<InvalidOperationException>(() => empty.Sample(rng));
            Assert.Throws<InvalidOperationException>(() => allOff.Sample(rng));
            Assert.IsFalse(allOff.TrySample(rng, out _));
            Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
        }

        [Test]
        public void Sample_DrawsExactlyOncePerPick()
        {
            var t = PlainTable();
            var rng = new Xoshiro128StarStar(123UL);
            var reference = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 10; i++) { t.Sample(rng); reference.NextFloat(); }
            Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
        }

        [TestCase(-1f, 5f)]
        [TestCase(-0.5f, 12.5f)]
        [TestCase(0f, 20f)]
        [TestCase(0.5f, 40f)]
        [TestCase(1f, 60f)]
        [TestCase(3f, 60f)]
        [TestCase(-3f, 5f)]
        [TestCase(float.NaN, 20f)]
        [TestCase(float.PositiveInfinity, 60f)]
        [TestCase(float.NegativeInfinity, 5f)]
        public void Influence_MapsOntoRange(float influence, float expectedWeight)
        {
            var t = new DynamicWeightTable<string>();
            t.Add("drain", 20f);
            t.SetInfluence("drain", 5f, 60f, () => influence);
            Assert.AreEqual(expectedWeight, t.TotalWeight);
            Assert.AreEqual(20f, t.GetWeight("drain"));
        }

        [Test]
        public void Influence_MinZero_SwitchesItemOff()
        {
            var t = PlainTable();
            t.SetInfluence("D", 0f, 4f, () => -1f);
            Assert.AreEqual(0f, t.GetProbability("D"));
            var rng = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 10_000; i++) Assert.AreNotEqual("D", t.Sample(rng));
        }

        [Test]
        public void Influence_IsReadAtPickTime()
        {
            float corruption = 0f;
            var t = PlainTable();
            t.SetInfluence("A", 1f, 91f, () => corruption);
            Assert.AreEqual(0.1f, t.GetProbability("A"), 1e-6f);
            corruption = 1f;
            Assert.AreEqual(0.91f, t.GetProbability("A"), 1e-6f);
            t.ClearInfluence("A");
            Assert.AreEqual(0.1f, t.GetProbability("A"), 1e-6f);
        }

        [Test]
        public void Influence_SourceCalledOncePerPick()
        {
            int calls = 0;
            var t = PlainTable();
            t.SetInfluence("A", 1f, 2f, () => { calls++; return 0f; });
            t.Sample(new Xoshiro128StarStar(123UL));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Influence_Validation()
        {
            var t = PlainTable();
            Assert.IsFalse(t.SetInfluence("Z", 0f, 1f, () => 0f));
            Assert.Throws<ArgumentNullException>(() => t.SetInfluence("B", 1f, 3f, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", -1f, 3f, () => 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", 2.5f, 3f, () => 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", 1f, 1.5f, () => 0f));
            t.SetInfluence("B", 1f, 3f, () => 0f);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetWeight("B", 4f));
            Assert.IsTrue(t.SetWeight("B", 3f));
        }

        [Test]
        public void Stock_RunsOut_ThenRefills()
        {
            var t = new DynamicWeightTable<string>();
            t.Add("relic", 1f);
            t.SetStock("relic", 2);
            var rng = new Xoshiro128StarStar(123UL);
            Assert.AreEqual("relic", t.Sample(rng));
            Assert.AreEqual("relic", t.Sample(rng));
            Assert.IsTrue(t.TryGetStock("relic", out int left));
            Assert.AreEqual(0, left);
            Assert.IsFalse(t.TrySample(rng, out _));
            Assert.IsTrue(t.Refill("relic"));
            Assert.AreEqual("relic", t.Sample(rng));
        }

        [Test]
        public void Stock_DepletedItem_LeavesTheRestPickable()
        {
            var t = PlainTable();
            t.SetStock("D", 1);
            var rng = new Xoshiro128StarStar(123UL);
            int dCount = 0;
            for (int i = 0; i < 1000; i++) if (t.Sample(rng) == "D") dCount++;
            Assert.AreEqual(1, dCount);
            Assert.AreEqual(0f, t.GetProbability("D"));
        }

        [Test]
        public void Stock_RestoreAfterLoad_AndValidation()
        {
            var t = PlainTable();
            Assert.IsTrue(t.SetStock("A", 1, 5));
            Assert.IsTrue(t.TryGetStock("A", out int units));
            Assert.AreEqual(1, units);
            t.RefillAll();
            t.TryGetStock("A", out units);
            Assert.AreEqual(5, units);
            Assert.IsFalse(t.TryGetStock("B", out _));
            Assert.IsFalse(t.Refill("B"));
            Assert.IsFalse(t.SetStock("Z", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", 6, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", -1, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", 0, 0));
            Assert.IsTrue(t.ClearStock("A"));
            Assert.IsFalse(t.TryGetStock("A", out _));
        }

        [Test]
        public void ThrowingSource_LeavesTableUntouched()
        {
            var t = PlainTable();
            foreach (var s in Letters) t.SetStock(s, 3);
            bool explode = true;
            t.SetInfluence("D", 1f, 8f, () => explode ? throw new InvalidOperationException("boom") : 0f);
            var rng = new Xoshiro128StarStar(123UL);
            var reference = new Xoshiro128StarStar(123UL);

            Assert.Throws<InvalidOperationException>(() => t.Sample(rng));
            foreach (var s in Letters)
            {
                t.TryGetStock(s, out int units);
                Assert.AreEqual(3, units);
            }
            Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
        }

        [Test]
        public void Remove_KeepsNeighbourSettingsOnTheRightItems()
        {
            var t = PlainTable();
            t.SetEnabled("A", false);
            t.SetStock("C", 2, 7);
            t.SetInfluence("D", 0f, 40f, () => 1f);
            Assert.IsTrue(t.TryRemove("B"));

            Assert.IsFalse(t.IsEnabled("A"));
            Assert.IsTrue(t.IsEnabled("C"));
            Assert.IsTrue(t.TryGetStock("C", out int units));
            Assert.AreEqual(2, units);
            Assert.IsFalse(t.TryGetStock("D", out _));
            Assert.AreEqual(43f, t.TotalWeight);
        }
    }
}
