namespace ShadowLib.RNG.Distributions;

using ShadowLib.RNG.Sources;
using System;
using System.Collections.Generic;

/// <summary>
/// A dynamic weight table that allows adding, updating, and removing items with associated weights.
/// Maintains separate base and dynamic weights so modifications do not alter the original base weights.
/// </summary>
/// <typeparam name="T">The type of items in the weight table.</typeparam>
/// <remarks>
/// This class while similar to WeightTable it is optimized for frequent weight changes without needing to rebuild the entire table on each change.
/// It uses a dirty flag to track when the cumulative weights need to be recalculated.
/// Base weights represent the original distribution and are preserved across dynamic modifications.
/// Call <see cref="ResetWeights"/> to restore dynamic weights back to their base values.
/// </remarks>
public class DynamicWeightTable<T> where T : IEquatable<T>
{
    private struct Entry
    {
        public T Item;
        public float Weight;
    }

    private readonly List<Entry> _entries;
    private readonly List<Entry> _baseEntries;
    private float _totalWeight;
    private bool _isDirty;

    // Cached cumulative weights for binary search
    private float[] _cumulativeWeights;

    /// <summary>
    /// Gets the number of items in the weight table.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the total dynamic weight of all items in the weight table.
    /// </summary>
    public float TotalWeight
    {
        get
        {
            RebuildIfDirty();
            return _totalWeight;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicWeightTable{T}"/> class with an optional initial capacity.
    /// </summary>
    /// <param name="initialCapacity">Initial capacity for the internal lists.</param>
    public DynamicWeightTable(int initialCapacity = 16)
    {
        _entries = new List<Entry>(initialCapacity);
        _baseEntries = new List<Entry>(initialCapacity);
        _cumulativeWeights = Array.Empty<float>();
        _isDirty = false;
    }

    /// <summary>
    /// Adds an item with the specified weight to the weight table.
    /// The weight is stored as both the base weight and the current dynamic weight.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="weight">The weight associated with the item. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the weight is less than or equal to zero.</exception>
    public void Add(T item, float weight)
    {
        if (weight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be greater than zero.");

        var entry = new Entry { Item = item, Weight = weight };
        _baseEntries.Add(entry);
        _entries.Add(entry);
        _isDirty = true;
    }

    /// <summary>
    /// Returns whether the table contains the specified item.
    /// </summary>
    /// <param name="item">The item to check for.</param>
    /// <returns>True if the item exists in the table; otherwise, false.</returns>
    public bool Contains(T item)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the current dynamic weight of the specified item.
    /// </summary>
    /// <param name="item">The item to look up.</param>
    /// <returns>The current dynamic weight of the item.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the item is not found.</exception>
    public float GetWeight(T item)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
                return _entries[i].Weight;
        }
        throw new KeyNotFoundException($"Item not found in the weight table.");
    }

    /// <summary>
    /// Tries to get the current dynamic weight of the specified item.
    /// </summary>
    /// <param name="item">The item to look up.</param>
    /// <param name="weight">When this method returns, contains the weight if found; otherwise, 0.</param>
    /// <returns>True if the item was found; otherwise, false.</returns>
    public bool TryGetWeight(T item, out float weight)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                weight = _entries[i].Weight;
                return true;
            }
        }
        weight = 0f;
        return false;
    }

    /// <summary>
    /// Gets the base weight of the specified item.
    /// </summary>
    /// <param name="item">The item to look up.</param>
    /// <returns>The base weight of the item.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the item is not found.</exception>
    public float GetBaseWeight(T item)
    {
        for (int i = 0; i < _baseEntries.Count; i++)
        {
            if (_baseEntries[i].Item.Equals(item))
                return _baseEntries[i].Weight;
        }
        throw new KeyNotFoundException($"Item not found in the weight table.");
    }

    /// <summary>
    /// Tries to get the base weight of the specified item.
    /// </summary>
    /// <param name="item">The item to look up.</param>
    /// <param name="weight">When this method returns, contains the base weight if found; otherwise, 0.</param>
    /// <returns>True if the item was found; otherwise, false.</returns>
    public bool TryGetBaseWeight(T item, out float weight)
    {
        for (int i = 0; i < _baseEntries.Count; i++)
        {
            if (_baseEntries[i].Item.Equals(item))
            {
                weight = _baseEntries[i].Weight;
                return true;
            }
        }
        weight = 0f;
        return false;
    }

    /// <summary>
    /// Sets the dynamic weight of an existing item. Does not modify the base weight.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <param name="newWeight">The new dynamic weight for the item. Must be greater than zero.</param>
    /// <returns>True if the item was found and updated; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the new weight is less than or equal to zero.</exception>
    public bool SetWeight(T item, float newWeight)
    {
        if (newWeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(newWeight), "Weight must be greater than zero.");

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                _entries[i] = new Entry { Item = item, Weight = newWeight };
                _isDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sets the base weight of an existing item. Also updates the dynamic weight to match.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <param name="newBaseWeight">The new base weight. Must be greater than zero.</param>
    /// <returns>True if the item was found and updated; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the new weight is less than or equal to zero.</exception>
    public bool SetBaseWeight(T item, float newBaseWeight)
    {
        if (newBaseWeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(newBaseWeight), "Weight must be greater than zero.");

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                _baseEntries[i] = new Entry { Item = item, Weight = newBaseWeight };
                _entries[i] = new Entry { Item = item, Weight = newBaseWeight };
                _isDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds weight to an existing item's dynamic weight. If the item does not exist, it returns false.
    /// Does not modify the base weight.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <param name="weight">The weight to add (can be positive or negative). If resulting weight is &lt;= 0, the item is removed.</param>
    /// <returns>True if the item was found and updated; otherwise, false.</returns>
    /// <remarks>
    /// This method will NOT create new items. If you attempt to add weight to a non-existent item,
    /// it returns false and no change occurs. Use <see cref="Add"/> to create new items.
    /// Note: removing an item via negative weight also removes its base weight entry.
    /// </remarks>
    public bool AddWeight(T item, float weight)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                float newWeight = _entries[i].Weight + weight;
                if (newWeight <= 0f)
                {
                    _entries.RemoveAt(i);
                    _baseEntries.RemoveAt(i);
                }
                else
                {
                    _entries[i] = new Entry { Item = item, Weight = newWeight };
                }
                _isDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Multiplies the dynamic weight of an item by a factor. Does not modify the base weight.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <param name="factor">The factor to multiply the dynamic weight by. Must be greater than zero.</param>
    /// <returns>True if the item was found and updated; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the factor is less than or equal to zero.</exception>
    public bool MultiplyWeight(T item, float factor)
    {
        if (factor <= 0f)
            throw new ArgumentOutOfRangeException(nameof(factor), "Factor must be greater than zero.");

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                float newWeight = _entries[i].Weight * factor;
                _entries[i] = new Entry { Item = item, Weight = newWeight };
                _isDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resets all dynamic weights back to their base weight values.
    /// </summary>
    public void ResetWeights()
    {
        if (_entries.Count == 0) return;

        for (int i = 0; i < _baseEntries.Count; i++)
        {
            _entries[i] = _baseEntries[i];
        }
        _isDirty = true;
    }

    /// <summary>
    /// Sets all dynamic item weights to a uniform value so they sum to 1.0.
    /// Does not modify base weights.
    /// </summary>
    public void UniformWeights()
    {
        if (_entries.Count == 0) return;

        float uniformWeight = 1f / _entries.Count;
        for (int i = 0; i < _entries.Count; i++)
        {
            _entries[i] = new Entry { Item = _entries[i].Item, Weight = uniformWeight };
        }
        _isDirty = true;
    }

    /// <summary>
    /// Normalizes all dynamic weights so they sum to 1.0, preserving their relative proportions.
    /// Does not modify base weights.
    /// </summary>
    public void NormalizeWeights()
    {
        if (_entries.Count == 0) return;

        RebuildIfDirty();

        if (_totalWeight <= 0f) return;

        float invTotal = 1f / _totalWeight;
        for (int i = 0; i < _entries.Count; i++)
        {
            _entries[i] = new Entry { Item = _entries[i].Item, Weight = _entries[i].Weight * invTotal };
        }
        _isDirty = true;
    }

    /// <summary>
    /// Removes the specified item from the weight table (both base and dynamic entries).
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns>True if the item was found and removed; otherwise, false.</returns>
    public bool TryRemove(T item)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Item.Equals(item))
            {
                _entries.RemoveAt(i);
                _baseEntries.RemoveAt(i);
                _isDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Clears all items from the weight table (both base and dynamic) and resets the total weight to zero.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _baseEntries.Clear();
        _totalWeight = 0f;
        _isDirty = false;
    }

    /// <summary>
    /// Selects a random item from the weight table based on their dynamic weights.
    /// </summary>
    /// <param name="rng">The random number generator to use for selection.</param>
    /// <returns>A randomly selected item.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the weight table is empty.</exception>
    public T Sample(IRandom rng)
    {
        if (_entries.Count == 0)
            throw new InvalidOperationException("Table is empty.");

        RebuildIfDirty();

        float target = rng.NextFloat() * _totalWeight;

        // Binary search
        int lo = 0, hi = _entries.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_cumulativeWeights[mid] < target)
                lo = mid + 1;
            else
                hi = mid;
        }

        return _entries[lo].Item;
    }

    private void RebuildIfDirty()
    {
        if (!_isDirty) return;

        if (_cumulativeWeights.Length < _entries.Count)
            _cumulativeWeights = new float[_entries.Count];

        float cumulative = 0f;
        for (int i = 0; i < _entries.Count; i++)
        {
            cumulative += _entries[i].Weight;
            _cumulativeWeights[i] = cumulative;
        }

        _totalWeight = cumulative;
        _isDirty = false;
    }
}
