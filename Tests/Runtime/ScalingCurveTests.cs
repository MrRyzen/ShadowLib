using NUnit.Framework;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class ScalingCurveTests
    {
        [Test]
        public void Linear_FollowsStartPlusRateTimesLevel()
        {
            var curve = ScalingCurve.Linear(start: 10f, rate: 2f);
            Assert.AreEqual(10f, curve.Evaluate(0), 1e-5f);
            Assert.AreEqual(12f, curve.Evaluate(1), 1e-5f);
            Assert.AreEqual(20f, curve.Evaluate(5), 1e-5f);
        }

        [Test]
        public void LinearClamped_RespectsCap()
        {
            var curve = ScalingCurve.LinearClamped(start: 0f, rate: 5f, cap: 12f);
            Assert.AreEqual(12f, curve.Evaluate(100), 1e-5f);
        }

        [Test]
        public void Stepped_ReturnsValueOfFirstUnreachedThreshold()
        {
            // Semantics: the value of the FIRST step whose threshold the level has NOT yet reached.
            // Once past every threshold, the last step's value sticks.
            var curve = ScalingCurve.Stepped((1, 10f), (5, 20f), (10, 30f));
            Assert.AreEqual(10f, curve.Evaluate(0), 1e-5f);   // 0 < 1 → 10
            Assert.AreEqual(20f, curve.Evaluate(1), 1e-5f);   // reached 1, 1 < 5 → 20
            Assert.AreEqual(20f, curve.Evaluate(4), 1e-5f);   // 4 < 5 → 20
            Assert.AreEqual(30f, curve.Evaluate(5), 1e-5f);   // reached 5, 5 < 10 → 30
            Assert.AreEqual(30f, curve.Evaluate(99), 1e-5f);  // past all → last
        }
    }
}
