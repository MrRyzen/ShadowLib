namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using System;

    /// <summary>Something that can be applied to a target.</summary>
    /// <typeparam name="TTarget">The type the effect acts on.</typeparam>
    public interface IEffect<TTarget>
    {
        /// <summary>Applies the effect to the target.</summary>
        void Apply(TTarget target);
    }

    /// <summary>
    /// A <see cref="DynamicWeightTable{T}"/> of effects that can pick one and apply it in a single call.
    /// </summary>
    /// <typeparam name="TEffect">The effect type.</typeparam>
    /// <typeparam name="TTarget">The type effects are applied to.</typeparam>
    public class EffectWheel<TEffect, TTarget> : DynamicWeightTable<TEffect>
        where TEffect : IEffect<TTarget>, IEquatable<TEffect>
    {
        /// <summary>Picks an effect by effective weight and applies it to the target.</summary>
        /// <param name="rng">The random number generator to draw from.</param>
        /// <param name="target">The target to apply the picked effect to.</param>
        /// <returns>The effect that was applied.</returns>
        /// <remarks>
        /// The pick is committed (stock spent, last pick recorded) before <see cref="IEffect{TTarget}.Apply"/> runs.
        /// A struct effect is applied on a copy, so it should not keep state.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Nothing is pickable.</exception>
        public TEffect SampleAndApply(IRandom rng, TTarget target)
        {
            TEffect effect = Sample(rng);
            effect.Apply(target);
            return effect;
        }
    }
}
