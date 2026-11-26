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
    }
}
