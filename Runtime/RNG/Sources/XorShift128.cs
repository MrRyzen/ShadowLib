namespace ShadowLib.RNG.Sources
{
    using ShadowLib.RNG.Utilities;
    using System;

    /// <summary>
    /// XorShift128 random number generator implementation.
    /// </summary>
    /// <remarks>
    /// This is a simple and fast pseudorandom number generator based on the XorShift algorithm.
    /// It is not suitable for cryptographic purposes.
    /// </remarks>
    public sealed class XorShift128
    {
        private uint x, y, z, w;

        // Constants for generating floats and doubles
        private const double IVN_53BIT = 1.0 / (1UL << 53);
        private const float INV_24BIT = 1f / (1 << 24);

        /// <summary>
        /// Initializes a new instance of the <see cref="XorShift128"/> class with the specified seed.
        /// </summary>
        /// <param name="seed"></param>
        public XorShift128(uint seed)
        {
            if (seed == 0)
                seed = 0x6D2B79F5;

            x = seed;
            y = seed * 1812433253u + 1;
            z = y * 1812433253u + 1;
            w = z * 1812433253u + 1;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="XorShift128"/> class with a random seed.
        /// </summary>
        public XorShift128()
        {
            uint seed = Seeding.GenerateSeed();
            if (seed == 0)
                seed = 0x6D2B79F5;
            x = seed;
            y = seed * 1812433253u + 1;
            z = y * 1812433253u + 1;
            w = z * 1812433253u + 1;
        }

        /// <summary>
        /// Generates a random integer within the specified range [min, max).
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int Range(int min, int max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            uint range = (uint)(max - min);
            uint limit = uint.MaxValue - (uint.MaxValue % range);

            uint r;
            do
            {
                r = (uint)NextInt();
            }
            while (r >= limit);

            return min + (int)(r % range);
        }

        /// <summary>
        /// Generates a random float within the specified range [min, max).
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public float Range(float min, float max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            return NextFloat() * (max - min) + min;
        }
        /// <summary>
        /// Generates a random double within the specified range [min, max).
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public double Range(double min, double max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            return NextDouble() * (max - min) + min;
        }

        ///<summary>
        /// Generates a random double in the range [0.0, 1.0).
        /// </summary>
        /// <returns></returns>
        public double NextDouble()
        {
            ulong a = (ulong)(uint)NextInt(); // 32 bits
            ulong b = (ulong)(uint)NextInt(); // 32 bits

            // Combine to 53 bits
            ulong value = (a << 21) ^ (b >> 11);

            return value * IVN_53BIT;
        }

        /// <summary>
        ///  Generates a random float in the range [0.0, 1.0).
        /// </summary>
        /// <returns></returns>
        public float NextFloat()
            => ((uint)NextInt() >> 8) * INV_24BIT;

        /// <summary>
        /// Generates the next random integer.
        /// </summary>
        /// <returns></returns>
        public int NextInt()
        {
            uint t = x ^ (x << 11);
            x = y;
            y = z;
            z = w;
            w ^= (w >> 19) ^ t ^ (t >> 8);

            // Scramble output (xorshift*)
            return (int)(w * 0x9E3779B9u);
        }
    }
}
