namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using System.Collections.Generic;
    using System;

    /// <summary>
    /// Weighted tables keyed by tier (rarity, level band): one <see cref="DynamicWeightTable{T}"/> per tier.
    /// </summary>
    /// <typeparam name="T">The type of items in the tiered table.</typeparam>
    public class TieredTable<T> where T : IEquatable<T>
    {
        private readonly Dictionary<int, DynamicWeightTable<T>> _entries = new ();

        /// <summary>
        /// Empty constructor for serialization and manual tier addition.
        /// </summary>
        public TieredTable()
        {
        }

        /// <summary>
        /// Adds a tier, or replaces it if the tier already exists.
        /// </summary>
        /// <param name="tier">The tier key.</param>
        /// <param name="items">The tier's items with their weights. Items must be unique within the tier.</param>
        /// <exception cref="ArgumentException">An item appears twice. An existing tier is left untouched.</exception>
        public void AddTier(int tier, IEnumerable<(T item, float weight)> items)
        {
            var table = new DynamicWeightTable<T>();

            foreach (var (item, weight) in items)
            {
                table.Add(item, weight);
            }

            _entries[tier] = table;
        }

        /// <summary>
        /// Picks <paramref name="drops"/> items from a tier, with replacement. One draw per pick.
        /// </summary>
        /// <param name="rng">The random source.</param>
        /// <param name="tier">The tier to pick from.</param>
        /// <param name="drops">How many items to pick.</param>
        /// <exception cref="ArgumentException">The tier does not exist.</exception>
        /// <exception cref="InvalidOperationException">The tier has nothing pickable.</exception>
        public T[] Sample(IRandom rng, int tier, int drops = 1)
        {
            var table = GetTier(tier);
            var results = new T[drops];

            for (int i = 0; i < drops; i++)
            {
                results[i] = table.Sample(rng);
            }

            return results;
        }

        /// <summary>
        /// Gets a tier's table, for influence, stock and the other per-item settings.
        /// </summary>
        /// <exception cref="ArgumentException">The tier does not exist.</exception>
        public DynamicWeightTable<T> GetTier(int tier)
        {
            if (!_entries.TryGetValue(tier, out var table))
                throw new ArgumentException($"Tier {tier} does not exist in the table.");

            return table;
        }

        /// <summary>
        /// Gets the keys of every tier, in no particular order.
        /// </summary>
        public int[] GetTiers()
        {
            var tiers = new int[_entries.Count];
            _entries.Keys.CopyTo(tiers, 0);
            return tiers;
        }
    }
}
