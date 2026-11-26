# Changelog

All notable changes to ShadowLib are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Sub-1.0 minor bumps may break public API.

## [Unreleased]

### Added

- `Orchestrator.Derive(context)`: a throwaway generator that yields the same stream on every call for a context. Nothing is stored.
- `Orchestrator.Next(context)`: a throwaway generator that is the next in a replayable per-context series.
- `Orchestrator.DeriveSeed(context)` / `NextSeed(context)`: the seeds behind `Derive` / `Next`, for passing into jobs.
- `Orchestrator.Save()` / `Load(ReadOnlySpan<byte>)`: a versioned `byte[]` holding the root seed, every long-lived stream's state and every `Next` series position. `Load` restores streams in place and throws `InvalidDataException` on bad input without changing anything.
- `Orchestrator.Fork(context)` for an independent child orchestrator, and `Orchestrator.RootSeed`.
- `Xoshiro128`: the xoshiro128** generator as an unmanaged struct for Unity jobs, Burst and ECS. Same output as `Xoshiro128StarStar` for the same seed, verified under Burst 2.0.0 in the default and `Deterministic` float modes (`FloatMode.Fast` changes the low bits of the float and double `Range` overloads only).
- `Seeding.DeriveSeed64(ulong rootSeed, ulong index)`: arithmetic-only seed derivation, usable inside jobs.
- `Orchestrator`'s stream and seed members are thread-safe. Generators remain single-threaded, and `Save` / `Load` must not overlap with draws from a `CreateRNG` stream on another thread.
- `DynamicWeightTable<T>.SetInfluence(item, min, max, Func<float>)` / `ClearInfluence`: an external value from -1 to 1 moves an item's weight between `min` and `max`, read on every pick. The designed weight is never overwritten.
- `DynamicWeightTable<T>.SetEnabled` / `IsEnabled`: take an item out of the pool without removing it.
- `DynamicWeightTable<T>.SetStock` / `ClearStock` / `TryGetStock` / `Refill` / `RefillAll`: limited stock per item; a pick spends one unit.
- `DynamicWeightTable<T>.PreventRepeat`, with `TryGetLastPicked` / `SetLastPicked` / `ClearLastPicked`: the last pick is left out of the next one, without an extra draw.
- `DynamicWeightTable<T>.SampleDistinct(rng, Span<T>)`: several different items in one call.
- `DynamicWeightTable<T>.TrySample` and `GetProbability`.
- `IEffect<TTarget>` and `EffectWheel<TEffect, TTarget>` with `SampleAndApply(rng, target)`.
- `Polycube`: a 3D shape (`FromBox`, `FromOffsets`, `FromPolyomino`), the counterpart of `Polyomino`.
- `SpatialGraph3D<TOccupant>` is now a complete 3D inventory that wears out: `Polycube` items placed across layers, `TryKill` / `TryKillRandom(rng)` returning a `KilledCell<TOccupant>` and leaving the item in place, `Repair`, `FindPockets`, plus `Exists`, `IsAlive`, `IsOccupied`, `TryGetOccupant`, `Contains`, `GetCells`, `Volume`, `ExistingCount`, `AliveCount` and an optional occupant comparer.

### Removed

- **Breaking:** `DynamicWeightTable<T>.AddWeight`, `MultiplyWeight`, `ResetWeights`, `UniformWeights`, `NormalizeWeights`, `GetBaseWeight`, `TryGetBaseWeight` and `SetBaseWeight`, along with the base/dynamic weight split. For odds that change with game state use `SetInfluence`; to take an item out use `SetEnabled(item, false)` or `TryRemove`; for a permanent change use `SetWeight`.
- **Breaking:** `WeightModifier<T>`, `ModifierOperation` and `ModifierLoader` (namespace `ShadowLib.RNG.Modifiers`). Nothing in the library applied them. `ConditionEvaluator` stays and can drive an influence source. The library no longer uses `System.Text.Json`.
- **Breaking:** `SparseGraph<TOccupant>`. It was untested scaffolding with no users; `SpatialGraph3D` no longer sits on top of it.
- **Breaking:** from `SpatialGraph3D<TOccupant>`: the `Graph` property, `AddNode` (now `AddCell`), `NodeExists` (now `Exists`), `ConnectAdjacent` and `ConnectLayers` (touching cells are always neighbours), the `initialCapacity` constructor parameter, and the `Polyomino` overloads of `AddPocket` / `CanPlace` / `Place` (pass `Polycube.FromPolyomino(shape)`). `AddPocket` now throws when the shape leaves the box instead of clipping it, and `Free` returns whether the item was placed.
- **Breaking:** static `Orchestrator.Initialize(seed)` and static `Orchestrator.CreateRNG(context)`. Replace with `var orchestrator = new Orchestrator(seed);` and `orchestrator.CreateRNG(context)`. Streams from `CreateRNG` are unchanged for the same root seed and context.
- **Breaking:** the implicit OS-entropy root seed. `Orchestrator` now always needs an explicit seed; pass `Seeding.GenerateSeed64()` for a non-repeatable run.
- **Breaking:** `XorShift128`. It never implemented `IRandom`; use `Xoshiro128StarStar` or `Xoshiro128`.

### Changed

- **Breaking:** `Orchestrator` is an instance class instead of a static one, so it can be owned by your own manager, one per world or save slot.
- `Xoshiro128StarStar` is now a thin `IRandom` wrapper over `Xoshiro128`; its output is unchanged.
- **Breaking:** `DynamicWeightTable<T>.Add` throws `ArgumentException` for an item that is already in the table (it used to add a second entry). This also applies to `TieredTable<T>.AddTier`.
- **Breaking:** `DynamicWeightTable<T>.SetWeight` now sets the item's one designed weight; there is no separate base weight to reset to.
- **Behavior change:** `DynamicWeightTable<T>.Sample` throws `InvalidOperationException` whenever nothing is pickable (all items disabled, out of stock or influenced to zero), not only when the table is empty. Results for existing tables with unique items are unchanged for the same seed.
- **Behavior change:** `DynamicWeightTable<T>.Add` and `SetWeight` reject `NaN` and infinite weights with `ArgumentOutOfRangeException`, and picking throws `OverflowException` if the weights sum past `float.MaxValue` (it used to pick lopsidedly).
- `DynamicWeightTable<T>` recomputes effective weights on every `Sample`, `TotalWeight` and `GetProbability` call, an O(n) pass instead of a cached binary search, because influence is read at pick time. For large fixed tables sampled in a hot loop, use `AliasTable<T>`.
- `TieredTable<T>.AddTier` leaves an existing tier untouched if the new items are rejected.

- **Behavior change:** `Seeding.DeriveSeed(uint, string)` now hashes the context with FNV-1a instead of summing its characters, so contexts that are anagrams of each other (`"chunk/1/2"` vs `"chunk/2/1"`) no longer collide. Seeds derived with the 32-bit overload differ from earlier previews; `DeriveSeed64` (and therefore `Orchestrator`) is unchanged.
- **Behavior change:** `BinPacker.AutoSort` breaks ties between equally sized shapes by input order. Previously the order of ties depended on the runtime's unstable sort, so layouts with many same-sized items may differ from earlier previews.
- `Grid2D.GetCellsForOccupant` returns indices in ascending order (and keeps the lowest ones when the buffer is too small) instead of exposing `HashSet` enumeration order.
- Float math in `Range(float, float)`, `SimplexNoise`, `ScalingCurve`, `RandomUtils.Chance` and the alias-table builds is pinned to single precision with explicit casts, so results match on runtimes that evaluate `float` expressions in double (Mono without `float32`). Output on .NET is unchanged.
- `RandomBag<T>.Sample` is O(1) instead of O(n); draw order is unchanged.
- `UUID.GenerateDeterministicUUID` no longer allocates and is roughly 2.5x faster; output is unchanged on little-endian platforms and now identical on big-endian ones too.

### Fixed

- `ConditionEvaluator` matched custom function names with the current culture, so `CONTAINS(...)` / `DISTANCE(...)` evaluated to `false` under Turkish locales. Matching is now culture-invariant.
- `ConditionEvaluator` no longer blocks NCalc's built-in functions (`Max`, `Round`, `Abs`, ...), which previously always evaluated to `false`.

## [0.1.0-preview.5] — 2026-07-25

### Added

- `RandomBag<T>.Refill()` — discards the current cycle and reshuffles the bag's full contents, and an `autoRefill` constructor parameter (default `true`).

### Changed

- **Behavior change:** `RandomBag<T>` now auto-refills by default — exhausting a cycle reshuffles all items and continues (Tetris 7-bag semantics), matching what the documentation already described. The previous drain-once-then-throw behavior is available via `new RandomBag<T>(items, rng, autoRefill: false)`. `HasNext` now reports `true` whenever a refill is available.

## [0.1.0-preview.4] — 2026-07-25

### Added

- `MarkovChain<T>` (`ShadowLib.RNG.Distributions`) — first-order weighted Markov chain. Transitions accumulate via `AddTransition` / `AddSequence`, then compile into a flat CSR layout with per-row alias tables (Vose's method) so `Next` / `TryNext` / `Walk` are O(1) per step and allocation-free. Deterministic given the same build sequence and `IRandom`.
- `MarkovWordGenerator` (`ShadowLib.Procedural`) — character-level order-N Markov word generation for procedural naming (companies, products, people). Trains on word lists with `^`/`$` start/end markers, generates length-bounded words with bounded retry, and offers an allocation-free `TryGenerate(Span<char>)` alongside a convenience `Generate`. Built on `MarkovChain<string>`.

### Fixed

- Documentation samples corrected to match the actual API: `DynamicWeightTable.UpdateWeight` → `SetWeight`, `RandomBag.Draw` → `Sample`, `FieldGrid` indexer/`GetRowSpan` → `Set`/`GetRowMutable`, `AliasTable` tuple-array constructor → `(items, weights)` lists, `BinPacker.AllFits` buffer signature, `TryPlace` nullable-position return, `AutoSort` returns *placed* (not unplaced) items, `TieredTable.Sample` parameter name `drops`, and a nonexistent `IRandom.Range(uint, uint)` mention.

## [0.1.0-preview.3] — 2026-04-27

### Fixed

- `WeightModifier.Stage`: removed the `= 999` auto-property initializer that required C# 10+ and broke compilation on Unity 2021.3 LTS (C# 9 — auto-property initializers in structs require C# 10).

### Changed

- **Behavior change:** `WeightModifier.Stage` now defaults to `0` instead of `999`. Modifiers constructed without an explicit `Stage` will now apply *first* rather than *last*. Set `Stage` explicitly on any modifier whose ordering matters.
- `.gitignore`: added `.claude/` so local Claude Code session state stays out of the repo.

## [0.1.0-preview.2] — 2026-04-27

### Added

- Unity `.meta` files for every shipped asset (root manifests, `Runtime/`, `Tests/`). Without these, Unity refused to import the package ("immutable folder, asset will be ignored").
- `scripts/generate-unity-meta.sh` — idempotent generator that creates missing meta files with deterministic, path-derived GUIDs. Documented in `CONTRIBUTING.md`.
- `scripts/generate-unity-meta.sh`: macOS `md5 -q` fallback when `md5sum` isn't on `PATH`, and optional support for `.uss`, `.uxml`, `.asset`, `.shader`, `.compute` assets.
- `ci.yml` `unity-meta-check` job: runs the meta generator on every push/PR and fails if it produces uncommitted `.meta` drift.

### Changed

- Converted all 31 `.cs` files from file-scoped to block-scoped namespaces so the library stays compilable on Unity 2021.3 LTS (C# 9, which doesn't support file-scoped namespaces). Public API is unchanged.

### Fixed

- CI/release workflows: bumped `actions/setup-node` to Node 22 LTS (silences the Node 20 deprecation warning) and made `npm pack --pack-destination` resilient to a missing destination directory.

## [0.1.0-preview.1] — 2026-04-27

Initial pre-release. Repository restructured for Unity UPM consumption alongside the existing .NET solution.

### Added

- Unity package layout at the repo root: `package.json`, `Runtime/`, `Tests/Runtime/`, `Samples~/BasicUsage/`.
- Unity asmdefs:
  - `ShadowLib.asmdef` (Runtime, `noEngineReferences: true`, no external dependencies).
  - `ShadowLib.Optional.asmdef` (under `Runtime/Optional/`, gated by the `SHADOWLIB_NCALC` scripting define) for NCalc condition strings + JSON modifier loader.
  - `ShadowLib.Tests.asmdef` (gated by `UNITY_INCLUDE_TESTS`).
  - `ShadowLib.Samples.BasicUsage.asmdef`.
- `.NET` test project at `Tests/Runtime/ShadowLib.Tests.csproj` (NUnit), with sanity tests for `Seeding`, `Orchestrator`, `ScalingCurve`, `Polyomino`, and `Dice`.
- GitHub Actions:
  - `ci.yml` — restore / build / test / dry-run pack on every push and PR.
  - `release.yml` — tag-driven build that attaches DLL, NuGet `.nupkg`, and UPM `.tgz` to the GitHub release.
- `.gitignore` covering .NET, Rider, Visual Studio, VS Code, and Unity-generated files.
- `.npmignore` so `npm pack` produces a clean UPM tarball without the .NET solution + tooling.
- `README.md`, `USAGE_GUIDE.md`, `CONTRIBUTING.md`, `LICENSE.md`.

### Changed

- Library source moved from `ShadowLib/` to `Runtime/` to match Unity package layout. Namespaces are unchanged.
- `Runtime/ShadowLib.csproj` gained NuGet packaging metadata (`PackageId`, `Version`, `Description`, license, README packaging).
- `ShadowLib.sln` updated to point at the new `Runtime/` and `Tests/Runtime/` paths; removed dead third-project GUID.
- `ShadowLib.Runner/ShadowLib.Runner.csproj` reference updated to `..\Runtime\ShadowLib.csproj`.
- `ConditionEvaluator.cs` and `ModifierLoader.cs` relocated to `Runtime/Optional/` so the main asmdef stays dependency-free.

### Known limitations

- Pre-1.0; public API may break before `1.0.0`.
- `XorShift128` does not fully implement `IRandom` (legacy).
- Roadmap items not yet implemented: `EffectWheel`, `SparseGraph<T>`, `Polyomino.Generate`, `RandomWalk`, `SpacedPlacement`, `CellularAutomata`, BSP / WFC / Maze gen.

[Unreleased]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.5...HEAD
[0.1.0-preview.5]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.4...v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.3...v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.2...v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/MrRyzen/ShadowLib/releases/tag/v0.1.0-preview.1
