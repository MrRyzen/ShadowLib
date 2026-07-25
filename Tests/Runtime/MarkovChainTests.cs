using System;
using NUnit.Framework;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class MarkovChainTests
    {
        private static MarkovChain<string> WeatherChain()
        {
            var chain = new MarkovChain<string>();
            chain.AddTransition("Sunny", "Sunny", 0.9f);
            chain.AddTransition("Sunny", "Rainy", 0.1f);
            chain.AddTransition("Rainy", "Sunny", 0.5f);
            chain.AddTransition("Rainy", "Rainy", 0.5f);
            return chain;
        }

        [Test]
        public void Next_IsDeterministic_ForSameSeed()
        {
            var a = WeatherChain();
            var b = WeatherChain();
            var rngA = new Xoshiro128StarStar(123UL);
            var rngB = new Xoshiro128StarStar(123UL);

            string stateA = "Sunny", stateB = "Sunny";
            for (int i = 0; i < 100; i++)
            {
                stateA = a.Next(stateA, rngA);
                stateB = b.Next(stateB, rngB);
                Assert.AreEqual(stateA, stateB);
            }
        }

        [Test]
        public void Next_RespectsTransitionWeights()
        {
            var chain = WeatherChain();
            var rng = new Xoshiro128StarStar(42UL);

            int sunny = 0;
            const int samples = 5000;
            for (int i = 0; i < samples; i++)
            {
                if (chain.Next("Sunny", rng) == "Sunny") sunny++;
            }

            // Expected 90% — allow generous slack for a deterministic seed.
            Assert.That(sunny / (float)samples, Is.EqualTo(0.9f).Within(0.03f));
        }

        [Test]
        public void AddTransition_AccumulatesRepeatedPairs()
        {
            var chain = new MarkovChain<char>();
            // 'a'→'b' added twice (2f total) vs 'a'→'c' once (1f).
            chain.AddTransition('a', 'b');
            chain.AddTransition('a', 'b');
            chain.AddTransition('a', 'c');

            var rng = new Xoshiro128StarStar(7UL);
            int b = 0;
            const int samples = 3000;
            for (int i = 0; i < samples; i++)
            {
                if (chain.Next('a', rng) == 'b') b++;
            }

            Assert.That(b / (float)samples, Is.EqualTo(2f / 3f).Within(0.04f));
        }

        [Test]
        public void AddSequence_TrainsAdjacentPairs()
        {
            var chain = new MarkovChain<char>();
            chain.AddSequence("abab".AsSpan());

            var rng = new Xoshiro128StarStar(1UL);
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual('b', chain.Next('a', rng));
                Assert.AreEqual('a', chain.Next('b', rng));
            }
        }

        [Test]
        public void TryNext_ReturnsFalse_ForTerminalAndUnknownStates()
        {
            var chain = new MarkovChain<string>();
            chain.AddTransition("start", "end");
            var rng = new Xoshiro128StarStar(1UL);

            Assert.IsFalse(chain.TryNext("end", rng, out _));      // terminal: only appears as a target
            Assert.IsFalse(chain.TryNext("missing", rng, out _));  // never added
            Assert.IsTrue(chain.TryNext("start", rng, out string next));
            Assert.AreEqual("end", next);
        }

        [Test]
        public void Next_Throws_ForTerminalState()
        {
            var chain = new MarkovChain<string>();
            chain.AddTransition("start", "end");
            var rng = new Xoshiro128StarStar(1UL);

            Assert.Throws<InvalidOperationException>(() => chain.Next("end", rng));
        }

        [Test]
        public void AddTransition_RejectsNonPositiveWeight()
        {
            var chain = new MarkovChain<int>();
            Assert.Throws<ArgumentOutOfRangeException>(() => chain.AddTransition(1, 2, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => chain.AddTransition(1, 2, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => chain.AddTransition(1, 2, float.NaN));
        }

        [Test]
        public void Walk_FillsSpan_AndStopsAtTerminal()
        {
            var chain = new MarkovChain<string>();
            chain.AddTransition("a", "b");
            chain.AddTransition("b", "c");
            var rng = new Xoshiro128StarStar(1UL);

            var buffer = new string[10];
            int written = chain.Walk("a", rng, buffer);

            Assert.AreEqual(3, written);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, buffer.AsSpan(0, written).ToArray());

            var full = WeatherChain();
            written = full.Walk("Sunny", rng, buffer);
            Assert.AreEqual(10, written);  // no terminal states — fills the whole span
        }

        [Test]
        public void Build_CanBeExtendedAndRebuilt()
        {
            var chain = new MarkovChain<string>();
            chain.AddTransition("a", "b");
            var rng = new Xoshiro128StarStar(1UL);
            Assert.AreEqual("b", chain.Next("a", rng));  // triggers lazy build

            chain.AddTransition("b", "a");               // mutate after build
            Assert.AreEqual("a", chain.Next("b", rng));  // rebuilds and samples
            Assert.AreEqual(2, chain.StateCount);
        }
    }
}
