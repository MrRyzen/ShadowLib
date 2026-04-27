# ShadowLib Roadmap

---

## Implemented

### RNG Core

- **`IRandom`** — Interface for all RNG sources. State save/restore, seeding, full numeric range generation with bias-free rejection sampling.
- **`Xoshiro128StarStar`** — Primary RNG. 128-bit state, SplitMix64 seeding, deterministic. Implements `IRandom`.
- **`XorShift128`** — Legacy/lightweight RNG. Does **not** implement `IRandom` (missing `GetState`/`SetState`/`Seed`/`NextUInt`/`NextULong`/`Range(ulong,ulong)`).
- **`Orchestrator`** — Central RNG factory. Root seed + context-derived deterministic streams via `CreateRNG(string context)`.
- **`Seeding`** — 32-bit and 64-bit seed generation (OS CSPRNG, string-based SHA-256, deterministic derivation via FNV-1a + SplitMix64).
- **`UUID`** — Random (v4) and deterministic (v5, RFC 4122) UUID generation with ShadowLib namespace.

### RNG Distributions

- **`DynamicWeightTable<T>`** — Mutable weighted sampling with base/dynamic weight split, binary search selection, bulk operations (normalize, uniform, reset).
- **`AliasTable<T>`** — O(1) weighted sampling via alias method. Immutable after construction.
- **`TieredTable<T>`** — Tier-keyed collection of `DynamicWeightTable<T>` instances for level/rarity-gated loot.
- **`RandomBag<T>`** — Shuffled draw-without-replacement bag.
- **`Dice<T>`** — Generic N-sided dice with mappable face values.

### RNG Utilities

- **`ScalingCurve`** — Zero-alloc `Curve` struct with Linear, Exponential, Logarithmic, and Stepped evaluation + optional cap.
- **`RandomUtils`** — Fisher-Yates shuffle (array + list), random select, probability chance roll.

### RNG Modifiers

- **`WeightModifier<T>`** — Struct-based modifier with condition string, target, operation (Add/Multiply/Set), and stage ordering.
- **`ConditionEvaluator`** — NCalc-based condition evaluation with custom functions (`contains`, `distance`, `count`).
- **`ModifierLoader`** — JSON deserialization of `WeightModifier<T>` collections from config files.

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
- **`Polyomino`** — Immutable shape struct from rect or explicit offsets, normalized to (0,0) anchor. Bounding box metadata.

**Future consideration:** `CanOccupyCell(x, y, w, h)` region check is O(w\*h). For large grids + large entity footprints (RTS base placement, 1000x1000 influence maps), consider adding an occupancy bitmask or 2D prefix-sum occupancy map for O(1) rect queries.

### Procedural

- **`BinPacker<TOccupant>`** — Polyomino bin packing on any `ISpace`. Strategies: FirstFit, BestFit (neighbor-contact scoring), BottomLeft. Includes `TryPlace`, `CanFit`, `AllFits`, and `AutoSort` (repack by decreasing size).

---

## Not Yet Implemented

### IEffect\<TTarget\> + EffectWheel\<TEffect, TTarget\>

Weighted effect selection + application. Combines `DynamicWeightTable` sampling with an `Apply()` call.

```csharp
public interface IEffect<TTarget>
{
    void Apply(TTarget target);
}

var wheel = new EffectWheel<PunishmentEffect, Player>();
wheel.Add(new PunishmentEffect("Drain", 25), weight: 20f);
wheel.Add(new PunishmentEffect("Lucky Escape", 1), weight: 15f);

PunishmentEffect result = wheel.SampleAndApply(rng, player);
```

### SparseGraph\<T\>

Coordinate-keyed node graph. `Dictionary<(int,int), T>` backed with bidirectional edges, BFS, A*, range queries, connected component analysis.

```csharp
var graph = new SparseGraph<CellData>();
var a = graph.AddNode(0, 0, new CellData());
var b = graph.AddNode(1, 0, new CellData());
graph.Connect(a, b);

List<Node<CellData>> path = graph.FindPath(a, target);
List<Node<CellData>> inRange = graph.GetNodesInRange(a, steps: 3);
int dist = graph.Distance(a, target);
List<List<Node<CellData>>> pockets = graph.FindConnectedComponents();
```

### Polyomino Generator

Generate N-cell shapes with tunable compactness. Grows from a seed cell, picking frontier cells weighted by neighbor count.

```csharp
// Compact bias → rectangular shapes (military rig)
var shape = Polyomino.Generate(rng, cellCount: 12, compactness: 0.9f);

// Low bias → sprawling shapes (scavenged pouch)
var shape = Polyomino.Generate(rng, cellCount: 8, compactness: 0.2f);

// Returns List<(int Col, int Row)> — cell positions relative to origin
```

### Random Walk

Biased directional walk with boundary clamping. Configurable step weights.

```csharp
var path = RandomWalk.Generate(rng,
    length: 10,
    bounds: (8, 6),
    bias: new DirectionBias(forward: 0.6f, up: 0.2f, down: 0.2f));

// Returns List<(int, int)> positions
```

### Spaced Placement

Place N items on a set of candidate positions with minimum distance constraints.

```csharp
var positions = SpacedPlacement.Place(rng,
    candidates: allNodes,
    count: 3,
    minDistance: 2,
    distanceFunc: (a, b) => graph.Distance(a, b));
```

### Cellular Automata

Step-based grid simulation. Define birth/survival rules, run N iterations. Operates on `FieldGrid<bool>`.

```csharp
var grid = new FieldGrid<bool>(width, height, cellSize: 1f);
// ... randomize fill ...

var ca = new CellularAutomata(birthRule: 5, surviveRule: 4);
ca.Step(grid, iterations: 4);

// Result: organic cave shapes on a dense grid
```

### BSP, WFC, Maze Gen

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
| 8 | `IEffect<T>` + `EffectWheel<T,T>` | `DynamicWeightTable` | Low | Pending |
| 9 | `SparseGraph<T>` | — | Medium | Pending |
| 10 | Connected Component Analysis | `SparseGraph` | Low | Pending |
| 11 | `Polyomino Generator` | `IRandom` | Low | Pending |
| 12 | `Random Walk` | `IRandom` | Low | Pending |
| 13 | `Spaced Placement` | — | Low | Pending |
| 14 | `Cellular Automata` | `FieldGrid` | Medium | Pending |
| 15 | BSP, WFC, Maze Gen | — | High | Pending |
