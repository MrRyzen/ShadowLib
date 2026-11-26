# Grid pathfinding: A*, Jump Point Search and flow fields

Date: 2026-11-26. Roadmap item 10 ("A* pathfinding on `Grid2D`"), widened by the user to three algorithms.

## Intent

The user wants pathfinding on `Grid2D` that is "extremely optimized and alloc friendly" and takes "the benefit of Jobs/Burst/ECS". Asked which load matters, they chose all three:

- **A\*** for individual point-to-point paths over weighted terrain.
- **Jump Point Search (JPS)** for the same on walkable/blocked-only maps, where it is several times faster.
- **Flow fields** for crowds heading to shared goals.

Movement is four-way by default, eight-way behind a flag.

## Goals

- No allocation per query. All working memory is supplied by the caller and reusable.
- The search code runs unchanged inside a Burst job and in plain C#, with identical results.
- Many jobs can search one read-only cost map at the same time, each with its own scratch buffer.
- Results are deterministic: same map and arguments give the same path on .NET, Mono and Burst in every float mode. No randomness is involved.
- `Grid2D` users get a small managed wrapper so they never touch raw buffers unless they want to.

## Non-goals

- Pathfinding on `SpatialGraph3D` (decided earlier, not reopened), on `FieldGrid` directly, hex grids, or any-angle / smoothed paths.
- Hierarchical pathfinding (HPA*), path caching, dynamic replanning (D* Lite), time-sliced searches.
- Agent steering, avoidance, or anything that moves entities.
- A job type or ECS system shipped by the library. `Runtime/` cannot reference `Unity.Jobs` / `Unity.Collections`; the library ships the Burst-safe functions and a documented job example.

## Anti-goals

- Any floating-point arithmetic in a search. Costs are integers so Burst's `FloatMode.Fast` cannot change a path.
- Any managed object, delegate, `ValueTuple`, or interface call inside the core. (`ValueTuple` is `LayoutKind.Auto`, which Burst rejects at boundaries.)
- A per-query clear of buffers proportional to map size.
- `unsafe` code. The asmdef has `allowUnsafeCode: false` and stays that way.

## Found during the build

- **`Span<byte>.Fill` is miscompiled by Burst 1.8.25** (four bytes written per element, overrunning the buffer). The core uses plain loops instead of `Fill` / `Clear` / `CopyTo`. Managed .NET never showed it; only the Unity Burst test did.
- **JPS is not faster everywhere.** Measured on 512x512, eight-way, plain .NET: rooms with doors A* 1.8 ms vs JPS 0.09 ms; caves 0.68 vs 0.24; noise 30 % 2.9 vs 1.8; fully open 0.05 vs 0.39. A* with its tie-break walks almost straight across open ground, while JPS scans whole rows and columns. The docs say when to use which.
- The scratch header grew to three ints (generation, cell count, algorithm id) so a scratch reused on another map size or by the other search clears itself instead of returning stale data.
- `GridFlowField` gained `StepX` / `StepY` to decode a direction, and its scratch needs no header because every build rewrites every output cell.

## Constraints

- `netstandard2.1`, no `UnityEngine`, no NuGet references.
- Burst accepts `Span<T>` / `ReadOnlySpan<T>` inside jobs and static methods but not as job fields or across the managed boundary. So a job holds `NativeArray<T>` fields and passes `array.AsSpan()` into the core. Verified against the Burst 1.8 docs; ShadowLab has Burst 1.8.25. The first build task proves it with a real Burst job before anything else is built on it.

## Shape of the solution

Two layers in namespace `ShadowLib.Spatial.Pathfinding`.

### Layer 1: the core (Burst-safe static methods over spans)

**The map** is a `ReadOnlySpan<byte> costs` of length `width * height`, row-major (`index = y * width + x`, the same as `Grid2D.GetIndex`). `0` means blocked. `1`–`255` is the cost of entering that cell.

**Cells are `int` indices** everywhere in the core. Paths are written as indices into a caller's `Span<int>`.

**Step costs** are integers: entering a cell orthogonally costs `10 * costs[cell]`, diagonally `14 * costs[cell]`. Exposed as `GridPathing.OrthogonalStep` and `GridPathing.DiagonalStep`.

**Diagonals** (`allowDiagonal: true`): a diagonal step is legal only when both cells it passes between are walkable. No squeezing through or around corners. This one rule applies to all three algorithms.

**Scratch** is one `Span<int>` per worker, sized by a helper (`GridAStar.ScratchLength(cellCount)` etc.). The core carves it into its own regions (best cost, parent, heap position, generation stamp, heap). It must be zeroed when first created and can then be reused for any number of queries on maps of the same size; a generation counter stored in the scratch makes stale entries invisible, so nothing is cleared per query. A scratch belongs to one algorithm and one thread at a time.

```csharp
public enum PathStatus { Found, NoPath, PathBufferTooSmall }

public static class GridPathing
{
    public const int OrthogonalStep = 10;
    public const int DiagonalStep = 14;
}

public static class GridAStar
{
    public static int ScratchLength(int cellCount);

    public static PathStatus FindPath(
        ReadOnlySpan<byte> costs, int width, int height,
        int start, int goal, bool allowDiagonal,
        Span<int> scratch, Span<int> path,
        out int pathLength, out int pathCost);
}

public static class GridJumpPointSearch
{
    public static int ScratchLength(int cellCount);

    // Same signature and result contract as GridAStar.FindPath.
    // Every non-zero cost is treated as 1.
    public static PathStatus FindPath(...);
}

public static class GridFlowField
{
    public const byte NoDirection = 255;

    public static int ScratchLength(int cellCount);

    public static void Build(
        ReadOnlySpan<byte> costs, int width, int height,
        ReadOnlySpan<int> goals, bool allowDiagonal,
        Span<int> scratch,
        Span<int> distances,      // total cost to the nearest goal; int.MaxValue when unreachable
        Span<byte> directions);   // 0..7 = step to take; NoDirection on goals, blocked and unreachable cells

    public static int NextCell(int cell, int width, byte direction);   // the neighbour index; cell itself for NoDirection
}
```

**Result contract for `FindPath`:**

- `Found`: `path[0..pathLength)` holds every cell from `start` to `goal` inclusive, each one step from the last. `pathCost` is the sum of step costs. `start == goal` is `Found` with length 1 and cost 0.
- `NoPath`: start or goal is blocked (including `start == goal` on a blocked cell), or no route exists. `pathLength` and `pathCost` are 0.
- `PathBufferTooSmall`: a route exists but does not fit. `pathLength` is the length needed, `pathCost` is valid, `path` is untouched. Passing an empty `path` is the supported way to ask only for reachability and cost.
- JPS returns the full cell-by-cell path, not just jump points, so the two searches are interchangeable.

**Optimality:** A* returns a cheapest path. JPS returns a cheapest path on the uniform-cost reading of the map; its `pathCost` equals what A* reports on the same map with every walkable cost set to 1. When several cheapest paths exist the two may return different ones.

**Heuristic:** Manhattan times 10 for four-way, octile (10 / 14) for eight-way. Both are admissible because the cheapest cell cost is 1.

**Determinism:** ties in the open set break by lowest total estimate, then highest cost so far, then lowest cell index. Neighbours are visited in a fixed order. So a given map and query always produce the same path.

**Overflow:** a relaxation whose cost would exceed `int.MaxValue` is skipped. A route that expensive is reported as `NoPath` instead of wrapping.

**Errors** (thrown; under Burst these abort in a player build like any other Burst exception, matching `Xoshiro128`'s range guards): `width` or `height` not positive, `costs.Length != width * height`, `start` / `goal` / any flow-field goal outside `0..cellCount-1`, scratch shorter than `ScratchLength`, `distances` or `directions` shorter than the cell count. An empty `goals` span is not an error: every cell becomes unreachable.

**Flow field detail:** a multi-source Dijkstra outward from the goals. `distances[c]` is the cheapest cost of walking from `c` to any goal (entering costs as above); `directions[c]` is the first step of that walk. A walker stepping from `c` to neighbour `n` pays for entering `n`, so the outward search from the goals relaxes `distances[c] = distances[n] + step * costs[n]`. Entering a goal is paid like any other step, so the numbers match A*; only the cell a walker starts on is free. Direction codes 0–7 are a fixed, documented order (E, W, N, S, NE, NW, SE, SW; N is +Y to match `RandomWalk`'s "up"). Blocked goals are ignored.

### Layer 2: the `Grid2D` wrapper (managed)

```csharp
public sealed class GridPathfinder
{
    public GridPathfinder(int width, int height);

    public int Width { get; }
    public int Height { get; }
    public Span<byte> Costs { get; }                    // edit directly, or:
    public void SetCost(int x, int y, byte cost);
    public void Bake<TCell, TOccupant>(Grid2D<TCell, TOccupant> grid, Func<TCell, byte> costOf);

    public PathStatus FindPath((int X, int Y) start, (int X, int Y) goal, Span<(int X, int Y)> path,
        out int pathLength, out int pathCost, bool allowDiagonal = false, bool uniformCost = false);

    public void BuildFlowField(ReadOnlySpan<(int X, int Y)> goals, bool allowDiagonal = false);
    public bool TryGetFlowStep(int x, int y, out int stepX, out int stepY);   // false on goals, blocked, unreachable, or before a field is built
    public int GetFlowDistance(int x, int y);                                 // int.MaxValue when unreachable
}
```

- Owns the cost array, one scratch per algorithm (created on first use), an index buffer for paths and the flow-field arrays. After warm-up, `FindPath` and `BuildFlowField` do not allocate.
- `uniformCost: true` routes to JPS; otherwise A*.
- `Bake` reads the grid once. The pathfinder does not watch the grid: after the grid changes, call `Bake` again or `SetCost` for the cells that changed.
- Not thread-safe (one scratch). For parallel searches use the core with one scratch per worker.

## Approaches

**Chosen: static span-based core + managed wrapper.** One implementation serves Burst and plain C#. `Span<int>` works for `int[]`, `stackalloc` and `NativeArray<int>.AsSpan()` alike.

**Rejected: pointer-based core (`int*`).** Slightly faster in non-Burst .NET because it skips bounds checks, but needs `unsafe` in the csproj and the asmdef, and Burst already removes the checks. If the Burst spike in task 1 shows spans do not work, this is the fallback and the user is told before it is taken.

**Rejected: a `Unity.Collections`-based core in a separate opt-in asmdef.** Would allow shipping a real `IJob`. It makes the feature unusable outside Unity and unrunnable under `dotnet test`. A documented job snippet in the usage guide covers the Unity side.

**Rejected: searching `Grid2D` cells directly through a delegate.** Simplest API, but a delegate call per neighbour, class-pointer chasing per cell, and no Burst.

**Rejected: lazy-deletion heap.** Simpler than an indexed heap but its size is bounded by edges, not cells, so the scratch would be up to eight times larger or could overflow.

## Decided by the user

- All three algorithms.
- Eight-way movement behind a flag, four-way by default.
- No allocation per query; usable from Jobs / Burst / ECS.

## The builder's calls (not user decisions; flag any you disagree with)

- Integer costs 10 / 14 and a `byte` cost per cell with 0 = blocked.
- The strict no-corner-cutting diagonal rule.
- One `Span<int>` scratch with an internal layout, instead of several typed buffers.
- JPS expands to a full cell path and has a four-way variant as well as the eight-way one.
- Flow fields accept many goals and weighted costs.
- Namespace `ShadowLib.Spatial.Pathfinding`; type names as above.
- The wrapper is a separate class, not methods on `Grid2D`, so `Grid2D` does not grow buffers it rarely needs.

## Left to the builder

- Scratch layout, heap arity, and how the generation counter is stored and wrapped.
- JPS pruning and forced-neighbour rules, as long as the equivalence test below holds. The no-corner-cutting variant has different forced-neighbour rules from textbook JPS; follow a published formulation rather than inventing one.
- Whether the three algorithms share an internal heap helper.

## Tests

- **Hand-built maps:** straight line, wall with one gap, enclosed goal (`NoPath`), blocked start, blocked goal, `start == goal`, weighted detour (road around mud is chosen when cheaper and not when dearer), diagonal squeeze between two blocked cells is refused, 1×1 and 1×N maps.
- **Path validity** on a few hundred seeded random maps, both movement modes: consecutive cells are legal steps, endpoints match, reported cost equals the recomputed sum.
- **A\* optimality:** cost equals a plain Dijkstra reference written in the test (no heuristic, simplest possible code).
- **JPS equivalence:** on seeded random uniform maps of several densities and both movement modes, JPS status and cost equal A*'s. This is the main guard on the pruning rules.
- **Flow field:** `distances[c]` equals A*'s cost from `c` to the nearest goal on random maps; following `directions` from any reachable cell arrives at a goal with exactly that cost; multi-goal picks the nearer one.
- **Buffer contract:** too-small and empty path buffers; scratch reuse across many queries gives the same answers as a fresh scratch; a scratch that has run 70,000 queries still answers correctly (generation wrap, if the counter is narrower than `int`).
- **No allocation** after warm-up for the core and the wrapper.
- **Pinned path** for one map per algorithm.
- **Errors:** each listed case.
- **Unity (ShadowLab, EditMode):** a Burst-compiled `IJob` per algorithm over `NativeArray` buffers, results equal to the managed call on the same map; one test in `FloatMode.Fast`. A parallel job running several A* queries on one shared cost map with per-worker scratch.
- **Benchmark:** an `[Explicit]` NUnit test printing time per query for A* and JPS on a 512×512 map and flow-field build time, so regressions can be measured by hand. Not asserted.

## Also required

Runner demo (deterministic output only, no timings), roadmap, changelog, usage guide with a Burst job example and the "For LLMs" list, README, `CLAUDE.md` architecture note, `.meta` files.
