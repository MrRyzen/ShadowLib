# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository

ShadowLib is a deterministic RNG + procedural/spatial primitives library for games, **structured as a Unity UPM package at the repo root** that also builds as a plain .NET solution. It is pre-release (`0.1.0-preview.3`).

The repo doubles as:
- A Unity package (consumed via `package.json`, `Runtime/`, `Tests/Runtime/`, `Samples~/`, asmdefs).
- A .NET solution (`ShadowLib.sln`) with three projects:
  - `Runtime/ShadowLib.csproj` — the library itself, **`netstandard2.1`** (deliberate — keeps it Unity-compatible). Nullable enabled, doc-XML on. Single external dependency: `NCalcSync` 5.11.0, used **only** by `Runtime/Optional/ConditionEvaluator.cs`.
  - `ShadowLib.Runner/ShadowLib.Runner.csproj` — `net9.0` console smoke-test / demo harness.
  - `Tests/Runtime/ShadowLib.Tests.csproj` — `net8.0` NUnit tests, dual-purpose (`dotnet test` and Unity Test Runner via the `UNITY_INCLUDE_TESTS` define).

`SHADOWLIB_ROADMAP.md` tracks what's implemented vs. pending — consult it before adding features so you don't reorder dependencies. `README.md` and `USAGE_GUIDE.md` are the user-facing surface; keep them in sync when changing public API.

## Commands

```bash
dotnet build  ShadowLib.sln                              # build everything
dotnet build  Runtime/ShadowLib.csproj                   # library only (faster)
dotnet test   ShadowLib.sln -c Release                   # run NUnit tests
dotnet test   Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~OrchestratorTests"
dotnet run    --project ShadowLib.Runner                 # console smoke run
dotnet pack   Runtime/ShadowLib.csproj -c Release -o artifacts/nuget
npm pack      --pack-destination artifacts/upm           # produce a UPM tarball
```

The library targets `netstandard2.1`; the runner targets `net9.0`; tests target `net8.0`. **Don't "upgrade" the library's TFM to net9.0** — Unity-compatibility is the point.

CI lives at `.github/workflows/ci.yml` (push/PR) and `.github/workflows/release.yml` (tag-driven). Releases trigger on tags matching `v*.*.*` or `v*.*.*-*`. The release workflow validates that `package.json` `version` and `Runtime/ShadowLib.csproj` `<Version>` agree with the tag — bump them together.

## Layout

```
.
├── package.json                  Unity UPM manifest (version must match csproj <Version>)
├── Runtime/                      UPM Runtime/ + .NET csproj source
│   ├── ShadowLib.asmdef          Main asmdef: noEngineReferences=true, no external deps
│   ├── ShadowLib.csproj
│   ├── RNG/  Spatial/  Procedural/
│   └── Optional/                 Opt-in features
│       ├── ShadowLib.Optional.asmdef    defineConstraints: ["SHADOWLIB_NCALC"]
│       ├── ConditionEvaluator.cs        depends on NCalc
│       └── ModifierLoader.cs            depends on System.Text.Json
├── Tests/Runtime/                NUnit tests + ShadowLib.Tests.asmdef
├── Samples~/BasicUsage/          Unity-importable sample (the ~ keeps it dormant until imported)
├── ShadowLib.Runner/             Plain .NET console demo (NOT part of UPM package — excluded by .npmignore)
├── .github/workflows/            ci.yml + release.yml
└── .npmignore                    Controls what `npm pack` ships in the UPM tarball
```

## Architecture

Three namespaces under `ShadowLib`, each a separately-usable layer.

### `ShadowLib.RNG` — deterministic randomness

The non-obvious shape here is the **Orchestrator → context-derived substreams** pattern:

```
root seed (ulong)
   │
   ├── Orchestrator.CreateRNG("loot")       ── Xoshiro128StarStar #1
   ├── Orchestrator.CreateRNG("worldgen")   ── Xoshiro128StarStar #2
   └── Orchestrator.CreateRNG("ai")         ── Xoshiro128StarStar #3
```

`Orchestrator.CreateRNG(string context)` derives a per-context seed via `Seeding.DeriveSeed64` (FNV-1a over UTF-16 chars + SplitMix64 finalizer). Same root + same context = identical stream forever. **Don't** instantiate `Xoshiro128StarStar` directly in feature code — go through `Orchestrator` so streams stay reproducible and decoupled across systems.

`Orchestrator` is `static` and process-global. `Initialize(seed)` clears all cached RNGs — call once at boot, not per-scene-load.

`IRandom` (in `Runtime/RNG/Sources/`) is the contract. **`Xoshiro128StarStar` implements it; `XorShift128` is legacy and does not** — it's missing `GetState`/`SetState`/`Seed`/`NextUInt`/`NextULong`/`Range(ulong,ulong)`. Don't pass a `XorShift128` where an `IRandom` is expected.

Sub-areas:
- `Distributions/` — `DynamicWeightTable<T>` (mutable, base+dynamic weights), `AliasTable<T>` (immutable, O(1)), `TieredTable<T>` (rarity-gated), `RandomBag<T>`, `Dice<T>`.
- `Modifiers/` — `WeightModifier<T>` (the struct + enum, no external deps; lives in main asmdef).
- `Noise/` — `SimplexNoise` (2D/3D + fBm `Fractal()`), seeded from any `IRandom`.
- `Utilities/` — `Seeding`, `UUID` (`GenerateRandomUUID()` for v4, `GenerateDeterministicUUID(ulong seed, ulong context)` for v5 with a ShadowLib namespace UUID), `ScalingCurve` (zero-alloc struct), `RandomUtils` (`Shuffle`, `RandomSelect`, `Chance`).

The NCalc / JSON-dependent code lives in `Runtime/Optional/`, gated by the `SHADOWLIB_NCALC` define for Unity. The .NET csproj compiles it unconditionally because NCalc + System.Text.Json are NuGet refs that always resolve.

### `ShadowLib.Spatial` — grids and shapes

The interface split is the load-bearing design:

- `ICell<TOccupant>` — a single grid cell. Cells are created by user-supplied factory (`cellFactory: (x, y) => new MyCell()`); the grid calls `Initialize(x, y)` after construction.
- `ISpace<TOccupant>` — anything that can hold polyomino-shaped occupants (`CanOccupy` / `Occupy` / `Free` / `IsOccupied`). `Grid2D` is the only implementation today; `BinPacker` and any future spatial algorithms target this interface, not `Grid2D` directly. **Code against `ISpace`, not `Grid2D`.**
- `Polyomino` — readonly struct, normalized so min(col,row) is always (0,0). Build via `Polyomino.FromRect(w, h)` or `Polyomino.FromOffsets(params (int Col, int Row)[])`. `Offsets` returns a `ReadOnlySpan` — no allocation on iteration.

Two grid types serve different purposes — don't conflate them:
- `Grid2D<TCell, TOccupant>` — class-based, dense flat array, internal occupancy index (`Dictionary<TOccupant, HashSet<int>>`) for O(1) `Free(occupant)`. Use when cells carry behavior or multi-cell occupancy matters.
- `FieldGrid<T>` — value-typed scalar field (density maps, heatmaps). Ref-access, mutable row spans. Use when each cell is just a number.

`CanOccupyCell` rect checks are O(w·h); for very large grids + footprints, consider an occupancy bitmask before adding more spatial features (roadmap item).

`SparseGraph.cs` and `SpatialGraph3D.cs` exist on disk but the roadmap lists `SparseGraph<T>` as **not yet implemented** — treat them as scaffolding, not stable API. In particular, `GetNodesInRange`, `FindPath`, and connected-component analysis are roadmap-only; do not write code that calls them.

### `ShadowLib.Procedural` — generation algorithms over `ISpace`

Currently just `BinPacker<TOccupant>`: polyomino bin packing on any `ISpace`. Strategies are `FirstFit`, `BestFit` (neighbor-contact scoring), `BottomLeft`, plus `AutoSort` which repacks by decreasing footprint. Anything new in this namespace should consume `ISpace` / `IRandom`, not concrete types.

## Working in this repo

- **Determinism is non-negotiable.** Anything that touches randomness must thread `IRandom` through — never call `System.Random`, `UnityEngine.Random`, `DateTime.Now` for seeding, or `Guid.NewGuid()` where a deterministic UUID is wanted (`UUID.GenerateDeterministicUUID(seed, context)` exists for that).
- When adding new RNG-consuming code, take an `IRandom` parameter. Don't reach into `Orchestrator` from a leaf class — let the caller pick the context.
- **`Runtime/` stays engine-free.** No `using UnityEngine` or `using UnityEditor`. The asmdef is `noEngineReferences: true` and that's load-bearing.
- **`Runtime/` stays NuGet-dependency-free** (outside `Runtime/Optional/`). New external NuGet refs go behind a new opt-in asmdef + scripting define, mirroring the Optional pattern.
- The library must keep building for `netstandard2.1`. `Random.Shared`, generic math, `static abstract` interface members, and `DateOnly` are all unavailable.
- **Add a test for any new public surface** in `Tests/Runtime/`. Tests are deterministic — seed via `Seeding.GenerateSeed64("test-name")` or pass `new Xoshiro128StarStar(123UL)` directly.
- `Program.cs` in `ShadowLib.Runner/` doubles as living documentation — it shows the canonical wiring of `Seeding` → `Orchestrator` → `TieredTable` → `Grid2D` → `BinPacker`. When you add a new public surface, extend the runner so a `dotnet run` still demonstrates it end-to-end.
- **Version is duplicated** in `package.json` and `Runtime/ShadowLib.csproj` `<Version>`. The release workflow fails fast if they disagree with the git tag — bump them together.

## Files Claude should treat as authoritative

- `README.md` — public-facing install/quick-start.
- `USAGE_GUIDE.md` — module-by-module API guide, includes a "For LLMs" section listing what *not* to hallucinate. **Read this before generating ShadowLib code.**
- `CONTRIBUTING.md` — review checklist + ground rules for new modules.
- `SHADOWLIB_ROADMAP.md` — what's done vs. pending. Roadmap-only APIs do not exist yet.
- `CHANGELOG.md` — keep `[Unreleased]` updated as you change public API.
