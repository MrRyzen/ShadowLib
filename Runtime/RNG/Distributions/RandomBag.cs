namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using ShadowLib.RNG.Utilities;
    using System.Collections.Generic;

    /// <summary>
    /// A random bag that allows adding and removing items with equal probability.
    /// </summary>
    /// <typeparam name="T">The type of items in the bag.</typeparam>
    /// <remarks>
    /// This class provides a way to store items and randomly select them, ensuring that each item has an equal chance of being chosen.
    /// </remarks>
    public class RandomBag<T>
    {
        private readonly List<T> _items;
        private readonly IRandom _rng;

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomBag{T}"/> class.
        /// </summary>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="items">The initial items to add to the bag.</param>
        public RandomBag(IEnumerable<T> items, IRandom rng)
        {
            _items = new List<T>(items);
            _rng = rng;
            RandomUtils.Shuffle(_items, _rng);
        }

        /// <summary>
        /// Adds an item to the bag.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="shuffleAfterAdd">If true, shuffles the bag after adding the item.</param>
        public void Add(T item, bool shuffleAfterAdd = true)
        {
            _items.Add(item);
            if (shuffleAfterAdd)
            {
                RandomUtils.Shuffle(_items, _rng);
            }
        }

        /// <summary>
        /// Removes and returns a random item from the bag.
        /// </summary>
        /// <returns>The randomly selected item.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the bag is empty.</exception>
        public T Sample()
        {
            if (_items.Count == 0)
                throw new System.InvalidOperationException("The bag is empty.");

            T item = _items[0];
            _items.RemoveAt(0);
            return item;
        }

        /// <summary>
        /// Returns a random item from the bag without removing it.
        /// <returns>The randomly selected item.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the bag is empty.</exception>
        public T Peek()
        {
            if (_items.Count == 0)
                throw new System.InvalidOperationException("The bag is empty.");

            return _items[0];
        }

        /// <summary>
        /// Gets the number of items in the bag.
        /// </summary>
        public int Count => _items.Count;
        /// <summary>
        /// Indicates whether the bag has any items.
        /// </summary>
        public bool HasNext => _items.Count > 0;
    }
}
