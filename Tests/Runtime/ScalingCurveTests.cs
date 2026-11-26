using System;
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

        [Test]
        public void Exponential_StaysWithinOneFloatStepOfThePowerFunction_AndIsExactAtLevelsZeroAndOne()
        {
            var curve = ScalingCurve.Exponential(100f, 0.137f);

            Assert.AreEqual(100f, curve.Evaluate(0));
            Assert.AreEqual(100f * (1f + 0.137f), curve.Evaluate(1));
            for (int level = -40; level < 500; level++)
            {
                double expected = 100.0 * Math.Pow((double)(1f + 0.137f), level);
                if (expected > float.MaxValue)
                {
                    Assert.IsTrue(float.IsPositiveInfinity(curve.Evaluate(level)), $"level {level}");
                    continue;
                }

                Assert.AreEqual(expected, curve.Evaluate(level), Math.Abs(expected) * 3e-7, $"level {level}");
            }
        }

        [Test]
        public void Exponential_ValuesArePinned_SoEveryPlatformMustProduceTheSameBits()
        {
            var curve = ScalingCurve.Exponential(100f, 0.137f);
            var levels = new[] { 2, 7, 64, 275, 499, -3 };
            var bits = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++)
                bits[i] = BitConverter.SingleToInt32Bits(curve.Evaluate(levels[i]));

            CollectionAssert.AreEqual(new[] { 1124157155, 1131784019, 1219812295, 1547678579, 1896271371, 1116213458 }, bits);
            Assert.AreEqual(1129314078, BitConverter.SingleToInt32Bits(ScalingCurve.Exponential(1f, 0.1f).Evaluate(56)),
                "This level is where the platform power function and the fixed-order multiply disagree on Windows.");
        }

        [Test]
        public void Exponential_HandlesShrinkingCurvesCapsAndExtremeLevels()
        {
            Assert.AreEqual(12.5f, ScalingCurve.Exponential(100f, -0.5f).Evaluate(3));
            Assert.AreEqual(250f, ScalingCurve.Exponential(100f, 1f, 250f).Evaluate(10));
            Assert.IsTrue(float.IsPositiveInfinity(ScalingCurve.Exponential(1f, 1f).Evaluate(int.MaxValue)));
            Assert.AreEqual(0f, ScalingCurve.Exponential(1f, 1f).Evaluate(int.MinValue));
        }

        [Test]
        public void Logarithmic_MatchesTheNaturalLogToFloatAccuracy_AndIsExactAtLevelZero()
        {
            var curve = ScalingCurve.Logarithmic(0f, 1f);

            Assert.AreEqual(0f, curve.Evaluate(0));
            for (int level = 0; level < 200_000; level += level < 5000 ? 1 : 37)
            {
                double expected = Math.Log(level + 1.0);
                Assert.AreEqual(expected, curve.Evaluate(level), Math.Max(expected, 1.0) * 1.5e-7, $"level {level}");
            }

            Assert.AreEqual(Math.Log(int.MaxValue), curve.Evaluate(int.MaxValue - 1), 3e-6);
        }

        [Test]
        public void Logarithmic_ValuesArePinned_SoEveryPlatformMustProduceTheSameBits()
        {
            var curve = ScalingCurve.Logarithmic(5f, 3.3f);
            var levels = new[] { 1, 2, 9, 99, 1000, 123456, 2_000_000_000 };
            var bits = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++)
                bits[i] = BitConverter.SingleToInt32Bits(curve.Evaluate(levels[i]));

            CollectionAssert.AreEqual(new[] { 1089024580, 1091174841, 1095340949, 1101108117, 1105093665, 1110360205, 1117215019 }, bits);
            Assert.AreEqual(1089186399, BitConverter.SingleToInt32Bits(ScalingCurve.Logarithmic(0f, 1f).Evaluate(1578)),
                "This level is where the platform log function and the fixed-order series disagree on Windows.");
        }

        [Test]
        public void Logarithmic_BelowLevelZero_BehavesLikeTheLogOfZeroAndOfANegativeNumber()
        {
            var curve = ScalingCurve.Logarithmic(5f, 3.3f);

            Assert.IsTrue(float.IsNegativeInfinity(curve.Evaluate(-1)));
            Assert.IsTrue(float.IsNaN(curve.Evaluate(-2)));
            Assert.IsTrue(float.IsNaN(curve.Evaluate(int.MaxValue)));
            Assert.AreEqual(6f, ScalingCurve.Logarithmic(5f, 3.3f, 6f).Evaluate(50));
        }
    }
}
