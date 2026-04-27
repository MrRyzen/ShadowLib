using NUnit.Framework;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class SeedingTests
    {
        [Test]
        public void GenerateSeed64_FromSameString_IsDeterministic()
        {
            ulong a = Seeding.GenerateSeed64("hello-world");
            ulong b = Seeding.GenerateSeed64("hello-world");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void GenerateSeed64_FromDifferentStrings_Differs()
        {
            ulong a = Seeding.GenerateSeed64("hello-world");
            ulong b = Seeding.GenerateSeed64("hello-world!");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void DeriveSeed64_StableForSameRootAndContext()
        {
            ulong root = 0xDEADBEEF12345678UL;
            ulong a = Seeding.DeriveSeed64(root, "loot");
            ulong b = Seeding.DeriveSeed64(root, "loot");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DeriveSeed64_DiffersByContext()
        {
            ulong root = 0xDEADBEEF12345678UL;
            Assert.AreNotEqual(
                Seeding.DeriveSeed64(root, "loot"),
                Seeding.DeriveSeed64(root, "worldgen"));
        }

        [Test]
        public void DeriveSeed64_NeverReturnsZero()
        {
            // Pathological input: the FNV+SplitMix mix happens to land on 0 — implementation
            // promises it normalizes to a non-zero value so downstream RNGs don't degenerate.
            ulong derived = Seeding.DeriveSeed64(0UL, "");
            Assert.AreNotEqual(0UL, derived);
        }
    }
}
