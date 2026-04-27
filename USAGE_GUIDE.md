# ShadowLib — Usage Guide

This document is the canonical reference for using ShadowLib. It is written for both human developers and LLM-assisted contributors. Where the two need different guidance, sections are marked accordingly.

> **Pre-release.** Public API may break before `1.0.0`. Pin a specific version when consuming.

---

## What ShadowLib provides

| Module | When to reach for it |
|---|---|
| `ShadowLib.RNG` (Sources, Utilities) | Anything that needs a deterministic random number, seed, or UUID. |
| `ShadowLib.RNG.Distributions` | Weighted / tiered / dice / bag sampling. |
| `ShadowLib.RNG.Noise` | Simplex noise for terrain, density fields, fBm-textured maps. |
| `ShadowLib.RNG.Utilities.ScalingCurve` | Level-driven progression curves (XP, damage scaling, drop weights). |
| `ShadowLib.Spatial` | 2D grids, scalar fields, polyomino shapes. |
| `ShadowLib.Procedural` | Algorithms that operate on `ISpace` — currently bin-packing. |
| `ShadowLib.Optional` (`SHADOWLIB_NCALC` opt-in) | NCalc-driven dynamic weight modifiers + JSON-loaded modifier files. |

If you can't decide between modules, follow the dependency arrow: most things ultimately consume an `IRandom`, which comes from the **`Orchestrator`**. Start there.

---

## Installation in Unity

### Via UPM Git URL

`Window → Package Manager → + → Add package from git URL…`

```
https://github.com/MrRyzen/ShadowLib.git#v0.1.0-preview.3
```

Always pin a tag — `main` will move under you.

### Via local path

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.shadowlib.core": "file:../../path/to/ShadowLib"
  }
}
```

### Referencing from your own asmdef

Add `"ShadowLib"` to the `references` array of your asmdef. The runtime asmdef has `noEngineReferences: true` and `autoReferenced: true`, so for most projects you do **not** need to do anything beyond installing the package — your default `Assembly-CSharp` will see it. Only when you have your own asmdef must you reference it explicitly:

```json
{
    "name": "MyGame.Combat",
    "references": [ "ShadowLib" ],
    "noEngineReferences": false
}
```

---

## Deterministic RNG — the rules

ShadowLib's headline feature is determinism. Everything else assumes you've followed these rules.

### 1. There is exactly one root seed per "world"

```csharp
using ShadowLib.RNG;
using ShadowLib.RNG.Utilities;

ulong root = Seeding.GenerateSeed64("my-world-string");   // Minecraft-style string seed
// or
ulong root = Seeding.GenerateSeed64();                     // OS CSPRNG (non-reproducible)

Orchestrator.Initialize(root);
```

`Orchestrator` is `static` and process-global. Calling `Initialize` again **clears all previously cached RNGs** — call it once at boot, not per-scene-load (unless you intentionally want a hard reset).

### 2. Get one stream per system, named by context

```csharp
var lootRng    = Orchestrator.CreateRNG("loot");
var worldRng   = Orchestrator.CreateRNG("worldgen");
var aiRng      = Orchestrator.CreateRNG("ai.archer.42");   // dot-paths are fine
```

Same context returns the same `IRandom` instance for the lifetime of the process. Same root + same context across runs → identical stream. **Do not** `new Xoshiro128StarStar(...)` directly in feature code — go through `Orchestrator` so streams stay decoupled and reproducible.

### 3. Pass `IRandom` down — don't reach for the orchestrator from leaves

Bad:

```csharp
public class LootRoller {
    public Item Roll() {
        var rng = Orchestrator.CreateRNG("loot");          // tight coupling
        return _table.Sample(rng);
    }
}
```

Good:

```csharp
public class LootRoller {
    private readonly IRandom _rng;
    public LootRoller(IRandom rng) { _rng = rng; }
    public Item Roll() => _table.Sample(_rng);
}
```

The caller decides which context the leaf belongs to. This makes your code testable (pass a fixed-seed `Xoshiro128StarStar`) and lets the same class be used from different streams.

### 4. Do not mix `System.Random`, `UnityEngine.Random`, or `Guid.NewGuid()` into deterministic flows

If you want a deterministic UUID, use:

```csharp
Guid id = UUID.GenerateDeterministicUUID(seed: someSeed, context: rng.NextULong());
```

`UUID.GenerateRandomUUID()` is non-deterministic and only safe for ephemeral IDs (network correlation IDs, log markers).

---

## RNG sources

### `IRandom`

The contract every RNG implements. Keep your code typed against this interface, not concrete classes.

```csharp
int    n  = rng.NextInt();
uint   u  = rng.NextUInt();
ulong  ul = rng.NextULong();
float  f  = rng.NextFloat();           // [0, 1)
double d  = rng.NextDouble();          // [0, 1)
int    r  = rng.Range(0, 10);          // [0, 10)
float  rf = rng.Range(0.0f, 1.0f);     // [0.0, 1.0)
ulong  rl = rng.Range(100UL, 200UL);   // [100, 200)
byte[] s  = rng.GetState();            // checkpoint
rng.SetState(s);                       // restore
```

All `Range(int, int)` / `Range(uint, uint)` / `Range(ulong, ulong)` use **rejection sampling** — they're bias-free across the full numeric range. Don't replace them with `rng.NextInt() % n`.

### `Xoshiro128StarStar` (preferred)

Default implementation produced by `Orchestrator.CreateRNG`. Fast, 128-bit state, uniform statistical properties. Use this everywhere.

### `XorShift128` (legacy — avoid for new code)

Exists for backwards compatibility. **Does not fully implement `IRandom`** (missing `GetState`/`SetState`/`Seed`/`NextUInt`/`NextULong`/`Range(ulong, ulong)`). Treat it as deprecated.

---

## Distributions

### `DynamicWeightTable<T>` — mutable weighted sampling

When weights change at runtime (buffs, gameplay state, modifiers).

```csharp
var table = new DynamicWeightTable<string>();
table.Add("Common",    weight: 70f);
table.Add("Uncommon",  weight: 25f);
table.Add("Rare",      weight: 5f);

string drop = table.Sample(rng);

table.UpdateWeight("Rare", 50f);     // boosted by some buff
string better = table.Sample(rng);

table.ResetWeights();                // back to original base weights
```

Maintains a **base** and **current** weight per entry. `ResetWeights()` restores base; `Add` always sets both.

### `AliasTable<T>` — immutable, O(1) sampling

When the distribution is fixed at build time and you sample millions of times (loot tables, biome distributions, NPC name pools).

```csharp
var alias = new AliasTable<string>(new (string, float)[] {
    ("Forest", 50f), ("Plains", 30f), ("Desert", 15f), ("Tundra", 5f)
});
string biome = alias.Sample(rng);   // O(1)
```

Cannot be modified after construction. If you need mutation, use `DynamicWeightTable<T>`.

### `TieredTable<T>` — rarity-keyed dynamic tables

For loot tables stratified by tier / rarity / level.

```csharp
var loot = new TieredTable<string>();
loot.AddTier(1, new (string, float)[] { ("Sword", 20f), ("Vest", 20f), ("Potion", 25f) });
loot.AddTier(2, new (string, float)[] { ("Cleaver", 5f), ("Plate", 10f) });

string[] firstChest  = loot.Sample(rng, tier: 1, drops: 3);
string[] bossChest   = loot.Sample(rng, tier: 2, drops: 2);
```

Each tier is a `DynamicWeightTable<T>` under the hood — apply per-tier modifiers via `loot.GetTier(tier).UpdateWeight(...)`.

### `RandomBag<T>` — draw without replacement

Tetris-style "shuffle the whole bag, then deal one piece at a time".

```csharp
var bag = new RandomBag<string>(new[] { "I", "O", "T", "S", "Z", "L", "J" }, rng);
string next = bag.Draw();   // each piece appears exactly once before any repeats
```

### `Dice<T>` — N-sided die with mappable faces

```csharp
var d6   = new Dice<int>(rng, new[] { 1, 2, 3, 4, 5, 6 });
var loot = new Dice<string>(rng, new[] { "gold", "gold", "gold", "potion", "trap", "rare" });
string roll = loot.Sample();
```

Throws on empty face arrays.

---

## Noise

### `SimplexNoise`

```csharp
using ShadowLib.RNG.Noise;

var noise = new SimplexNoise(rng);

float h2d = noise.Sample(x, y);                   // 2D, ~[-1, 1]
float h3d = noise.Sample(x, y, z);                // 3D, ~[-1, 1]
float fbm = noise.Fractal(x, y, octaves: 4);      // fBm layered noise
float fbm3 = noise.Fractal(x, y, z, octaves: 4, persistence: 0.5f, lacunarity: 2.0f);
```

Always seed it from an `IRandom` (don't construct an `IRandom` just for noise — pull one from the `Orchestrator`). The internal permutation table is Fisher-Yates shuffled at construction; same seed → identical noise field.

---

## Scaling curves

Zero-allocation `readonly struct`. Build once, call `Evaluate` per frame freely.

```csharp
using ShadowLib.RNG.Utilities;

var xp        = ScalingCurve.Exponential(start: 100f, rate: 0.15f);
var damage    = ScalingCurve.LinearClamped(start: 10f, rate: 2f, cap: 100f);
var rarity    = ScalingCurve.Logarithmic(start: 1f, rate: 0.5f);
var dropTier  = ScalingCurve.Stepped((1, 10f), (5, 20f), (10, 30f));

float xpReq = xp.Evaluate(playerLevel);
float dmg   = damage.Evaluate(playerLevel);
```

`Stepped` returns the value of the **first step whose threshold the level has not yet reached** (and the last step's value once past all thresholds). Read it as "the prize you're working toward at this level," not "the prize you've earned." With steps `(1, 10), (5, 20), (10, 30)`:

| `level` | `Evaluate` |
|---|---|
| 0 | 10 |
| 1 | 20 |
| 4 | 20 |
| 5 | 30 |
| 99 | 30 |

---

## Spatial

### `Polyomino`

Immutable shape made of cell offsets, anchored at (0, 0).

```csharp
using ShadowLib.Spatial;

var rect   = Polyomino.FromRect(width: 2, height: 3);
var custom = Polyomino.FromOffsets(new (int Col, int Row)[] {
    (0, 0), (1, 0), (1, 1), (2, 1)   // an "S"
});

int n = rect.CellCount;       // 6
int w = rect.Width;           // 2
int h = rect.Height;          // 3
foreach (var (c, r) in rect.Offsets) { /* zero-alloc enumeration */ }
```

`FromOffsets` automatically normalizes so the minimum column and row are both 0 — pass any anchor, get a canonical shape.

### `Grid2D<TCell, TOccupant>`

Dense flat-array-backed 2D grid. Cells are user-defined and created via factory.

```csharp
public class MyCell : ICell<MyItem> {
    public int X { get; private set; }
    public int Y { get; private set; }
    public MyItem Occupant { get; set; }
    public bool IsOccupied { get; private set; }
    public void Initialize(int x, int y) { X = x; Y = y; }
    public bool Contains(MyItem item) => IsOccupied && Occupant.Equals(item);
    public void Clear() { Occupant = default; IsOccupied = false; }
    public void Occupy(MyItem item) { Occupant = item; IsOccupied = true; }
}

var grid = new Grid2D<MyCell, MyItem>(
    width: 16, height: 16, cellSize: 1f,
    cellFactory: (x, y) => new MyCell()
);

bool ok = grid.CanOccupy(x: 2, y: 3, Polyomino.FromRect(2, 2));
grid.Occupy(2, 3, item, Polyomino.FromRect(2, 2));
grid.Free(item);                            // O(1) thanks to internal occupancy index
var occupied = grid.GetOccupiedCells();     // Dictionary<TOccupant, ...>
```

The internal `Dictionary<TOccupant, HashSet<int>>` index is what makes `Free(occupant)` O(1) — keep `TOccupant` cheap to hash.

### `FieldGrid<T>` — value-type scalar field

When each cell is "just a number" (density, heat, influence). No occupancy index, no `ICell` ceremony.

```csharp
var density = new FieldGrid<float>(width: 64, height: 64, cellSize: 1f);
density[10, 5] = 0.8f;
ref float cell = ref density.GetRef(10, 5);  // mutate in place
density.Fill(0f);
Span<float> row = density.GetRowSpan(5);     // mutable span over a single row
```

### `ICell<T>` / `ISpace<T>`

Code your spatial algorithms against the interfaces, not `Grid2D`. Anything that operates on "a thing that holds polyomino-shaped occupants" — including `BinPacker` — lives at the `ISpace` level so future spatial backends drop in cleanly.

---

## Procedural

### `BinPacker<TOccupant>`

```csharp
using ShadowLib.Procedural;

var packer = new BinPacker<MyItem>(grid);

// Strategies — all return (col, row)? = null when nothing fits
var pos1 = packer.FirstFit(shape);     // top-left scan, fastest
var pos2 = packer.BestFit(shape);      // tightest neighbour-contact score
var pos3 = packer.BottomLeft(shape);   // gravity-style
var all  = packer.AllFits(shape);      // every valid position

// One-shot place
bool ok = packer.TryPlace(item, shape);

// Sort items by decreasing footprint and pack greedily
var unplaced = packer.AutoSort(new (MyItem item, Polyomino shape)[] {
    (sword,  Polyomino.FromRect(1, 2)),
    (vest,   Polyomino.FromRect(2, 2)),
    (potion, Polyomino.FromRect(1, 1)),
});
```

`AutoSort` returns the items it could **not** place; an empty result means everything fit.

---

## Optional module — opt-in features

`Runtime/Optional/` holds the parts that depend on third-party assemblies (NCalc, System.Text.Json). It's a **separate asmdef** with `defineConstraints: ["SHADOWLIB_NCALC"]`.

To enable in Unity:

1. Install NCalc into your project (NuGetForUnity is the simplest path).
2. `Project Settings → Player → Scripting Define Symbols` → add `SHADOWLIB_NCALC`.
3. Reference `ShadowLib.Optional` from any asmdef that needs it.

Then:

```csharp
using ShadowLib.RNG.Modifiers;
using ShadowLib.RNG.Utilities;

var modifiers = ModifierLoader.LoadModifiers<string>("Assets/loot-modifiers.json");

var ctx = new Dictionary<string, object> {
    ["PlayerLevel"] = 12,
    ["Environment"] = "Dungeon",
};
var evaluator = new ConditionEvaluator();

foreach (var mod in modifiers) {
    if (evaluator.Evaluate(mod.Condition, ctx))
        ApplyModifier(table, mod);
}
```

In a non-Unity .NET project this is always available — the .csproj pulls NCalc from NuGet directly, no opt-in needed.

---

## Performance / zero-allocation patterns

- **`Polyomino.Offsets` returns a `ReadOnlySpan`** — iterate without allocating.
- **`ScalingCurve` is a `readonly struct`** — keep curves as fields, evaluate in hot paths without GC pressure.
- **`Grid2D` uses a flat backing array** with a precomputed occupancy index — `Free(occupant)` is O(1).
- **`AliasTable` sample is O(1)** with no per-call allocation.
- Prefer `Range(int, int)` over `(int)(NextFloat() * n)` — the former is bias-free.
- Avoid LINQ in hot paths; the library never uses LINQ internally and you shouldn't either at scale.
- `Orchestrator.CreateRNG("ctx")` re-uses the same instance for the same context — *don't* call it per frame; cache the returned `IRandom`.

---

## Common mistakes to avoid

| Mistake | Fix |
|---|---|
| Calling `new Xoshiro128StarStar(seed)` directly | Use `Orchestrator.CreateRNG("context")` so streams stay decoupled. |
| Calling `Orchestrator.CreateRNG` from a leaf method every call | Cache the returned `IRandom` in a field. |
| Re-`Initialize`-ing the orchestrator mid-game | This wipes all cached streams. Initialize once at boot. |
| Using `XorShift128` for new code | It doesn't fully implement `IRandom`. Use `Xoshiro128StarStar`. |
| `rng.NextInt() % n` for ranged ints | Biased. Use `rng.Range(0, n)`. |
| Importing `UnityEngine.*` in `Runtime/` | The asmdef has `noEngineReferences: true`. Keep engine code in `Editor/` or in user-side asmdefs. |
| Editing `Samples~/` expecting Unity to see it on import | `Samples~/` only ships when imported via Package Manager → "Import" button. The tilde keeps it dormant. |
| Reading `DateTime.Now` for seeding | Non-deterministic. Use `Seeding.GenerateSeed64("string")` or a stored `ulong`. |
| Mutating an `AliasTable` after construction | It's immutable by design. Use `DynamicWeightTable<T>` if you need writes. |

---

## For LLMs

Read this section before generating ShadowLib code.

### How to choose the correct API

1. **Need a random number?** → `Orchestrator.CreateRNG("context").NextInt()` / `Range(...)`. Never `System.Random`, never `UnityEngine.Random`, never `Guid.NewGuid()`.
2. **Need weighted picking?**
   - Mutable weights → `DynamicWeightTable<T>`.
   - Immutable + hot loop → `AliasTable<T>`.
   - Stratified by tier/rarity → `TieredTable<T>`.
   - Each item exactly once before repeats → `RandomBag<T>`.
   - Mappable faces with equal weight → `Dice<T>`.
3. **Need a 2D grid?**
   - With occupants and shapes → `Grid2D<TCell, TOccupant>`.
   - Just scalar values → `FieldGrid<T>`.
4. **Need to place items in a grid?** → `BinPacker<TOccupant>` against any `ISpace<TOccupant>`.
5. **Need a deterministic ID?** → `UUID.GenerateDeterministicUUID(seed, context)`.
6. **Need a level-driven curve?** → `ScalingCurve.{Linear, Exponential, Logarithmic, Stepped}`.

### Safe defaults

- For a single throwaway demo: `Orchestrator.Initialize(Seeding.GenerateSeed64("demo")); var rng = Orchestrator.CreateRNG("main");`
- For tests: pass a fresh `Xoshiro128StarStar(123UL)` directly — no orchestrator, fully controlled.
- For a Unity sample: a `MonoBehaviour` with `[Tooltip]`-decorated `string seedString` and `Orchestrator.Initialize(Seeding.GenerateSeed64(seedString))` in `Start()`.

### Do not hallucinate these

The following do **not** exist (as of `0.1.0-preview.3`):

- `UUID.V4()` / `UUID.V5()` — the methods are `GenerateRandomUUID()` and `GenerateDeterministicUUID(ulong seed, ulong context)`.
- `Orchestrator.GetRng(string)` — it's `CreateRNG(string)`, which both creates and caches.
- `Polyomino.Generate(rng, cellCount, compactness)` — listed in the roadmap as **pending**, not yet implemented.
- `RandomWalk`, `SpacedPlacement`, `CellularAutomata`, `SparseGraph<T>.FindPath`, `SparseGraph<T>.GetNodesInRange` — all roadmap-only. Do not write code that calls these.
- `EffectWheel<,>`, `IEffect<>` — roadmap-only.
- `IRandom.NextBool()` / `IRandom.Pick(...)` — not on the interface. Use `rng.Range(0, 2) == 0` and `RandomUtils.RandomSelect(list, rng)`.
- `RandomUtils.Select(...)` — the method is named `RandomSelect`. There is also `RandomUtils.Chance(probability, rng)` and `RandomUtils.Shuffle(array, rng)` / `Shuffle(list, rng)`.
- A `using ShadowLib;` namespace shortcut — there is no such namespace; use `ShadowLib.RNG`, `ShadowLib.Spatial`, etc. explicitly.

If unsure whether something exists, check `SHADOWLIB_ROADMAP.md` (status table at the bottom) before writing code that calls it.

### How to generate valid examples

A valid ShadowLib example has these in order, top to bottom:

1. `using` directives for the specific sub-namespaces being used (`ShadowLib.RNG`, `ShadowLib.RNG.Distributions`, etc.).
2. `Orchestrator.Initialize(Seeding.GenerateSeed64("..."))` exactly once at the top.
3. One or more `var rng = Orchestrator.CreateRNG("context-name")`, cached in a local or field.
4. Whatever distribution / grid / packer the example demonstrates, taking the cached `IRandom`.
5. No `using UnityEngine` if the file is meant to compile in `Runtime/` — only in `Samples~/`, `Editor/`, or user-game code.

When asked for a "minimal example," produce the smallest snippet that still includes `Initialize` + `CreateRNG`. Skipping seeding is the most common mistake — don't skip it.
