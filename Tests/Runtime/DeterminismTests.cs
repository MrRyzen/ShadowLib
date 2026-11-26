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
        private static int ReferenceRange(Xoshiro128StarStar rng, int min, int max)
        {
            uint range = (uint)(max - min);
            uint threshold = unchecked(0u - range) % range;
            while (true)
            {
                uint r = rng.NextUInt();
                if (r >= threshold)
                    return min + (int)(r % range);
            }
        }

        private static ulong ReferenceRange(Xoshiro128StarStar rng, ulong min, ulong max)
        {
            ulong range = max - min;
            ulong threshold = unchecked(0UL - range) % range;
            while (true)
            {
                ulong r = rng.NextULong();
                if (r >= threshold)
                    return min + (r % range);
            }
        }

        [Test]
        public void Range_Int_DrawsAndReturnsExactlyLikeTheOriginalRejectionSampler()
        {
            var actual = new Xoshiro128StarStar(2024UL);
            var expected = new Xoshiro128StarStar(2024UL);
            var shape = new Xoshiro128StarStar(7UL);
            var spans = new long[] { 1, 2, 3, 4, 5, 7, 8, 100, 1000, 1024, 65536, 65537, 1L << 30, (1L << 31) - 1, 1L << 31, (1L << 31) + 1, 3_000_000_000, (1L << 32) - 1 };

            for (int i = 0; i < 400_000; i++)
            {
                long span = i % 3 == 0 ? spans[i % spans.Length] : 1 + (long)(shape.NextUInt() % (i % 2 == 0 ? 5000u : uint.MaxValue));
                int min = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue - span, (long)shape.NextInt()));
                long max = min + span;
                if (max > int.MaxValue)
                {
                    min = int.MinValue;
                    max = Math.Min(int.MaxValue, int.MinValue + span);
                }

                Assert.AreEqual(ReferenceRange(expected, min, (int)max), actual.Range(min, (int)max), $"draw {i}: [{min}, {max})");
            }

            Assert.AreEqual(expected.NextUInt(), actual.NextUInt(), "the two samplers consumed a different number of draws");
        }

        [Test]
        public void Range_ULong_DrawsAndReturnsExactlyLikeTheOriginalRejectionSampler()
        {
            var actual = new Xoshiro128StarStar(2025UL);
            var expected = new Xoshiro128StarStar(2025UL);
            var shape = new Xoshiro128StarStar(8UL);
            var spans = new ulong[] { 1, 2, 3, 8, 1000, 1UL << 32, (1UL << 32) + 1, 1UL << 63, (1UL << 63) + 1, 12_000_000_000_000_000_000UL, ulong.MaxValue };

            for (int i = 0; i < 300_000; i++)
            {
                ulong span = i % 3 == 0 ? spans[i % spans.Length] : 1 + shape.NextULong() % (i % 2 == 0 ? 5000UL : ulong.MaxValue);
                ulong min = span == ulong.MaxValue ? 0 : shape.NextULong() % (ulong.MaxValue - span);

                Assert.AreEqual(ReferenceRange(expected, min, min + span), actual.Range(min, min + span), $"draw {i}: [{min}, +{span})");
            }

            Assert.AreEqual(expected.NextUInt(), actual.NextUInt(), "the two samplers consumed a different number of draws");
        }

        [Test]
        public void NextDouble_ValuesArePinned()
        {
            var rng = new Xoshiro128StarStar(12345UL);
            var bits = new long[4];
            for (int i = 0; i < bits.Length; i++)
                bits[i] = BitConverter.DoubleToInt64Bits(rng.NextDouble());

            CollectionAssert.AreEqual(
                new[] { 4603029116163366187L, 4602292499468008690L, 4598989170424644226L, 4599725355290549728L }, bits);
        }

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

        private static void AssertUnmanaged<T>() where T : unmanaged { }

        [Test]
        public void Xoshiro128Struct_IsUnmanaged() => AssertUnmanaged<Xoshiro128>();

        [Test]
        public void Xoshiro128Struct_MatchesClassBitForBit()
        {
            var s = new Xoshiro128(987654321UL);
            var c = new Xoshiro128StarStar(987654321UL);
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(c.NextUInt(), s.NextUInt());
                Assert.AreEqual(c.NextInt(), s.NextInt());
                Assert.AreEqual(c.NextULong(), s.NextULong());
                Assert.AreEqual(c.NextFloat(), s.NextFloat());
                Assert.AreEqual(c.NextDouble(), s.NextDouble());
                Assert.AreEqual(c.Range(-5, 17), s.Range(-5, 17));
                Assert.AreEqual(c.Range(0.5f, 9f), s.Range(0.5f, 9f));
                Assert.AreEqual(c.Range(1.0, 2.0), s.Range(1.0, 2.0));
                Assert.AreEqual(c.Range(3UL, 1UL << 40), s.Range(3UL, 1UL << 40));
            }
            CollectionAssert.AreEqual(c.GetState(), s.State.ToBytes());
        }

        [Test]
        public void Xoshiro128Struct_ZeroSeedAndZeroState_StillGenerate()
        {
            Assert.AreNotEqual(0u, new Xoshiro128(0UL).NextUInt() | new Xoshiro128(0UL).State.S0);

            var zeroed = new Xoshiro128(5UL) { State = default };
            zeroed.NextUInt();
            Assert.AreNotEqual(0u, zeroed.NextUInt() | zeroed.NextUInt());
        }

        [Test]
        public void DeriveSeed64_ByIndex_IsStableDistinctAndNonZero()
        {
            Assert.AreEqual(Seeding.DeriveSeed64(42UL, 7UL), Seeding.DeriveSeed64(42UL, 7UL));

            var seen = new HashSet<ulong>();
            for (ulong i = 0; i < 10000; i++)
            {
                ulong seed = Seeding.DeriveSeed64(42UL, i);
                Assert.AreNotEqual(0UL, seed);
                Assert.IsTrue(seen.Add(seed));
            }

            Assert.AreNotEqual(Seeding.DeriveSeed64(42UL, 1UL), Seeding.DeriveSeed64(43UL, 1UL));
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
