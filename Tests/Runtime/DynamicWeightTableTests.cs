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
    }
}
