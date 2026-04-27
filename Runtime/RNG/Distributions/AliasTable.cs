namespace ShadowLib.RNG.Distributions;
using System;
using System.Collections.Generic;
using System.Linq;
using ShadowLib.RNG.Sources;

/// <summary>
/// Implements the alias method for efficient weighted random sampling.
/// </summary>
/// <typeparam name="T">The type of items in the alias table.</typeparam>
/// <remarks>
/// The alias method allows for O(1) sampling time after O(n) preprocessing time. It is particularly efficient for large distributions where many samples are needed.
/// </remarks>
public class AliasTable<T>
{
    private readonly T[] _items;
    private readonly float[] _probabilities;
    private readonly int[] _aliases;
    private readonly int _count;

    /// <summary>
    /// Build alias table from items and weights.
    /// Construction is O(n), but selection is O(1).
    /// </summary>
    /// <param name="items">The items to sample from.</param>
    /// <param name="weights">The weights associated with each item. Must be non-negative and not all zero.</param>
    /// <exception cref="ArgumentException">Thrown when items and weights counts do not match, or when weights are invalid.</exception>
    public AliasTable(IReadOnlyList<T> items, IReadOnlyList<float> weights)
    {
        if (items.Count != weights.Count)
            throw new ArgumentException("Items and weights must have same count");
        if (items.Count == 0)
            throw new ArgumentException("Must have at least one item");

        _count = items.Count;
        _items = items.ToArray();
        _probabilities = new float[_count];
        _aliases = new int[_count];

        BuildTable(weights);
    }

    private void BuildTable(IReadOnlyList<float> weights)
    {
        float totalWeight = 0f;
        foreach (var w in weights) totalWeight += w;

        // Scale probabilities so average = 1
        float scale = _count / totalWeight;
        var scaledProbs = new float[_count];
        for (int i = 0; i < _count; i++)
            scaledProbs[i] = weights[i] * scale;

        // Partition into small (< 1) and large (>= 1)
        var small = new Stack<int>(_count);
        var large = new Stack<int>(_count);

        for (int i = 0; i < _count; i++)
        {
            if (scaledProbs[i] < 1f)
                small.Push(i);
            else
                large.Push(i);
        }

        // Build alias table
        while (small.Count > 0 && large.Count > 0)
        {
            int s = small.Pop();
            int l = large.Pop();

            _probabilities[s] = scaledProbs[s];
            _aliases[s] = l;

            scaledProbs[l] = scaledProbs[l] + scaledProbs[s] - 1f;

            if (scaledProbs[l] < 1f)
                small.Push(l);
            else
                large.Push(l);
        }

        // Handle remaining (should be ~1.0 due to floating point)
        while (large.Count > 0)
            _probabilities[large.Pop()] = 1f;
        while (small.Count > 0)
            _probabilities[small.Pop()] = 1f;
    }

    /// <summary>
    /// O(1) weighted random selection.
    /// </summary>
    /// <param name="rng">The random number generator to use for selection.</param>
    /// <returns>A randomly selected item.</returns>
    public T Sample(IRandom rng)
    {
        int i = rng.Range(0, _count);
        return rng.NextFloat() < _probabilities[i] ? _items[i] : _items[_aliases[i]];
    }

    /// <summary>
    /// Batch selection, very efficient for large counts.
    /// </summary>
    /// <param name="rng">The random number generator to use for selection.</param>
    /// <param name="results">A span to store the results. Must be pre-allocated.</param>
    public void SampleMultiple(IRandom rng, Span<T> results)
    {
        for (int j = 0; j < results.Length; j++)
        {
            int i = rng.Range(0, _count);
            results[j] = rng.NextFloat() < _probabilities[i] ? _items[i] : _items[_aliases[i]];
        }
    }
}