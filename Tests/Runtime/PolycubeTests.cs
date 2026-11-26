using System;
using NUnit.Framework;
using ShadowLib.Spatial;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class PolycubeTests
    {
        [Test]
        public void FromBox_ReportsSizeAndCells()
        {
            var box = Polycube.FromBox(2, 3, 4);
            Assert.AreEqual((2, 3, 4, 24), (box.Width, box.Height, box.Depth, box.CellCount));
            Assert.AreEqual((0, 0, 0), box.Offsets[0]);
            Assert.AreEqual((1, 2, 3), box.Offsets[23]);
        }

        [Test]
        public void FromBox_RejectsBadSizes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.FromBox(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.FromBox(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.FromBox(1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.FromBox(int.MaxValue, 2, 1));
        }

        [Test]
        public void FromOffsets_NormalizesAndKeepsOrder()
        {
            var shape = Polycube.FromOffsets((5, 5, 5), (6, 5, 5), (5, 5, 7));
            Assert.AreEqual((2, 1, 3, 3), (shape.Width, shape.Height, shape.Depth, shape.CellCount));
            Assert.AreEqual((0, 0, 0), shape.Offsets[0]);
            Assert.AreEqual((1, 0, 0), shape.Offsets[1]);
            Assert.AreEqual((0, 0, 2), shape.Offsets[2]);
        }

        [Test]
        public void FromOffsets_RejectsEmptyAndDuplicates()
        {
            Assert.Throws<ArgumentException>(() => Polycube.FromOffsets());
            Assert.Throws<ArgumentException>(() => Polycube.FromOffsets(null!));
            Assert.Throws<ArgumentException>(() => Polycube.FromOffsets((0, 0, 0), (1, 0, 0), (0, 0, 0)));
        }

        [Test]
        public void FromPolyomino_Extrudes()
        {
            var corner = Polyomino.FromOffsets((0, 0), (1, 0), (1, 1));
            var shape = Polycube.FromPolyomino(corner, depth: 2);
            Assert.AreEqual((2, 2, 2, 6), (shape.Width, shape.Height, shape.Depth, shape.CellCount));
            Assert.IsTrue(shape.Offsets.Contains((1, 1, 1)));
            Assert.IsFalse(shape.Offsets.Contains((0, 1, 0)));
            Assert.AreEqual(3, Polycube.FromPolyomino(corner).CellCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => Polycube.FromPolyomino(corner, 0));
            Assert.Throws<ArgumentException>(() => Polycube.FromPolyomino(default, 1));
        }

        [Test]
        public void Single_And_Default()
        {
            Assert.AreEqual(1, Polycube.Single.CellCount);
            Assert.AreEqual(0, default(Polycube).CellCount);
            Assert.IsTrue(default(Polycube).Offsets.IsEmpty);
        }
    }
}
