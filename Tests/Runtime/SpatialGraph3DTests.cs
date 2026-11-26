using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.RNG.Sources;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class SpatialGraph3DTests
    {
        private static readonly Polycube Crate = Polycube.FromBox(2, 2, 2);

        private static SpatialGraph3D<string> FullPack()
        {
            var pack = new SpatialGraph3D<string>(4, 3, 2);
            pack.AddPocket(Polycube.FromBox(4, 3, 2), 0, 0, 0);
            return pack;
        }

        [Test]
        public void Constructor_ValidatesAndReportsVolume()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGraph3D<string>(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGraph3D<string>(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGraph3D<string>(1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGraph3D<string>(50000, 50000, 2));
            var pack = new SpatialGraph3D<string>(4, 3, 2);
            Assert.AreEqual((24, 0, 0), (pack.Volume, pack.ExistingCount, pack.AliveCount));
        }

        [Test]
        public void AddCell_And_AddPocket()
        {
            var pack = new SpatialGraph3D<string>(4, 3, 2);
            Assert.IsTrue(pack.AddCell(0, 0, 0));
            Assert.IsFalse(pack.AddCell(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => pack.AddCell(4, 0, 0));

            Assert.AreEqual(7, pack.AddPocket(Polycube.FromBox(2, 2, 2), 0, 0, 0));
            Assert.AreEqual((8, 8), (pack.ExistingCount, pack.AliveCount));
            Assert.IsTrue(pack.Exists(1, 1, 1));
            Assert.IsFalse(pack.Exists(2, 0, 0));

            Assert.Throws<ArgumentOutOfRangeException>(() => pack.AddPocket(Polycube.FromBox(2, 1, 1), 3, 0, 0));
            Assert.AreEqual(8, pack.ExistingCount);
            Assert.Throws<ArgumentException>(() => pack.AddPocket(default, 0, 0, 0));
        }

        [Test]
        public void Place_OccupiesEveryCellAcrossLayers()
        {
            var pack = FullPack();
            Assert.IsTrue(pack.Place(0, 0, 0, "crate", Crate));
            Assert.IsTrue(pack.Contains("crate"));
            Assert.IsTrue(pack.IsOccupied(1, 1, 1));
            Assert.IsFalse(pack.IsOccupied(2, 0, 0));
            Assert.IsTrue(pack.TryGetOccupant(0, 1, 1, out string found));
            Assert.AreEqual("crate", found);

            var cells = new (int X, int Y, int Layer)[8];
            Assert.AreEqual(8, pack.GetCells("crate", cells));
            Assert.AreEqual((0, 0, 0), cells[0]);
            Assert.AreEqual((1, 1, 1), cells[7]);
        }

        [Test]
        public void Place_Fails_WithoutLeavingAnythingBehind()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            Assert.IsFalse(pack.CanPlace(1, 1, 1, Polycube.Single));
            Assert.IsFalse(pack.Place(1, 1, 0, "box", Crate));
            Assert.IsFalse(pack.Contains("box"));
            Assert.IsFalse(pack.IsOccupied(2, 2, 0));

            var shallow = new SpatialGraph3D<string>(4, 3, 2);
            shallow.AddPocket(Polycube.FromBox(2, 2, 1), 0, 0, 0);
            Assert.IsFalse(shallow.Place(0, 0, 0, "crate", Crate));
            Assert.IsFalse(shallow.IsOccupied(0, 0, 0));

            Assert.IsFalse(pack.CanPlace(2, 0, 0, default));
            Assert.IsFalse(pack.Place(2, 0, 0, "nothing", default));
        }

        [Test]
        public void Place_SameOccupantTwice_Throws()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            Assert.Throws<ArgumentException>(() => pack.Place(2, 0, 0, "crate", Polycube.Single));
            Assert.IsFalse(pack.IsOccupied(2, 0, 0));
            Assert.AreEqual(8, pack.GetCells("crate", new (int X, int Y, int Layer)[24]));
        }

        [Test]
        public void GetCells_IsInIndexOrder_WhateverTheShapeOrder()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "bent", Polycube.FromOffsets((1, 1, 1), (1, 0, 0), (0, 0, 0)));
            var cells = new (int X, int Y, int Layer)[3];
            Assert.AreEqual(3, pack.GetCells("bent", cells));
            CollectionAssert.AreEqual(new[] { (0, 0, 0), (1, 0, 0), (1, 1, 1) }, cells);
        }

        [Test]
        public void Constructor_RejectsABoxTooBigToAllocate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGraph3D<byte>(int.MaxValue, 1, 1));
        }

        [Test]
        public void NullOccupant_Throws()
        {
            var pack = FullPack();
            Assert.Throws<ArgumentNullException>(() => pack.Place(0, 0, 0, null!, Polycube.Single));
            Assert.Throws<ArgumentNullException>(() => pack.Free(null!));
            Assert.Throws<ArgumentNullException>(() => pack.Contains(null!));
            Assert.Throws<ArgumentNullException>(() => pack.GetCells(null!, new (int X, int Y, int Layer)[1]));
            Assert.IsFalse(pack.IsOccupied(0, 0, 0));
        }

        [Test]
        public void KilledCell_CanBeBuiltByGameTests()
        {
            var cell = new KilledCell<string>(1, 2, 3, true, "crate");
            Assert.AreEqual((1, 2, 3, true, "crate"), (cell.X, cell.Y, cell.Layer, cell.WasOccupied, cell.Occupant));
        }

        [Test]
        public void FindPockets_FollowsAPocketBackwardsOnEveryAxis()
        {
            var bendsBackInY = new SpatialGraph3D<string>(3, 2, 1);
            foreach (var (x, y) in new[] { (0, 0), (0, 1), (1, 1), (2, 1), (2, 0) }) bendsBackInY.AddCell(x, y, 0);
            Assert.AreEqual(1, bendsBackInY.FindPockets(new int[6]));

            var bendsBackInX = new SpatialGraph3D<string>(2, 2, 1);
            foreach (var (x, y) in new[] { (1, 0), (1, 1), (0, 1) }) bendsBackInX.AddCell(x, y, 0);
            Assert.AreEqual(1, bendsBackInX.FindPockets(new int[4]));

            var bendsBackInLayer = new SpatialGraph3D<string>(3, 1, 2);
            foreach (var (x, layer) in new[] { (0, 0), (0, 1), (1, 1), (2, 1), (2, 0) }) bendsBackInLayer.AddCell(x, 0, layer);
            Assert.AreEqual(1, bendsBackInLayer.FindPockets(new int[6]));
        }

        [Test]
        public void Free_ClearsTheItem()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            Assert.IsTrue(pack.Free("crate"));
            Assert.IsFalse(pack.Contains("crate"));
            Assert.IsFalse(pack.IsOccupied(1, 1, 1));
            Assert.IsFalse(pack.Free("crate"));
            Assert.IsTrue(pack.Place(0, 0, 0, "crate", Crate));
        }

        [Test]
        public void Shapes_NeverWrapAroundAnEdge()
        {
            var pack = FullPack();
            Assert.IsFalse(pack.CanPlace(3, 0, 0, Polycube.FromBox(2, 1, 1)));
            Assert.IsFalse(pack.CanPlace(0, 2, 0, Polycube.FromBox(1, 2, 1)));
            Assert.IsFalse(pack.CanPlace(0, 0, 1, Polycube.FromBox(1, 1, 2)));
            Assert.IsFalse(pack.Place(3, 0, 0, "long", Polycube.FromBox(2, 1, 1)));
            Assert.IsFalse(pack.IsOccupied(0, 1, 0));
        }

        [Test]
        public void Queries_OutsideTheBox_AreFalse()
        {
            var pack = FullPack();
            Assert.IsFalse(pack.Exists(-1, 0, 0));
            Assert.IsFalse(pack.Exists(0, 3, 0));
            Assert.IsFalse(pack.IsAlive(4, 0, 0));
            Assert.IsFalse(pack.IsOccupied(0, 0, 2));
            Assert.IsFalse(pack.TryGetOccupant(0, -1, 0, out _));
            Assert.IsFalse(pack.CanPlace(-1, 0, 0, Polycube.Single));
            Assert.IsFalse(pack.IsValidPosition(0, 0, -1));
        }

        [Test]
        public void Index_RoundTrips()
        {
            var pack = FullPack();
            Assert.AreEqual(23, pack.GetIndex(3, 2, 1));
            Assert.AreEqual((3, 2, 1), pack.GetPosition(23));
            Assert.AreEqual((1, 0, 0), pack.GetPosition(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pack.GetPosition(24));
            Assert.Throws<ArgumentOutOfRangeException>(() => pack.GetPosition(-1));
        }

        [Test]
        public void GetCells_SmallBufferAndUnknownItem()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            var three = new (int X, int Y, int Layer)[3];
            Assert.AreEqual(3, pack.GetCells("crate", three));
            Assert.AreEqual((0, 0, 0), three[0]);
            Assert.AreEqual((0, 1, 0), three[2]);
            Assert.AreEqual(0, pack.GetCells("ghost", three));
        }

        [Test]
        public void OccupantComparer_IsUsed()
        {
            var pack = new SpatialGraph3D<string>(2, 1, 1, StringComparer.OrdinalIgnoreCase);
            pack.AddPocket(Polycube.FromBox(2, 1, 1), 0, 0, 0);
            pack.Place(0, 0, 0, "Crate", Polycube.Single);
            Assert.IsTrue(pack.Contains("crate"));
            Assert.Throws<ArgumentException>(() => pack.Place(1, 0, 0, "CRATE", Polycube.Single));
        }

        [Test]
        public void TryKill_ReportsTheItem_AndLeavesItInPlace()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);

            Assert.IsTrue(pack.TryKill(1, 1, 1, out var cell));
            Assert.AreEqual((1, 1, 1), (cell.X, cell.Y, cell.Layer));
            Assert.IsTrue(cell.WasOccupied);
            Assert.AreEqual("crate", cell.Occupant);

            Assert.IsFalse(pack.IsAlive(1, 1, 1));
            Assert.IsTrue(pack.Exists(1, 1, 1));
            Assert.AreEqual((24, 23), (pack.ExistingCount, pack.AliveCount));
            Assert.IsTrue(pack.Contains("crate"));
            Assert.IsTrue(pack.IsOccupied(1, 1, 1));
            Assert.IsTrue(pack.TryGetOccupant(1, 1, 1, out string still));
            Assert.AreEqual("crate", still);
            Assert.AreEqual(8, pack.GetCells("crate", new (int X, int Y, int Layer)[8]));
        }

        [Test]
        public void TryKill_EmptyCell_TwiceAndMissing()
        {
            var pack = new SpatialGraph3D<string>(4, 3, 2);
            pack.AddCell(0, 0, 0);
            Assert.IsTrue(pack.TryKill(0, 0, 0, out var cell));
            Assert.IsFalse(cell.WasOccupied);
            Assert.IsNull(cell.Occupant);
            Assert.IsFalse(pack.TryKill(0, 0, 0, out _));
            Assert.IsFalse(pack.TryKill(1, 0, 0, out _));
            Assert.IsFalse(pack.TryKill(-1, 0, 0, out _));
            Assert.AreEqual(0, pack.AliveCount);
        }

        [Test]
        public void DeadCell_RejectsPlacement_AndAFreedItemCannotReturn()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            pack.TryKill(1, 1, 1, out _);
            pack.TryKill(3, 2, 0, out _);
            Assert.IsFalse(pack.CanPlace(3, 2, 0, Polycube.Single));

            Assert.IsTrue(pack.Free("crate"));
            Assert.IsFalse(pack.IsOccupied(1, 1, 1));
            Assert.IsFalse(pack.IsAlive(1, 1, 1));
            Assert.IsTrue(pack.Exists(1, 1, 1));
            Assert.AreEqual((24, 22), (pack.ExistingCount, pack.AliveCount));
            Assert.IsFalse(pack.CanPlace(0, 0, 0, Crate));
            Assert.IsTrue(pack.Place(2, 0, 0, "crate", Crate));
        }

        [Test]
        public void Repair_BringsACellBack_WithItsItem()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            pack.TryKill(0, 0, 0, out _);
            Assert.IsTrue(pack.Repair(0, 0, 0));
            Assert.IsTrue(pack.IsAlive(0, 0, 0));
            Assert.IsTrue(pack.IsOccupied(0, 0, 0));
            Assert.AreEqual(24, pack.AliveCount);
            Assert.IsFalse(pack.Repair(0, 0, 0));
            Assert.IsFalse(pack.Repair(9, 9, 9));
            var sparse = new SpatialGraph3D<string>(2, 1, 1);
            Assert.IsFalse(sparse.Repair(0, 0, 0));
        }

        [Test]
        public void AddingCells_NeverRevivesADeadOne()
        {
            var pack = FullPack();
            pack.TryKill(2, 1, 0, out _);
            Assert.IsFalse(pack.AddCell(2, 1, 0));
            Assert.AreEqual(0, pack.AddPocket(Polycube.FromBox(4, 3, 2), 0, 0, 0));
            Assert.IsFalse(pack.IsAlive(2, 1, 0));
        }

        [Test]
        public void TryKillRandom_DrawsOnce_ByRank_UntilNothingIsLeft()
        {
            var pack = FullPack();
            var rng = new Xoshiro128StarStar(123UL);
            var reference = new Xoshiro128StarStar(123UL);
            var killed = new HashSet<int>();

            for (int alive = 24; alive > 0; alive--)
            {
                int rank = reference.Range(0, alive);
                int expected = -1;
                for (int index = 0, seen = 0; index < pack.Volume; index++)
                {
                    var (x, y, layer) = pack.GetPosition(index);
                    if (pack.IsAlive(x, y, layer) && seen++ == rank) { expected = index; break; }
                }

                Assert.IsTrue(pack.TryKillRandom(rng, out var cell));
                Assert.AreEqual(expected, pack.GetIndex(cell.X, cell.Y, cell.Layer));
                Assert.IsTrue(killed.Add(expected));
            }

            Assert.AreEqual(0, pack.AliveCount);
            Assert.IsFalse(pack.TryKillRandom(rng, out _));
            Assert.AreEqual(reference.NextUInt(), rng.NextUInt());
            Assert.Throws<ArgumentNullException>(() => pack.TryKillRandom(null!, out _));
        }

        [Test]
        public void TryKillRandom_ReplaysForTheSameSeed_AndDoesNotAllocate()
        {
            var a = FullPack();
            var b = FullPack();
            var rngA = new Xoshiro128StarStar(123UL);
            var rngB = new Xoshiro128StarStar(123UL);
            a.TryKillRandom(rngA, out _);
            b.TryKillRandom(rngB, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                a.TryKillRandom(rngA, out var cellA);
                b.TryKillRandom(rngB, out var cellB);
                if ((cellA.X, cellA.Y, cellA.Layer) != (cellB.X, cellB.Y, cellB.Layer)) Assert.Fail("diverged");
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void StructOccupant_DefaultValue_IsARealItem()
        {
            var pack = new SpatialGraph3D<int>(1, 1, 1);
            pack.AddCell(0, 0, 0);
            Assert.IsTrue(pack.Place(0, 0, 0, 0, Polycube.Single));
            Assert.IsTrue(pack.IsOccupied(0, 0, 0));
            Assert.IsTrue(pack.Contains(0));
            Assert.IsTrue(pack.TryKill(0, 0, 0, out var cell));
            Assert.IsTrue(cell.WasOccupied);
            Assert.AreEqual(0, cell.Occupant);
        }

        [Test]
        public void SavedState_IsRebuilt_CellsThenItemsThenDeadCells()
        {
            var original = FullPack();
            original.Place(0, 0, 0, "crate", Crate);
            original.TryKill(1, 0, 0, out _);
            original.TryKill(3, 2, 1, out _);

            var rebuilt = new SpatialGraph3D<string>(4, 3, 2);
            for (int index = 0; index < original.Volume; index++)
            {
                var (x, y, layer) = original.GetPosition(index);
                if (original.Exists(x, y, layer)) rebuilt.AddCell(x, y, layer);
            }
            var cells = new (int X, int Y, int Layer)[8];
            original.GetCells("crate", cells);
            Assert.IsTrue(rebuilt.Place(cells[0].X, cells[0].Y, cells[0].Layer, "crate", Crate));
            for (int index = 0; index < original.Volume; index++)
            {
                var (x, y, layer) = original.GetPosition(index);
                if (!original.IsAlive(x, y, layer)) rebuilt.TryKill(x, y, layer, out _);
            }

            for (int index = 0; index < original.Volume; index++)
            {
                var (x, y, layer) = original.GetPosition(index);
                Assert.AreEqual(original.IsAlive(x, y, layer), rebuilt.IsAlive(x, y, layer));
                Assert.AreEqual(original.IsOccupied(x, y, layer), rebuilt.IsOccupied(x, y, layer));
            }
            Assert.IsTrue(rebuilt.IsOccupied(1, 0, 0));
            Assert.IsFalse(rebuilt.IsAlive(1, 0, 0));

            var wrongOrder = FullPack();
            wrongOrder.TryKill(1, 0, 0, out _);
            Assert.IsFalse(wrongOrder.Place(0, 0, 0, "crate", Crate));
        }

        [Test]
        public void FindPockets_OnePocket()
        {
            var pack = FullPack();
            var ids = new int[24];
            Assert.AreEqual(1, pack.FindPockets(ids));
            CollectionAssert.AreEqual(new int[24], ids);
        }

        [Test]
        public void FindPockets_SeparatePockets_AndSplitByADeadCell()
        {
            var apart = new SpatialGraph3D<string>(5, 1, 1);
            apart.AddPocket(Polycube.FromBox(2, 1, 1), 0, 0, 0);
            apart.AddPocket(Polycube.FromBox(2, 1, 1), 3, 0, 0);
            var ids = new int[5];
            Assert.AreEqual(2, apart.FindPockets(ids));
            CollectionAssert.AreEqual(new[] { 0, 0, -1, 1, 1 }, ids);

            var split = new SpatialGraph3D<string>(5, 1, 1);
            split.AddPocket(Polycube.FromBox(5, 1, 1), 0, 0, 0);
            Assert.AreEqual(1, split.FindPockets(ids));
            split.TryKill(2, 0, 0, out _);
            Assert.AreEqual(2, split.FindPockets(ids));
            CollectionAssert.AreEqual(new[] { 0, 0, -1, 1, 1 }, ids);
            split.Repair(2, 0, 0);
            Assert.AreEqual(1, split.FindPockets(ids));
        }

        [Test]
        public void FindPockets_ConnectsThroughLayers_ButNotDiagonalsOrWrappedEdges()
        {
            var stacked = new SpatialGraph3D<string>(1, 1, 2);
            stacked.AddPocket(Polycube.FromBox(1, 1, 2), 0, 0, 0);
            Assert.AreEqual(1, stacked.FindPockets(new int[2]));

            var rowWrap = new SpatialGraph3D<string>(2, 2, 1);
            rowWrap.AddCell(1, 0, 0);
            rowWrap.AddCell(0, 1, 0);
            var ids = new int[4];
            Assert.AreEqual(2, rowWrap.FindPockets(ids));
            CollectionAssert.AreEqual(new[] { -1, 0, 1, -1 }, ids);

            var layerWrap = new SpatialGraph3D<string>(2, 1, 2);
            layerWrap.AddCell(1, 0, 0);
            layerWrap.AddCell(0, 0, 1);
            Assert.AreEqual(2, layerWrap.FindPockets(ids));
        }

        [Test]
        public void FindPockets_BufferRules_OccupancyAndAllocation()
        {
            var pack = FullPack();
            pack.Place(0, 0, 0, "crate", Crate);
            Assert.Throws<ArgumentException>(() => pack.FindPockets(new int[23]));

            var ids = new int[26];
            ids[24] = 99;
            ids[25] = 99;
            Assert.AreEqual(1, pack.FindPockets(ids));
            Assert.AreEqual(0, ids[0]);
            Assert.AreEqual((99, 99), (ids[24], ids[25]));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) pack.FindPockets(ids);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);

            Assert.AreEqual(0, new SpatialGraph3D<string>(2, 2, 2).FindPockets(new int[8]));
        }
    }
}
