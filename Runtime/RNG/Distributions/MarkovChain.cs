namespace ShadowLib.RNG.Distributions
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Sources;

    /// <summary>
    /// First-order Markov chain with weighted transitions and O(1) stepping.
    /// </summary>
    /// <typeparam name="T">The state type. For higher-order chains, use a composite state (e.g. a tuple of the last N states).</typeparam>
    /// <remarks>
    /// Transitions are accumulated during a mutable build phase (<see cref="AddTransition"/> / <see cref="AddSequence"/>),
    /// then compiled into a flat CSR layout with a per-row alias table (Vose's method). After compilation, stepping is O(1)
    /// per transition and allocation-free — suitable for per-frame use. Compilation happens lazily on the first step after
    /// a mutation, or eagerly via <see cref="Build"/>.
    /// </remarks>
    public sealed class MarkovChain<T>
    {
        private readonly Dictionary<T, int> _stateIndex;
        private readonly List<T> _stateList;
        private readonly List<Dictionary<int, float>> _outgoing;
        private bool _dirty = true;

        // Compiled representation: CSR rows over per-slot alias tables.
        private T[] _states = Array.Empty<T>();
        private int[] _rowStart = Array.Empty<int>();
        private float[] _prob = Array.Empty<float>();
        private int[] _target = Array.Empty<int>();
        private int[] _aliasTarget = Array.Empty<int>();

        /// <summary>
        /// Creates an empty Markov chain.
        /// </summary>
        /// <param name="comparer">Optional equality comparer for states. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public MarkovChain(IEqualityComparer<T>? comparer = null)
        {
            _stateIndex = new Dictionary<T, int>(comparer ?? EqualityComparer<T>.Default);
            _stateList = new List<T>();
            _outgoing = new List<Dictionary<int, float>>();
        }

        /// <summary>
        /// Gets the number of distinct states seen so far (including terminal states that only appear as targets).
        /// </summary>
        public int StateCount => _stateList.Count;

        /// <summary>
        /// Adds weight to the transition <paramref name="from"/> → <paramref name="to"/>.
        /// Repeated calls for the same pair accumulate.
        /// </summary>
        /// <param name="from">The source state.</param>
        /// <param name="to">The destination state.</param>
        /// <param name="weight">The weight to add. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="weight"/> is not positive and finite.</exception>
        public void AddTransition(T from, T to, float weight = 1f)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive and finite");

            int fromIdx = GetOrAddState(from);
            int toIdx = GetOrAddState(to);

            var row = _outgoing[fromIdx];
            row.TryGetValue(toIdx, out float existing);
            row[toIdx] = existing + weight;
            _dirty = true;
        }

        /// <summary>
        /// Trains the chain from an observed sequence: every adjacent pair adds <paramref name="weight"/> to its transition.
        /// </summary>
        /// <param name="sequence">The observed state sequence.</param>
        /// <param name="weight">The weight each observed pair contributes. Must be positive and finite.</param>
        public void AddSequence(ReadOnlySpan<T> sequence, float weight = 1f)
        {
            for (int i = 1; i < sequence.Length; i++)
                AddTransition(sequence[i - 1], sequence[i], weight);
        }

        /// <summary>
        /// Compiles the accumulated transitions into the O(1) sampling structure.
        /// Optional — stepping compiles lazily — but call this at load time to keep gameplay frames allocation-free.
        /// </summary>
        public void Build()
        {
            int stateCount = _stateList.Count;
            _states = _stateList.ToArray();
            _rowStart = new int[stateCount + 1];

            int total = 0, maxRow = 0;
            for (int i = 0; i < stateCount; i++)
            {
                _rowStart[i] = total;
                int n = _outgoing[i].Count;
                total += n;
                if (n > maxRow) maxRow = n;
            }
            _rowStart[stateCount] = total;

            _prob = new float[total];
            _target = new int[total];
            _aliasTarget = new int[total];

            // Build-time scratch; sampling itself never allocates.
            var keys = new int[maxRow];
            var scaled = new float[maxRow];
            var small = new int[maxRow];
            var large = new int[maxRow];

            for (int i = 0; i < stateCount; i++)
                BuildRow(i, keys, scaled, small, large);

            _dirty = false;
        }

        private void BuildRow(int state, int[] keys, float[] scaled, int[] small, int[] large)
        {
            var row = _outgoing[state];
            int n = row.Count;
            if (n == 0) return;

            int start = _rowStart[state];

            // Sort targets by state index so the compiled layout is deterministic
            // regardless of dictionary enumeration order.
            int k = 0;
            foreach (var kvp in row) keys[k++] = kvp.Key;
            Array.Sort(keys, 0, n);

            float totalWeight = 0f;
            for (int j = 0; j < n; j++)
            {
                _target[start + j] = keys[j];
                totalWeight += row[keys[j]];
            }

            // Vose's alias method per row: scale so the average probability is 1,
            // then pair each below-average slot with an above-average donor.
            float scale = n / totalWeight;
            int smallCount = 0, largeCount = 0;
            for (int j = 0; j < n; j++)
            {
                scaled[j] = row[keys[j]] * scale;
                if (scaled[j] < 1f) small[smallCount++] = j;
                else large[largeCount++] = j;
            }

            while (smallCount > 0 && largeCount > 0)
            {
                int s = small[--smallCount];
                int l = large[--largeCount];

                _prob[start + s] = scaled[s];
                _aliasTarget[start + s] = _target[start + l];

                scaled[l] = scaled[l] + scaled[s] - 1f;
                if (scaled[l] < 1f) small[smallCount++] = l;
                else large[largeCount++] = l;
            }

            while (largeCount > 0)
            {
                int l = large[--largeCount];
                _prob[start + l] = 1f;
                _aliasTarget[start + l] = _target[start + l];
            }
            while (smallCount > 0)
            {
                int s = small[--smallCount];
                _prob[start + s] = 1f;
                _aliasTarget[start + s] = _target[start + s];
            }
        }

        /// <summary>
        /// Attempts one O(1), allocation-free step from <paramref name="current"/>.
        /// </summary>
        /// <param name="current">The current state.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="next">The sampled next state, or <c>default</c> when stepping is impossible.</param>
        /// <returns><c>false</c> when <paramref name="current"/> is unknown or terminal (no outgoing transitions); otherwise <c>true</c>.</returns>
        public bool TryNext(T current, IRandom rng, out T next)
        {
            if (_dirty) Build();

            if (!_stateIndex.TryGetValue(current, out int i))
            {
                next = default!;
                return false;
            }

            int start = _rowStart[i];
            int n = _rowStart[i + 1] - start;
            if (n == 0)
            {
                next = default!;
                return false;
            }

            int slot = start + rng.Range(0, n);
            int t = rng.NextFloat() < _prob[slot] ? _target[slot] : _aliasTarget[slot];
            next = _states[t];
            return true;
        }

        /// <summary>
        /// Performs one O(1), allocation-free step from <paramref name="current"/>.
        /// </summary>
        /// <param name="current">The current state.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <returns>The sampled next state.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="current"/> is unknown or terminal. Use <see cref="TryNext"/> to probe.</exception>
        public T Next(T current, IRandom rng)
        {
            if (!TryNext(current, rng, out T next))
                throw new InvalidOperationException($"State '{current}' is unknown or has no outgoing transitions");
            return next;
        }

        /// <summary>
        /// Walks the chain from <paramref name="start"/>, writing states (including <paramref name="start"/>)
        /// into <paramref name="destination"/>. Allocation-free.
        /// </summary>
        /// <param name="start">The starting state. Must be a known state.</param>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="destination">The span to fill. The walk length is capped by its length.</param>
        /// <returns>The number of states written. Less than the span length when a terminal state is reached.</returns>
        public int Walk(T start, IRandom rng, Span<T> destination)
        {
            if (destination.Length == 0) return 0;

            destination[0] = start;
            int count = 1;
            T current = start;
            while (count < destination.Length && TryNext(current, rng, out T next))
            {
                destination[count++] = next;
                current = next;
            }
            return count;
        }

        private int GetOrAddState(T state)
        {
            if (_stateIndex.TryGetValue(state, out int idx))
                return idx;

            idx = _stateList.Count;
            _stateIndex.Add(state, idx);
            _stateList.Add(state);
            _outgoing.Add(new Dictionary<int, float>());
            return idx;
        }
    }
}
