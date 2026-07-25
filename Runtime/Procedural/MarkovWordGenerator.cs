namespace ShadowLib.Procedural
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Distributions;
    using ShadowLib.RNG.Sources;

    /// <summary>
    /// Character-level order-N Markov word generator for procedural naming (companies, products, people).
    /// </summary>
    /// <remarks>
    /// Training words are padded with start (<c>^</c>) and end (<c>$</c>) markers and sliced into k-grams;
    /// transitions between consecutive k-grams are accumulated in a <see cref="MarkovChain{T}"/>, so generation
    /// steps are O(1) and allocation-free. An order-2 chain predicts the next character from the previous two.
    /// Validation beyond length bounds (banned fragments, duplicate sets, readability) is the caller's concern —
    /// call <see cref="TryGenerate"/> in a retry loop and apply game-side rules to each candidate.
    /// </remarks>
    public sealed class MarkovWordGenerator
    {
        /// <summary>The start-of-word marker used to pad training words.</summary>
        public const char StartMarker = '^';

        /// <summary>The end-of-word marker appended to training words.</summary>
        public const char EndMarker = '$';

        private readonly MarkovChain<string> _chain = new(StringComparer.Ordinal);
        private readonly string _startState;
        private bool _trained;

        /// <summary>
        /// Creates an empty generator.
        /// </summary>
        /// <param name="order">How many previous characters predict the next one. Order 2 or 3 is typical; higher orders copy training words more literally.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="order"/> is less than 1.</exception>
        public MarkovWordGenerator(int order = 2)
        {
            if (order < 1)
                throw new ArgumentOutOfRangeException(nameof(order), "Order must be at least 1");
            Order = order;
            _startState = new string(StartMarker, order);
        }

        /// <summary>
        /// Gets the chain order (number of previous characters used to predict the next).
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Trains the generator on one word: pads it with markers and accumulates every k-gram transition.
        /// Case is preserved — normalize before training if you want case-insensitive output.
        /// </summary>
        /// <param name="word">The training word. Must be non-empty and must not contain <c>^</c> or <c>$</c>.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="word"/> is null, empty, or contains a marker character.</exception>
        public void Train(string word)
        {
            if (string.IsNullOrEmpty(word))
                throw new ArgumentException("Training word must be non-empty", nameof(word));
            if (word.IndexOf(StartMarker) >= 0 || word.IndexOf(EndMarker) >= 0)
                throw new ArgumentException($"Training word must not contain '{StartMarker}' or '{EndMarker}'", nameof(word));

            string padded = _startState + word + EndMarker;
            for (int i = 0; i + Order < padded.Length; i++)
                _chain.AddTransition(padded.Substring(i, Order), padded.Substring(i + 1, Order));
            _trained = true;
        }

        /// <summary>
        /// Trains the generator on a list of words.
        /// </summary>
        /// <param name="words">The training words.</param>
        public void Train(IEnumerable<string> words)
        {
            foreach (var word in words) Train(word);
        }

        /// <summary>
        /// Compiles the underlying chain. Optional — generation compiles lazily — but call this at load
        /// time to keep gameplay frames allocation-free.
        /// </summary>
        public void Build() => _chain.Build();

        /// <summary>
        /// Attempts to generate a word into <paramref name="destination"/>. Allocation-free after <see cref="Build"/>.
        /// </summary>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="destination">The buffer to write into. Its length is the maximum word length.</param>
        /// <param name="length">The number of characters written on success.</param>
        /// <param name="minLength">The minimum acceptable word length. An attempt that ends or dead-ends shorter restarts.</param>
        /// <param name="maxAttempts">How many restarts to allow before giving up.</param>
        /// <returns><c>true</c> when a word within the length bounds was produced; <c>false</c> when attempts were exhausted or the generator is untrained.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minLength"/> exceeds the destination length or is less than 1, or <paramref name="maxAttempts"/> is less than 1.</exception>
        public bool TryGenerate(IRandom rng, Span<char> destination, out int length, int minLength = 4, int maxAttempts = 50)
        {
            if (minLength < 1 || minLength > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(minLength), "minLength must be between 1 and the destination length");
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1");

            length = 0;
            if (!_trained) return false;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string state = _startState;
                int len = 0;
                bool ended = false;

                while (len < destination.Length)
                {
                    if (!_chain.TryNext(state, rng, out string next))
                        break; // dead end — restart

                    char c = next[Order - 1];
                    if (c == EndMarker)
                    {
                        ended = true;
                        break;
                    }

                    destination[len++] = c;
                    state = next;
                }

                // Accept when the chain ended naturally at a valid length, or the buffer
                // filled up past the minimum (truncated but still well-formed).
                if (len >= minLength && (ended || len == destination.Length))
                {
                    length = len;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generates a word as a string. Convenience wrapper over <see cref="TryGenerate"/>; allocates the result.
        /// </summary>
        /// <param name="rng">The random number generator to use.</param>
        /// <param name="minLength">The minimum acceptable word length.</param>
        /// <param name="maxLength">The maximum word length.</param>
        /// <param name="maxAttempts">How many restarts to allow before giving up.</param>
        /// <returns>The generated word.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no word within the bounds was produced after <paramref name="maxAttempts"/> attempts. Use <see cref="TryGenerate"/> to fall back to an authored name instead.</exception>
        public string Generate(IRandom rng, int minLength = 4, int maxLength = 10, int maxAttempts = 50)
        {
            if (maxLength < minLength)
                throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be >= minLength");

            Span<char> buffer = maxLength <= 128 ? stackalloc char[maxLength] : new char[maxLength];
            if (!TryGenerate(rng, buffer, out int length, minLength, maxAttempts))
                throw new InvalidOperationException($"Failed to generate a word of length {minLength}-{maxLength} in {maxAttempts} attempts — train with more data or widen the bounds");
            return new string(buffer[..length]);
        }
    }
}
