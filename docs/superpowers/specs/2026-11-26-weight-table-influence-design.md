# Weight table revamp + EffectWheel — design

Date: 2026-11-26. Roadmap item 8, grown to include the table underneath it.

## Intent

Odds in a weighted table should respond to game state ("the more corrupted the player, the likelier Drain"). Today `DynamicWeightTable<T>` only supports pushing new weights in (`SetWeight`, `MultiplyWeight`, `AddWeight`) and remembering to call `ResetWeights`. Nothing bounds how far odds drift, `AddWeight` deletes an item that reaches zero, and `WeightModifier<T>` / `ModifierLoader` are never applied by any runtime code.

The user wants the RNGNeeds "Probability Influence" model instead: each item has a designed weight, a min/max range, and a source that says where in that range it sits right now. Four more RNGNeeds features come along because they live in the same table: enabled flags, effective-probability query, repeat prevention, distinct multi-pick, and depletable stock.

## Goals

- Influence is pulled at pick time; designed weights are never overwritten by it.
- One `IRandom` draw per picked item, always, so replay with the same seed and same game state is exact.
- A table that uses none of the new features, and has no duplicate items, returns the same item for the same seed as it does today.
- `EffectWheel<TEffect, TTarget>` and `TieredTable<T>` get all of it for free.

## Non-goals

- Pick history, locked items, a selection-method registry, pick-count curves, an inspector/test tool.
- Saving table state. The game owns stock counts and the last pick; the table exposes enough to read and restore them.
- Thread safety. The table stays single-threaded like every other distribution.

## Anti-goals

- Extra RNG draws that depend on state (re-rolling on a repeat or a disabled item). That breaks replay.
- A push-style API surviving next to influence. Two ways to move odds is what made the old one bad.

## Constraints

`netstandard2.1`, no `UnityEngine`, no new NuGet refs, randomness only through `IRandom`, every new `.cs` needs a `.meta` (`scripts/generate-unity-meta.sh`), deleted `.cs` files lose their `.meta` too. C# uses 4 spaces. Comments only where code cannot say it.

## Approaches

1. **Influence beside the push API.** Nothing breaks, but two mechanisms overlap and the bad one stays.
2. **Influence, and delete only `WeightModifier` / `ModifierLoader`.** Removes dead code, keeps the overlap.
3. **Influence replaces the push API (picked by the user).** One way to move odds. Breaking, acceptable pre-1.0, recorded in the changelog.

For `EffectWheel`: a subclass of the table adding `SampleAndApply` (picked), over wrapping a private table (re-exposes the whole API for nothing) or an extension method (no `EffectWheel` type, which the roadmap names).

## Design

### `DynamicWeightTable<T> where T : IEquatable<T>` (name kept)

Per item: designed `Weight`, `Enabled` (default true), optional influence (`Min`, `Max`, `Func<float>`), optional stock (`Units`, `MaxUnits`; absent means infinite).

```csharp
// items
void Add(T item, float weight);                 // weight > 0; duplicate item throws ArgumentException
bool TryRemove(T item);
void Clear();
bool Contains(T item);
int Count { get; }

// designed weight
float GetWeight(T item);                        // KeyNotFoundException if missing
bool TryGetWeight(T item, out float weight);
bool SetWeight(T item, float weight);           // weight > 0, and inside [min, max] if influenced

// influence
bool SetInfluence(T item, float min, float max, Func<float> source);
bool ClearInfluence(T item);

// enabled
bool SetEnabled(T item, bool enabled);
bool IsEnabled(T item);

// stock
bool SetStock(T item, int units);               // units = maxUnits
bool SetStock(T item, int units, int maxUnits); // 0 <= units <= maxUnits, maxUnits >= 1
bool ClearStock(T item);                        // back to infinite
bool TryGetStock(T item, out int units);        // false if missing or infinite
bool Refill(T item);
void RefillAll();

// repeat prevention
bool PreventRepeat { get; set; }                // default false
bool TryGetLastPicked(out T item);
bool SetLastPicked(T item);                     // for restoring after a load
void ClearLastPicked();

// picking
float TotalWeight { get; }                      // sum of effective weights right now
float GetProbability(T item);                   // chance the next Sample returns it, 0..1
T Sample(IRandom rng);                          // InvalidOperationException if nothing is pickable
bool TrySample(IRandom rng, out T item);
int SampleDistinct(IRandom rng, Span<T> results); // returns how many were written
```

`bool` returns mean "item found", matching the current class. Argument errors throw `ArgumentOutOfRangeException` / `ArgumentNullException`.

**Removed:** `AddWeight`, `MultiplyWeight`, `ResetWeights`, `UniformWeights`, `NormalizeWeights`, `GetBaseWeight`, `TryGetBaseWeight`, `SetBaseWeight`, the old `SetWeight` meaning, and the internal base/dynamic split.

### Effective weight

For one pick, an item's effective weight is:

1. `0` if disabled, or if it has stock and `Units == 0`, or if it is the excluded repeat (below).
2. Otherwise the designed weight if it has no influence.
3. Otherwise read `i = source()`, treat `NaN` as `0`, clamp to `[-1, 1]`, then
   `i < 0 ? Lerp(weight, min, -i) : Lerp(weight, max, i)`.

`SetInfluence` requires `0 <= min <= weight <= max` and finite values. `min` may be `0`, so influence can switch an item off. Each source is called exactly once per `Sample`, `TrySample`, `GetProbability`, `TotalWeight`, and once per `SampleDistinct` call (not once per slot).

### Picking

Compute effective weights, sum them, draw `rng.NextFloat() * total`, return the first item whose running sum is `>=` the target and whose effective weight is `> 0`. This is the same rule the current binary search implements, so plain tables keep their results. If the total is `0`, `Sample` throws and `TrySample` returns false **without drawing**.

After a successful pick: stock items lose one unit; the item becomes the last pick.

**Repeat prevention.** When `PreventRepeat` is on and there is a last pick, that item is excluded from the pick if at least one other item has positive effective weight; if it is the only pickable item it is allowed. Removing the last-picked item, or `Clear`, clears the last pick. The last pick is tracked whether or not `PreventRepeat` is on.

**`SampleDistinct`.** Fills `results` with different items, one draw per item, each slot excluding the items already chosen in this call. Stops early when nothing pickable is left and returns the count. Each chosen item consumes one stock unit. Repeat prevention applies to the first slot only. The last pick afterwards is the last item written.

**`GetProbability`** returns effective weight / total for the next `Sample`, including repeat exclusion; `0` when the total is `0` or the item is missing. It does not draw or change state.

### Edge rules (designer's calls, from the builder check)

- **Duplicates.** `Add` of an item already present throws `ArgumentException`. The old class accepted it and summed the weights; `TieredTable.AddTier` now throws for a tier with duplicates. Changelog: breaking.
- **Float rounding.** If no item satisfies the running-sum rule, return the last item with positive effective weight. No extra draw.
- **Lerp.** `a + (b - a) * t`, returning `a` exactly at `t == 0` and `b` exactly at `t == 1`.
- **`SetWeight`** outside an existing influence range throws `ArgumentOutOfRangeException`; inside it, the range is kept.
- **Sources.** All sources are read before anything is mutated, so a throwing source propagates and leaves the table unchanged. `±Infinity` clamps like any other value. A source that calls back into the same table is unsupported. No caching between calls.
- **Stock** is spent at pick time only (`Sample`, `TrySample`, `SampleDistinct`), so it is already spent if `IEffect.Apply` throws. `Refill` returns false for a missing or infinite item. `SetStock` with `units > maxUnits` throws.
- **`SampleDistinct`.** An empty span returns `0` without drawing. A span longer than the pickable count stops early. When nothing is written the last pick is unchanged. The pre-call last pick may appear from slot 2 onward.
- **`GetProbability`** applies the "only pickable item may repeat" rule, so that item reports `1`.
- **Last pick** survives `SetEnabled(item, false)`; it is only cleared by removal, `Clear`, or `ClearLastPicked`.
- **Nulls.** `rng` and `source` are null-checked. Items are not.
- **`TieredTable.Sample(rng, tier, drops)`** keeps looping `Sample`; if a tier runs out of pickable items mid-loop the exception propagates.
- **Lookups** stay a linear `Equals` scan, including the duplicate check in `Add`. No dictionary.
- **Struct effects** have `Apply` run on a copy; fine for stateless effects, documented in the XML doc.

### `IEffect<TTarget>` and `EffectWheel<TEffect, TTarget>`

One new file, `Runtime/RNG/Distributions/EffectWheel.cs`:

```csharp
public interface IEffect<TTarget> { void Apply(TTarget target); }

public class EffectWheel<TEffect, TTarget> : DynamicWeightTable<TEffect>
    where TEffect : IEffect<TTarget>, IEquatable<TEffect>
{
    public TEffect SampleAndApply(IRandom rng, TTarget target);   // Sample, Apply, return
}
```

### `TieredTable<T>`

API unchanged. `GetTier` keeps returning the table, so callers reach influence through it.

### Deleted

`Runtime/RNG/Modifiers/WeightModifier.cs` (which also holds the `ModifierOperation` enum), `Runtime/Optional/ModifierLoader.cs`, their `.meta` files, and the now-empty `Runtime/RNG/Modifiers` folder with its `Runtime/RNG/Modifiers.meta`. `ConditionEvaluator` stays; it is standalone and can drive an influence lambda. `ModifierLoader` was the only user of `System.Text.Json`, so every doc that describes `Runtime/Optional/` as "NCalc + System.Text.Json" changes to NCalc only.

## Tests (`Tests/Runtime/DynamicWeightTableTests.cs`, `EffectWheelTests.cs`)

No tests exist for the table today. Before changing the class, capture a pinned sequence from the current implementation (fixed seed, plain weights) and keep it as a regression test. Then at least: influence at -1 / 0 / +1 and clamping; `min = 0` switches an item off; disabled items never picked; stock runs out and refills; repeat prevention never repeats with two or more pickable items and allows the repeat with one; `SampleDistinct` distinctness and early stop; `GetProbability` sums to 1; empty and all-zero tables; one draw per pick (compare RNG state against a reference generator); `SampleAndApply` applies exactly the returned effect.

## Other surface

Runner demo in `ShadowLib.Runner/Program.cs`; `SHADOWLIB_ROADMAP.md` (item 8 Done, the `DynamicWeightTable` and `TieredTable` descriptions, the whole Modifiers section, the pending `EffectWheel` section); `CHANGELOG.md` `[Unreleased]` with the breaking removals; `USAGE_GUIDE.md` (weight-table section, the `GetTier(tier).SetWeight` example, the `ModifierLoader` example in the Optional section, the "For LLMs" list); `README.md`, `CONTRIBUTING.md` and `CLAUDE.md` wherever they mention removed members or System.Text.Json. Grep for every removed name before calling the docs done.

## Calls made by the designer (not the user)

- Table keeps its name; no new provider interface, `Func<float>` is the source.
- Duplicate `Add` throws.
- Disabled, depleted and excluded items weigh zero rather than being picked and discarded.
- Repeat prevention excludes the last pick instead of re-rolling.
- Last pick and stock are readable and restorable but not serialized by the table.
- `TrySample` added so an all-depleted table is not an exception path.
- `WeightModifier` and `ModifierLoader` deleted.

## Left to the builder

Internal layout (one entry struct vs parallel lists), scratch buffer strategy for effective weights and for `SampleDistinct` exclusion (must not allocate per pick after warm-up), exact exception messages.
