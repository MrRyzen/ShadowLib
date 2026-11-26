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
            public int Units;
            public int MaxUnits;

            public readonly bool HasStock => MaxUnits > 0;
        }

        private readonly List<Entry> _entries;
        private float[] _effective = Array.Empty<float>();
        private int _lastPicked = -1;
        private int _repeatExcluded = -1;
        private float _repeatExcludedWeight;

        /// <summary>
        /// Gets or sets whether the last picked item is kept out of the next pick.
        /// It stays pickable when it is the only pickable item.
        /// </summary>
        public bool PreventRepeat { get; set; }

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
            if (index == _lastPicked) _lastPicked = -1;
            else if (index < _lastPicked) _lastPicked--;
            return true;
        }

        /// <summary>Removes every item.</summary>
        public void Clear()
        {
            _entries.Clear();
            _lastPicked = -1;
        }

        /// <summary>Gets the most recently picked item. Tracked whether or not <see cref="PreventRepeat"/> is on.</summary>
        /// <returns>False if nothing has been picked, or the last pick was removed or cleared.</returns>
        public bool TryGetLastPicked(out T item)
        {
            item = _lastPicked >= 0 ? _entries[_lastPicked].Item : default!;
            return _lastPicked >= 0;
        }

        /// <summary>Sets the last picked item, for restoring saved state.</summary>
        /// <returns>True if the item was found.</returns>
        public bool SetLastPicked(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;

            _lastPicked = index;
            return true;
        }

        /// <summary>Forgets the last picked item.</summary>
        public void ClearLastPicked()
        {
            _lastPicked = -1;
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

        /// <summary>Gives an item a limited stock, full at <paramref name="units"/>. Each pick spends one unit; at zero the item is unpickable.</summary>
        /// <returns>True if the item was found.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="units"/> is less than one.</exception>
        public bool SetStock(T item, int units) => SetStock(item, units, units);

        /// <summary>Gives an item a limited stock with separate current and full amounts, for restoring saved state.</summary>
        /// <param name="item">The item to stock.</param>
        /// <param name="units">Units left now.</param>
        /// <param name="maxUnits">Units after a refill.</param>
        /// <returns>True if the item was found.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Breaks maxUnits &gt;= 1 or 0 &lt;= units &lt;= maxUnits.</exception>
        public bool SetStock(T item, int units, int maxUnits)
        {
            if (maxUnits < 1)
                throw new ArgumentOutOfRangeException(nameof(maxUnits), "Max units must be at least one.");
            if (units < 0 || units > maxUnits)
                throw new ArgumentOutOfRangeException(nameof(units), "Units must be between zero and max units.");

            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            entry.Units = units;
            entry.MaxUnits = maxUnits;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Makes an item's stock unlimited again.</summary>
        /// <returns>True if the item was found.</returns>
        public bool ClearStock(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;

            Entry entry = _entries[index];
            entry.Units = 0;
            entry.MaxUnits = 0;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Gets the units an item has left.</summary>
        /// <returns>False if the item is missing or its stock is unlimited.</returns>
        public bool TryGetStock(T item, out int units)
        {
            int index = IndexOf(item);
            bool hasStock = index >= 0 && _entries[index].HasStock;
            units = hasStock ? _entries[index].Units : 0;
            return hasStock;
        }

        /// <summary>Restores an item's stock to full.</summary>
        /// <returns>False if the item is missing or its stock is unlimited.</returns>
        public bool Refill(T item)
        {
            int index = IndexOf(item);
            if (index < 0 || !_entries[index].HasStock) return false;

            Entry entry = _entries[index];
            entry.Units = entry.MaxUnits;
            _entries[index] = entry;
            return true;
        }

        /// <summary>Restores every stocked item to full.</summary>
        public void RefillAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                entry.Units = entry.MaxUnits;
                _entries[i] = entry;
            }
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

            int index = PickIndex(rng, total);
            item = _entries[index].Item;
            CommitPick(index);
            return true;
        }

        /// <summary>
        /// Picks up to <c>results.Length</c> different items by effective weight, one draw per item.
        /// Influence sources are read once for the whole call, and <see cref="PreventRepeat"/> only
        /// constrains the first item.
        /// </summary>
        /// <param name="rng">The random number generator to draw from.</param>
        /// <param name="results">Receives the picked items, in pick order.</param>
        /// <returns>How many items were written; fewer than requested when the pickable items run out.</returns>
        public int SampleDistinct(IRandom rng, Span<T> results)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (results.IsEmpty) return 0;

            float total = ComputeEffectiveWeights();
            int written = 0;
            while (written < results.Length && total > 0f)
            {
                int index = PickIndex(rng, total);
                results[written++] = _entries[index].Item;
                CommitPick(index);

                if (written == 1 && _repeatExcluded >= 0)
                    _effective[_repeatExcluded] = _repeatExcludedWeight;
                _effective[index] = 0f;
                total = SumEffectiveWeights();
            }
            return written;
        }

        private void CommitPick(int index)
        {
            _lastPicked = index;
            Entry entry = _entries[index];
            if (!entry.HasStock) return;

            entry.Units--;
            _entries[index] = entry;
        }

        private float ComputeEffectiveWeights()
        {
            if (_effective.Length < _entries.Count)
                _effective = new float[_entries.Count];

            for (int i = 0; i < _entries.Count; i++)
                _effective[i] = EffectiveWeight(_entries[i]);

            _repeatExcluded = -1;
            if (PreventRepeat && _lastPicked >= 0 && AnyPickableExcept(_lastPicked))
            {
                _repeatExcluded = _lastPicked;
                _repeatExcludedWeight = _effective[_lastPicked];
                _effective[_lastPicked] = 0f;
            }

            return SumEffectiveWeights();
        }

        private bool AnyPickableExcept(int excluded)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i != excluded && _effective[i] > 0f)
                    return true;
            }
            return false;
        }

        private float SumEffectiveWeights()
        {
            float total = 0f;
            for (int i = 0; i < _entries.Count; i++)
                total += _effective[i];
            return total;
        }

        private static float EffectiveWeight(in Entry entry)
        {
            if (!entry.Enabled || (entry.HasStock && entry.Units == 0)) return 0f;
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
