using System;
using NUnit.Framework;
using ShadowLib.RNG;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Utilities;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class DiceTests
    {
        [Test]
        public void Sample_AlwaysReturnsValueFromFaceArray()
        {
            Orchestrator.Initialize(Seeding.GenerateSeed64("dice-test"));
            var rng = Orchestrator.CreateRNG("dice");
            string[] faces = { "a", "b", "c", "d", "e", "f" };
            var d6 = new Dice<string>(rng, faces);

            for (int i = 0; i < 200; i++)
            {
                string roll = d6.Sample();
                CollectionAssert.Contains(faces, roll);
            }
        }

        [Test]
        public void Constructor_RejectsEmptyFaceArray()
        {
            Orchestrator.Initialize(Seeding.GenerateSeed64("dice-test"));
            var rng = Orchestrator.CreateRNG("dice");
            Assert.Throws<ArgumentException>(() => _ = new Dice<int>(rng, Array.Empty<int>()));
        }

        [Test]
        public void SampleMultiple_ReturnsRequestedCount()
        {
            Orchestrator.Initialize(Seeding.GenerateSeed64("dice-test"));
            var rng = Orchestrator.CreateRNG("dice");
            var d = new Dice<int>(rng, new[] { 1, 2, 3, 4 });
            var rolls = d.SampleMultiple(7);
            Assert.AreEqual(7, rolls.Length);
        }
    }
}
