# Shape generators: `Polyomino.Generate` and `Polycube.Generate`

Date: 2026-11-26. Roadmap item 11.

## Intent

Games need generated item and pocket shapes for `Grid2D` inventories and the `SpatialGraph3D` backpack: blocky ones (a military rig) and sprawling ones (a scavenged pouch), reproducible from a seed, and guaranteed to fit the container they are made for.

## Goals

- One growth rule shared by 2D and 3D, so `compactness` means the same thing in both.
- Same seed and arguments give the same shape on .NET, Mono and Burst-adjacent code.
- A shape can be limited to a bounding size so it always fits a known container.

## Non-goals

- Rotation or mirroring helpers, deduplicating shapes, enumerating all shapes of a size.
- A per-axis growth bias. The size limit covers "keep it flat" (`maxDepth: 1` or `2`).
- A Burst-callable generator. These return managed structs holding arrays.

## Anti-goals

- Any float operation whose result can differ between runtimes (`MathF.Pow`, `Exp`, `Log`). Only add, subtract, multiply, divide and compare.
- Any iteration order that depends on `Dictionary` or `HashSet` enumeration.

## API

```csharp
public static Polyomino Generate(IRandom rng, int cellCount, float compactness,
    int maxWidth = int.MaxValue, int maxHeight = int.MaxValue);

public static Polycube Generate(IRandom rng, int cellCount, float compactness,
    int maxWidth = int.MaxValue, int maxHeight = int.MaxValue, int maxDepth = int.MaxValue);
```

Static methods on the existing structs. The result is normalized like every other `Polyomino` / `Polycube`; offsets are in the order the cells were grown.

## Behaviour

1. The shape starts as one cell. No draw.
2. The frontier is every empty cell that shares a face with the shape (four neighbours in 2D, six in 3D) and that would keep the shape's bounding size within the limits. The limits apply to the extent, not to a fixed position: the shape may grow in any direction until an axis reaches its maximum.
3. Each frontier cell weighs `(1 - compactness) * stringy[n] + compactness * blocky[n]`, where `n` is how many shape cells it touches. `blocky` rises steeply with `n`, `stringy` falls steeply with `n`.
4. One `rng.NextFloat()` picks a frontier cell by cumulative weight, walking the frontier in the order cells first joined it. Repeat from 2 until `cellCount` cells exist.

Exactly `cellCount - 1` draws, each a single `NextFloat()`.

Growth cannot get stuck: while fewer cells exist than the limit box holds, some frontier cell is always allowed. That is why the fit check below is enough.

## Errors

- `rng` null: `ArgumentNullException`.
- `cellCount <= 0`: `ArgumentOutOfRangeException`.
- `compactness` below 0, above 1, or NaN: `ArgumentOutOfRangeException`.
- Any limit `<= 0`: `ArgumentOutOfRangeException`.
- `cellCount` larger than the limit box volume (computed in `long`): `ArgumentOutOfRangeException`.

## Approaches

- **Chosen: one internal 3D growth core, 2D calls it with depth limited to 1.** One rule to test and pin. The 2D call passes a neighbour maximum of 4 so its weight tables match a flat shape.
- **Separate 2D and 3D implementations.** Slightly faster in 2D, but two copies of the rule that can drift. Rejected.
- **Use `DynamicWeightTable` as the picker.** Brings `IEquatable` lookups, influence delegates and allocation for a job a twenty-line cumulative walk does. Rejected.

## Left to the builder

- The exact `blocky` / `stringy` tables. Start from powers of eight (`blocky[n] = 8^(n-1)`, `stringy[n] = 8^(N-n)`), print shapes in the runner at compactness 0, 0.5 and 1, and adjust until 0 is visibly stringy and 1 is visibly blocky. Then pin. After the pin test exists the tables do not change.
- Internal data structures, as long as frontier order is insertion order.
- Where the core lives (an internal static class in `Runtime/Spatial/`).

## Tests

- Pinned offsets for one seed, 2D and 3D. Changing them is a breaking change.
- For several seeds and compactness values: exact cell count, no repeated cell, face-connected, within limits.
- Draw count is `cellCount - 1` (counting `IRandom` wrapper); `cellCount: 1` draws nothing.
- `cellCount` equal to the limit volume fills the box exactly.
- Mean bounding-box area at compactness 1 is smaller than at compactness 0 over a fixed set of seeds.
- Each error case.

## Also required

Runner demo, roadmap row to Done, `CHANGELOG.md` `[Unreleased]`, `USAGE_GUIDE.md` (including the "For LLMs" list), README if it lists shapes, `.meta` for any new `.cs`, and a Unity EditMode test asserting Mono produces the same pinned shapes.
