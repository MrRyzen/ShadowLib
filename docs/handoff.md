# Handoff — 2026-11-26 (end of third session)

## Goal

Finish the ShadowLib roadmap (`SHADOWLIB_ROADMAP.md`) in three projects, agreed with the user in this order:

1. **Orchestrator redesign + thread/Burst safety + `XorShift128` removal** — done.
2. **The small roadmap items** — done. This session: `Polyomino.Generate` / `Polycube.Generate` (item 11), `RandomWalk` (12), `SpacedPlacement` (13), `CellularAutomata` (14), and grid pathfinding (item 10), which the user widened from "A*" to A*, Jump Point Search and flow fields.
3. **BSP, WFC, maze generation** — **deferred by the user on 2026-11-26 ("drop those till needed")**. Do not start them unprompted. If one is asked for, it needs its own short design conversation first; the roadmap has no API for any of them.

So this roadmap pass is finished. Nothing is in progress.

## Current State

- Local `main` is at `f70836b` plus this handoff commit. **Nothing is pushed**; the user pushes. No feature branches or extra worktrees remain from this work (`determinism-fixes-and-perf` is an older branch, not touched).
- `dotnet test ShadowLib.sln -c Release`: 278/278 (the `[Explicit]` benchmark is not counted). `dotnet run --project ShadowLib.Runner` exits 0 and prints the same output on repeated `--no-build` runs.
- Unity (`L:\Projects\games\ShadowLab`, Unity 6000.6.10f1, Burst 1.8.25): 23/23 EditMode tests against the tarball built from `f70836b`.
- Version is still `0.1.0-preview.5`. `CHANGELOG.md` `[Unreleased]` holds all three sessions' work, including breaking changes. No release was cut.

## Files in Flight

None in this repo; the working tree is clean on both branches.

In `L:\Projects\games\ShadowLab` (a different repo, left uncommitted on purpose because its manifest already carried the user's own pending changes):

- `Packages/manifest.json` / `packages-lock.json` — `com.shadowlib.core` pointing at `file:L:/Projects/packages/shadowlib/artifacts/upm/com.shadowlib.core-0.1.0-preview.5.tgz` (absolute path to a git-ignored build output; this machine only).
- `Assets/_project/Tests/ShadowLibBurst/` — `ShadowLib.BurstTests.asmdef`, `XoshiroBurstTests.cs`, `WeightTableUnityTests.cs`, `Backpack3DUnityTests.cs`, and new this session `ShapeGenerateUnityTests.cs` (4 pin tests), `ProceduralUnityTests.cs` (walk pin, placement pin, cellular automata) and `PathfindingBurstTests.cs` (A*, A* in fast-float mode, JPS, mixed A*/JPS on one scratch, flow field with overrun padding, parallel A*).

## Changed

Commits on `main` from this session, oldest first:

| Commit | What |
|---|---|
| `cf5592d`, `58e7e69` | Design doc and plan for the shape generators; A* performance requirements recorded in the roadmap |
| `8ed3690` | `Polyomino.Generate`, `Polycube.Generate`, internal `Runtime/Spatial/ShapeGrowth.cs` |
| `fbbaa17` | Generator docs, runner demo, roadmap, changelog |
| `2b58a1f` | Review fixes: pins at compactness 0.2 and 0.9 under size limits, draw count under limits, fuller 2D tests, `Polycube.Generate` skips the redundant distinct check |
| `d812a47` | `RandomWalk` + `DirectionBias` (`Runtime/Procedural/RandomWalk.cs`), design doc `docs/superpowers/specs/2026-11-26-random-walk-design.md` |
| `d559150` | `SpacedPlacement`, `CellularAutomata`, design doc `docs/superpowers/specs/2026-11-26-spaced-placement-cellular-automata-design.md` |
| `5fcab65` | Review fixes: NaN distance counts as too close, lazy shuffle in `SpacedPlacement` (pin changed before release), `RandomWalk` validates before allocating, tests for every mutation the reviewer saw survive |
| `82c1422`, `d225a48`, `4fa3281` | Pathfinding design doc and plan (`docs/superpowers/specs/2026-11-26-grid-pathfinding-design.md`, `docs/superpowers/plans/2026-11-26-grid-pathfinding.md`) |
| `68eb8ca` | `ShadowLib.Spatial.Pathfinding`: `GridPathing`, `PathScratch`, `PathHeap`, `GridAStar` |
| `7f88bf8` | `GridFlowField` |
| `c72b448` | `GridJumpPointSearch` |
| `72916db` | `GridPathfinder` (the `Grid2D` wrapper) |
| `19ed6dd` | Faster JPS straight scans, runner demo, `[Explicit]` benchmark, docs |
| `15f99ee` | Loops instead of `Span.Fill` / `Clear` in the core (Burst miscompile) |
| `4b5696e` | Roadmap: BSP / WFC / maze deferred |
| `f70836b` | Review fixes: A* and JPS share one scratch format, `GridPathfinder.Costs` read-only with `EditCosts()`, JPS guidance corrected, tests for every mutation the reviewer saw survive |

## Failed Attempts

Carried over, still true:

- **Bash heredocs containing apostrophes fail in this environment.** Use the Write/Edit tools for file content. A `python - <<'EOF'` heredoc works only if the script has no apostrophes; for anything longer, Write a `.py` file to the scratchpad and run it.
- **`unity command recompile` without `--focus true`** reports `completed` without importing new files.
- **`run_tests --filter_type assembly`** ignores the filter. `--filter_type testName --filter "ShadowLib.BurstTests"` runs every class in that namespace.
- **`npm pack --pack-destination artifacts/upm`** fails if the directory does not exist. `mkdir -p artifacts/upm` first.
- **`unity command recompile` straight after `package_add`** fails with `Network error`. `package_add` already triggers the reimport; poll `editor_status`, then run the tests.
- **`.superpowers/` is not git-ignored in this repo.** It is excluded through `.git/info/exclude` (local only).
- **Comparing two `dotnet run` outputs to check determinism** fails if the first run also builds. Use `--no-build` for both.
- **`Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before)`** reports 24 bytes from boxing. Read the counter into a `long` local first.
- **Chaining `git commit` after `dotnet test ... | grep ...` with `&&`** commits even when tests fail. Check for `Passed!` first.

New this session:

- **A linear blend of a "stringy" and a "blocky" weight table for `compactness`** stayed stringy until compactness was almost 1. The shipped rule multiplies a spot's weight by `r = (1 + 31c) / (32 - 31c)` per extra touching cell.
- **Pinning generated shapes only at compactness 0.5** pins nothing about the weight maths, because `r` is exactly 1 there. The pins at 0.2 and 0.9 exist for that reason. Even so, only one of the four new pins failed when the steepness constant was changed from 32 to 8: eight-cell shapes are not very sensitive.
- **A Python string `"\n"` written through a bash heredoc** became a real newline inside a C# string and broke the runner build. The test suite did not catch it because the runner is a separate project: always run `dotnet run --project ShadowLib.Runner` after touching `Program.cs`.
- **`editor_status` JSON has a space after the colon** (`"compiling": false`). A poll loop matching `"compiling":false` never matches and just runs out its retries.
- **`git branch -d` refuses a branch merged into `main` when `HEAD` is on a different branch.** Check `git rev-parse main` equals the branch tip, then `-D`.
- **`git fetch . feature:main`** fast-forwards `main` without checking it out, which is how a worktree branch was merged while the main worktree sat on another branch.
- **Writing code and tests together for `SpacedPlacement` and `CellularAutomata`** (no watched failure) left five mutations alive that the reviewer found. Write the test first and watch it fail, or mutation-check afterwards.
- **`Span<byte>.Fill` inside Burst-compiled code** (Burst 1.8.25) wrote four bytes per element and overran the buffer into neighbouring native memory. `dotnet test` cannot see it. The symptom in Unity was an unrelated `NativeArray` holding 255. The core now uses plain loops; `PathfindingBurstTests.FlowField_UnderBurst_MatchesManaged` pads its buffers and checks the padding.
- **Asserting a `NativeArray<bool>` written by a Burst job** gave "Expected: True But was: True" while chasing the above. Use `int` flags (1 / 0) for "ran in Burst" checks; they also showed the real value (255).
- **JPS as first written** (a generic `IsWalkable` call per scanned cell) beat A* only on room maps. Tight index-stepping loops in `ScanStraight` made it 14-20x faster than A* on rooms and 2-3x on caves. It is still slower than A* on wide-open maps and short trips, by nature; that needs bit-row scanning or precomputed jump distances, not tuning.
- **Hand-computed test expectations were wrong three times this session** (a tie between two equal-cost routes, 14+10+10 added up as 38, a distance that forgot a weighted cell). Each time the code was right. Check the arithmetic before suspecting the code, and avoid test maps with equal-cost alternatives unless the tie-break is the point.
- **A test map without a `G`** left `Goal = -1` and failed with an argument exception that looked like a library bug.
- **Pinning a tie-break on a square corner-to-corner trip in eight-way mode** pins nothing: the pure diagonal is the only cheapest path. The pins use a non-square trip for eight-way.
- **A `python - <<'EOF'` heredoc with apostrophes in the text** fails like any other heredoc here. Write the script to the scratchpad with the Write tool and run the file.

## Next Step

Nothing is queued. The roadmap's remaining entry (BSP, WFC, maze generation) is deferred until the user asks. Things the user may ask for next, none started:

- **A release:** bump `package.json` `version` and `Runtime/ShadowLib.csproj` `<Version>` together, move `[Unreleased]` in `CHANGELOG.md` under the new version, tag `v*.*.*`. The user pushes and tags.
- **Burst timings for pathfinding:** only managed-.NET numbers exist (`GridPathfindingBenchmark`).
- **Faster flow fields:** a bucket queue instead of the binary heap (marked with a `ponytail:` comment in `GridFlowField.cs`).

## Notes

Durable facts worth carrying into any future sessions (see also `memory/MEMORY.md`):

- **Orchestrator decisions, do not reopen:** instance not static; no parameterless constructor; `CreateRNG` seeds unchanged (`Seeding.DeriveSeed64(root, context)`); `Derive`/`Next`/`Fork` XOR a per-kind constant into the root; save is a versioned `byte[]`; `Fork` children are not part of the parent's save.
- **Weight table decisions, do not reopen:** influence replaces the push-style API entirely; the source is a `Func<float>`; disabled / depleted / repeat-excluded items weigh zero; repeat prevention excludes rather than re-rolls; duplicate `Add` throws; stock and last pick are not serialized by the table.
- **3D backpack decisions, do not reopen:** Tarkov-style backpack with depth that degrades; 2D inventories stay on `Grid2D`; one class with built-in six-neighbour adjacency; no pathfinding on it; killing a cell returns the cell and its item and never moves the item; no rotation helper; an item can be placed only once; it is not an `ISpace`.
- **Shape generator decisions:** the user approved the bounding-size limits (`maxWidth` / `maxHeight` / `maxDepth`) and returning the structs instead of a list. The weight rule, `double` maths and the shared 3D core are the builder's calls.
- **Pathfinding decisions by the user (2026-11-26):** all three algorithms (A* weighted, JPS uniform, flow fields); eight-way movement behind a flag; "extremely optimized and alloc friendly", usable from Jobs / Burst / ECS. The user approved the design doc with its six listed builder's calls (integer 10/14 costs, strict no-corner-cutting diagonals, JPS returns full paths and ignores weights, multi-goal weighted flow fields, no shipped `IJob`, the wrapper does not watch the grid). Do not reopen those.
- **Pathfinding facts:** cost map is one byte per cell, row-major, 0 = blocked. A* and JPS share one scratch format (`2 + 6 * cellCount` ints: generation, cell count, then stamps, cost so far, estimate, parent, heap position, heap). A flow-field scratch is `2 * cellCount` and needs no zeroing. Core exceptions are `ArgumentException` with constant strings (Burst cannot box values into messages). `GridJumpPointSearchTests.RandomMaps_StatusAndCostMatchAStarOnTheUniformMap` is the guard on the pruning rules, which follow PathFinding.js `JPFNeverMoveDiagonally` and `JPFMoveDiagonallyIfNoObstacles`.
- **Pathfinding limits, accepted:** `start == goal` with an empty path buffer returns `PathBufferTooSmall` with length 1; a route costing more than `int.MaxValue` is `NoPath`; a span kept from `EditCosts()` before a flow-field build can change costs unnoticed (documented); no Burst test covers the throw paths; IL2CPP player builds are unchecked.
- **Measured (512x512, eight-way, managed .NET, per query):** rooms A* 1.8 ms / JPS 0.09 ms; caves 0.68 / 0.24; noise 30 % 2.9 / 1.8; open 0.05 / 0.39. Flow field 12-30 ms per build.
- **The user accepted building small items without a design conversation** ("build the general one" for `RandomWalk`); `SpacedPlacement` and `CellularAutomata` were then built from the roadmap sketches with the builder's calls stated afterwards. My reading: fine for low-complexity items with a sketch, not for anything the size of pathfinding.
- **Pinned values:** `OrchestratorTests.DerivedSeeds_ArePinned`, `DynamicWeightTableTests.Sample_PlainTable_SequenceIsPinned`, the `Generate_*_ShapeIsPinned` tests (three each in `PolyominoGenerateTests` / `PolycubeGenerateTests`), `RandomWalkTests.Generate_Seed123_PathIsPinned`, `SpacedPlacementTests.Place_Seed123_ResultIsPinned`, and `Path_IsPinned` / `OpenGround_TieBreakIsPinned` in `GridAStarTests` and `GridJumpPointSearchTests`. If one fails, existing seeds would produce different content: fix the code, not the pin, unless the break is intended and goes in the changelog.
- **Draw-count invariants, each with a test:** weight table, one `NextFloat` per pick; `TryKillRandom`, one `Range`; shape generators, `cellCount - 1` `NextFloat`; `RandomWalk`, one `NextFloat` per step taken and none when stuck; `SpacedPlacement`, one `Range` per candidate examined except the last in the list; `CellularAutomata.Randomize`, one `NextFloat` per cell, row-major.
- **Cross-runtime float rule used this session:** cumulative weight picks are summed in `double` with add / multiply / divide only, and compared against partial sums computed in the same order as the total, so a draw below 1 can never fall past the last allowed option.
- **Known limits, accepted:** shape growth is O(cellCount²) (5,000 cells ≈ 0.3 s); `SpacedPlacement` is a single greedy pass and may return fewer than `count`; `CellularAutomata` reallocates its scratch when it meets a larger grid; a tiny `RandomWalk` weight next to a huge one is never picked.
- **Thread-safety contract:** orchestrator stream/seed members take one lock; generators, distributions, `CellularAutomata` instances are single-threaded.
- **Burst:** `Xoshiro128` is identical to managed output in `FloatMode.Default` and `Deterministic`; `FloatMode.Fast` changes about 1 in 4 results of `Range(float)` / `Range(double)`. IL2CPP player builds are unchecked. Burst accepts `Span<T>` inside jobs and static methods but not as job fields; `ValueTuple` is `LayoutKind.Auto` and is rejected at boundaries (Burst 1.8 docs, read 2026-11-26).
- **Unity compile coverage is Unity 6 only.** The package claims Unity 2021.3 (C# 9) but nothing has compiled it there.
- **Re-running the Unity check:** `mkdir -p artifacts/upm && npm pack --pack-destination artifacts/upm`, then in ShadowLab `unity command --project-path . package_add --identifier "file:<tgz>" --confirm true --wait true`, poll `editor_status`, then `run_tests --mode editmode --filter_type testName --filter "ShadowLib.BurstTests" --result-only`. The editor must be open on the project.
- **Every tracked `.cs` in the package needs a Unity `.meta`:** `bash scripts/generate-unity-meta.sh` after adding one.
- **New public surface needs:** a test in `Tests/Runtime/`, a runner demo, roadmap row moved to Done, `CHANGELOG.md` `[Unreleased]`, the "For LLMs" list and the namespace table at the top of `USAGE_GUIDE.md`, and the README's pending list.
- **Library constraints:** `netstandard2.1`, no `UnityEngine` in `Runtime/`, no NuGet refs outside `Runtime/Optional/`, all randomness through `IRandom`, `allowUnsafeCode: false`. In the pathfinding core also: no floats, managed objects, delegates, tuples, `Span.Fill` / `Clear` / `CopyTo`.
- **User's working rules:** commit every verified change, never push; subagents default to Sonnet with the model stated (final whole-branch reviews ran on Fable); tabs/arrow-function style applies to TS/JS only, this repo's C# uses 4 spaces; review minors get fixed, not deferred.
- **Deferred, not blocking:** `Seeding` seed-expansion means `Xoshiro128(S)` and `Xoshiro128(S + 0x9E3779B97F4A7C15)` share half their state (pre-existing). NuGet advisory NU1902 on `NCalcSync` 5.11.0 predates this work.
