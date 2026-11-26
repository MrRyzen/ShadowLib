# `SpacedPlacement` and `CellularAutomata`

Date: 2026-11-26. Roadmap items 13 and 14. Short design. Both were built from the roadmap's API sketches without a separate design conversation; every choice below is the builder's call, not a user decision.

## `SpacedPlacement`

```csharp
public static T[] Place<T>(IRandom rng, IReadOnlyList<T> candidates, int count, float minDistance, Func<T, T, float> distanceFunc);
```

- One `RandomUtils.Shuffle` of the candidate indices (`candidates.Count - 1` `Range` draws), then a single greedy pass: keep a candidate when its distance to every kept one is `>= minDistance`, stop at `count`.
- Returns the kept candidates in kept order, possibly fewer than `count`. No retry, no optimal packing.
- `count == 0` or no candidates: empty result, no draws.
- Errors: null `rng` / `candidates` / `distanceFunc` → `ArgumentNullException`; negative `count`, negative or NaN `minDistance` → `ArgumentOutOfRangeException`.
- Rejected: Poisson-disc sampling (needs a continuous space, the roadmap picks from given candidates); best-of-K retries (more draws, a knob nobody asked for).

## `CellularAutomata`

```csharp
public sealed class CellularAutomata
{
    public CellularAutomata(int birthRule, int surviveRule, bool edgesAlive = true);
    public void Step(FieldGrid<bool> grid, int iterations = 1);
    public static void Randomize(FieldGrid<bool> grid, IRandom rng, float aliveChance);
}
```

- Eight-neighbour count. Dead cell → alive when count `>= birthRule`; living cell stays when count `>= surviveRule`. Rules are 0..9, 9 meaning never.
- Every generation is computed from a copy of the previous one (not in place).
- Outside the grid counts as alive when `edgesAlive` (default), which walls the border.
- The instance keeps one scratch `bool[]`, grown on demand: no allocation per `Step` after the first for a given grid size. Not thread-safe.
- `Randomize`: `cell = rng.NextFloat() < aliveChance`, row-major, one draw per cell. Not in the roadmap sketch; added because every use needs it and its draw order should be fixed by the library.
- Errors: rules outside 0..9, negative `iterations`, chance outside 0..1 or NaN → `ArgumentOutOfRangeException`; null grid or rng → `ArgumentNullException`.
- Not included: exact-count rule sets (Game of Life B3/S23), wrap-around edges, `Grid2D` support, a Burst form.

## Tests

`Tests/Runtime/SpacedPlacementTests.cs`, `Tests/Runtime/CellularAutomataTests.cs`: hand-computed small grids for each rule, same-generation update, edges, iteration equivalence, no allocation after warm-up, draw counts, pinned placement, each error.
