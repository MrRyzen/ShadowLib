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
| `ShadowLib.Procedural` | Generation algorithms — bin-packing over `ISpace`, Markov word generation. |
| `ShadowLib.Optional` (`SHADOWLIB_NCALC` opt-in) | NCalc-driven dynamic weight modifiers + JSON-loaded modifier files. |

If you can't decide between modules, follow the dependency arrow: most things ultimately consume an `IRandom`, which comes from an **`Orchestrator`**. Start there.

---

## Installation in Unity

### Via UPM Git URL

`Window → Package Manager → + → Add package from git URL…`

```
https://github.com/MrRyzen/ShadowLib.git#v0.1.0-preview.5
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

var orchestrator = new Orchestrator(root);
```

`Orchestrator` is an ordinary class with no global state. Create one per world, run or save slot and keep it in your own manager; two orchestrators never affect each other. The root seed is always explicit: there is no parameterless constructor.

### 2. Get streams from the orchestrator, named by context

There are three kinds. Pick by how long the generator lives and whether a repeat should give the same numbers.

```csharp
// Long-lived: one stream per system, continues where it left off.
IRandom lootRng = orchestrator.CreateRNG("loot");
IRandom aiRng   = orchestrator.CreateRNG("ai.archer.42");   // dot-paths are fine

// Throwaway, repeatable: the same stream every time for the same context.
IRandom chunkRng = orchestrator.Derive("chunk/3/4");        // regenerate chunk (3,4) identically, any time, in any order

// Throwaway, in series: each call is the next stream for that context.
IRandom firstChest  = orchestrator.Next("chest");           // chest #0
IRandom secondChest = orchestrator.Next("chest");           // chest #1, different from #0
```

| Method | Lifetime | Same context again gives | Stored in the orchestrator |
|---|---|---|---|
| `CreateRNG(context)` | Keep it | The same instance, further along | The stream |
| `Derive(context)` | Use and drop | A new generator with the identical stream | Nothing |
| `Next(context)` | Use and drop | A new generator with the next stream in the series | One counter per context |

Same root seed + same calls across runs → identical streams. The three kinds never collide: `CreateRNG("loot")`, `Derive("loot")` and `Next("loot")` are unrelated streams.

Prefer `Derive` / `Next` over `new Xoshiro128StarStar(seed)` for throwaways, so they are tied to the root seed and replay with it.

`Fork(context)` returns an independent child orchestrator with its own derived root seed, for handing a subsystem its own. The parent does not track children: save each child yourself.

### 3. Save and load

```csharp
byte[] save = orchestrator.Save();     // write these bytes wherever your save data lives
// ...
orchestrator.Load(save);
```

A save holds the root seed, the state of every `CreateRNG` stream and the position of every `Next` series. `Derive` needs nothing saved.

- `Load` restores streams **in place**: `IRandom` references you already hold stay valid and continue from the saved point.
- A `CreateRNG` stream that exists but is not in the save restarts from its seed under the loaded root.
- After loading, `Next("chest")` continues the series (chest #2 after two saved calls), it does not start over.
- Bad input (wrong version, truncated, trailing bytes) throws `System.IO.InvalidDataException` and changes nothing.

### 4. Threads, Unity jobs, Burst and ECS

Every `Orchestrator` member is safe to call from multiple threads. The generators it returns are not:

- **One generator per thread or per job iteration.** Never share an `IRandom` or `Xoshiro128` between threads.
- **`Derive` / `DeriveSeed` are order-independent**, so they are safe to call from anywhere and still replay.
- **`Next` / `NextSeed` hand out series positions in call order.** Calling them from racing threads is safe but not replayable. Call them from one thread and pass the result in.

Burst cannot use classes, interfaces or strings, so jobs use the `Xoshiro128` struct and numeric seeds:

```csharp
ulong seed = orchestrator.DeriveSeed("chunks");              // main thread, once

struct ChunkJob : IJobParallelFor
{
    public ulong Seed;
    public void Execute(int index)
    {
        var rng = new Xoshiro128(Seeding.DeriveSeed64(Seed, (ulong)index));
        float density = rng.NextFloat();
    }
}
```

`Xoshiro128` is an unmanaged struct with the same methods and the same output as `Xoshiro128StarStar` for the same seed. It is mutable: keep it in a local or a non-readonly field, and pass it by `ref` if a callee should advance it. The .NET test suite checks that it is unmanaged and matches the class bit for bit; it does not run the Burst compiler.

### 5. Pass `IRandom` down — don't reach for the orchestrator from leaves

Bad:

```csharp
public class LootRoller {
    private readonly Orchestrator _orchestrator;
    public Item Roll() {
        var rng = _orchestrator.CreateRNG("loot");         // tight coupling
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

### 6. Do not mix `System.Random`, `UnityEngine.Random`, or `Guid.NewGuid()` into deterministic flows

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

All `Range(int, int)` / `Range(ulong, ulong)` use **rejection sampling** — they're bias-free across the full numeric range. Don't replace them with `rng.NextInt() % n`.

### `Xoshiro128StarStar` (preferred)

The `IRandom` implementation every `Orchestrator` method returns. Fast, 128-bit state, uniform statistical properties. Use this everywhere you need an `IRandom`.

### `Xoshiro128` (struct, for jobs)

The same generator as an unmanaged value type: no `IRandom`, no references, identical output for the same seed. Use it inside Unity jobs, Burst code and ECS components; see "Threads, Unity jobs, Burst and ECS" above.

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

table.SetWeight("Rare", 50f);        // boosted by some buff (base weight untouched)
string better = table.Sample(rng);

table.ResetWeights();                // back to original base weights
```

Maintains a **base** and **current** weight per entry. `ResetWeights()` restores base; `Add` always sets both.

### `AliasTable<T>` — immutable, O(1) sampling

When the distribution is fixed at build time and you sample millions of times (loot tables, biome distributions, NPC name pools).

```csharp
var alias = new AliasTable<string>(
    items:   new[] { "Forest", "Plains", "Desert", "Tundra" },
    weights: new[] { 50f, 30f, 15f, 5f });
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

Each tier is a `DynamicWeightTable<T>` under the hood — apply per-tier modifiers via `loot.GetTier(tier).SetWeight(...)`.

### `RandomBag<T>` — draw without replacement

Tetris-style "shuffle the whole bag, then deal one piece at a time". When a cycle is exhausted the bag reshuffles its full contents and starts a new one, so every item appears exactly once per cycle and the bag never runs dry.

```csharp
var bag = new RandomBag<string>(new[] { "I", "O", "T", "S", "Z", "L", "J" }, rng);
string next = bag.Sample();   // each piece appears exactly once before any repeats
string peek = bag.Peek();     // look without removing; bag.HasNext / bag.Count to check state
```

To drain the bag once and throw when empty instead, pass `autoRefill: false`; `bag.Refill()` restarts a drained bag manually either way. `Add(item)` joins both the current cycle and every future refill.

### `Dice<T>` — N-sided die with mappable faces

```csharp
var d6   = new Dice<int>(rng, new[] { 1, 2, 3, 4, 5, 6 });
var loot = new Dice<string>(rng, new[] { "gold", "gold", "gold", "potion", "trap", "rare" });
string roll = loot.Sample();
```

Throws on empty face arrays.

### `MarkovChain<T>` — weighted state transitions

For state sequences where the next state depends on the current one: weather systems, AI behavior drift, procedural name/text generation.

```csharp
var weather = new MarkovChain<string>();
weather.AddTransition("Sunny", "Sunny", 0.7f);
weather.AddTransition("Sunny", "Cloudy", 0.3f);
weather.AddTransition("Cloudy", "Rainy", 0.5f);
weather.AddTransition("Cloudy", "Sunny", 0.5f);
weather.AddTransition("Rainy", "Cloudy", 1f);
weather.Build();                                   // optional — compiles lazily otherwise

string tomorrow = weather.Next("Sunny", rng);       // O(1), zero-alloc
if (weather.TryNext(state, rng, out var next)) { }  // false for terminal/unknown states

var forecast = new string[7];
int days = weather.Walk("Sunny", rng, forecast);    // fills span, stops at terminal states
```

Training from observed sequences (each adjacent pair accumulates weight):

```csharp
var names = new MarkovChain<char>();
names.AddSequence("kael".AsSpan());
names.AddSequence("karn".AsSpan());
```

Internally compiles to a flat CSR layout with per-row alias tables (Vose's method) — stepping is O(1) regardless of how many transitions a state has, and allocation-free. Mutating after a build marks it dirty; the next step recompiles. Call `Build()` at load time to keep gameplay frames allocation-free. First-order only — for order-N, use a composite state type (e.g. a struct holding the last N states).

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

Always seed it from an `IRandom` (don't construct an `IRandom` just for noise — pull one from your `Orchestrator`). The internal permutation table is Fisher-Yates shuffled at construction; same seed → identical noise field.

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
density.Set(10, 5, 0.8f);
ref float cell = ref density.GetRef(10, 5);   // mutate in place
density.Fill(0f);
Span<float> row = density.GetRowMutable(5);   // mutable span over a single row
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

// Every valid position, written into a caller-owned buffer (returns the count)
Span<(int col, int row)> fits = stackalloc (int, int)[64];
int found = packer.AllFits(shape, fits);

// One-shot place — returns the anchor position, or null when it didn't fit
var placedAt = packer.TryPlace(item, shape);

// Sort items by decreasing footprint and pack greedily
var placed = packer.AutoSort(new (MyItem item, Polyomino shape)[] {
    (sword,  Polyomino.FromRect(1, 2)),
    (vest,   Polyomino.FromRect(2, 2)),
    (potion, Polyomino.FromRect(1, 1)),
});
```

`AutoSort` frees and repacks everything, returning the successfully placed items with their new positions — anything missing from the result did **not** fit.

### `MarkovWordGenerator` — character-level name generation

For procedural naming (companies, products, people): trains an order-N character Markov chain on curated word lists, then generates new words that resemble — but don't copy — the training data.

```csharp
var gen = new MarkovWordGenerator(order: 2);   // next char predicted from the previous 2
gen.Train(new[] {
    "Novartis", "Raytheon", "Blackstone", "Palantir", "Salesforce",
    "Lockheed", "Theranos", "Northrop", "Medtronic", "Crowdstrike"
});
gen.Build();                                    // optional — compiles lazily otherwise

string word = gen.Generate(rng, minLength: 4, maxLength: 10);
// e.g. "Raytheonic", "Lockstrop", "Blanos"

// Allocation-free variant for hot paths — caller owns the buffer
Span<char> buffer = stackalloc char[10];
if (gen.TryGenerate(rng, buffer, out int length, minLength: 4, maxAttempts: 50))
    UseName(buffer.Slice(0, length));
```

Training words are padded with `^` (start) and `$` (end) markers internally; each attempt walks the chain from the start state and restarts if it ends outside the length bounds or dead-ends, up to `maxAttempts`. `Generate` throws when attempts are exhausted; `TryGenerate` returns `false` so you can fall back to an authored name.

The generator only produces raw words. Composition (suffixes like "Inc.", sector vocabulary, patterns), duplicate prevention, and content validation are game-side concerns — run each candidate through your own checks and retry:

```csharp
string name;
do { name = gen.Generate(companyNameRng); }
while (usedNames.Contains(Normalize(name)));
```

Case is preserved from training data. Use separate generator instances per category (company names, first names, last names) — each with its own training list and `IRandom` stream.

Loading training lists is deliberately your concern — the library takes strings, not file paths, so it works the same on desktop, Android, and WebGL:

```csharp
// Plain .NET — one word per line in a .txt file
gen.Train(File.ReadAllLines("company_prefixes.txt"));

// Unity — ship the list as a TextAsset
gen.Train(listAsset.text.Split('\n')
    .Select(w => w.Trim())          // strips \r on Windows-authored files
    .Where(w => w.Length > 0));     // Train rejects empty words
```

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
- `orchestrator.CreateRNG("ctx")` re-uses the same instance for the same context — *don't* call it per frame; cache the returned `IRandom`.
- `Derive` and `Next` allocate one small generator per call. In a hot loop or a job, take `DeriveSeed` / `NextSeed` once and build `Xoshiro128` structs from `Seeding.DeriveSeed64(seed, index)` instead.
- Don't use `CreateRNG` with per-object contexts like `"chunk/3/4"`: every context is cached and saved. That is what `Derive` is for.

---

## Common mistakes to avoid

| Mistake | Fix |
|---|---|
| Calling `new Xoshiro128StarStar(seed)` with a hand-made seed | Use `orchestrator.Derive("context")` or `Next("context")` so the generator replays with the root seed. |
| Calling `orchestrator.CreateRNG` from a leaf method every call | Cache the returned `IRandom` in a field. |
| Using `CreateRNG` for per-chunk or per-entity contexts | Each one is cached and saved forever. Use `Derive`. |
| Creating a new `Orchestrator` mid-game to "reset" | Streams from the old one keep their old state. Use `Load`, or replace the orchestrator and re-fetch every stream. |
| Calling `Next` from several threads at once | Safe, but the order is not replayable. Call it from one thread. |
| Sharing one `IRandom` or `Xoshiro128` across threads or jobs | Generators are single-threaded. One per thread or job iteration. |
| Storing `Xoshiro128` in a `readonly` field | It's a mutable struct; a readonly copy never advances. |
| `rng.NextInt() % n` for ranged ints | Biased. Use `rng.Range(0, n)`. |
| Importing `UnityEngine.*` in `Runtime/` | The asmdef has `noEngineReferences: true`. Keep engine code in `Editor/` or in user-side asmdefs. |
| Editing `Samples~/` expecting Unity to see it on import | `Samples~/` only ships when imported via Package Manager → "Import" button. The tilde keeps it dormant. |
| Reading `DateTime.Now` for seeding | Non-deterministic. Use `Seeding.GenerateSeed64("string")` or a stored `ulong`. |
| Mutating an `AliasTable` after construction | It's immutable by design. Use `DynamicWeightTable<T>` if you need writes. |

---

## For LLMs

Read this section before generating ShadowLib code.

### How to choose the correct API

1. **Need a random number?** → get an `IRandom` from an `Orchestrator` instance, then `NextInt()` / `Range(...)`. Never `System.Random`, never `UnityEngine.Random`, never `Guid.NewGuid()`.
   - One stream for a whole system → `orchestrator.CreateRNG("context")`, cached.
   - Throwaway that must be identical every time (a chunk, an entity by ID) → `orchestrator.Derive("context")`.
   - Throwaway for a repeated event (each chest, each wave) → `orchestrator.Next("context")`.
   - Inside a Unity job / Burst → `new Xoshiro128(Seeding.DeriveSeed64(seed, index))` with `seed` from `orchestrator.DeriveSeed("context")`.
2. **Need weighted picking?**
   - Mutable weights → `DynamicWeightTable<T>`.
   - Immutable + hot loop → `AliasTable<T>`.
   - Stratified by tier/rarity → `TieredTable<T>`.
   - Each item exactly once before repeats → `RandomBag<T>`.
   - Mappable faces with equal weight → `Dice<T>`.
   - Next state depends on current state → `MarkovChain<T>`.
   - Generated names/words from example lists → `MarkovWordGenerator` (in `ShadowLib.Procedural`).
3. **Need a 2D grid?**
   - With occupants and shapes → `Grid2D<TCell, TOccupant>`.
   - Just scalar values → `FieldGrid<T>`.
4. **Need to place items in a grid?** → `BinPacker<TOccupant>` against any `ISpace<TOccupant>`.
5. **Need a deterministic ID?** → `UUID.GenerateDeterministicUUID(seed, context)`.
6. **Need a level-driven curve?** → `ScalingCurve.{Linear, Exponential, Logarithmic, Stepped}`.

### Safe defaults

- For a single throwaway demo: `var orchestrator = new Orchestrator(Seeding.GenerateSeed64("demo")); var rng = orchestrator.CreateRNG("main");`
- For tests: pass a fresh `Xoshiro128StarStar(123UL)` directly — no orchestrator, fully controlled.
- For a Unity sample: a `MonoBehaviour` with `[Tooltip]`-decorated `string seedString` and `new Orchestrator(Seeding.GenerateSeed64(seedString))` in `Start()`.

### Do not hallucinate these

The following do **not** exist (as of `0.1.0-preview.5`):

- `UUID.V4()` / `UUID.V5()` — the methods are `GenerateRandomUUID()` and `GenerateDeterministicUUID(ulong seed, ulong context)`.
- `Orchestrator.GetRng(string)` — it's `CreateRNG(string)`, which both creates and caches.
- `Orchestrator.Initialize(seed)` and static `Orchestrator.CreateRNG(...)` — removed. `Orchestrator` is an instance: `new Orchestrator(seed)`, then `orchestrator.CreateRNG(...)`.
- `new Orchestrator()` with no seed — there is no parameterless constructor.
- `XorShift128` — removed. Use `Xoshiro128StarStar` (class, `IRandom`) or `Xoshiro128` (struct, jobs).
- `Xoshiro128` implementing `IRandom` — it does not; it is a plain struct. The `IRandom` implementation is `Xoshiro128StarStar`.
- `Orchestrator.Save()` returning a typed snapshot or JSON — it returns `byte[]`; `Load` takes `ReadOnlySpan<byte>` (a `byte[]` converts implicitly).
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
2. `var orchestrator = new Orchestrator(Seeding.GenerateSeed64("..."))` exactly once at the top.
3. One or more `var rng = orchestrator.CreateRNG("context-name")`, cached in a local or field.
4. Whatever distribution / grid / packer the example demonstrates, taking the cached `IRandom`.
5. No `using UnityEngine` if the file is meant to compile in `Runtime/` — only in `Samples~/`, `Editor/`, or user-game code.

When asked for a "minimal example," produce the smallest snippet that still includes `new Orchestrator(seed)` + `CreateRNG`. Skipping seeding is the most common mistake — don't skip it.
