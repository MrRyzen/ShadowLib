namespace ShadowLib.RNG.Sources
{
    using System;
    using System.Runtime.InteropServices;
    using System.Buffers.Binary;

    /// <summary>
    /// State container for Xoshiro128StarStar.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Xoshiro128State
    {
        /// <summary>
        /// The internal state of the Xoshiro128** generator, consisting of four 32-bit unsigned integers (total 128 bits).
        /// </summary>
        public uint S0, S1, S2, S3;

        /// <summary>
        /// Converts the state to a byte array for serialization.
        /// The state is represented as 16 bytes (4 uints x 4 bytes each).
        /// </summary>
        public readonly byte[] ToBytes()
        {
            var bytes = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), S0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), S1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), S2);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), S3);
            return bytes;
        }

        /// <summary>
        /// Creates a Xoshiro128State from a byte array. The byte array must be at least 16 bytes long and should be in the format produced by ToBytes.
        /// The first 4 bytes correspond to S0, the next 4 bytes to S1, the next 4 bytes to S2, and the last 4 bytes to S3.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Xoshiro128State FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 16) throw new ArgumentException("State requires 16 bytes");

            return new Xoshiro128State
            {
                S0 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4)),
                S1 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)),
                S2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4)),
                S3 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)),
            };
        }
    }

    /// <summary>
    /// xoshiro128** PRNG (Blackman and Vigna) as an unmanaged value type.
    /// Holds no references, so it can be stored by value in Unity jobs, Burst code and ECS components.
    /// Not thread-safe and not cryptographically secure: use one instance per thread or job iteration.
    /// </summary>
    /// <remarks>
    /// This is a mutable struct. Keep it in a local or a non-readonly field, and pass it by <c>ref</c>
    /// when a callee should advance it; a copy advances independently of the original.
    /// </remarks>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Xoshiro128
    {
        private uint _s0, _s1, _s2, _s3;

        private const float INV_24BIT = 1f / (1 << 24);                  // 2^-24
        private const double INV_53BIT = 1.0 / (1UL << 53);              // 2^-53

        /// <summary>
        /// Create with an explicit 64-bit seed.
        /// </summary>
        public Xoshiro128(ulong seed)
        {
            _s0 = _s1 = _s2 = _s3 = 0u;
            Seed(seed);
        }

        /// <summary>
        /// Gets or sets the full generator state. An all-zero state is invalid and is replaced by a non-zero one.
        /// </summary>
        public Xoshiro128State State
        {
            readonly get => new Xoshiro128State { S0 = _s0, S1 = _s1, S2 = _s2, S3 = _s3 };
            set
            {
                _s0 = value.S0; _s1 = value.S1; _s2 = value.S2; _s3 = value.S3;
                if ((_s0 | _s1 | _s2 | _s3) == 0u) _s0 = 1u;
            }
        }

        /// <summary>
        /// Re-seed this generator deterministically.
        /// </summary>
        public void Seed(ulong seed)
        {
            if (seed == 0UL)
                seed = 0x9E3779B97F4A7C15UL; // non-zero fallback

            // Use SplitMix64 to expand 64-bit seed into 128-bit state.
            // We need 4x uint => 2 SplitMix64 outputs (each 64-bit -> 2 uints).
            ulong x = seed;

            ulong a = SplitMix64.Next(ref x);
            ulong b = SplitMix64.Next(ref x);

            _s0 = (uint)(a);
            _s1 = (uint)(a >> 32);
            _s2 = (uint)(b);
            _s3 = (uint)(b >> 32);

            // Avoid the forbidden all-zero state (extremely unlikely, but guard anyway).
            if ((_s0 | _s1 | _s2 | _s3) == 0u)
                _s0 = 1u;
        }

        /// <summary>
        /// Core next uint (32-bit).
        /// </summary>
        public uint NextUInt()
        {
            // xoshiro128**:
            // result = rotl(s1 * 5, 7) * 9
            uint result = RotL(_s1 * 5u, 7) * 9u;

            uint t = _s1 << 9;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;

            _s3 = RotL(_s3, 11);

            return result;
        }

        /// <summary>
        /// Next signed int (full 32-bit range).
        /// </summary>
        public int NextInt()
            => unchecked((int)NextUInt());

        /// <summary>
        /// Next ulong (64-bit) by combining two 32-bit outputs.
        /// </summary>
        public ulong NextULong()
        {
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            return (hi << 32) | lo;
        }

        /// <summary>
        /// Uniform float in [0,1).
        /// Uses top 24 bits (good practice).
        /// </summary>
        public float NextFloat()
            => (int)(NextUInt() >> 8) * INV_24BIT;

        /// <summary>
        /// Uniform double in [0,1).
        /// Uses 53 bits of randomness.
        /// </summary>
        public double NextDouble()
        {
            // Use 53 bits from a 64-bit value
            long v = (long)(NextULong() >> 11); // keep top 53 bits; signed so the conversion to double is the fast one
            return v * INV_53BIT;
        }

        /// <summary>
        /// Uniform int in [min, max) with NO modulo bias.
        /// </summary>
        /// <remarks>
        /// (max-min) must be &lt;= 2^32 to avoid overflow. For larger ranges, use Range(ulong, ulong).
        /// This uses rejection sampling with a threshold to ensure uniformity without bias, even for ranges that do not divide 2^32 evenly.
        /// </remarks>
        public int Range(int min, int max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            uint range = (uint)(max - min);
            uint mask = range - 1;
            if ((range & mask) == 0)
                return min + (int)(NextUInt() & mask);

            // Rejection sampling: a draw below (2^32 % range) is redrawn. That threshold is always below
            // the range, so the division that computes it is skipped unless the draw is that small.
            uint r = NextUInt();
            if (r < range)
            {
                uint threshold = unchecked(0u - range) % range;
                while (r < threshold)
                    r = NextUInt();
            }

            return min + (int)(r % range);
        }

        /// <summary>
        /// Generates a random float within the specified range [min, max).
        /// </summary>
        public float Range(float min, float max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            // Explicit casts pin every step to single precision on runtimes that evaluate float math in double.
            return (float)((float)(NextFloat() * (float)(max - min)) + min);
        }

        /// <summary>
        /// Generates a random double within the specified range [min, max).
        /// </summary>
        public double Range(double min, double max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            return NextDouble() * (max - min) + min;
        }

        /// <summary>
        /// Uniform ulong in [min, max) with NO modulo bias (for big ranges).
        /// </summary>
        public ulong Range(ulong min, ulong max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

            ulong range = max - min;
            ulong mask = range - 1;
            if ((range & mask) == 0)
                return min + (NextULong() & mask);

            ulong r = NextULong();
            if (r < range)
            {
                ulong threshold = unchecked(0UL - range) % range;
                while (r < threshold)
                    r = NextULong();
            }

            return min + (r % range);
        }

        private static uint RotL(uint x, int k)
            => (x << k) | (x >> (32 - k));
    }

    /// <summary>
    /// xoshiro128** PRNG (Blackman and Vigna). Deterministic, fast, good statistical quality.
    /// Not cryptographically secure.
    ///
    /// <see cref="IRandom"/> wrapper over <see cref="Xoshiro128"/>; both produce identical output for the same seed.
    /// </summary>
    public sealed class Xoshiro128StarStar : IRandom
    {
        private Xoshiro128 _core;

        /// <summary>
        /// Gets or sets the internal state of the generator as a Xoshiro128State struct. This allows for easy saving and restoring of the generator's state.
        /// </summary>
        public Xoshiro128State State
        {
            get => _core.State;
            set => _core.State = value;
        }

        /// <summary>
        /// Create with explicit 64-bit seed.
        /// </summary>
        public Xoshiro128StarStar(ulong seed)
        {
            _core = new Xoshiro128(seed);
        }

        /// <summary>
        /// Create with high-entropy OS seed (non-deterministic).
        /// </summary>
        public Xoshiro128StarStar()
        {
            _core = new Xoshiro128(Utilities.Seeding.GenerateSeed64());
        }

        /// <summary>
        /// Gets the current state of the RNG as a 16-byte array (four little-endian 32-bit unsigned integers).
        /// </summary>
        /// <returns>A byte array representing the current state of the RNG.</returns>
        public byte[] GetState() => State.ToBytes();

        /// <summary>
        /// Sets the state of the RNG from a byte array in the format produced by <see cref="GetState"/>. Must be at least 16 bytes.
        /// </summary>
        /// <param name="state">A byte array representing the state to restore.</param>
        public void SetState(byte[] state) => SetState(state.AsSpan());

        /// <summary>
        /// Sets the state of the RNG from a span of bytes in the format produced by <see cref="GetState"/>. Must be at least 16 bytes.
        /// </summary>
        /// <param name="state">A read-only span of bytes representing the state to restore.</param>
        public void SetState(ReadOnlySpan<byte> state)
        {
            State = Xoshiro128State.FromBytes(state);
        }

        /// <summary>
        /// Re-seed this generator deterministically.
        /// </summary>
        public void Seed(ulong seed) => _core.Seed(seed);

        /// <summary>
        /// Core next uint (32-bit).
        /// </summary>
        public uint NextUInt() => _core.NextUInt();

        /// <summary>
        /// Next signed int (full 32-bit range).
        /// </summary>
        public int NextInt() => _core.NextInt();

        /// <summary>
        /// Next ulong (64-bit) by combining two 32-bit outputs.
        /// </summary>
        public ulong NextULong() => _core.NextULong();

        /// <summary>
        /// Uniform float in [0,1).
        /// </summary>
        public float NextFloat() => _core.NextFloat();

        /// <summary>
        /// Uniform double in [0,1).
        /// </summary>
        public double NextDouble() => _core.NextDouble();

        /// <summary>
        /// Uniform int in [min, max) with NO modulo bias.
        /// </summary>
        public int Range(int min, int max) => _core.Range(min, max);

        /// <summary>
        /// Generates a random float within the specified range [min, max).
        /// </summary>
        public float Range(float min, float max) => _core.Range(min, max);

        /// <summary>
        /// Generates a random double within the specified range [min, max).
        /// </summary>
        public double Range(double min, double max) => _core.Range(min, max);

        /// <summary>
        /// Uniform ulong in [min, max) with NO modulo bias (for big ranges).
        /// </summary>
        public ulong Range(ulong min, ulong max) => _core.Range(min, max);
    }

    internal static class SplitMix64
    {
        public static ulong Next(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
