// NCalc-backed code is opt-in under Unity (SHADOWLIB_NCALC), so these run on dotnet test only.
#if !UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using ShadowLib.RNG.Modifiers;

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
        public void BuiltInNCalcFunctions_StillEvaluate()
        {
            Assert.IsTrue(new ConditionEvaluator().Evaluate("Max(level, 20) == 20", Context));
        }
    }
}
#endif
