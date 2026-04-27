namespace ShadowLib.RNG.Sources;

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
/// xoshiro128** PRNG (Blackman and Vigna). Deterministic, fast, good statistical quality.
/// Not cryptographically secure.
///
/// State: 4x uint (128-bit). Seeded via SplitMix64 (ulong).
/// </summary>
public sealed class Xoshiro128StarStar : IRandom
{
    // 128-bit state: 4x 32-bit
    private uint _s0, _s1, _s2, _s3;

    // Float/double scaling constants
    private const float INV_24BIT = 1f / (1 << 24);                  // 2^-24
    private const double INV_53BIT = 1.0 / (1UL << 53);              // 2^-53

    /// <summary>
    /// Gets or sets the internal state of the generator as a Xoshiro128State struct. This allows for easy saving and restoring of the generator's state.
    /// </summary> 
    public Xoshiro128State State
    {
        get => new() { S0 = _s0, S1 = _s1, S2 = _s2, S3 = _s3 };
        set
        {
            _s0 = value.S0; _s1 = value.S1; _s2 = value.S2; _s3 = value.S3;
            if ((_s0 | _s1 | _s2 | _s3) == 0u) _s0 = 1u;
        }
    }

    /// <summary>
    /// Create with explicit 64-bit seed.
    /// </summary>
    public Xoshiro128StarStar(ulong seed)
    {
        Seed(seed);
    }

    /// <summary>
    /// Create with high-entropy OS seed (non-deterministic).
    /// </summary>
    public Xoshiro128StarStar()
    {
        Seed(Utilities.Seeding.GenerateSeed64());
    }


    /// <summary>
    /// Gets the current state of the RNG as a byte array. 
    /// This can be used to save the state for later restoration. 
    /// The byte array is 16 bytes long, representing the four 32-bit unsigned integers of the state.
    /// </summary>
    /// <returns>A byte array representing the current state of the RNG.</returns>
    public byte[] GetState() => State.ToBytes();

    /// <summary>
    /// Sets the state of the RNG from a byte array. This can be used to restore a previously saved state. The byte array must be at least 16 bytes long and should be in the format produced by GetState, where the first 4 bytes correspond to S0, the next 4 bytes to S1, the next 4 bytes to S2, and the last 4 bytes to S3.
    /// </summary>
    /// <param name="state">A byte array representing the state to restore. Must be in the format returned by GetState.</param>
    public void SetState(byte[] state) => SetState(state.AsSpan());

    /// <summary>
    /// Sets the state of the RNG from a read-only span of bytes. This can be used to restore a previously saved state without allocating a new array. The span must be at least 16 bytes long and should be in the format produced by GetState, where the first 4 bytes correspond to S0, the next 4 bytes to S1, the next 4 bytes to S2, and the last 4 bytes to S3.
    /// </summary>
    /// <param name="state">A read-only span of bytes representing the state to restore. Must be in the format returned by GetState.</param>
    public void SetState(ReadOnlySpan<byte> state)
    {
        State = Xoshiro128State.FromBytes(state);
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
        => (NextUInt() >> 8) * INV_24BIT;

    /// <summary>
    /// Uniform double in [0,1).
    /// Uses 53 bits of randomness.
    /// </summary>
    public double NextDouble()
    {
        // Use 53 bits from a 64-bit value
        ulong v = NextULong() >> 11; // keep top 53 bits
        return v * INV_53BIT;
    }

    /// <summary>
    /// Uniform int in [min, max) with NO modulo bias.
    /// </summary>
    /// <remarks>
    /// (max-min) must be <= 2^32 to avoid overflow. For larger ranges, use Range(ulong, ulong).
    /// This uses rejection sampling with a threshold to ensure uniformity without bias, even for ranges that do not divide 2^32 evenly.
    /// </remarks>
    public int Range(int min, int max)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

        uint range = (uint)(max - min);

        // Rejection sampling (Lemire-style threshold)
        // threshold = (2^32 % range)
        uint threshold = (uint)(unchecked((0u - range) % range));

        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return min + (int)(r % range);
        }
    }
    /// <summary>
    /// Generates a random float within the specified range [min, max).
    /// </summary>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Uniform ulong in [0, bound) with NO modulo bias (for big ranges).
    /// </summary>
    public ulong Range(ulong min, ulong max)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min");

        ulong range = max - min;

        // Rejection sampling (Lemire-style threshold)
        // threshold = (2^64 % range)
        ulong threshold = (ulong)(unchecked((0UL - range) % range));

        while (true)
        {
            ulong r = NextULong();
            if (r >= threshold)
                return min + (r % range);
        }
    }

    private static uint RotL(uint x, int k)
        => (x << k) | (x >> (32 - k));
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
