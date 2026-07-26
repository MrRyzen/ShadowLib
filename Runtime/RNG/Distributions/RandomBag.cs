namespace ShadowLib.RNG.Distributions
{
    using ShadowLib.RNG.Sources;
    using ShadowLib.RNG.Utilities;
    using System.Collections.Generic;

    /// <summary>
    /// A shuffled draw-without-replacement bag: every item is drawn exactly once per cycle, then the bag
    /// reshuffles its full contents and starts a new cycle (Tetris-style 7-bag).
    /// </summary>
    /// <typeparam name="T">The type of items in the bag.</typeparam>
    /// <remarks>
    /// With <see cref="AutoRefill"/> enabled (the default), the bag never runs dry — exhausting a cycle
    /// reshuffles all items and continues. Disable it to drain the bag once and throw when empty.
    /// </remarks>
    public class RandomBag<T>
    {
        private readonly List<T> _pool;
        private readonly List<T> _items;
        private readonly IRandom _rng;

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomBag{T}"/> class.
        /// </summary>
        /// <param name="items">The initial items to add to the bag.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="autoRefill">When true (default), exhausting the bag reshuffles its full contents and starts a new cycle. When false, the bag drains once and <see cref="Sample"/> throws when empty.</param>
        public RandomBag(IEnumerable<T> items, IRandom rng, bool autoRefill = true)
        {
            _pool = new List<T>(items);
            _items = new List<T>(_pool);
            _rng = rng;
            AutoRefill = autoRefill;
            RandomUtils.Shuffle(_items, _rng);
        }

        /// <summary>
        /// Gets whether the bag reshuffles its full contents and starts a new cycle when exhausted.
        /// </summary>
        public bool AutoRefill { get; }

        /// <summary>
        /// Adds an item to the bag. It joins both the current cycle and every future refill.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="shuffleAfterAdd">If true, shuffles the current cycle after adding the item.</param>
        public void Add(T item, bool shuffleAfterAdd = true)
        {
            _pool.Add(item);
            _items.Add(item);
            if (shuffleAfterAdd)
            {
                RandomUtils.Shuffle(_items, _rng);
            }
        }

        /// <summary>
        /// Removes and returns a random item from the bag. When the current cycle is exhausted and
        /// <see cref="AutoRefill"/> is enabled, the bag reshuffles its full contents first.
        /// </summary>
        /// <returns>The randomly selected item.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the bag is empty and cannot refill.</exception>
        public T Sample()
        {
            RefillIfNeeded();
            if (_items.Count == 0)
                throw new System.InvalidOperationException("The bag is empty.");

            T item = _items[0];
            _items.RemoveAt(0);
            return item;
        }

        /// <summary>
        /// Returns the next item without removing it, refilling first if needed (see <see cref="Sample"/>).
        /// </summary>
        /// <returns>The randomly selected item.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the bag is empty and cannot refill.</exception>
        public T Peek()
        {
            RefillIfNeeded();
            if (_items.Count == 0)
                throw new System.InvalidOperationException("The bag is empty.");

            return _items[0];
        }

        /// <summary>
        /// Discards the current cycle and starts a fresh one containing every item, reshuffled.
        /// Useful for a manual reset regardless of <see cref="AutoRefill"/>.
        /// </summary>
        public void Refill()
        {
            _items.Clear();
            _items.AddRange(_pool);
            RandomUtils.Shuffle(_items, _rng);
        }

        private void RefillIfNeeded()
        {
            if (AutoRefill && _items.Count == 0 && _pool.Count > 0)
                Refill();
        }

        /// <summary>
        /// Gets the number of items remaining in the current cycle.
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Indicates whether another draw will succeed (remaining items in this cycle, or a refill is available).
        /// </summary>
        public bool HasNext => _items.Count > 0 || (AutoRefill && _pool.Count > 0);
    }
}
