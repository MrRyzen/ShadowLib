namespace ShadowLib.RNG.Utilities
{
    using ShadowLib.RNG.Sources;
    using System.Collections.Generic;

    /// <summary>
    /// Utility methods for random number generation.
    /// </summary>
    /// <remarks>
    /// This class provides static methods for generating random numbers, shuffling collections, and selecting random items.
    /// It uses an underlying random number generator that can be seeded for reproducibility.
    /// </remarks>
    public static class RandomUtils
    {
        /// <summary>
        /// Shuffles the specified array in place using the provided random number generator.
        /// </summary>
        /// <typeparam name="T">The type of elements in the array.</typeparam>
        /// <param name="array">The array to shuffle.</param>
        /// <param name="rng">The random number generator to use.</param>
        public static void Shuffle<T>(T[] array, IRandom rng)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (array[j], array[i]) = (array[i], array[j]);
            }
        }

        /// <summary>
        /// Shuffles the specified list in place using the provided random number generator.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to shuffle.</param>
        /// <param name="rng">The random number generator to use.</param>
        public static void Shuffle<T>(List<T> list, IRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[j], list[i]) = (list[i], list[j]);
            }
        }

        /// <summary>
        /// Selects a random item from the specified list using the provided random number generator.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="items">The list of items to select from.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <returns>A randomly selected item from the list.</returns>
        /// <exception cref="System.ArgumentException">Thrown when the list is null or empty.</exception>
        /// <remarks>
        /// This method throws an exception if the list is null or empty.
        /// </remarks>
        public static T RandomSelect<T>(List<T> items, IRandom rng)
        {
            if (items == null || items.Count == 0)
            {
                throw new System.ArgumentException("The list cannot be null or empty.");
            }

            int index = rng.Range(0, items.Count);
            return items[index];
        }
        /// <summary>
        /// Determines whether an event occurs based on the specified probability using the provided random number generator.
        /// </summary>
        /// <param name="probability">The probability of the event occurring, between 0.0 and 1.0.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <returns>True if the event occurs; otherwise, false.</returns>
        public static bool Chance(float probability, IRandom rng)
        {
            return rng.Range(0, 10000) < (int)(probability * 10000);
        }
    }
}
