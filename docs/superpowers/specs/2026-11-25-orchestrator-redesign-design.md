# Orchestrator redesign

## Why

Users bypass `Orchestrator` and build their own manager around bare `Xoshiro128StarStar`. The orchestrator is a process-global static they cannot own, it only offers long-lived cached streams, and `Save()` is a no-op with no `Load`. They mainly need throwaway generators that still replay.

## Goals

- An orchestrator a user's own manager can own: `new Orchestrator(rootSeed)`.
- Three kinds of stream from one root seed.
- Save and load that restore a run exactly.

- Usable from Unity jobs, Burst and ECS, and from plain managed threads.
- `XorShift128` removed.

Non-goals: new RNG algorithms, a serializer dependency, any `UnityEngine` or `Unity.*` reference in `Runtime/`. The remaining roadmap items are separate projects.

Anti-goal: any path that produces a non-replayable stream by default.

## Approaches

1. **Ordinary class, static API removed (chosen).** Ownable, one per save slot or world, tests stop sharing state. Breaking, accepted for a preview release.
2. Ordinary class plus a static wrapper over a default instance. Not breaking, but leaves two ways to do everything.
3. Stay static and add the features. Smallest diff, does not fix ownership.

## Design

```csharp
public sealed class Orchestrator
{
    public Orchestrator(ulong rootSeed);
    public ulong RootSeed { get; }

    public IRandom CreateRNG(string context);  // long-lived, cached, same instance per context
    public IRandom Derive(string context);     // throwaway, identical stream on every call, nothing stored
    public IRandom Next(string context);       // throwaway, next in a per-context series, one counter stored

    public Orchestrator Fork(string context);  // child orchestrator with a derived root seed

    public ulong GetSeedForContext(string context);

    public byte[] Save();
    public void Load(ReadOnlySpan<byte> data);
}
```

- **No parameterless constructor.** The root seed is always explicit. Callers who want a random run pass `Seeding.GenerateSeed64()`.
- **`CreateRNG` seeds are unchanged**: `Seeding.DeriveSeed64(root, context)`, so existing worlds keep their streams. The cache is keyed by context name, not derived seed.
- **The three kinds never collide.** `Derive`, `Next` and `Fork` derive from a prefixed context, so `"loot"` gives unrelated streams in each. `Next` also mixes in its counter.
- **Save format**: version byte, root seed, each long-lived stream as (context, length-prefixed `GetState()` bytes), each `Next` counter as (context, count). Little-endian, written with `BinaryWriter`. Entries are sorted by context so equal state gives equal bytes.
- **Load restores in place.** Cached streams named in the save get `SetState`, so `IRandom` references already held stay valid. Streams in the save that do not exist yet are created. Cached streams absent from the save are reseeded from the loaded root. Counters are replaced.
- **Load rejects bad input**: unknown version or truncated data throws and leaves the orchestrator untouched.
- `Fork` children are independent. The parent's save does not include them; whoever owns a child saves it.

## Threads, jobs, Burst

Burst cannot touch classes, interfaces, strings or dictionaries, so a lock alone does not make the orchestrator job-usable. Two parts:

- **Unmanaged generator.** A new `Xoshiro128` struct (four `uint` fields, no references) carries the whole algorithm: `NextUInt`, `NextInt`, `NextULong`, `NextFloat`, `NextDouble`, the four `Range` overloads, and a `State` property. The `Xoshiro128StarStar` class becomes a thin `IRandom` wrapper over it, so there is one implementation and both produce identical output for the same seed. Jobs hold the struct by value.
- **Seeds without strings.** `Orchestrator.DeriveSeed(context)` and `NextSeed(context)` return the `ulong` that `Derive` and `Next` would seed with. `Seeding.DeriveSeed64(ulong root, ulong index)` is a new arithmetic-only overload. The main thread asks the orchestrator for one seed, and each job iteration builds `new Xoshiro128(Seeding.DeriveSeed64(seed, index))`.

Managed threads: the stream cache and the `Next` counters sit behind one lock, so `CreateRNG`, `Next`, `Save` and `Load` are safe to call concurrently. Rules the docs must state:

- An `IRandom` or `Xoshiro128` is single-threaded. One per thread or per job iteration.
- `Derive` and `DeriveSeed` are order-independent and safe anywhere.
- `Next` hands out series positions in call order, so calling it from racing threads is safe but not replayable. Call it from one thread and pass the result in.

Verification limit: `dotnet test` can prove the struct is unmanaged and matches the class bit for bit. It cannot run the Burst compiler; that needs a Unity project.

## XorShift128

Deleted. Nothing in the repo references it, it does not implement `IRandom`, and the docs already call it deprecated.

## Touches

- `Runtime/RNG/Orchestrator.cs` rewritten.
- `Runtime/RNG/Sources/Xoshiro128StarStar.cs` split into struct plus wrapper; `Seeding.cs` gains the index overload; `XorShift128.cs` and its `.meta` removed.
- `Tests/Runtime/OrchestratorTests.cs` rewritten; `DiceTests.cs`, `ShadowLib.Runner/Program.cs`, `Samples~/BasicUsage/ShadowLibBasicUsage.cs` moved to an instance.
- `README.md`, `USAGE_GUIDE.md`, `CONTRIBUTING.md`, `CLAUDE.md`, `SHADOWLIB_ROADMAP.md`, `CHANGELOG.md` `[Unreleased]`.

## Left to the builder

Prefix strings, exact byte layout, exception types.
