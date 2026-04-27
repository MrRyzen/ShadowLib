namespace ShadowLib.RNG.Sources;

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

    void Seed(ulong seed);
    uint NextUInt();

    /// <summary>
    /// Generates the next random integer.
    /// </summary>
    /// <returns></returns>
    int NextInt();
    ulong NextULong();
    /// <summary>
    /// Generates a random float in the range [0.0, 1.0).
    /// </summary>
    float NextFloat();
    /// <summary>
    /// Generates a random double in the range [0.0, 1.0).
    /// </summary>
    /// <returns></returns>
    double NextDouble();
    /// <summary>
    /// Generates a random integer within the specified range [min, max).
    /// </summary>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    int Range(int min, int max);
    /// <summary>
    /// Generates a random float within the specified range [min, max).
    /// </summary>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    float Range(float min, float max);
    /// <summary>
    /// Generates a random double within the specified range [min, max).
    /// </summary>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    double Range(double min, double max);

    ulong Range(ulong min, ulong max);
}
