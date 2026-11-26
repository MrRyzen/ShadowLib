namespace ShadowLib.RNG.Sources
{
    using System;

    /// <summary>
    /// Interface for random number generators.
    /// </summary>
    public interface IRandom
    {
        /// <summary>
        /// Gets the current state of the RNG as a byte array.
        /// This can be used to save the state for later restoration.
        /// </summary>
        /// <returns>A byte array representing the current state of the RNG.</returns>
        byte[] GetState();

        /// <summary>
        /// Sets the state of the RNG from a byte array.
        /// This can be used to restore a previously saved state.
        /// </summary>
        /// <param name="state">A byte array representing the state to restore. Must be in the format returned by GetState.</param>
        void SetState(byte[] state);

        /// <summary>
        /// Sets the state of the RNG from a read-only span of bytes.
        /// This can be used to restore a previously saved state without allocating a new array.
        /// </summary>
        /// <param name="state">A read-only span of bytes representing the state to restore. Must be in the format returned by GetState.</param>
        void SetState(ReadOnlySpan<byte> state);

        /// <summary>
        /// Restarts the generator from a seed. The same seed always gives the same stream.
        /// </summary>
        /// <param name="seed">The seed to expand into the generator state.</param>
        void Seed(ulong seed);

        /// <summary>
        /// Generates the next random unsigned 32-bit integer over its full range.
        /// </summary>
        uint NextUInt();

        /// <summary>
        /// Generates the next random integer over the full <see cref="int"/> range, negatives included.
        /// </summary>
        int NextInt();

        /// <summary>
        /// Generates the next random unsigned 64-bit integer over its full range.
        /// </summary>
        ulong NextULong();

        /// <summary>
        /// Generates a random float in the range [0.0, 1.0).
        /// </summary>
        float NextFloat();

        /// <summary>
        /// Generates a random double in the range [0.0, 1.0).
        /// </summary>
        double NextDouble();

        /// <summary>
        /// Generates a random integer within the specified range [min, max), without modulo bias.
        /// </summary>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Exclusive upper bound.</param>
        int Range(int min, int max);

        /// <summary>
        /// Generates a random float within the specified range [min, max).
        /// </summary>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Exclusive upper bound.</param>
        float Range(float min, float max);

        /// <summary>
        /// Generates a random double within the specified range [min, max).
        /// </summary>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Exclusive upper bound.</param>
        double Range(double min, double max);

        /// <summary>
        /// Generates a random unsigned 64-bit integer within the specified range [min, max), without modulo bias.
        /// </summary>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Exclusive upper bound.</param>
        ulong Range(ulong min, ulong max);
    }
}
