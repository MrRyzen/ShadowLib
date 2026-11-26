namespace ShadowLib.Procedural
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Sources;
    using ShadowLib.RNG.Utilities;

    /// <summary>Picks random candidates that keep a minimum distance from each other.</summary>
    public static class SpacedPlacement
    {
        /// <summary>
        /// Visits the candidates in a shuffled order and keeps each one that is at least
        /// <paramref name="minDistance"/> from everything kept so far, until <paramref name="count"/> are kept.
        /// Returns fewer when the candidates run out first; a different seed may fit more.
        /// </summary>
        /// <typeparam name="T">The candidate type, usually a position.</typeparam>
        /// <param name="rng">The random source. Draws one shuffle of the candidates, or nothing when there is nothing to place.</param>
        /// <param name="candidates">The positions to choose from. Not modified.</param>
        /// <param name="count">The most candidates to return.</param>
        /// <param name="minDistance">The smallest allowed distance between two results.</param>
        /// <param name="distanceFunc">The distance between two candidates.</param>
        /// <returns>The kept candidates, in the order they were kept.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Count or minimum distance is negative, or the distance is NaN.</exception>
        public static T[] Place<T>(
            IRandom rng, IReadOnlyList<T> candidates, int count, float minDistance, Func<T, T, float> distanceFunc)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (distanceFunc == null)
                throw new ArgumentNullException(nameof(distanceFunc));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            if (!(minDistance >= 0f))
                throw new ArgumentOutOfRangeException(nameof(minDistance), minDistance, "Minimum distance must not be negative.");
            if (count == 0 || candidates.Count == 0)
                return Array.Empty<T>();

            var order = new int[candidates.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            RandomUtils.Shuffle(order, rng);

            // ponytail: greedy with an O(kept) check per candidate; a spatial hash if candidate lists reach tens of thousands
            var kept = new List<T>(Math.Min(count, order.Length));
            for (int i = 0; i < order.Length && kept.Count < count; i++)
            {
                var candidate = candidates[order[i]];
                if (IsClearOf(kept, candidate, minDistance, distanceFunc))
                    kept.Add(candidate);
            }

            return kept.ToArray();
        }

        private static bool IsClearOf<T>(List<T> kept, T candidate, float minDistance, Func<T, T, float> distanceFunc)
        {
            for (int i = 0; i < kept.Count; i++)
            {
                if (distanceFunc(candidate, kept[i]) < minDistance)
                    return false;
            }

            return true;
        }
    }
}
