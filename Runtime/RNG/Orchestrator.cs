namespace ShadowLib.RNG;

using System.Collections.Generic;
using ShadowLib.RNG.Sources;
using ShadowLib.RNG.Utilities;

/// <summary>
/// Orchestrator class for RNG management. Provides a high-level interface for creating and using RNG instances.
/// </summary>
/// <remarks>
/// This class serves as a central point for RNG creation and management. It can be extended in the future to support additional features such as RNG state management, seeding strategies, or integration with other systems.
/// </remarks>
public static class Orchestrator
{
    private static ulong _rootSeed;
    private static readonly Dictionary<ulong, IRandom> _rngInstances = new();
    private static readonly List<IRandom> _rngList = new();

    static Orchestrator()
    {
        _rootSeed = Seeding.GenerateSeed64();
    }

    /// <summary>
    /// Initializes the RNG orchestrator with a specific root seed. This can be used to ensure reproducibility across runs by using the same root seed.
    /// </summary>
    /// <param name="rootSeed">The root seed to initialize the RNG orchestrator with.</param>
    public static void Initialize(ulong rootSeed)
    {
        _rootSeed = rootSeed;
        _rngInstances.Clear();
        _rngList.Clear();
    }

    /// <summary>
    /// Creates a new RNG instance based on the specified algorithm and seed.
    /// </summary>
    /// <param name="context">The context for deriving the seed. Used to generate a unique seed based on the root seed and context.</param>
    /// <returns>An instance of IRandom based on the specified algorithm and seed.</returns>
    public static IRandom CreateRNG(string context)
    {
        var derivedSeed = Seeding.DeriveSeed64(_rootSeed, context);
        if (_rngInstances.TryGetValue(derivedSeed, out var existingRng))
        {
            return existingRng;
        }
        var rng = new Xoshiro128StarStar(derivedSeed);
        _rngInstances[derivedSeed] = rng;
        _rngList.Add(rng);
        return rng;
    }

    /// <summary>
    /// Gets the seed associated with a specific context. This can be used to retrieve the seed for a given context, allowing for reproducibility and tracking of RNG instances.
    /// </summary>
    /// <param name="context"> The context for which to retrieve the seed.</param>
    /// <returns>The seed associated with the specified context.</returns>
    public static ulong GetSeedForContext(string context)
    {
        //REMEMBER 100% DETERMINISTIC - same context and root seed will always produce the same derived seed
        return Seeding.DeriveSeed64(_rootSeed, context);
    }

    public static void Save()
    {
        foreach (var rng in _rngList)
        {
            var state = rng.GetState();
        }
    }
}