# `RandomWalk`

Date: 2026-11-26. Roadmap item 12. Short design; the user chose "the general one" over a forward-only level path.

## Intent

A seeded walk over a bounded grid: corridors, rivers, wandering paths, cave carving. Four direction weights cover both a biased crossing (`right: 0.6, up: 0.2, down: 0.2`) and a drunkard's walk (`DirectionBias.Uniform`).

## API (`ShadowLib.Procedural`)

```csharp
public readonly struct DirectionBias
{
    public DirectionBias(float right, float left, float up, float down);
    public float Right { get; }   // +X
    public float Left { get; }    // -X
    public float Up { get; }      // +Y
    public float Down { get; }    // -Y
    public static DirectionBias Uniform { get; }   // 1, 1, 1, 1
}

public static class RandomWalk
{
    // Writes the path into the caller's buffer; returns how many positions were written. No allocation.
    public static int Generate(IRandom rng, Span<(int X, int Y)> path, int width, int height, (int X, int Y) start, DirectionBias bias);

    // Convenience: allocates and returns the path, at most `length` positions.
    public static (int X, int Y)[] Generate(IRandom rng, int length, int width, int height, (int X, int Y) start, DirectionBias bias);
}
```

## Behaviour

- The first position is `start`. Each further position is one step right, left, up or down from the previous one.
- A step that would leave `0 <= X < width`, `0 <= Y < height` weighs zero and is never picked. The other directions keep their weights.
- One `rng.NextFloat()` per step taken, picked by cumulative weight in the order right, left, up, down, summed in `double`. No draw for the start.
- If no direction has weight (a rightward-only walk reaching the right edge, or a 1x1 grid), the walk ends early with no draw. The return value, or the array length, says how far it got.
- Revisiting cells is allowed. The result is the ordered path; a caller carving caves marks each position.

## Errors

- `rng` null: `ArgumentNullException`.
- A weight that is negative, NaN or infinite, or all four zero: `ArgumentOutOfRangeException` from the `DirectionBias` constructor. `default(DirectionBias)` (all zero) is rejected by `Generate` with `ArgumentException`.
- `width` or `height` `<= 0`, `start` outside the bounds, `length <= 0`: `ArgumentOutOfRangeException`.
- An empty span returns 0, after the other arguments are checked.

## Approaches

- **Chosen: four weights, blocked steps weigh zero.** One draw per step, replayable, covers both uses.
- **Forward / up / down only (roadmap sketch).** A special case of the above (`left: 0`); as the only form it could not carve caves.
- **Blocked steps "clamp" (stay in place).** Produces repeated positions and wasted steps at walls. Rejected.

## Not included

Self-avoiding walks, eight directions, momentum, walking on a `Grid2D` with obstacles. Add when a game needs one.

## Tests

In bounds and unit steps over many seeds; first position is start; draws equal steps taken; right-only walk is a straight line that stops at the wall; zero-weight direction never taken; highest draw picks an allowed direction; 1x1 returns only the start with no draw; span and array forms agree; pinned path for one seed; each error.

## Also required

Runner demo, roadmap, changelog, usage guide ("For LLMs" list), `.meta` files, Unity pin test.
