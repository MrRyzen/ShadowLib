namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Weighted sampling table. Each item has a designed weight that is never changed by sampling;
    /// what a pick actually uses is the item's effective weight, computed at pick time.
    /// </summary>
    /// <typeparam name="T">The type of items in the table.</typeparam>
    /// <remarks>
    /// Every pick consumes exactly one <see cref="IRandom.NextFloat"/> draw, and none when nothing is pickable.
    /// Not thread-safe.
    /// </remarks>
    public class DynamicWeightTable<T> where T : IEquatable<T>
    {
        private struct Entry
        {
            public T Item;
            public float Weight;
            public bool Enabled;
            public Func<float>? InfluenceSource;
            public float MinWeight;
            public float MaxWeight;
        }

        private readonly List<Entry> _entries;
        private float[] _effective = Array.Empty<float>();

        /// <summary>Initializes an empty table.</summary>
        /// <param name="initialCapacity">Initial capacity of the item list.</param>
        public DynamicWeightTable(int initialCapacity = 16)
        {
            _entries = new List<Entry>(initialCapacity);
        }

        /// <summary>Gets the number of items in the table, pickable or not.</summary>
        public int Count => _entries.Count;

        /// <summary>Gets the sum of the effective weights a pick would use right now.</summary>
        public float TotalWeight => ComputeEffectiveWeights();

        /// <summary>Adds an item with its designed weight.</summary>
        /// <param name="item">The item to add.</param>
        /// <param name="weight">The designed weight. Must be finite and greater than zero.</param>
        /// <exception cref="ArgumentOutOfRangeException">The weight is not finite or not greater than zero.</exception>
        /// <exception cref="ArgumentException">The item is already in the table.</exception>
        public void Add(T item, float weight)
        {
            RequirePositiveFinite(weight, nameof(weight));
            if (IndexOf(item) >= 0)
                throw new ArgumentException("Item is already in the table.", nameof(item));

            _entries.Add(new Entry { Item = item, Weight = weight, Enabled = true });
        }

        /// <summary>Removes an item and everything configured for it.</summary>
        /// <returns>True if the item was found.</returns>
        public bool TryRemove(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;

            _entries.RemoveAt(index);
            return true;
        }

        /// <summary>Removes every item.</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>Returns whether the table contains the item.</summary>
        public bool Contains(T item) => IndexOf(item) >= 0;

        /// <summary>Gets the designed weight of an item.</summary>
        /// <exception cref="KeyNotFoundException">The item is not in the table.</exception>
        public float GetWeight(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
                throw new KeyNotFoundException("Item not found in the weight table.");
            return _entries[index].Weight;
        }

        /// <summary>Tries to get the designed weight of an item.</summary>
        /// <returns>True if the item was found.</returns>
        public bool TryGetWeight(T item, out float weight)
        {
            int index = IndexOf(item);
            weight = index < 0 ? 0f : _entries[index].Weight;
            return index >= 0;
        }

        /// <summary>Sets the designed weight of an item.</summary>
        /// <param name="item">The item to update.</param>
        /// <param name="weight">The new designed weight. Must be finite and greater than zero.</param>
        /// <returns>True if the item was found.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The weight is not finite, not greater than zero, or outside the item's influence range.
        /// </exception>
        public bool SetWeight(T item, float weight)
        {
            RequirePositiveFinite(weight, nameof(weight));
            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            if (entry.InfluenceSource != null && (weight < entry.MinWeight || weight > entry.MaxWeight))
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight must stay inside the item's influence range.");

            entry.Weight = weight;
            _entries[index] = entry;
            return true;
        }

        /// <summary>
        /// Lets an external value move an item's weight between <paramref name="min"/> and <paramref name="max"/>.
        /// The source is read on every pick: -1 gives <paramref name="min"/>, 0 the designed weight, +1 <paramref name="max"/>,
        /// linear in between. Values outside -1..1 are clamped and NaN counts as 0.
        /// </summary>
        /// <param name="item">The item to influence.</param>
        /// <param name="min">Weight at influence -1. May be zero, which makes the item unpickable there.</param>
        /// <param name="max">Weight at influence +1.</param>
        /// <param name="source">Returns the current influence. Must not call back into this table.</param>
        /// <returns>True if the item was found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The range is not finite or breaks 0 &lt;= min &lt;= designed weight &lt;= max.</exception>
        public bool SetInfluence(T item, float min, float max, Func<float> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            if (!(min >= 0f && min <= entry.Weight))
                throw new ArgumentOutOfRangeException(nameof(min), "Min must be between zero and the designed weight.");
            if (!(max >= entry.Weight) || float.IsInfinity(max))
                throw new ArgumentOutOfRangeException(nameof(max), "Max must be finite and at least the designed weight.");

            entry.InfluenceSource = source;
            entry.MinWeight = min;
            entry.MaxWeight = max;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Removes an item's influence, leaving its designed weight.</summary>
        /// <returns>True if the item was found.</returns>
        public bool ClearInfluence(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            entry.InfluenceSource = null;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Enables or disables an item. A disabled item stays in the table but is never picked.</summary>
        /// <returns>True if the item was found.</returns>
        public bool SetEnabled(T item, bool enabled)
        {
            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            entry.Enabled = enabled;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Returns whether an item is enabled. False if the item is not in the table.</summary>
        public bool IsEnabled(T item)
        {
            int index = IndexOf(item);
            return index >= 0 && _entries[index].Enabled;
        }

        /// <summary>Gets the chance, from 0 to 1, that the next <see cref="Sample"/> returns the item.</summary>
        /// <returns>The probability, or 0 if the item is missing or nothing is pickable.</returns>
        public float GetProbability(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return 0f;

            float total = ComputeEffectiveWeights();
            return total > 0f ? _effective[index] / total : 0f;
        }

        /// <summary>Picks an item by effective weight.</summary>
        /// <param name="rng">The random number generator to draw from.</param>
        /// <exception cref="InvalidOperationException">Nothing is pickable.</exception>
        public T Sample(IRandom rng)
        {
            if (!TrySample(rng, out T item))
                throw new InvalidOperationException("Nothing in the table is pickable.");
            return item;
        }

        /// <summary>Picks an item by effective weight, without throwing when nothing is pickable.</summary>
        /// <param name="rng">The random number generator to draw from.</param>
        /// <param name="item">The picked item, or default when nothing is pickable.</param>
        /// <returns>True if an item was picked. When false, no random number was drawn.</returns>
        public bool TrySample(IRandom rng, out T item)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            float total = ComputeEffectiveWeights();
            if (total <= 0f)
            {
                item = default!;
                return false;
            }

            item = _entries[PickIndex(rng, total)].Item;
            return true;
        }

        private float ComputeEffectiveWeights()
        {
            if (_effective.Length < _entries.Count)
                _effective = new float[_entries.Count];

            float total = 0f;
            for (int i = 0; i < _entries.Count; i++)
            {
                float weight = EffectiveWeight(_entries[i]);
                _effective[i] = weight;
                total += weight;
            }
            return total;
        }

        private static float EffectiveWeight(in Entry entry)
        {
            if (!entry.Enabled) return 0f;
            if (entry.InfluenceSource == null) return entry.Weight;

            float influence = entry.InfluenceSource();
            if (float.IsNaN(influence)) return entry.Weight;

            return influence < 0f
                ? Lerp(entry.Weight, entry.MinWeight, Math.Min(-influence, 1f))
                : Lerp(entry.Weight, entry.MaxWeight, Math.Min(influence, 1f));
        }

        private static float Lerp(float from, float to, float t)
        {
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            return from + (to - from) * t;
        }

        private int PickIndex(IRandom rng, float total)
        {
            float target = rng.NextFloat() * total;
            float running = 0f;
            int lastPickable = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_effective[i] <= 0f) continue;

                running += _effective[i];
                lastPickable = i;
                if (running >= target) return i;
            }
            return lastPickable;
        }

        private int IndexOf(T item)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Item.Equals(item))
                    return i;
            }
            return -1;
        }

        private static void RequirePositiveFinite(float value, string paramName)
        {
            if (!(value > 0f) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(paramName, "Weight must be finite and greater than zero.");
        }
    }
}
