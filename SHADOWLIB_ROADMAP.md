# ShadowLib Roadmap

---

## Implemented

### RNG Core

- **`IRandom`** — Interface for all RNG sources. State save/restore, seeding, full numeric range generation with bias-free rejection sampling.
- **`Xoshiro128StarStar`** — Primary RNG. 128-bit state, SplitMix64 seeding, deterministic. Implements `IRandom`.
- **`Xoshiro128`** — The same generator as an unmanaged struct for Unity jobs, Burst and ECS. Identical output to `Xoshiro128StarStar` for the same seed; does not implement `IRandom`.
- **`Orchestrator`** — Instance-based RNG factory owned by the caller. One root seed, three stream kinds: `CreateRNG` (long-lived, cached), `Derive` (throwaway, same stream every call), `Next` (throwaway, next in a replayable series). `DeriveSeed` / `NextSeed` for jobs, `Fork` for child orchestrators, `Save` / `Load` to a `byte[]`. Thread-safe.
- **`Seeding`** — 32-bit and 64-bit seed generation (OS CSPRNG, string-based SHA-256, deterministic derivation via FNV-1a + SplitMix64, plus an arithmetic-only `DeriveSeed64(root, index)` for jobs).
- **`UUID`** — Random (v4) and deterministic (v5, RFC 4122) UUID generation with ShadowLib namespace.

### RNG Distributions

- **`DynamicWeightTable<T>`** — Weighted sampling whose odds follow game state. Designed weight per item plus pull-based influence (`SetInfluence(item, min, max, Func<float>)`, read at pick time), enabled flags, depletable stock, repeat prevention, `SampleDistinct`, `TrySample` and `GetProbability`. One RNG draw per pick.
- **`IEffect<TTarget>` + `EffectWheel<TEffect, TTarget>`** — A `DynamicWeightTable` of effects with `SampleAndApply(rng, target)`.
- **`AliasTable<T>`** — O(1) weighted sampling via alias method. Immutable after construction.
- **`TieredTable<T>`** — Tier-keyed collection of `DynamicWeightTable<T>` instances for level/rarity-gated loot.
- **`RandomBag<T>`** — Shuffled draw-without-replacement bag with cycle-based auto-refill (opt-out) and manual `Refill()`.
- **`Dice<T>`** — Generic N-sided dice with mappable face values.
- **`MarkovChain<T>`** — First-order weighted Markov chain. Mutable build phase (`AddTransition` / `AddSequence`), compiled to flat CSR + per-row alias tables (Vose) for O(1), zero-alloc stepping (`Next` / `TryNext` / `Walk`).

### RNG Utilities

- **`ScalingCurve`** — Zero-alloc `Curve` struct with Linear, Exponential, Logarithmic, and Stepped evaluation + optional cap.
- **`RandomUtils`** — Fisher-Yates shuffle (array + list), random select, probability chance roll.

### RNG Conditions (optional module)

- **`ConditionEvaluator`** — NCalc-based condition evaluation with custom functions (`contains`, `distance`, `count`).

### RNG Noise

- **`SimplexNoise`** — 2D and 3D deterministic simplex noise seeded via `IRandom`. Fisher-Yates shuffled permutation table, pre-computed mod12, `AggressiveInlining`. Includes `Fractal()` (fBm) with configurable octaves, persistence, and lacunarity. See `ShadowLib/RNG/Noise/`.

```csharp
var noise = new SimplexNoise(rng);
float value = noise.Sample(x, y);              // 2D, ~[-1, 1]
float value = noise.Sample(x, y, z);           // 3D, ~[-1, 1]
float fbm   = noise.Fractal(x, y, octaves: 4); // fBm layered noise
```

### Spatial

- **`ICell<TOccupant>`** — Cell interface for grid occupancy (coordinates, occupy/clear/contains).
- **`ISpace<TOccupant>`** — Spatial container interface for polyomino-shaped occupancy (canOccupy, occupy, free).
- **`Grid2D<TCell, TOccupant>`** — Dense flat-array-backed 2D grid with zero-alloc Span queries, internal occupancy index, world-space conversion, neighbor queries (4/8-way), range/rect queries, struct enumerator.
- **`FieldGrid<T>`** — Lightweight struct-value grid for scalar fields (density maps, heatmaps, influence grids). Ref access, mutable row spans, bulk fill/copy/snapshot.
- **`Polyomino`** — Immutable shape struct from rect or explicit offsets, normalized to (0,0) anchor. Bounding box metadata. `Polyomino.Generate` grows a seeded N-cell shape with tunable compactness and an optional size limit.

- **`Polycube`** — Immutable 3D shape struct from a box, explicit offsets, or an extruded `Polyomino`. Normalized to a (0,0,0) anchor. `Polycube.Generate` is the 3D shape generator, same growth rule as `Polyomino.Generate`.
- **`SpatialGraph3D<TOccupant>`** — 3D inventory box that wears out (a Tarkov-style backpack with depth). Pockets of cells, all-or-nothing `Polycube` item placement, cells that die and can be repaired (`TryKill`, seeded `TryKillRandom`, `Repair`), and `FindPockets` for which living cells still form one compartment. Killing a cell reports what was on it and leaves the item in place.

- **Grid pathfinding (`ShadowLib.Spatial.Pathfinding`)** — `GridAStar` (cheapest path on weighted terrain), `GridJumpPointSearch` (walkable/blocked maps, fastest on rooms and corridors) and `GridFlowField` (one field for any number of agents sharing goals). Static functions over a byte cost map and caller-owned `Span` buffers: no allocation per query, integer costs, four-way or eight-way movement, and callable from Burst jobs. `GridPathfinder` wraps them for `Grid2D` in (x, y) terms.

**Future consideration:** `CanOccupyCell(x, y, w, h)` region check is O(w\*h). For large grids + large entity footprints (RTS base placement, 1000x1000 influence maps), consider adding an occupancy bitmask or 2D prefix-sum occupancy map for O(1) rect queries.

### Procedural

- **`BinPacker<TOccupant>`** — Polyomino bin packing on any `ISpace`. Strategies: FirstFit, BestFit (neighbor-contact scoring), BottomLeft. Includes `TryPlace`, `CanFit`, `AllFits`, and `AutoSort` (repack by decreasing size).
- **`RandomWalk` + `DirectionBias`** — Seeded walk over a bounded grid, one orthogonal step at a time, with four direction weights. Steps that would leave the grid are never picked; the walk ends early if it has nowhere to go. Array and allocation-free `Span` forms. One RNG draw per step.
- **`SpacedPlacement`** — Picks up to N candidates that keep a minimum distance from each other, using a caller-supplied distance function. Visits candidates in a seeded random order and keeps each that is clear of the ones already kept; one draw per candidate examined.
- **`CellularAutomata`** — Birth / survive threshold rules stepped over a `FieldGrid<bool>` (eight neighbours, optional solid border), plus a seeded `Randomize` fill. No allocation per step once its scratch buffer fits the grid.
- **`MarkovWordGenerator`** — Character-level order-N Markov word generation for procedural naming. `^`/`$` start/end markers, min/max length bounds, bounded retry, allocation-free `TryGenerate(Span<char>)`. Built on `MarkovChain<string>` k-gram states.

---

## Not Yet Implemented

### BSP, WFC, Maze Gen (deferred)

Deferred on 2026-11-26: not to be built until a game needs one. No API is designed; each needs its own short design conversation first.

Advanced procedural generation algorithms. High complexity, no dependencies on unbuilt items.

---

## Implementation Order

| # | Addition | Depends on | Complexity | Status |
|---|---|---|---|---|
| 1 | `ScalingCurve` | — | Low | **Done** |
| 2 | `TieredTable<T>` | — | Low | **Done** |
| 3 | `Grid2D<TCell, TOccupant>` | — | Medium | **Done** |
| 4 | `FieldGrid<T>` | — | Medium | **Done** |
| 5 | `Polyomino` (struct) | — | Low | **Done** |
| 6 | `BinPacker<T>` | `ISpace`, `Polyomino` | Medium | **Done** |
| 7 | `SimplexNoise` | `IRandom` | Medium | **Done** |
| 8 | `IEffect<T>` + `EffectWheel<T,T>` | `DynamicWeightTable` | Low | **Done** |
| 9 | `SpatialGraph3D` + `Polycube` (3D inventory that wears out, with pocket analysis) | `Polyomino` | Medium | **Done** |
| 10 | Grid pathfinding: A*, Jump Point Search, flow fields | `Grid2D` | Medium | **Done** |
| 11 | `Polyomino Generator` + `Polycube Generator` | `IRandom` | Low | **Done** |
| 12 | `Random Walk` | `IRandom` | Low | **Done** |
| 13 | `Spaced Placement` | — | Low | **Done** |
| 14 | `Cellular Automata` | `FieldGrid` | Medium | **Done** |
| 15 | BSP, WFC, Maze Gen | — | High | Deferred until a game needs them |
| 16 | `MarkovChain<T>` | `IRandom` | Medium | **Done** |
| 17 | `MarkovWordGenerator` | `MarkovChain` | Low | **Done** |
