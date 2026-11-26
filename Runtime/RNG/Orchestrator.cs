namespace ShadowLib.RNG
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
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

        private const byte SaveVersion = 1;
        private const int StreamStateLength = 16;

        private readonly object _gate = new object();
        private readonly Dictionary<string, IRandom> _streams = new Dictionary<string, IRandom>(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> _nextCounters = new Dictionary<string, ulong>(StringComparer.Ordinal);
        private ulong _rootSeed;

        /// <summary>
        /// Creates an orchestrator for the given root seed. Same root seed and same calls give the same streams forever.
        /// For a non-repeatable run pass <see cref="Seeding.GenerateSeed64()"/>.
        /// </summary>
        public Orchestrator(ulong rootSeed)
        {
            _rootSeed = rootSeed;
        }

        /// <summary>
        /// The root seed every stream is derived from. Changes only when <see cref="Load"/> restores a save.
        /// </summary>
        public ulong RootSeed
        {
            get { lock (_gate) return _rootSeed; }
        }

        /// <summary>
        /// Returns the long-lived stream for a context, creating it on first use.
        /// The same context always returns the same instance, which keeps advancing across calls.
        /// </summary>
        public IRandom CreateRNG(string context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            lock (_gate)
            {
                if (!_streams.TryGetValue(context, out var rng))
                {
                    rng = new Xoshiro128StarStar(Seeding.DeriveSeed64(_rootSeed, context));
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
            if (context == null) throw new ArgumentNullException(nameof(context));
            ulong root, position;
            lock (_gate)
            {
                root = _rootSeed;
                _nextCounters.TryGetValue(context, out position);
                _nextCounters[context] = position + 1;
            }
            return Seeding.DeriveSeed64(Seeding.DeriveSeed64(root ^ NextDomain, context), position);
        }

        /// <summary>
        /// Creates an independent child orchestrator whose root seed is derived from this one and the context.
        /// The child is not tracked: it is not part of this orchestrator's saved state.
        /// </summary>
        public Orchestrator Fork(string context) => new Orchestrator(Seeding.DeriveSeed64(RootSeed ^ ForkDomain, context));

        /// <summary>
        /// Captures everything needed to resume this orchestrator: the root seed, the state of every long-lived
        /// stream and the position of every <see cref="Next"/> series. Equal state always yields equal bytes.
        /// Store the result however you like; restore it with <see cref="Load"/>.
        /// </summary>
        public byte[] Save()
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.UTF8);

            lock (_gate)
            {
                writer.Write(SaveVersion);
                writer.Write(_rootSeed);

                writer.Write(_streams.Count);
                foreach (string context in SortedKeys(_streams))
                {
                    byte[] state = _streams[context].GetState();
                    writer.Write(context);
                    writer.Write(state.Length);
                    writer.Write(state);
                }

                writer.Write(_nextCounters.Count);
                foreach (string context in SortedKeys(_nextCounters))
                {
                    writer.Write(context);
                    writer.Write(_nextCounters[context]);
                }
            }

            writer.Flush();
            return buffer.ToArray();
        }

        /// <summary>
        /// Restores a state produced by <see cref="Save"/>, replacing the root seed, stream states and series positions.
        /// Streams already handed out are restored in place, so references to them stay valid. A long-lived stream
        /// that is not in the save restarts from its seed under the loaded root.
        /// </summary>
        /// <exception cref="InvalidDataException">The data is not a complete save of a supported version. Nothing is changed.</exception>
        public void Load(ReadOnlySpan<byte> data)
        {
            ulong rootSeed;
            var streamStates = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var counters = new Dictionary<string, ulong>(StringComparer.Ordinal);

            try
            {
                using var reader = new BinaryReader(new MemoryStream(data.ToArray()), Encoding.UTF8);

                byte version = reader.ReadByte();
                if (version != SaveVersion)
                    throw new InvalidDataException($"Unsupported orchestrator save version {version}.");

                rootSeed = reader.ReadUInt64();

                int streamCount = ReadCount(reader);
                for (int i = 0; i < streamCount; i++)
                {
                    string context = reader.ReadString();
                    if (reader.ReadInt32() != StreamStateLength)
                        throw new InvalidDataException($"Stream '{context}' has an unsupported state length.");
                    byte[] state = reader.ReadBytes(StreamStateLength);
                    if (state.Length != StreamStateLength)
                        throw new EndOfStreamException();
                    if (!streamStates.TryAdd(context, state))
                        throw new InvalidDataException($"Stream '{context}' appears twice.");
                }

                int counterCount = ReadCount(reader);
                for (int i = 0; i < counterCount; i++)
                {
                    string context = reader.ReadString();
                    if (!counters.TryAdd(context, reader.ReadUInt64()))
                        throw new InvalidDataException($"Series '{context}' appears twice.");
                }

                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Orchestrator save has trailing data.");
            }
            catch (Exception e) when (e is EndOfStreamException || e is FormatException)
            {
                throw new InvalidDataException("Orchestrator save is truncated or corrupt.", e);
            }

            lock (_gate)
            {
                _rootSeed = rootSeed;

                foreach (var cached in _streams)
                {
                    if (!streamStates.ContainsKey(cached.Key))
                        cached.Value.Seed(Seeding.DeriveSeed64(rootSeed, cached.Key));
                }

                foreach (var saved in streamStates)
                {
                    if (!_streams.TryGetValue(saved.Key, out var rng))
                    {
                        rng = new Xoshiro128StarStar(Seeding.DeriveSeed64(rootSeed, saved.Key));
                        _streams[saved.Key] = rng;
                    }
                    rng.SetState(saved.Value);
                }

                _nextCounters.Clear();
                foreach (var counter in counters)
                    _nextCounters[counter.Key] = counter.Value;
            }
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0)
                throw new InvalidDataException("Orchestrator save has a negative count.");
            return count;
        }

        private static List<string> SortedKeys<TValue>(Dictionary<string, TValue> source)
        {
            var keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
