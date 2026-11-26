using System;
using NUnit.Framework;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    [TestFixture]
    public class EffectWheelTests
    {
        private sealed class Player
        {
            public int Health = 100;
        }

        private sealed class Damage : IEffect<Player>, IEquatable<Damage>
        {
            public readonly int Amount;
            public Damage(int amount) => Amount = amount;
            public void Apply(Player target) => target.Health -= Amount;
            public bool Equals(Damage? other) => other != null && other.Amount == Amount;
            public override bool Equals(object? obj) => Equals(obj as Damage);
            public override int GetHashCode() => Amount;
        }

        private static EffectWheel<Damage, Player> Wheel()
        {
            var wheel = new EffectWheel<Damage, Player>();
            wheel.Add(new Damage(25), 20f);
            wheel.Add(new Damage(1), 15f);
            return wheel;
        }

        [Test]
        public void SampleAndApply_AppliesExactlyTheReturnedEffect()
        {
            var wheel = Wheel();
            var rng = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 100; i++)
            {
                var player = new Player();
                Damage applied = wheel.SampleAndApply(rng, player);
                Assert.AreEqual(100 - applied.Amount, player.Health);
            }
        }

        [Test]
        public void SampleAndApply_MatchesSample_ForTheSameSeed()
        {
            var a = Wheel();
            var b = Wheel();
            var rngA = new Xoshiro128StarStar(123UL);
            var rngB = new Xoshiro128StarStar(123UL);
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(a.Sample(rngA), b.SampleAndApply(rngB, new Player()));
        }

        [Test]
        public void SampleAndApply_EmptyWheel_Throws()
        {
            var wheel = new EffectWheel<Damage, Player>();
            Assert.Throws<InvalidOperationException>(() => wheel.SampleAndApply(new Xoshiro128StarStar(123UL), new Player()));
        }

        [Test]
        public void Wheel_InheritsInfluence()
        {
            var drain = new Damage(25);
            var wheel = new EffectWheel<Damage, Player>();
            wheel.Add(drain, 20f);
            wheel.Add(new Damage(1), 20f);
            wheel.SetInfluence(drain, 0f, 60f, () => 1f);
            Assert.AreEqual(0.75f, wheel.GetProbability(drain), 1e-6f);
        }
    }
}
