using System;
using NUnit.Framework;
using ShadowLib.Procedural;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class MarkovWordGeneratorTests
    {
        private static readonly string[] CompanyNames =
        {
            "Novartis", "Raytheon", "Blackstone", "Palantir", "Salesforce",
            "Lockheed", "Theranos", "Northrop", "Medtronic", "Crowdstrike"
        };

        [Test]
        public void Generate_IsDeterministic_ForSameSeed()
        {
            var a = new MarkovWordGenerator(order: 2);
            a.Train(CompanyNames);
            var b = new MarkovWordGenerator(order: 2);
            b.Train(CompanyNames);

            var rngA = new Xoshiro128StarStar(123UL);
            var rngB = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(a.Generate(rngA), b.Generate(rngB));
        }

        [Test]
        public void Generate_RespectsLengthBounds_AndTrainingAlphabet()
        {
            var gen = new MarkovWordGenerator(order: 2);
            gen.Train(CompanyNames);
            gen.Build();
            var rng = new Xoshiro128StarStar(42UL);

            string alphabet = string.Concat(CompanyNames);
            for (int i = 0; i < 100; i++)
            {
                string word = gen.Generate(rng, minLength: 4, maxLength: 10);
                Assert.That(word.Length, Is.InRange(4, 10));
                foreach (char c in word)
                    StringAssert.Contains(c.ToString(), alphabet);
            }
        }

        [Test]
        public void Generate_SingleTrainingWord_ReproducesIt()
        {
            // One word → the chain is a single deterministic path.
            var gen = new MarkovWordGenerator(order: 2);
            gen.Train("nova");
            var rng = new Xoshiro128StarStar(1UL);

            for (int i = 0; i < 10; i++)
                Assert.AreEqual("nova", gen.Generate(rng, minLength: 4, maxLength: 10));
        }

        [Test]
        public void TryGenerate_ReturnsFalse_WhenBoundsAreImpossible()
        {
            var gen = new MarkovWordGenerator(order: 2);
            gen.Train("ab"); // only ever produces "ab" (length 2)
            var rng = new Xoshiro128StarStar(1UL);

            Span<char> buffer = stackalloc char[10];
            Assert.IsFalse(gen.TryGenerate(rng, buffer, out _, minLength: 5, maxAttempts: 10));
        }

        [Test]
        public void TryGenerate_ReturnsFalse_WhenUntrained()
        {
            var gen = new MarkovWordGenerator();
            var rng = new Xoshiro128StarStar(1UL);
            Span<char> buffer = stackalloc char[10];
            Assert.IsFalse(gen.TryGenerate(rng, buffer, out _));
        }

        [Test]
        public void Train_RejectsMarkerCharacters_AndEmptyWords()
        {
            var gen = new MarkovWordGenerator();
            Assert.Throws<ArgumentException>(() => gen.Train(""));
            Assert.Throws<ArgumentException>(() => gen.Train("bad^word"));
            Assert.Throws<ArgumentException>(() => gen.Train("bad$word"));
        }

        [Test]
        public void Constructor_RejectsOrderBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MarkovWordGenerator(order: 0));
        }

        [Test]
        public void Generate_WorksAcrossOrders()
        {
            var rng = new Xoshiro128StarStar(9UL);
            for (int order = 1; order <= 3; order++)
            {
                var gen = new MarkovWordGenerator(order);
                gen.Train(CompanyNames);
                string word = gen.Generate(rng, minLength: 4, maxLength: 12);
                Assert.That(word.Length, Is.InRange(4, 12));
            }
        }
    }
}
