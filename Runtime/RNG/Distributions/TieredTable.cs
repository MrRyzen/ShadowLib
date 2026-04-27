namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using System.Collections.Generic;
    using System;


    /// <summary>
    /// 
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

        public void AddTier(int tier, IEnumerable<(T item, float weight)> items)
        {
            _entries[tier] = new DynamicWeightTable<T>();

            foreach (var (item, weight) in items)
            {
                _entries[tier].Add(item, weight);
            }
        }

        public T[] Sample(IRandom rng, int tier, int drops = 1)
        {
            if (!_entries.ContainsKey(tier))
                throw new ArgumentException($"Tier {tier} does not exist in the table.");

            var results = new T[drops];

            for (int i = 0; i < drops; i++)
            {
                results[i] = _entries[tier].Sample(rng);
            }

            return results;
        }

        public DynamicWeightTable<T> GetTier(int tier)
        {
            if (!_entries.ContainsKey(tier))
                throw new ArgumentException($"Tier {tier} does not exist in the table.");

            return _entries[tier];
        }

        public int[] GetTiers()
        {
            var tiers = new int[_entries.Count];
            _entries.Keys.CopyTo(tiers, 0);
            return tiers;
        }
    }
}
