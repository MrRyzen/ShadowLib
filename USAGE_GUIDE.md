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
| `ShadowLib.Spatial` | 2D grids, scalar fields, polyomino shapes, and a 3D inventory box that wears out. |
| `ShadowLib.Spatial.Pathfinding` | A*, Jump Point Search and flow fields on a grid; allocation-free and Burst-ready. |
| `ShadowLib.Procedural` | Generation algorithms — bin-packing over `ISpace`, Markov word generation, random walks, spaced placement, cellular automata. |
| `ShadowLib.Optional` (`SHADOWLIB_NCALC` opt-in) | NCalc condition strings (`ConditionEvaluator`), e.g. to drive a table's influence from data. |

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

Same root seed + same calls across runs → identical streams. Within one orchestrator the three kinds are unrelated: `CreateRNG("loot")`, `Derive("loot")` and `Next("loot")` give different streams. Each kind mixes a fixed constant into the root seed, so two *different* orchestrators could share a stream across kinds only if their root seeds differ by exactly one of those constants; roots from `Seeding.GenerateSeed64` or `Fork` will not.

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

The `Orchestrator` members that hand out streams and seeds (`CreateRNG`, `Derive`, `Next`, `DeriveSeed`, `NextSeed`, `Fork`) are safe to call from multiple threads. The generators they return are not:

- **One generator per thread or per job iteration.** Never share an `IRandom` or `Xoshiro128` between threads.
- **`Save` and `Load` need the long-lived streams to be idle.** They read and write the `CreateRNG` streams directly, so call them only while no other thread is drawing from one (finish or pause worker jobs first). A save taken mid-draw can capture a half-advanced stream that does not replay.
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

`Xoshiro128` is an unmanaged struct with the same methods and the same output as `Xoshiro128StarStar` for the same seed. It is mutable: keep it in a local or a non-readonly field, and pass it by `ref` if a callee should advance it. 

**Burst and bit-exact replay.** Checked under Burst 2.0.0 in Unity 6000.6 against managed `Xoshiro128StarStar`, 16,384 draws per method:

| Burst `FloatMode` | Integer draws, `Range(int)`, `Range(ulong)`, `NextFloat`, `NextDouble`, final state | `Range(float, float)`, `Range(double, double)` |
|---|---|---|
| `Default` | Identical | Identical |
| `Deterministic` | Identical | Identical |
| `Fast` | Identical | About 1 in 4 results differ in the low bits |

So for replay across Burst and managed code, leave `FloatMode` at its default (or use `Deterministic`), or use only the integer and `NextFloat` / `NextDouble` draws in `Fast` jobs. Your own float math is subject to the same rule: a bound like `0.37f + index * 0.011f` computed once inside a Burst job and once in managed code can differ in the last bit, which then changes `Range(min, max)`. Compute such inputs in one place and pass them in.

What has been compared bit for bit (2026-11-26, one x64 machine, Unity 6000.6, Burst 2.0.0): raw draws, seeding, orchestrator streams and saves, every distribution, noise, curves, the shape and map generators and the pathfinders all matched across Windows .NET 8, Linux .NET 8 and 9, the Unity editor, and built Windows and Linux players on both Mono and IL2CPP. Nothing has been run on ARM. `Tests/Runtime/DeterminismFingerprint.cs` is the code that was hashed; run its `WriteFingerprint` test on two runtimes and compare the files to repeat the check.

This check lives in a Unity project, not in this repo's .NET test suite, which only proves the struct is unmanaged and matches the class bit for bit. Those Burst tests run in the editor only; the built IL2CPP players were covered by the fingerprint comparison above, not by them.

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

### `DynamicWeightTable<T>` — weighted sampling that follows game state

When odds depend on what is happening in the game (buffs, corruption, distance, limited stock).

```csharp
var table = new DynamicWeightTable<string>();
table.Add("Common",    weight: 70f);
table.Add("Uncommon",  weight: 25f);
table.Add("Rare",      weight: 5f);

// Influence: luck runs from -1 to 1 and moves "Rare" between weight 1 and 50.
// The table reads it on every pick; the designed weight (5) is never overwritten.
table.SetInfluence("Rare", min: 1f, max: 50f, () => player.Luck);

table.SetEnabled("Uncommon", false);         // stays in the table, never picked
table.SetStock("Rare", 2);                   // only two Rares until Refill
table.PreventRepeat = true;                  // never the same item twice in a row

float rareOdds = table.GetProbability("Rare");   // 0..1, what the next Sample would use
string drop = table.Sample(rng);                 // throws if nothing is pickable
bool ok = table.TrySample(rng, out string maybe);

var offer = new string[3];
int count = table.SampleDistinct(rng, offer);    // up to 3 different items
```

- **Influence.** `-1` gives `min`, `0` the designed weight, `+1` gives `max`, linear in between. Values outside `-1..1` are clamped and `NaN` counts as `0`. `min` may be `0`, which switches the item off at `-1`. The range must satisfy `0 <= min <= weight <= max`. `ClearInfluence(item)` removes it. The source must not call back into the same table.
- **Designed weight.** `SetWeight(item, weight)` changes it permanently. There is no separate "current" weight to reset.
- **Stock.** A pick spends one unit; at zero the item is unpickable until `Refill(item)` / `RefillAll()`. `ClearStock(item)` makes it unlimited again.
- **Repeat prevention.** The last pick is left out of the next one unless it is the only pickable item. This does not re-roll, so it never costs an extra draw.
- **`SampleDistinct`** returns how many items it wrote, which is fewer than requested when the pickable items run out. It reads each influence source once and only its first item is subject to repeat prevention.
- **Determinism.** Every pick is exactly one `rng.NextFloat()`, and none when nothing is pickable, so the same seed with the same game state replays exactly.
- **Items are unique.** `Add` throws `ArgumentException` for an item already in the table.
- **Weights are single-precision.** `NaN` and infinite weights are rejected, and picking throws `OverflowException` if the weights sum past `float.MaxValue`. An item whose weight is tiny next to the total (1 beside 100,000,000) can round away and never be picked, so keep weights within a few orders of magnitude of each other.
- **Cost.** Each `Sample`, `TotalWeight` and `GetProbability` is one pass over the items. For a large fixed table in a hot loop use `AliasTable<T>`.

Stock and the last pick are state the table does not save. Store and restore them yourself:

```csharp
// save
bool stocked = table.TryGetStock("Rare", out int unitsLeft);
bool hasLast = table.TryGetLastPicked(out string lastPick);

// load
if (stocked) table.SetStock("Rare", unitsLeft, maxUnits: 2);
if (hasLast) table.SetLastPicked(lastPick);
```

### `EffectWheel<TEffect, TTarget>` — pick an effect and apply it

A `DynamicWeightTable<TEffect>` whose items implement `IEffect<TTarget>`. Everything above (influence, stock, repeat prevention) works on it.

```csharp
public sealed class Punishment : IEffect<Player>, IEquatable<Punishment>
{
    public string Name;
    public int Damage;
    public void Apply(Player target) => target.Health -= Damage;
    public bool Equals(Punishment other) => other != null && other.Name == Name;
}

var wheel = new EffectWheel<Punishment, Player>();
wheel.Add(drain, weight: 20f);
wheel.Add(luckyEscape, weight: 15f);
wheel.SetInfluence(drain, min: 5f, max: 60f, () => player.Corruption);

Punishment applied = wheel.SampleAndApply(rng, player);
```

The pick is committed (stock spent, last pick recorded) before `Apply` runs. Effects should be classes or stateless structs: a struct effect is applied on a copy.

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

Each tier is a `DynamicWeightTable<T>` under the hood, so influence, stock and the rest are available through `loot.GetTier(tier)`, for example `loot.GetTier(2).SetInfluence("Cleaver", 1f, 20f, () => player.Luck)`. A tier's items must be unique. `Sample(rng, tier, drops)` picks with replacement and throws if the tier runs out of pickable items part-way.

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
// Linear, Exponential and Stepped give the same bits on every platform.
// Logarithmic uses MathF.Log, which is not guaranteed to: do not feed it into anything that must replay across Windows and Linux.

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

#### Generated shapes

```csharp
var rig   = Polyomino.Generate(rng, cellCount: 12, compactness: 0.9f);                        // blocky
var pouch = Polyomino.Generate(rng, cellCount: 8,  compactness: 0.2f, maxWidth: 4, maxHeight: 3); // sprawling, but fits 4x3
var lump  = Polycube.Generate(rng, cellCount: 10, compactness: 0.7f, maxWidth: 4, maxHeight: 4, maxDepth: 2);
```

- The shape grows one cell at a time from a single cell; every cell shares an edge (2D) or a face (3D) with the rest. `Offsets` lists the cells in the order they were grown.
- `compactness` runs 0 to 1: 0 prefers spots touching one cell (thin, sprawling), 0.5 treats all spots equally, 1 prefers spots touching many cells (dense, blocky).
- The `max` limits cap the bounding box, so a shape always fits a container of that size. `maxDepth: 1` gives a flat `Polycube`. Asking for more cells than the limits hold throws.
- Exactly `cellCount - 1` `rng.NextFloat()` draws. Same generator state and arguments give the same shape on every platform.

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

### `SpatialGraph3D<TOccupant>` + `Polycube` — a 3D inventory that wears out

A backpack with depth: a `width × height × layers` box where only the cells you add exist (the pockets), items are 3D shapes, and damage kills cells. For flat inventories use `Grid2D`.

```csharp
using ShadowLib.Spatial;

var pack = new SpatialGraph3D<Item>(width: 7, height: 4, layers: 3);
pack.AddPocket(Polycube.FromBox(4, 4, 3), x: 0, y: 0);            // main compartment
pack.AddPocket(Polycube.FromBox(2, 2, 1), x: 5, y: 0);            // side pouch, one column away so it is its own pocket

var crateShape = Polycube.FromBox(2, 2, 2);
if (pack.CanPlace(0, 0, 0, crateShape))
    pack.Place(0, 0, 0, ammoCrate, crateShape);                   // all cells or none

// Damage: the library reports, your game decides.
int dropChance = 25;
if (pack.TryKillRandom(rng, out var dead) && dead.WasOccupied)
{
    if (rng.Range(0, 100) < dropChance) pack.Free(dead.Occupant); // item falls out
    else dropChance += 25;                                        // it hangs on; try harder next time
}

pack.TryKill(1, 0, 0, out _);       // kill a specific cell
pack.Repair(1, 0, 0);               // bring it back

var pocketIds = new int[pack.Volume];
int pockets = pack.FindPockets(pocketIds);   // which living cells still form one compartment
```

- **Shapes.** `Polycube.FromBox(w, h, d)`, `Polycube.FromOffsets((col, row, layer), ...)`, or `Polycube.FromPolyomino(flatShape, depth)` to extrude a 2D shape. Shapes do not rotate: build the rotated one (`FromBox(1, 1, 5)` instead of `FromBox(5, 1, 1)`).
- **Killing a cell never moves or removes an item.** `TryKill` / `TryKillRandom` hand back a `KilledCell` (`X`, `Y`, `Layer`, `WasOccupied`, `Occupant`). The item stays on all its cells, dead one included, until you `Free` it. Check `WasOccupied` before reading `Occupant`.
- **A dead cell rejects new placements.** An item you free from a damaged spot cannot go back there; it needs a spot where every cell is alive. `Repair` revives a cell; adding cells never does.
- **`TryKillRandom` replays.** It makes exactly one `rng.Range(0, AliveCount)` call and none when nothing is alive, so the same seed damages the same cells.
- **Pockets.** Living cells that touch face to face (not diagonally) are one pocket. `pocketIds` is indexed by `pack.GetIndex(x, y, layer)`; dead and missing cells get `-1`.
- **An item is in the backpack once.** Placing it again throws; move it with `Free` then `Place`. Items are told apart by the comparer you pass to the constructor (default equality otherwise).
- **Looking things up.** `Exists`, `IsAlive`, `IsOccupied`, `TryGetOccupant` and `CanPlace` return false outside the box instead of throwing. `GetCells(item, buffer)` lists where an item sits.
- **Rebuilding a saved backpack:** add the cells, place the items, then kill the dead cells. No other order can recreate an item sitting on a dead cell.
- **Limits.** Memory is proportional to `Volume` (the whole box), so this suits backpacks, not huge sparse spaces. It is not an `ISpace`, so `BinPacker` does not work on it, and it has no pathfinding or distance queries.

---

### Pathfinding — A*, Jump Point Search and flow fields

Three searches over the same map format, in `ShadowLib.Spatial.Pathfinding`. Pick by load:

| You have | Use |
|---|---|
| Units with their own goals on terrain with different costs | A* (`uniformCost: false`, the default) |
| Walkable/blocked maps made of rooms, corridors or caves | Jump Point Search (`uniformCost: true`) |
| Many agents heading for the same goal or goals | A flow field, built once and read by every agent |

#### With a `Grid2D`

```csharp
using ShadowLib.Spatial.Pathfinding;

var pathfinder = new GridPathfinder(grid.Width, grid.Height);
pathfinder.Bake(grid, cell => cell.IsOccupied ? (byte)0 : cell.MoveCost);   // 0 = blocked, 1-255 = cost to enter

Span<(int X, int Y)> path = stackalloc (int X, int Y)[256];
PathStatus status = pathfinder.FindPath((2, 3), (40, 17), path, out int length, out int cost, allowDiagonal: true);
if (status == PathStatus.Found)
    foreach (var (x, y) in path[..length]) { /* walk it */ }

pathfinder.BuildFlowField(stackalloc[] { (40, 17) }, allowDiagonal: true);
if (pathfinder.TryGetFlowStep(agentX, agentY, out int stepX, out int stepY))
    MoveAgent(stepX, stepY);
```

- `PathStatus.NoPath` means the start or goal is blocked or nothing connects them. `PathBufferTooSmall` means a path exists but does not fit: `length` then holds the size needed, and nothing was written. Pass an empty span to ask only "is it reachable, and at what cost".
- The pathfinder does not watch the grid. After the grid changes, call `Bake` again, or `SetCost(x, y, cost)` for the cells that changed. Changing costs through `Bake`, `SetCost` or `EditCosts()` makes the flow field stale: `TryGetFlowStep` returns false until `BuildFlowField` runs again.
- One `GridPathfinder` is for one thread. It allocates its buffers on first use; after that only a goal list longer than any before allocates.
- `Costs` is a read-only view. For bulk edits call `EditCosts()`, which hands out the writable span and marks the flow field stale; ask for it again for each round of edits rather than keeping it.

#### Costs and movement

- A step costs `GridPathing.OrthogonalStep` (10) or `GridPathing.DiagonalStep` (14) times the cost of the cell being entered. The cell you start on is free. Everything is integer maths, so results are identical on every platform and Burst float mode.
- Movement is four-way unless you pass `allowDiagonal: true`. A diagonal step needs both cells it passes between to be walkable, so paths never squeeze past a corner.
- Jump Point Search ignores weights: any non-zero cell costs 1. It returns the full cell-by-cell path, like A*.
- A route whose total cost would not fit in an `int` is reported as `NoPath`.

#### Which is faster

Measured on a 512x512 map in plain .NET (`GridPathfindingBenchmark`, eight-way, per query): on a map of 32x32 rooms with one door per wall, A* 1.8 ms and JPS 0.09 ms; on generated caves, A* 0.68 ms and JPS 0.24 ms; on a completely open map, A* 0.05 ms and JPS 0.39 ms. So use JPS for rooms, corridors and caves, and A* for wide-open ground or weighted terrain. The gap on open ground is wider for four-way movement (A* 0.12 ms, JPS 1.2 ms) and for short trips, because a JPS scan runs to the map edge however near the goal is. A flow field for the whole map took 13 to 30 ms to build; reading it is one array lookup per agent.

#### In jobs / Burst

The static functions take spans and plain integers, so a Burst job can call them directly. Cells are indices (`y * width + x`). Give each worker its own scratch; any number of workers can read the same cost map.

```csharp
[BurstCompile]
struct FindPathJob : IJob
{
    [ReadOnly] public NativeArray<byte> Costs;      // shared, read-only
    public NativeArray<int> Scratch;                // GridAStar.ScratchLength(cellCount), zeroed once, one per worker
    public NativeArray<int> Path;
    public NativeArray<int> Result;                 // [0] status, [1] length, [2] cost
    public int Width, Height, Start, Goal;

    public void Execute()
    {
        var status = GridAStar.FindPath(Costs.AsReadOnlySpan(), Width, Height, Start, Goal, allowDiagonal: true,
            Scratch.AsSpan(), Path.AsSpan(), out int length, out int cost);
        Result[0] = (int)status; Result[1] = length; Result[2] = cost;
    }
}
```

- `GridJumpPointSearch.FindPath` has the same signature. `GridFlowField.Build` fills a `distances` span (`int.MaxValue` = cannot reach a goal) and a `directions` span (0 E, 1 W, 2 N, 3 S, 4 NE, 5 NW, 6 SE, 7 SW, `GridFlowField.NoDirection` on goals and unreachable cells); `GridFlowField.NextCell`, `StepX` and `StepY` decode a direction.
- A scratch buffer is reused across queries without clearing. `GridAStar` and `GridJumpPointSearch` scratches are interchangeable and may move between map sizes (a size change clears it once). Anything else handed in as a search scratch must be zeroed first. A `GridFlowField` scratch is separate and its contents never matter.
- Spans cannot be job fields. Keep `NativeArray` fields and call `.AsSpan()` inside `Execute`.

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

### `RandomWalk` — a seeded path over a grid

```csharp
using ShadowLib.Procedural;

// A river crossing left to right: never steps back, drifts up and down.
(int X, int Y)[] river = RandomWalk.Generate(rng, length: 20, width: 16, height: 5, start: (0, 2),
    new DirectionBias(right: 0.6f, left: 0f, up: 0.2f, down: 0.2f));

// A drunkard's walk for carving caves, written into your own buffer (no allocation).
Span<(int X, int Y)> steps = stackalloc (int X, int Y)[256];
int taken = RandomWalk.Generate(rng, steps, width: 32, height: 32, start: (16, 16), DirectionBias.Uniform);
```

- The first position is `start`; each next one is a single step right (+X), left (-X), up (+Y) or down (-Y).
- A step that would leave the grid is never picked; the other directions keep their relative weights.
- The walk ends early when no weighted direction is open (a right-only walk stops at the right edge), so check the array length or the returned count. The river above does not stop at the far edge: once there it keeps moving up and down, so pick a length that fits or cut the path at the first position with `X == width - 1`.
- Cells can be visited more than once. One `rng.NextFloat()` per step taken.

### `SpacedPlacement` — random picks that keep their distance

```csharp
(int X, int Y)[] torches = SpacedPlacement.Place(rng,
    candidates: floorCells,              // any IReadOnlyList<T>
    count: 4,
    minDistance: 6,
    distanceFunc: (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y));
```

- Candidates are visited in a random order; each is kept if it is at least `minDistance` from everything already kept. A NaN distance counts as too close.
- It may return fewer than `count` when the spacing cannot be met in that order. It never returns candidates closer than `minDistance`.
- `T` can be anything; the distance function defines what "apart" means (Manhattan, Euclidean, path length).
- One `rng.Range` draw per candidate examined, so picking a few from a long list is cheap (nothing is drawn when `count` is 0 or there are no candidates).
- Results are distinct entries of the list. If the list itself holds the same value twice and `minDistance` is 0, both can come back.

### `CellularAutomata` — smooth noise into caves

```csharp
var cave = new FieldGrid<bool>(width: 64, height: 32, cellSize: 1f);   // true = wall
CellularAutomata.Randomize(cave, rng, aliveChance: 0.45f);

var rules = new CellularAutomata(birthRule: 5, surviveRule: 4);
rules.Step(cave, iterations: 4);
```

- Each step looks at the eight cells around a cell: a dead cell comes alive with at least `birthRule` living neighbours, a living cell stays alive with at least `surviveRule`.
- Cells outside the grid count as alive by default, which walls in the border; pass `edgesAlive: false` for open edges.
- `Randomize` makes one `rng.NextFloat()` draw per cell, row by row. `Step` uses no randomness.
- Keep one `CellularAutomata` and reuse it: it holds a scratch buffer, so a step allocates only when the grid is larger than any it has stepped before. It is not thread-safe.

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

`Runtime/Optional/` holds the part that depends on a third-party assembly (NCalc). It's a **separate asmdef** with `defineConstraints: ["SHADOWLIB_NCALC"]`.

To enable in Unity:

1. Install NCalc into your project (NuGetForUnity is the simplest path).
2. `Project Settings → Player → Scripting Define Symbols` → add `SHADOWLIB_NCALC`.
3. Reference `ShadowLib.Optional` from any asmdef that needs it.

Then:

```csharp
using ShadowLib.RNG.Utilities;

var ctx = new Dictionary<string, object> {
    ["PlayerLevel"] = 12,
    ["Environment"] = "Dungeon",
};
var evaluator = new ConditionEvaluator();

// A data-driven condition string deciding an item's influence.
table.SetInfluence("Rare", min: 1f, max: 50f,
    () => evaluator.Evaluate("PlayerLevel > 10 && Environment == 'Dungeon'", ctx) ? 1f : 0f);
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
   - Odds that follow game state, limited stock, no repeats, or several different picks → `DynamicWeightTable<T>`.
   - Pick an effect and apply it → `EffectWheel<TEffect, TTarget>`.
   - Immutable + hot loop → `AliasTable<T>`.
   - Stratified by tier/rarity → `TieredTable<T>`.
   - Each item exactly once before repeats → `RandomBag<T>`.
   - Mappable faces with equal weight → `Dice<T>`.
   - Next state depends on current state → `MarkovChain<T>`.
   - Generated names/words from example lists → `MarkovWordGenerator` (in `ShadowLib.Procedural`).
3. **Need a 2D grid?**
   - With occupants and shapes → `Grid2D<TCell, TOccupant>`.
   - Just scalar values → `FieldGrid<T>`.
   - A 3D inventory whose cells can be destroyed → `SpatialGraph3D<TOccupant>` with `Polycube` shapes.
4. **Need to place items in a grid?** → `BinPacker<TOccupant>` against any `ISpace<TOccupant>` (2D only).
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
- `Polyomino.Generate` / `Polycube.Generate` returning a `List` of positions — they return the `Polyomino` / `Polycube` struct. Their `max` limits are bounding-box sizes, not coordinates, and there is no per-axis bias parameter.
- `RandomWalk.Generate(rng, length, bounds: (w, h), bias: new DirectionBias(forward, up, down))` — that was the roadmap sketch. The real call takes `width`, `height`, `start` and a four-weight `DirectionBias(right, left, up, down)`; up is +Y. There is no self-avoiding or eight-direction walk.
- BSP, WFC, maze generation — deferred, not implemented. Do not write code that calls these.
- `grid.FindPath(...)`, `Grid2D.AStar`, a pathfinding delegate for walkability, float costs, or a `List` of nodes — none exist. Pathfinding is `GridPathfinder` (managed, (x, y) tuples) or the static `GridAStar` / `GridJumpPointSearch` / `GridFlowField` (cell indices, spans). Costs are bytes (0 = blocked); results are a `PathStatus` plus `out` length and cost.
- A ShadowLib `IJob`, `NativeArray` overload or ECS system for pathfinding — there is none. Write the job yourself and pass `nativeArray.AsSpan()` into the static functions.
- Pathfinding on `SpatialGraph3D` or `FieldGrid` — not supported.
- `SpacedPlacement.Place` guaranteeing `count` results, or finding the best possible spread — it is a single greedy pass and may return fewer. It returns `T[]`, not a `List`.
- `CellularAutomata` with Game-of-Life style exact-count rules (B3/S23), or working on `Grid2D` — it takes two "at least N neighbours" thresholds and a `FieldGrid<bool>`.
- `SparseGraph<T>`, `FindPath`, `GetNodesInRange`, `Distance`, `FindConnectedComponents`, `SpatialGraph3D.Graph`, `ConnectLayers` — do not exist. The 3D inventory is `SpatialGraph3D<TOccupant>`; its connectivity query is `FindPockets`.
- `Polycube` / `Polyomino` rotation helpers, and `SpatialGraph3D` implementing `ISpace` or working with `BinPacker` — none of these exist.
- `DynamicWeightTable<T>.AddWeight` / `MultiplyWeight` / `ResetWeights` / `UniformWeights` / `NormalizeWeights` / `GetBaseWeight` / `SetBaseWeight` — removed. Use `SetInfluence` for odds that change with game state, `SetEnabled(item, false)` to take an item out, `SetWeight` for a permanent change.
- `WeightModifier<T>`, `ModifierOperation`, `ModifierLoader`, the `ShadowLib.RNG.Modifiers` namespace — removed. `ConditionEvaluator` still exists.
- An influence provider interface (`IProbabilityInfluenceProvider` or similar) — there is none; `SetInfluence` takes a `Func<float>`.
- `DynamicWeightTable<T>` saving its own stock or last pick — it does not; see the save/load snippet in its section.
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
