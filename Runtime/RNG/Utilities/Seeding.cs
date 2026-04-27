namespace ShadowLib.RNG.Utilities
{
    using System;
    using System.Buffers.Binary;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Utility class for generating seeds from strings.
    /// </summary>
    public static class Seeding
    {
        #region 32-bit Seed Generation
        /// <summary>
        /// Generates a high-entropy random seed.
        /// Suitable for initializing deterministic RNG streams.
        /// </summary>
        /// <returns>A high-entropy random seed.</returns>
        public static uint GenerateSeed()
        {
            Span<byte> buffer = stackalloc byte[4];
            RandomNumberGenerator.Fill(buffer);

            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        /// <summary>
        /// Generates a high-entropy random seed, Using input string as additional entropy.
        /// Used for user-provided seed strings. Think minecraft seeds.
        /// Suitable for initializing deterministic RNG streams.
        /// </summary>
        /// <param name="input">The input string to derive the seed from.</param>
        /// <returns>A high-entropy random seed derived from the input string.</returns>
        public static uint GenerateSeed(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Seed input string cannot be null or empty.", nameof(input));

            using var sha256 = SHA256.Create();
            Span<byte> hash = stackalloc byte[32];
            sha256.TryComputeHash(
                System.Text.Encoding.UTF8.GetBytes(input),
                hash,
                out _
            );

            uint seed = 0;
            for (int i = 0; i < 32; i += 4)
                seed ^= BinaryPrimitives.ReadUInt32LittleEndian(hash.Slice(i, 4));

            return seed;
        }


        /// <summary>
        /// Generates a deterministic derived seed from a root seed and context.
        /// </summary>
        public static uint DeriveSeed(uint rootSeed, string context)
        {
            unchecked
            {
                uint z = rootSeed;
                foreach (char c in context)
                    z += c;

                z += 0x9E3779B9;
                z = (z ^ (z >> 16)) * 0x85EBCA6B;
                z = (z ^ (z >> 13)) * 0xC2B2AE35;
                return z ^ (z >> 16);
            }
        }
        #endregion

        #region 64-bit Seed Generation
        /// <summary>
            /// High-entropy 64-bit seed from OS CSPRNG.
            /// </summary>
            public static ulong GenerateSeed64()
            {
                Span<byte> buffer = stackalloc byte[8];
                RandomNumberGenerator.Fill(buffer);
                return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
            }

            /// <summary>
            /// Deterministic 64-bit seed from a string (Minecraft-style seed strings).
            /// Uses SHA-256, folds to 64-bit.
            /// </summary>
            public static ulong GenerateSeed64(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    throw new ArgumentException("Seed input string cannot be null or empty.", nameof(input));

                using var sha256 = SHA256.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(input);

                Span<byte> hash = stackalloc byte[32];
                sha256.TryComputeHash(bytes, hash, out _);

                // Fold 256 -> 64 (xor the four 64-bit lanes)
                ulong a = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(0, 8));
                ulong b = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(8, 8));
                ulong c = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(16, 8));
                ulong d = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(24, 8));
                ulong seed = a ^ b ^ c ^ d;

                return seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
            }

            /// <summary>
            /// Deterministically derive a new 64-bit seed from a root seed + context label.
            /// Good for: world->region->chunk->entity seed chains.
            ///
            /// This is stable across platforms/runtimes.
            /// </summary>
            public static ulong DeriveSeed64(ulong rootSeed, string context)
            {
                if (context == null)
                    throw new ArgumentNullException(nameof(context));

                unchecked
                {
                    // FNV-1a 64 over UTF-16 chars (stable in .NET; Unity too)
                    ulong h = 14695981039346656037UL;
                    for (int i = 0; i < context.Length; i++)
                    {
                        h ^= context[i];
                        h *= 1099511628211UL;
                    }

                    // Mix root + context hash, then run through SplitMix64 once for avalanche
                    ulong x = rootSeed ^ h ^ 0x9E3779B97F4A7C15UL;

                    // Inline SplitMix64 finalizer
                    x += 0x9E3779B97F4A7C15UL;
                    ulong z = x;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    z = z ^ (z >> 31);

                    return z == 0UL ? 1UL : z;
                }
            }
        #endregion
    }
}
