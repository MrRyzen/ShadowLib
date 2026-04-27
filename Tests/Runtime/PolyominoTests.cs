using NUnit.Framework;
using ShadowLib.Spatial;

namespace ShadowLib.Tests;

[TestFixture]
public class PolyominoTests
{
    [Test]
    public void FromRect_ProducesExpectedDimensions()
    {
        var p = Polyomino.FromRect(3, 2);
        Assert.AreEqual(3, p.Width);
        Assert.AreEqual(2, p.Height);
        Assert.AreEqual(6, p.CellCount);
    }

    [Test]
    public void FromRect_OffsetsAreNormalizedFromZero()
    {
        var p = Polyomino.FromRect(2, 2);
        var offsets = p.Offsets.ToArray();
        Assert.That(offsets, Has.Member((0, 0)));
        Assert.That(offsets, Has.Member((1, 0)));
        Assert.That(offsets, Has.Member((0, 1)));
        Assert.That(offsets, Has.Member((1, 1)));
    }
}
