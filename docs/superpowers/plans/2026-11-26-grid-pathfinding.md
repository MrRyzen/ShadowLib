# Grid Pathfinding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A*, Jump Point Search and flow fields over a byte cost map, allocation-free and Burst-safe, plus a `Grid2D` wrapper.

**Architecture:** Static span-based functions in `ShadowLib.Spatial.Pathfinding` share one internal indexed binary heap and one set of direction helpers. `GridPathfinder` is a managed class that owns buffers and converts `(x, y)` to indices.

**Tech Stack:** C# `netstandard2.1`, NUnit, Unity Burst 1.8.25 for the EditMode checks.

**Spec:** `docs/superpowers/specs/2026-11-26-grid-pathfinding-design.md`

## Global Constraints

- No floats, no managed objects, no delegates, no `ValueTuple`, no `unsafe` in the core (`GridPathing`, `PathHeap`, `GridAStar`, `GridJumpPointSearch`, `GridFlowField`).
- Exceptions in the core use only `(string message)` or `(string message, string paramName)` constructors with constant strings (boxing a value into an exception is not Burst-compatible).
- Step costs: orthogonal `10 * costs[cell]`, diagonal `14 * costs[cell]`. `0` = blocked.
- Direction codes: 0 E (+X), 1 W, 2 N (+Y), 3 S, 4 NE, 5 NW, 6 SE, 7 SW. Neighbours are visited in that order.
- A diagonal step needs both orthogonal cells it passes between to be walkable.
- Open-set order: lowest estimate, then highest cost so far, then lowest cell index.
- 4-space indent, no explanatory comments, `.meta` for each new `.cs`, check for `Passed!` before each commit, run the runner after touching `Program.cs`.

## Review Focus

1. Reusing one scratch across maps of different sizes or across algorithms must not return stale data (header stores cell count and algorithm id; mismatch clears).
2. A route whose cost would exceed `int.MaxValue` reports `NoPath`, never a wrapped cost.
3. JPS and A* agree on status and cost on maps with corridors one cell wide, diagonal-only gaps, and start/goal adjacent or equal.
4. A flow-field goal list containing duplicates, blocked cells or nothing at all.
5. `GridPathfinder.FindPath` with a `path` span shorter than the route returns `PathBufferTooSmall` and leaves the span untouched; `TryGetFlowStep` before any field is built returns false.

Tests for each are in the task that owns the code.

---

### Task 0: Burst spike — DONE before this plan was written

A Burst `IJob` in ShadowLab calling a span-based static method (slices, a throw guard, `NativeArray.AsSpan()`) passed and matched managed output (`PathfindingBurstTests.Spike_...`). The span-based core stands.

### Task 1: Shared pieces and A*

**Files:**
- Create: `Runtime/Spatial/Pathfinding/GridPathing.cs` (`PathStatus`, `GridPathing` constants, internal direction helpers and argument checks), `PathHeap.cs` (internal), `GridAStar.cs`
- Test: `Tests/Runtime/Pathfinding/PathTestMaps.cs` (map parsing, seeded random maps, a plain Dijkstra reference, path validation), `Tests/Runtime/Pathfinding/GridAStarTests.cs`

**Interfaces (produced):**
- `public enum PathStatus { Found, NoPath, PathBufferTooSmall }`
- `public static class GridPathing { public const int OrthogonalStep = 10; public const int DiagonalStep = 14; }` plus internal `DirectionX(int direction)`, `DirectionY(int direction)`, `Opposite(int direction)`, `CanStep(ReadOnlySpan<byte> costs, int width, int height, int x, int y, int direction, out int neighbour)`, `Heuristic(int dx, int dy, bool allowDiagonal)`, `RequireMap(...)`, `RequireCell(...)`.
- `internal static class PathHeap`: `Push`, `Pop`, `Improve` over `(Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, ref int count)`.
- `GridAStar.ScratchLength(int cellCount)` = `3 + 6 * cellCount` (header: generation, cell count, algorithm id; regions: stamp, cost so far, estimate, parent, heap position, heap). Throws when that exceeds `int.MaxValue`.
- `GridAStar.FindPath(...)` exactly as in the spec.
- Internal `PathScratch.Begin(Span<int> scratch, int cellCount, int algorithmId)` returning the generation; clears the scratch when the header's cell count or algorithm id differ or the generation would wrap.

- [ ] Write the failing tests: every hand-built map in the spec's Tests section; validity + optimality against the Dijkstra reference on 300 seeded random maps (sizes 1–24, densities 0–45 %, weights 1–9, both movement modes); buffer contract (too small, empty, untouched); scratch reuse equals fresh scratch over 500 queries; one scratch used on a 10×10 map then a 5×20 map then a 10×10 map; generation wrap (header slot set to `int.MaxValue - 1`, then three queries); overflow (a long corridor of cost 255 with the cost-so-far region pre-seeded is not possible from outside, so test through a 1×N map where `N * 2550 > int.MaxValue` is infeasible: instead assert via the internal relaxation guard using `InternalsVisibleTo`, or skip if no seam exists and record a ruling); no allocation after warm-up; pinned path; each error.
- [ ] Run → compile failure. Implement. Run → `Passed!`. Meta files. Commit.

### Task 2: Flow field

**Files:** Create `Runtime/Spatial/Pathfinding/GridFlowField.cs`; test `Tests/Runtime/Pathfinding/GridFlowFieldTests.cs`.

**Interfaces:** as in the spec. `ScratchLength(cellCount)` = `2 * cellCount` (heap position, heap). No header: `Build` rewrites every output cell, so nothing can be stale.

- [ ] Failing tests: `distances[c]` equals `GridAStar` cost from `c` to the nearest goal for every cell of 100 seeded random maps, both modes; following `directions` with `NextCell` from every reachable cell reaches a goal and the summed step costs equal `distances[c]`; goals have distance 0 and `NoDirection`; blocked and unreachable cells have `int.MaxValue` and `NoDirection`; two goals, each cell flows to the cheaper; duplicate goals, blocked goals, empty goals; diagonal squeeze refused; no allocation; pinned direction field for one map; errors.
- [ ] Run → fail. Implement. Run → `Passed!`. Meta. Commit.

### Task 3: Jump Point Search

**Files:** Create `Runtime/Spatial/Pathfinding/GridJumpPointSearch.cs`; test `Tests/Runtime/Pathfinding/GridJumpPointSearchTests.cs`.

**Interfaces:** as in the spec; scratch layout and header shared with A* (different algorithm id).

Pruning rules follow PathFinding.js `JPFNeverMoveDiagonally` (four-way) and `JPFMoveDiagonallyIfNoObstacles` (eight-way), written as loops instead of recursion: a horizontal scan, a vertical scan that runs horizontal scans at each cell (four-way only), a straight scan and a diagonal scan that runs straight scans at each cell (eight-way).

- [ ] Failing tests: equivalence with `GridAStar` on a cost map flattened to 1 — status and cost — on 2,000 seeded random maps (sizes 1–32, densities 0–45 %, both modes) plus hand-built corridor, diagonal-gap, adjacent and equal start/goal maps; path validity (every consecutive pair is one legal step); weighted maps are read as uniform; buffer contract; scratch reuse; no allocation; pinned path; errors.
- [ ] Run → fail. Implement. Run → `Passed!`. If equivalence fails, print the smallest failing map and fix the rule; do not weaken the test. Meta. Commit.

### Task 4: `GridPathfinder` wrapper

**Files:** Create `Runtime/Spatial/Pathfinding/GridPathfinder.cs`; test `Tests/Runtime/Pathfinding/GridPathfinderTests.cs`.

**Interfaces:** as in the spec. All cells start at cost 1. Out-of-range `(x, y)` arguments throw `ArgumentOutOfRangeException`. `Bake` throws `ArgumentException` when the grid size differs. Any change to costs (`SetCost`, `Bake`, or handing out `Costs`) marks the flow field stale, after which `TryGetFlowStep` returns false and `GetFlowDistance` returns `int.MaxValue` until `BuildFlowField` runs again.

- [ ] Failing tests: `Bake` from a `Grid2D` with a cost delegate; `FindPath` both algorithms agree with the core; coordinates round-trip; too-small span untouched; flow step before build is false; stale after `SetCost`; no allocation after warm-up; errors.
- [ ] Run → fail. Implement. Run → `Passed!`. Meta. Commit.

### Task 5: Docs, demo, benchmark, Unity

- [ ] Runner demo: a small map printed with an A* path, the JPS cost for the same endpoints, and a flow field drawn as arrows. Two `--no-build` runs identical.
- [ ] `[Explicit]` benchmark test printing per-query times on a 512×512 map.
- [ ] Docs: roadmap (item 10 Done, section moved), changelog, usage guide section with a Burst job example and "For LLMs" entries, namespace table, README, `CLAUDE.md` architecture note.
- [ ] Unity: replace the spike with Burst jobs for A*, JPS and the flow field compared with managed results; one in `FloatMode.Fast`; an `IJobParallelFor` running A* queries against one shared cost map with a per-index scratch slice.
- [ ] Full suite `Passed!`, runner exits 0, Unity all pass. Commit.
