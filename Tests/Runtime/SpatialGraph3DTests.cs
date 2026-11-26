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
    }
}
