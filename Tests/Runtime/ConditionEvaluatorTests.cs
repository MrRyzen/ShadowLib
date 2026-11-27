// NCalc-backed code is opt-in under Unity (SHADOWLIB_NCALC), so these run on dotnet test only.
#if !UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class ConditionEvaluatorTests
    {
        private static readonly Dictionary<string, object> Context = new()
        {
            ["level"] = 12,
            ["tags"] = new object[] { "fire", "ice" },
        };

        [Test]
        public void CustomFunctionNames_IgnoreCurrentCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                Assert.IsTrue(new ConditionEvaluator().Evaluate("CONTAINS(tags, 'fire')", Context));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void EmptyCondition_IsTrue()
        {
            Assert.IsTrue(new ConditionEvaluator().Evaluate("  ", Context));
        }

        [Test]
        public void MalformedCondition_Throws()
        {
            Assert.Catch(() => new ConditionEvaluator().Evaluate("level >", Context));
        }

        [Test]
        public void UnknownName_Throws()
        {
            Assert.Catch(() => new ConditionEvaluator().Evaluate("missing > 1", Context));
        }

        [Test]
        public void NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ConditionEvaluator().Evaluate("1 == 1", null!));
        }

        [Test]
        public void Distance_MeasuresBetweenNumericsVectors()
        {
            var context = new Dictionary<string, object>
            {
                ["a"] = new System.Numerics.Vector3(0f, 0f, 0f),
                ["b"] = new System.Numerics.Vector3(3f, 4f, 0f),
            };

            Assert.IsTrue(new ConditionEvaluator().Evaluate("distance(a, b) == 5", context));
        }

        [Test]
        public void Distance_OfSomethingElse_Throws()
        {
            Assert.Catch(() => new ConditionEvaluator().Evaluate("distance(level, level) == 0", Context));
        }

        [Test]
        public void BuiltInNCalcFunctions_StillEvaluate()
        {
            Assert.IsTrue(new ConditionEvaluator().Evaluate("Max(level, 20) == 20", Context));
        }
    }
}
#endif
