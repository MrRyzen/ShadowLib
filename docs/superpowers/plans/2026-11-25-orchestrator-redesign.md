# Orchestrator Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the static `Orchestrator` with an ownable instance offering long-lived, repeatable-throwaway and series-throwaway streams, working save/load, and a job-safe struct generator; delete `XorShift128`.

**Architecture:** A new unmanaged `Xoshiro128` struct holds the algorithm; the `Xoshiro128StarStar` class wraps it for `IRandom`. `Orchestrator` becomes a sealed class holding a root seed, a context-keyed stream cache and per-context `Next` counters behind one lock. Save is a versioned little-endian `byte[]`.

**Tech Stack:** C# 8 / `netstandard2.1` library, NUnit on `net8.0`, `net9.0` runner.

**Spec:** `docs/superpowers/specs/2026-11-25-orchestrator-redesign-design.md`

## Global Constraints

- Library stays `netstandard2.1`. No `UnityEngine`, `UnityEditor` or `Unity.*` references in `Runtime/`. No new NuGet references.
- No `System.Random`, `DateTime`, `Guid.NewGuid()` anywhere in new code.
- No new `.cs` files: every tracked `.cs` needs a Unity `.meta`, so new types go in existing files.
- `CreateRNG(context)` must seed with `Seeding.DeriveSeed64(RootSeed, context)` exactly as today.
- `Xoshiro128StarStar` output for a given seed must not change.
- Repo C# style: 4-space indent, namespace-scoped `using`s, XML doc on every public member (doc-XML is on).
- Never `git push`. Commit after each task. `.github/workflows/release.yml` and `CLAUDE.md` carry the user's uncommitted edits: never stage `release.yml`; see Task 4 for `CLAUDE.md`.
- Test command: `dotnet test ShadowLib.sln -c Release`.

## Review Focus

1. Truncated, empty or garbage bytes passed to `Load`: throws `InvalidDataException`, orchestrator unchanged (Task 3 test).
2. `Load` of a save taken under a different root seed while streams are already held: held references follow the save (Task 3 test).
3. `Next` after `Load` continues the series instead of restarting it (Task 3 test).
4. Non-ASCII and empty-string context names survive a save round trip; `null` context throws `ArgumentNullException` (Task 2 and 3 tests).
5. Same history built in a different call order yields byte-identical saves (Task 3 test).

---

### Task 1: Unmanaged `Xoshiro128` struct, index seed derivation, remove `XorShift128`

**Files:**
- Modify: `Runtime/RNG/Sources/Xoshiro128StarStar.cs`
- Modify: `Runtime/RNG/Utilities/Seeding.cs`
- Delete: `Runtime/RNG/Sources/XorShift128.cs`, `Runtime/RNG/Sources/XorShift128.cs.meta`
- Test: `Tests/Runtime/DeterminismTests.cs`

**Interfaces:**
- Produces:
  - `public struct Xoshiro128` in `ShadowLib.RNG.Sources` with `Xoshiro128(ulong seed)`, `void Seed(ulong seed)`, `Xoshiro128State State { get; set; }`, `uint NextUInt()`, `int NextInt()`, `ulong NextULong()`, `float NextFloat()`, `double NextDouble()`, `int Range(int,int)`, `float Range(float,float)`, `double Range(double,double)`, `ulong Range(ulong,ulong)`.
  - `public static ulong Seeding.DeriveSeed64(ulong rootSeed, ulong index)`.

- [ ] **Step 1: Write the failing tests** in `DeterminismTests.cs`

```csharp
static void AssertUnmanaged<T>() where T : unmanaged { }

[Test]
public void Xoshiro128Struct_IsUnmanaged() => AssertUnmanaged<Xoshiro128>();

[Test]
public void Xoshiro128Struct_MatchesClassBitForBit()
{
    var s = new Xoshiro128(987654321UL);
    var c = new Xoshiro128StarStar(987654321UL);
    for (int i = 0; i < 200; i++)
    {
        Assert.AreEqual(c.NextUInt(), s.NextUInt());
        Assert.AreEqual(c.NextULong(), s.NextULong());
        Assert.AreEqual(c.NextFloat(), s.NextFloat());
        Assert.AreEqual(c.NextDouble(), s.NextDouble());
        Assert.AreEqual(c.Range(-5, 17), s.Range(-5, 17));
        Assert.AreEqual(c.Range(0.5f, 9f), s.Range(0.5f, 9f));
        Assert.AreEqual(c.Range(1.0, 2.0), s.Range(1.0, 2.0));
        Assert.AreEqual(c.Range(3UL, 1UL << 40), s.Range(3UL, 1UL << 40));
    }
    CollectionAssert.AreEqual(c.GetState(), s.State.ToBytes());
}

[Test]
public void Xoshiro128StarStar_OutputIsPinned()
{
    // Capture these three values from the code BEFORE refactoring and paste them in.
    var rng = new Xoshiro128StarStar(123UL);
    Assert.AreEqual(/* captured */ 0u, rng.NextUInt());
    Assert.AreEqual(/* captured */ 0u, rng.NextUInt());
    Assert.AreEqual(/* captured */ 0u, rng.NextUInt());
}

[Test]
public void DeriveSeed64_ByIndex_IsStableDistinctAndNonZero()
{
    Assert.AreEqual(Seeding.DeriveSeed64(42UL, 7UL), Seeding.DeriveSeed64(42UL, 7UL));
    var seen = new System.Collections.Generic.HashSet<ulong>();
    for (ulong i = 0; i < 10000; i++)
    {
        ulong s = Seeding.DeriveSeed64(42UL, i);
        Assert.AreNotEqual(0UL, s);
        Assert.IsTrue(seen.Add(s));
    }
    Assert.AreNotEqual(Seeding.DeriveSeed64(42UL, 1UL), Seeding.DeriveSeed64(43UL, 1UL));
}
```

Capture the pinned values first with a throwaway test or `dotnet run` print against the unmodified class; if `DeterminismTests.cs` already pins `Xoshiro128StarStar(seed)` output, drop this test and rely on that one.

- [ ] **Step 2: Run** `dotnet test ShadowLib.sln -c Release` — expected: compile failure, `Xoshiro128` not found.

- [ ] **Step 3: Implement `Xoshiro128`** in `Xoshiro128StarStar.cs`. Move the algorithm bodies (seeding, `NextUInt`, conversions, all `Range` overloads, the all-zero guard, the `seed == 0` fallback) from the class into the struct unchanged. `[Serializable]`, `[StructLayout(LayoutKind.Sequential)]`, four private `uint` fields, no other fields. The class keeps one non-readonly `private Xoshiro128 _core;` and forwards every `IRandom` member and `State` to it; `GetState`/`SetState` stay on the class.

- [ ] **Step 4: Implement the index overload** in `Seeding.cs`, next to the string overload:

```csharp
public static ulong DeriveSeed64(ulong rootSeed, ulong index)
{
    unchecked
    {
        ulong x = rootSeed;
        ulong y = SplitMix64.Next(ref x) + index;
        ulong z = SplitMix64.Next(ref y);
        return z == 0UL ? 1UL : z;
    }
}
```

`SplitMix64` is `internal` in `ShadowLib.RNG.Sources`.

- [ ] **Step 5: Delete** `XorShift128.cs` and `XorShift128.cs.meta` with `git rm`.

- [ ] **Step 6: Run** `dotnet test ShadowLib.sln -c Release` — expected: all pass.

- [ ] **Step 7: Commit** — `Add unmanaged Xoshiro128 struct and index seed derivation; remove XorShift128`

---

### Task 2: Instance `Orchestrator` with three stream kinds

**Files:**
- Modify: `Runtime/RNG/Orchestrator.cs` (rewrite)
- Modify: `Tests/Runtime/OrchestratorTests.cs` (rewrite), `Tests/Runtime/DiceTests.cs:15-39`
- Modify: `ShadowLib.Runner/Program.cs:46-84`, `Samples~/BasicUsage/ShadowLibBasicUsage.cs:26-27`

**Interfaces:**
- Consumes: `Xoshiro128StarStar(ulong)`, `Seeding.DeriveSeed64(ulong, string)`, `Seeding.DeriveSeed64(ulong, ulong)`.
- Produces, on `public sealed class Orchestrator`:
  - `Orchestrator(ulong rootSeed)` (no parameterless constructor), `ulong RootSeed { get; }`
  - `IRandom CreateRNG(string context)`, `ulong GetSeedForContext(string context)`
  - `IRandom Derive(string context)`, `ulong DeriveSeed(string context)`
  - `IRandom Next(string context)`, `ulong NextSeed(string context)`
  - `Orchestrator Fork(string context)`

Seed rules (private constants in the class):

| Member | Seed |
|---|---|
| `CreateRNG` / `GetSeedForContext` | `DeriveSeed64(RootSeed, context)` |
| `DeriveSeed` | `DeriveSeed64(RootSeed ^ 0xD1B54A32D192ED03UL, context)` |
| `NextSeed` | `DeriveSeed64(DeriveSeed64(RootSeed ^ 0xABC98388FB8FAC03UL, context), n)` where `n` is that context's counter before increment, starting at 0 |
| `Fork` root | `DeriveSeed64(RootSeed ^ 0x8CB92BA72F3D8DD7UL, context)` |

`Derive(c)` is `new Xoshiro128StarStar(DeriveSeed(c))`; `Next(c)` is `new Xoshiro128StarStar(NextSeed(c))`. State: `Dictionary<string, IRandom>` (ordinal) for streams, `Dictionary<string, ulong>` for counters, one private lock object taken by `CreateRNG` and `NextSeed`. `null` context throws `ArgumentNullException` from every member.

- [ ] **Step 1: Write the failing tests** — replace `OrchestratorTests.cs` body with:

```csharp
static uint[] Take(IRandom r, int n = 8) { var a = new uint[n]; for (int i = 0; i < n; i++) a[i] = r.NextUInt(); return a; }
const ulong Root = 0xC0FFEEUL;

[Test] public void CreateRNG_SameContext_ReturnsSameInstance()
{ var o = new Orchestrator(Root); Assert.AreSame(o.CreateRNG("loot"), o.CreateRNG("loot")); }

[Test] public void CreateRNG_SeedIsUnchangedFromLegacyDerivation()
{
    var o = new Orchestrator(Root);
    Assert.AreEqual(Seeding.DeriveSeed64(Root, "loot"), o.GetSeedForContext("loot"));
    CollectionAssert.AreEqual(Take(new Xoshiro128StarStar(Seeding.DeriveSeed64(Root, "loot"))), Take(o.CreateRNG("loot")));
}

[Test] public void TwoOrchestrators_SameRoot_AreIndependentAndIdentical()
{
    var a = new Orchestrator(Root); var b = new Orchestrator(Root);
    Assert.AreNotSame(a.CreateRNG("x"), b.CreateRNG("x"));
    CollectionAssert.AreEqual(Take(a.CreateRNG("x")), Take(b.CreateRNG("x")));
}

[Test] public void Derive_ReturnsFreshIdenticalStreamEachCall()
{
    var o = new Orchestrator(Root);
    var first = o.Derive("chunk/3/4"); var second = o.Derive("chunk/3/4");
    Assert.AreNotSame(first, second);
    CollectionAssert.AreEqual(Take(first), Take(second));
    CollectionAssert.AreEqual(Take(new Xoshiro128StarStar(o.DeriveSeed("chunk/3/4"))), Take(o.Derive("chunk/3/4")));
}

[Test] public void Next_AdvancesSeries_AndReplaysFromSameRoot()
{
    var a = new Orchestrator(Root); var b = new Orchestrator(Root);
    var a0 = Take(a.Next("chest")); var a1 = Take(a.Next("chest"));
    CollectionAssert.AreNotEqual(a0, a1);
    CollectionAssert.AreEqual(a0, Take(b.Next("chest")));
    CollectionAssert.AreEqual(a1, Take(new Xoshiro128StarStar(b.NextSeed("chest"))));
}

[Test] public void Next_ContextsHaveSeparateCounters()
{
    var a = new Orchestrator(Root); var b = new Orchestrator(Root);
    a.Next("other"); a.Next("other");
    CollectionAssert.AreEqual(Take(b.Next("chest")), Take(a.Next("chest")));
}

[Test] public void StreamKinds_WithSameContext_DoNotCollide()
{
    var o = new Orchestrator(Root);
    var seeds = new[] { o.GetSeedForContext("loot"), o.DeriveSeed("loot"), o.NextSeed("loot"), o.Fork("loot").RootSeed };
    CollectionAssert.AllItemsAreUnique(seeds);
}

[Test] public void Fork_IsDeterministicAndIndependentOfParent()
{
    var o = new Orchestrator(Root);
    Assert.AreEqual(o.Fork("world/2").RootSeed, new Orchestrator(Root).Fork("world/2").RootSeed);
    Assert.AreNotEqual(o.RootSeed, o.Fork("world/2").RootSeed);
    Assert.AreNotEqual(o.Fork("world/1").RootSeed, o.Fork("world/2").RootSeed);
}

[Test] public void NullContext_Throws()
{
    var o = new Orchestrator(Root);
    Assert.Throws<ArgumentNullException>(() => o.CreateRNG(null!));
    Assert.Throws<ArgumentNullException>(() => o.Derive(null!));
    Assert.Throws<ArgumentNullException>(() => o.Next(null!));
    Assert.Throws<ArgumentNullException>(() => o.Fork(null!));
}

[Test] public void EmptyContext_IsAllowed()
{ var o = new Orchestrator(Root); Assert.DoesNotThrow(() => { o.CreateRNG(""); o.Derive(""); o.Next(""); }); }

[Test] public void ConcurrentCreateAndNext_DoNotCorruptState()
{
    var o = new Orchestrator(Root);
    var seeds = new System.Collections.Concurrent.ConcurrentBag<ulong>();
    System.Threading.Tasks.Parallel.For(0, 2000, i => { o.CreateRNG("s" + (i % 50)); seeds.Add(o.NextSeed("n")); });
    Assert.AreEqual(2000, new System.Collections.Generic.HashSet<ulong>(seeds).Count);
    var expected = new Orchestrator(Root);
    for (int i = 0; i < 2000; i++) expected.NextSeed("n");
    Assert.AreEqual(expected.NextSeed("n"), o.NextSeed("n"));
}
```

- [ ] **Step 2: Run** — expected: compile failure (`Orchestrator` is static).

- [ ] **Step 3: Rewrite `Orchestrator.cs`** to the Interfaces block and seed table above.

- [ ] **Step 4: Move callers to an instance.** `DiceTests.cs`: each `Orchestrator.Initialize(seed); Orchestrator.CreateRNG("dice")` pair becomes `new Orchestrator(seed).CreateRNG("dice")`. `Program.cs`: one `var orchestrator = new Orchestrator(rootSeed);` replacing `Initialize`, and the three `CreateRNG` calls go through it; add a short block that prints one value each from `Derive("chunk/3/4")` twice (equal) and `Next("chest")` twice (different). `ShadowLibBasicUsage.cs`: same instance change.

- [ ] **Step 5: Run** `dotnet test ShadowLib.sln -c Release` then `dotnet run --project ShadowLib.Runner` — expected: all pass; runner prints the new block and exits 0.

- [ ] **Step 6: Commit** — `Make Orchestrator an instance with long-lived, derived and series streams`

---

### Task 3: Save and Load

**Files:**
- Modify: `Runtime/RNG/Orchestrator.cs`
- Modify: `ShadowLib.Runner/Program.cs`
- Test: `Tests/Runtime/OrchestratorTests.cs`

**Interfaces:**
- Consumes: Task 2's `Orchestrator` state (stream cache, counters, lock).
- Produces: `byte[] Save()`, `void Load(ReadOnlySpan<byte> data)`. `RootSeed` gains a private setter.

Format, little-endian, strings as `BinaryWriter.Write(string)` (UTF-8, length-prefixed):

```
byte   version = 1
ulong  rootSeed
int    streamCount
  repeat: string context, int stateLength, byte[stateLength] state   // sorted by context, ordinal
int    counterCount
  repeat: string context, ulong count                                // sorted by context, ordinal
```

`Load` parses the whole buffer into locals first and only then mutates, under the lock. It throws `System.IO.InvalidDataException` when: the buffer is empty, the version is not 1, the data ends early, a count or length is negative, a stream state is not 16 bytes, or bytes remain after the last counter. Apply step: set `RootSeed`; for each saved stream, `SetState` on the cached instance or create one (`new Xoshiro128StarStar(seed)` then `SetState`) and cache it; every cached stream absent from the save gets `Seed(DeriveSeed64(newRoot, context))`; counters are replaced wholesale.

- [ ] **Step 1: Write the failing tests** (append to `OrchestratorTests.cs`)

```csharp
[Test] public void SaveLoad_RestoresHeldStreamsInPlace()
{
    var o = new Orchestrator(Root);
    var loot = o.CreateRNG("loot"); Take(loot, 5);
    byte[] save = o.Save();
    var expected = Take(loot);
    Take(loot, 100);
    o.Load(save);
    Assert.AreSame(loot, o.CreateRNG("loot"));
    CollectionAssert.AreEqual(expected, Take(loot));
}

[Test] public void Load_IntoFreshOrchestrator_WithDifferentRoot_FollowsTheSave()
{
    var a = new Orchestrator(Root);
    Take(a.CreateRNG("loot"), 3); a.Next("chest"); a.Next("chest");
    byte[] save = a.Save();

    var b = new Orchestrator(999UL);
    var held = b.CreateRNG("loot");
    var stale = b.CreateRNG("not-in-save"); Take(stale, 4);
    b.Load(save);

    Assert.AreEqual(Root, b.RootSeed);
    CollectionAssert.AreEqual(Take(a.CreateRNG("loot")), Take(held));
    CollectionAssert.AreEqual(Take(a.Next("chest")), Take(b.Next("chest")));          // series resumes at #2
    CollectionAssert.AreEqual(Take(a.Derive("chunk/1/1")), Take(b.Derive("chunk/1/1")));
    CollectionAssert.AreEqual(Take(new Orchestrator(Root).CreateRNG("not-in-save")), Take(stale));
}

[Test] public void Save_IsIndependentOfCreationOrder()
{
    var a = new Orchestrator(Root); a.CreateRNG("a"); a.CreateRNG("b"); a.Next("x"); a.Next("y");
    var b = new Orchestrator(Root); b.Next("y"); b.CreateRNG("b"); b.Next("x"); b.CreateRNG("a");
    CollectionAssert.AreEqual(a.Save(), b.Save());
}

[Test] public void SaveLoad_RoundTripsNonAsciiAndEmptyContexts()
{
    var a = new Orchestrator(Root);
    Take(a.CreateRNG("战利品/ñ"), 2); Take(a.CreateRNG(""), 2); a.Next("宝箱");
    var b = new Orchestrator(1UL); b.Load(a.Save());
    CollectionAssert.AreEqual(Take(a.CreateRNG("战利品/ñ")), Take(b.CreateRNG("战利品/ñ")));
    CollectionAssert.AreEqual(Take(a.CreateRNG("")), Take(b.CreateRNG("")));
    CollectionAssert.AreEqual(Take(a.Next("宝箱")), Take(b.Next("宝箱")));
}

[Test] public void Load_RejectsBadData_AndLeavesStateUntouched()
{
    var o = new Orchestrator(Root);
    var loot = o.CreateRNG("loot"); Take(loot, 3); o.Next("chest");
    byte[] good = o.Save();

    byte[] wrongVersion = (byte[])good.Clone(); wrongVersion[0] = 99;
    byte[] truncated = good.AsSpan(0, good.Length - 3).ToArray();
    byte[] trailing = new byte[good.Length + 1]; good.CopyTo(trailing, 0);

    foreach (var bad in new[] { System.Array.Empty<byte>(), wrongVersion, truncated, trailing, new byte[] { 1, 2, 3 } })
        Assert.Throws<System.IO.InvalidDataException>(() => o.Load(bad));

    CollectionAssert.AreEqual(good, o.Save());
}
```

- [ ] **Step 2: Run** — expected: compile failure, `Save`/`Load` signatures missing (the old no-op `Save` returned `void` and was removed in Task 2).

- [ ] **Step 3: Implement `Save` and `Load`** per the format and rules above.

- [ ] **Step 4: Extend the runner**: after some draws, `Save()`, draw three values from a long-lived stream, `Load()`, draw three again, print both rows and whether they match.

- [ ] **Step 5: Run** `dotnet test ShadowLib.sln -c Release` and `dotnet run --project ShadowLib.Runner` — expected: all pass; runner reports a match.

- [ ] **Step 6: Commit** — `Add Orchestrator save and load`

---

### Task 4: Documentation

**Files:**
- Modify: `README.md`, `USAGE_GUIDE.md`, `CONTRIBUTING.md`, `SHADOWLIB_ROADMAP.md`, `CHANGELOG.md`, `CLAUDE.md`, `.npmignore`, `package.json` (sample description only if it names static usage)

- [ ] **Step 1: Find every stale reference**: `rg -n "Orchestrator|XorShift128|Xoshiro128" --glob "*.md"`.

- [ ] **Step 2: Update each file** so that:
  - Every code sample uses `var orchestrator = new Orchestrator(seed);` and instance calls. No `Orchestrator.Initialize`, no "static and process-global".
  - `USAGE_GUIDE.md` gains a section covering the three stream kinds with the chest/chunk example from the spec, `Fork`, `Save`/`Load` (what is stored, in-place restore, `InvalidDataException`), and a "Threads, jobs, Burst" section with the three rules from the spec plus this pattern:

    ```csharp
    ulong seed = orchestrator.DeriveSeed("chunks");            // main thread
    // inside IJobParallelFor.Execute(int index):
    var rng = new Xoshiro128(Seeding.DeriveSeed64(seed, (ulong)index));
    ```

    State that Burst compilation is not covered by the .NET test suite.
  - The "do not `new Xoshiro128StarStar` directly" advice is softened to: prefer `Derive`/`Next` for throwaways so they replay; `Xoshiro128` by value is the intended form inside jobs.
  - The "For LLMs" lists drop `XorShift128`, and list `Orchestrator.Initialize` and static `Orchestrator.CreateRNG` as APIs that no longer exist.
  - `SHADOWLIB_ROADMAP.md`: remove the `XorShift128` line, add `Xoshiro128`, rewrite the `Orchestrator` line.
  - `CHANGELOG.md` `[Unreleased]`: **Breaking** `Orchestrator` is now an instance; **Added** `Derive`, `Next`, `DeriveSeed`, `NextSeed`, `Fork`, `RootSeed`, `Save`, `Load`, `Xoshiro128`, `Seeding.DeriveSeed64(ulong, ulong)`; **Removed** `XorShift128`, `Orchestrator.Initialize`.
  - `.npmignore`: add `docs/` under repo-only docs.

- [ ] **Step 3: `CLAUDE.md`.** It has the user's own uncommitted edits. Run `git diff CLAUDE.md` first. Make the orchestrator and `XorShift128` corrections, but do not stage or commit the file; report that it is left modified for the user.

- [ ] **Step 4: Verify** `rg -n "Orchestrator\.(Initialize|CreateRNG)|XorShift128" --glob "*.md" --glob "*.cs" --glob "!docs/**" --glob "!CHANGELOG.md"` returns nothing, and `dotnet test ShadowLib.sln -c Release` still passes.

- [ ] **Step 5: Commit** everything except `CLAUDE.md` and `.github/workflows/release.yml` — `Document instance Orchestrator, stream kinds, save/load and job usage`
