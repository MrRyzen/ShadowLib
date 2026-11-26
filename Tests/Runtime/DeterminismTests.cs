using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;
using ShadowLib.RNG.Utilities;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class DeterminismTests
    {
        [Test]
        public void Xoshiro_MatchesPublishedReferenceVector()
        {
            var rng = new Xoshiro128StarStar(1UL)
            {
                State = new Xoshiro128State { S0 = 1, S1 = 2, S2 = 3, S3 = 4 }
            };
            uint[] expected = { 11520u, 0u, 5927040u, 70819200u, 2031721883u, 1637235492u, 1287239034u, 3734860849u };

            foreach (uint value in expected)
                Assert.AreEqual(value, rng.NextUInt());
        }

        [Test]
        public void Xoshiro_SeedExpansion_MatchesSplitMix64Reference()
        {
            var state = new Xoshiro128StarStar(1234567UL).State;

            Assert.AreEqual(6457827717110365317UL, state.S0 | ((ulong)state.S1 << 32));
            Assert.AreEqual(3203168211198807973UL, state.S2 | ((ulong)state.S3 << 32));
        }

        [Test]
        public void RangeInt_MatchesPlainRejectionSampling()
        {
            var actual = new Xoshiro128StarStar(42UL);
            var reference = new Xoshiro128StarStar(42UL);

            foreach (int max in new[] { 1, 2, 3, 6, 7, 100, 1000, 65537, 1 << 30, int.MaxValue })
            {
                uint range = (uint)max;
                uint threshold = unchecked(0u - range) % range;
                for (int i = 0; i < 2000; i++)
                {
                    uint r;
                    do { r = reference.NextUInt(); } while (r < threshold);
                    Assert.AreEqual((int)(r % range), actual.Range(0, max));
                }
            }

            Assert.AreEqual(reference.NextUInt(), actual.NextUInt());
        }

        [Test]
        public void RangeULong_MatchesPlainRejectionSampling()
        {
            var actual = new Xoshiro128StarStar(43UL);
            var reference = new Xoshiro128StarStar(43UL);

            foreach (ulong range in new[] { 1UL, 3UL, 1000UL, 1UL << 40, (1UL << 63) + 12345UL, ulong.MaxValue })
            {
                ulong threshold = unchecked(0UL - range) % range;
                for (int i = 0; i < 2000; i++)
                {
                    ulong r;
                    do { r = reference.NextULong(); } while (r < threshold);
                    Assert.AreEqual(r % range, actual.Range(0UL, range));
                }
            }

            Assert.AreEqual(reference.NextUInt(), actual.NextUInt());
        }

        [Test]
        public void DeterministicUUID_IsPinned()
        {
            Assert.AreEqual(
                Guid.Parse("cb6b2db1-38ce-542c-9370-7e987eba7d57"),
                UUID.GenerateDeterministicUUID(0xC0FFEE1234ABCDEFUL, 7UL));
        }

        [Test]
        public void DeriveSeed_IsSensitiveToCharacterOrder()
        {
            Assert.AreNotEqual(Seeding.DeriveSeed(7u, "chunk/1/2"), Seeding.DeriveSeed(7u, "chunk/2/1"));
        }

        [Test]
        public void AutoSort_EqualSizedItems_KeepInputOrder()
        {
            const int count = 40;
            var grid = new Grid2D<TestCell, int>(count, 1, 1f, (x, y) => new TestCell());
            var items = new List<(int, Polyomino)>();
            for (int i = 0; i < count; i++)
                items.Add((i, Polyomino.FromRect(1, 1)));

            var placed = new BinPacker<int>(grid).AutoSort(items);

            Assert.AreEqual(count, placed.Count);
            for (int i = 0; i < count; i++)
                Assert.AreEqual((i, i, 0), placed[i]);
        }


        [Test]
        public void GetCellsForOccupant_ReturnsAscendingIndices_RegardlessOfPlacementOrder()
        {
            var grid = new Grid2D<TestCell, int>(8, 2, 1f, (x, y) => new TestCell());
            var cell = Polyomino.FromRect(1, 1);
            foreach (int x in new[] { 6, 1, 4, 0, 7, 3 })
                grid.Occupy(x, 0, 99, cell);

            Span<int> all = stackalloc int[8];
            int count = grid.GetCellsForOccupant(99, all);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4, 6, 7 }, all.Slice(0, count).ToArray());

            Span<int> truncated = stackalloc int[3];
            count = grid.GetCellsForOccupant(99, truncated);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, truncated.Slice(0, count).ToArray());
        }

        private sealed class TestCell : ICell<int>
        {
            public int X { get; private set; }
            public int Y { get; private set; }
            public int Occupant { get; set; }
            public bool IsOccupied { get; private set; }

            public void Initialize(int x, int y) { X = x; Y = y; }
            public bool Contains(int item) => IsOccupied && Occupant == item;
            public void Clear() { Occupant = 0; IsOccupied = false; }
            public void Occupy(int item) { Occupant = item; IsOccupied = true; }
        }
    }
}
