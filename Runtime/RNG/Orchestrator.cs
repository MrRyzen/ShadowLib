namespace ShadowLib.RNG
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Sources;
    using ShadowLib.RNG.Utilities;

    /// <summary>
    /// Hands out deterministic random streams derived from one root seed.
    /// Own one instance per run, world or save slot; instances share nothing.
    /// </summary>
    /// <remarks>
    /// Three kinds of stream, which never collide even for the same context name:
    /// <list type="bullet">
    /// <item><description><see cref="CreateRNG"/>: long-lived, cached, continues where it left off.</description></item>
    /// <item><description><see cref="Derive"/>: throwaway, the same stream on every call for a context.</description></item>
    /// <item><description><see cref="Next"/>: throwaway, the next stream in a replayable per-context series.</description></item>
    /// </list>
    /// All members are safe to call from multiple threads. The returned generators are not: use each from one thread.
    /// <see cref="Next"/> and <see cref="NextSeed"/> hand out series positions in call order, so call them from one
    /// thread if the run must replay.
    /// </remarks>
    public sealed class Orchestrator
    {
        private const ulong DeriveDomain = 0xD1B54A32D192ED03UL;
        private const ulong NextDomain = 0xABC98388FB8FAC03UL;
        private const ulong ForkDomain = 0x8CB92BA72F3D8DD7UL;

        private readonly object _gate = new object();
        private readonly Dictionary<string, IRandom> _streams = new Dictionary<string, IRandom>(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> _nextCounters = new Dictionary<string, ulong>(StringComparer.Ordinal);

        /// <summary>
        /// Creates an orchestrator for the given root seed. Same root seed and same calls give the same streams forever.
        /// For a non-repeatable run pass <see cref="Seeding.GenerateSeed64()"/>.
        /// </summary>
        public Orchestrator(ulong rootSeed)
        {
            RootSeed = rootSeed;
        }

        /// <summary>
        /// The root seed every stream is derived from.
        /// </summary>
        public ulong RootSeed { get; }

        /// <summary>
        /// Returns the long-lived stream for a context, creating it on first use.
        /// The same context always returns the same instance, which keeps advancing across calls.
        /// </summary>
        public IRandom CreateRNG(string context)
        {
            ulong seed = GetSeedForContext(context);
            lock (_gate)
            {
                if (!_streams.TryGetValue(context, out var rng))
                {
                    rng = new Xoshiro128StarStar(seed);
                    _streams[context] = rng;
                }
                return rng;
            }
        }

        /// <summary>
        /// The seed the long-lived stream for a context starts from.
        /// </summary>
        public ulong GetSeedForContext(string context) => Seeding.DeriveSeed64(RootSeed, context);

        /// <summary>
        /// Returns a new throwaway generator for a context. Every call with the same context yields the same stream,
        /// and nothing is stored. Use where the context is the identity, such as <c>"chunk/3/4"</c>.
        /// </summary>
        public IRandom Derive(string context) => new Xoshiro128StarStar(DeriveSeed(context));

        /// <summary>
        /// The seed <see cref="Derive"/> uses for a context. Pass it into a job and build an <see cref="Xoshiro128"/> there.
        /// </summary>
        public ulong DeriveSeed(string context) => Seeding.DeriveSeed64(RootSeed ^ DeriveDomain, context);

        /// <summary>
        /// Returns a new throwaway generator that is the next in the context's series: each call yields a different
        /// stream, and replaying the same calls from the same root seed yields the same series.
        /// Use for repeated events, such as each chest opened.
        /// </summary>
        public IRandom Next(string context) => new Xoshiro128StarStar(NextSeed(context));

        /// <summary>
        /// The seed <see cref="Next"/> would use, advancing the context's series by one.
        /// </summary>
        public ulong NextSeed(string context)
        {
            ulong seriesSeed = Seeding.DeriveSeed64(RootSeed ^ NextDomain, context);
            ulong position;
            lock (_gate)
            {
                _nextCounters.TryGetValue(context, out position);
                _nextCounters[context] = position + 1;
            }
            return Seeding.DeriveSeed64(seriesSeed, position);
        }

        /// <summary>
        /// Creates an independent child orchestrator whose root seed is derived from this one and the context.
        /// The child is not tracked: it is not part of this orchestrator's saved state.
        /// </summary>
        public Orchestrator Fork(string context) => new Orchestrator(Seeding.DeriveSeed64(RootSeed ^ ForkDomain, context));
    }
}
