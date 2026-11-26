# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository

ShadowLib is a deterministic RNG + procedural/spatial primitives library for games, **structured as a Unity UPM package at the repo root** that also builds as a plain .NET solution. It is pre-release (`0.1.0-preview.5`).

The repo doubles as:
- A Unity package (consumed via `package.json`, `Runtime/`, `Tests/Runtime/`, `Samples~/`, asmdefs).
- A .NET solution (`ShadowLib.sln`) with three projects:
  - `Runtime/ShadowLib.csproj` — the library itself, **`netstandard2.1`** (deliberate — keeps it Unity-compatible). Nullable enabled, doc-XML on. Single external dependency: `NCalcSync` 5.11.0, used **only** by `Runtime/Optional/ConditionEvaluator.cs`.
  - `ShadowLib.Runner/ShadowLib.Runner.csproj` — `net9.0` console smoke-test / demo harness.
  - `Tests/Runtime/ShadowLib.Tests.csproj` — `net8.0` NUnit tests, dual-purpose (`dotnet test` and Unity Test Runner via the `UNITY_INCLUDE_TESTS` define).

`SHADOWLIB_ROADMAP.md` tracks what's implemented vs. pending — consult it before adding features so you don't reorder dependencies. `README.md` and `USAGE_GUIDE.md` are the user-facing surface; keep them in sync when changing public API.

## Commands

```bash
dotnet test   ShadowLib.sln -c Release                   # run NUnit tests
dotnet test   Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~OrchestratorTests"
dotnet run    --project ShadowLib.Runner                 # console smoke run
dotnet pack   Runtime/ShadowLib.csproj -c Release -o artifacts/nuget
npm pack      --pack-destination artifacts/upm           # produce a UPM tarball
```

The library targets `netstandard2.1`; the runner targets `net9.0`; tests target `net8.0`. **Don't "upgrade" the library's TFM to net9.0** — Unity-compatibility is the point.

CI lives at `.github/workflows/ci.yml` (push/PR) and `.github/workflows/release.yml` (tag-driven). Releases trigger on tags matching `v*.*.*` or `v*.*.*-*`. The release workflow validates that `package.json` `version` and `Runtime/ShadowLib.csproj` `<Version>` agree with the tag — bump them together.

## Architecture

Three namespaces under `ShadowLib`, each a separately-usable layer.

### `ShadowLib.RNG` — deterministic randomness

The non-obvious shape here is the **Orchestrator → context-derived substreams** pattern:

```
new Orchestrator(rootSeed)
   │
   ├── CreateRNG("loot")      ── long-lived, cached, same instance per context
   ├── Derive("chunk/3/4")    ── throwaway, identical stream on every call, nothing stored
   └── Next("chest")          ── throwaway, next stream in a per-context series (one counter stored)
```

`Orchestrator` is an **instance** owned by the caller (one per world / save slot), not a static. `CreateRNG` derives its seed via `Seeding.DeriveSeed64(root, context)` (FNV-1a over UTF-16 chars + SplitMix64 finalizer); `Derive`, `Next` and `Fork` XOR a per-kind domain constant into the root first so the kinds never collide. Same root + same calls = identical streams forever. **Don't** hand-seed `Xoshiro128StarStar` in feature code — use `Derive`/`Next` so throwaways replay with the root seed.

`Save()` returns a versioned `byte[]` (root seed, long-lived stream states, `Next` counters, sorted by context); `Load` restores streams in place and throws `InvalidDataException` without mutating on bad input. All orchestrator members take one lock; generators themselves are single-threaded. `Next` is call-order dependent, so it only replays when called from one thread.

`IRandom` (in `Runtime/RNG/Sources/`) is the contract, implemented by the `Xoshiro128StarStar` class. That class is a thin wrapper over the unmanaged `Xoshiro128` struct in the same file, which is the form to use in Unity jobs / Burst (seeded via `DeriveSeed`/`NextSeed` + `Seeding.DeriveSeed64(ulong, ulong)`). Keep the struct free of references and keep both bit-identical; `dotnet test` cannot run the Burst compiler; the Burst check is `Assets/_project/Tests/ShadowLibBurst/` in `L:\Projects\games\ShadowLab` (EditMode tests, run through the `unity` CLI against the open editor, package installed from the `npm pack` tarball). Re-run it after touching `Xoshiro128` or `Seeding.DeriveSeed64(ulong, ulong)`.

`DynamicWeightTable<T>` is the weighted-picking core (`TieredTable` and `EffectWheel` build on it). Items have a designed weight that sampling never changes; odds move through `SetInfluence(item, min, max, Func<float>)`, read at pick time. It also carries enabled flags, stock, repeat prevention and `SampleDistinct`. Two invariants to keep: exactly one `rng.NextFloat()` per picked item (none when nothing is pickable), and `DynamicWeightTableTests.Sample_PlainTable_SequenceIsPinned` must not change. There is no push-style weight API (`AddWeight`, `MultiplyWeight`, `ResetWeights`) and no `WeightModifier`; don't reintroduce them.

The NCalc-dependent code (`ConditionEvaluator`) lives in `Runtime/Optional/`, gated by the `SHADOWLIB_NCALC` define for Unity. The .NET csproj compiles it unconditionally because NCalc is a NuGet ref that always resolves.

### `ShadowLib.Spatial` — grids and shapes

The interface split is the load-bearing design:

- `ICell<TOccupant>` — a single grid cell. Cells are created by user-supplied factory (`cellFactory: (x, y) => new MyCell()`); the grid calls `Initialize(x, y)` after construction.
- `ISpace<TOccupant>` — anything that can hold polyomino-shaped occupants (`CanOccupy` / `Occupy` / `Free` / `IsOccupied`). `Grid2D` is the only implementation today; `BinPacker` and any future spatial algorithms target this interface, not `Grid2D` directly. **Code against `ISpace`, not `Grid2D`.**
- `Polyomino` — readonly struct, normalized so min(col,row) is always (0,0). Build via `Polyomino.FromRect(w, h)` or `Polyomino.FromOffsets(params (int Col, int Row)[])`. `Offsets` returns a `ReadOnlySpan` — no allocation on iteration. `Polyomino.Generate` / `Polycube.Generate` share one growth rule in `ShapeGrowth` (2D is the 3D rule with depth 1); its maths is `double` add/multiply/divide only (no `Math.Pow`) and the frontier is walked in insertion order, so shapes match across runtimes. The `Generate_*_ShapeIsPinned` tests (both test classes; the sprawling and blocky ones cover the weight maths and the size limits) must not change.

Two grid types serve different purposes — don't conflate them:
- `Grid2D<TCell, TOccupant>` — class-based, dense flat array, internal occupancy index (`Dictionary<TOccupant, HashSet<int>>`) for O(1) `Free(occupant)`. Use when cells carry behavior or multi-cell occupancy matters.
- `FieldGrid<T>` — value-typed scalar field (density maps, heatmaps). Ref-access, mutable row spans. Use when each cell is just a number.

`CanOccupyCell` rect checks are O(w·h); for very large grids + footprints, consider an occupancy bitmask before adding more spatial features (roadmap item).

`SpatialGraph3D<TOccupant>` is the 3D inventory box that wears out (a backpack with depth); 2D inventories stay on `Grid2D`. Items are `Polycube` shapes (the 3D counterpart of `Polyomino`). Things to keep true:
- Storage is dense arrays sized to the box, indexed by `GetIndex`; every coordinate is bounds-checked per axis before it becomes an index, so shapes and neighbours never wrap around an edge.
- Killing a cell (`TryKill`, `TryKillRandom`) never moves or removes an item; it returns a `KilledCell` and the game decides. Dead cells reject new placements; `Repair` revives them.
- `TryKillRandom` makes exactly one `rng.Range(0, AliveCount)` call and picks by rank in ascending index order, so damage replays from the seed.
- `FindPockets` uses six-neighbour adjacency computed from coordinates; nothing stores connections.
- It is not an `ISpace`, `BinPacker` does not work on it, and it has no pathfinding, distance or range queries by design. There is no `SparseGraph`.

`ShadowLib.Spatial.Pathfinding` holds the grid searches. `GridAStar`, `GridJumpPointSearch` and `GridFlowField` are static functions over a `ReadOnlySpan<byte>` cost map (0 = blocked, otherwise the cost of entering) and caller-owned `Span<int>` scratch, so they run unchanged inside Burst jobs; `GridPathfinder` is the managed wrapper for `Grid2D`. Things to keep true:
- The core (`GridPathing`, `PathScratch`, `PathHeap` and the three searches) uses no floats, managed objects, delegates, tuples or `unsafe`, and its exceptions take constant strings only. Anything else breaks Burst. Do not use `Span.Fill`, `Span.Clear` or `Span.CopyTo` there either: under Burst 2.0.0 `Span<byte>.Fill` wrote four bytes per element and corrupted neighbouring memory (caught by `PathfindingBurstTests.FlowField_UnderBurst_MatchesManaged`, 2026-11-26). Use plain loops.
- Costs are integers: 10 per orthogonal step and 14 per diagonal, times the cost of the cell entered. A diagonal needs both cells it passes between to be walkable, in all three searches.
- Nothing is cleared per query: the A* / JPS scratch (one shared format) carries a generation counter and the map size in its first two ints.
- `GridJumpPointSearchTests.RandomMaps_StatusAndCostMatchAStarOnTheUniformMap` is the guard on the JPS pruning rules; never weaken it to make a change pass.
- After touching the core, re-run the Unity check: `PathfindingBurstTests` compares each search under Burst with managed output.

### `ShadowLib.Procedural` — generation algorithms over `ISpace`

Anything new in this namespace should consume `ISpace` / `IRandom`, not concrete types.

## Working in this repo

- **Determinism is non-negotiable.** Anything that touches randomness must thread `IRandom` through — never call `System.Random`, `UnityEngine.Random`, `DateTime.Now` for seeding, or `Guid.NewGuid()` where a deterministic UUID is wanted (`UUID.GenerateDeterministicUUID(seed, context)` exists for that).
- When adding new RNG-consuming code, take an `IRandom` parameter. Don't reach into `Orchestrator` from a leaf class — let the caller pick the context.
- **`Runtime/` stays engine-free.** No `using UnityEngine` or `using UnityEditor`. The asmdef is `noEngineReferences: true` and that's load-bearing.
- **`Runtime/` stays NuGet-dependency-free** (outside `Runtime/Optional/`). New external NuGet refs go behind a new opt-in asmdef + scripting define, mirroring the Optional pattern.
- The library must keep building for `netstandard2.1`. `Random.Shared`, generic math, `static abstract` interface members, and `DateOnly` are all unavailable.
- **Add a test for any new public surface** in `Tests/Runtime/`. Tests are deterministic — seed via `Seeding.GenerateSeed64("test-name")` or pass `new Xoshiro128StarStar(123UL)` directly.
- `Program.cs` in `ShadowLib.Runner/` doubles as living documentation — it shows the canonical wiring of `Seeding` → `Orchestrator` → `TieredTable` → `Grid2D` → `BinPacker`. When you add a new public surface, extend the runner so a `dotnet run` still demonstrates it end-to-end.

## Files Claude should treat as authoritative

- `README.md` — public-facing install/quick-start.
- `USAGE_GUIDE.md` — module-by-module API guide, includes a "For LLMs" section listing what *not* to hallucinate. **Read this before generating ShadowLib code.**
- `CONTRIBUTING.md` — review checklist + ground rules for new modules.
- `SHADOWLIB_ROADMAP.md` — what's done vs. pending. Roadmap-only APIs do not exist yet.
- `CHANGELOG.md` — keep `[Unreleased]` updated as you change public API.
