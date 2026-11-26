# Handoff — 2026-11-26 (second session)

## Goal

Finish the ShadowLib roadmap (`SHADOWLIB_ROADMAP.md`) in three projects, agreed with the user in this order:

1. **Orchestrator redesign + thread/Burst safety + `XorShift128` removal** — done.
2. **The small roadmap items** — in progress. Done: `IEffect<T>` + `EffectWheel<TEffect, TTarget>` (item 8), which grew into a revamp of `DynamicWeightTable<T>`. Remaining: `SparseGraph<T>` with connected-component analysis (items 9, 10), `Polyomino.Generate`, `RandomWalk`, `SpacedPlacement`, `CellularAutomata`.
3. **BSP, WFC, maze generation** — after project 2. The roadmap gives these one line and no API, so each needs its own short design conversation first.

## Current State

- Everything is on local `main`, tip `b89aab5` plus this handoff commit. **Nothing is pushed**; the user pushes.
- `dotnet test ShadowLib.sln -c Release`: 121/121 pass. `dotnet run --project ShadowLib.Runner` exits 0.
- Unity (`L:\Projects\games\ShadowLab`, Unity 6000.6.10f1): 7/7 EditMode tests pass against the tarball built from `b89aab5`: the 4 Burst tests and 3 new weight-table tests, one of which checks that Mono produces the same pinned pick sequence as .NET.
- Version is still `0.1.0-preview.5`. `CHANGELOG.md` `[Unreleased]` holds both sessions' work, including breaking changes. No release was cut.
- The `SparseGraph<T>` design conversation has been opened with the user but nothing is decided or written.

## Files in Flight

None in this repo; the working tree is clean.

In `L:\Projects\games\ShadowLab` (a different repo, left uncommitted on purpose because its manifest already carried the user's own pending changes):

- `Packages/manifest.json` / `packages-lock.json` — `com.shadowlib.core` pointing at `file:L:/Projects/packages/shadowlib/artifacts/upm/com.shadowlib.core-0.1.0-preview.5.tgz` (absolute path to a git-ignored build output; this machine only).
- `Assets/_project/Tests/ShadowLibBurst/` — `ShadowLib.BurstTests.asmdef`, `XoshiroBurstTests.cs`, and new this session `WeightTableUnityTests.cs`.

## Changed

Commits on `main` from this session, oldest first:

| Commit | What |
|---|---|
| `bb16f77` | Design doc: `docs/superpowers/specs/2026-11-26-weight-table-influence-design.md` |
| `eb069db` | Plan: `docs/superpowers/plans/2026-11-26-weight-table-influence.md` |
| `13b2be5` | Pinned the old table's pick sequence in a test before touching the class |
| `3bdee08` | `DynamicWeightTable<T>` core rewritten: designed weights, enabled flag, `TrySample`, `GetProbability`; push-style API removed |
| `d55aa43` | `SetInfluence(item, min, max, Func<float>)` / `ClearInfluence` |
| `e85c662` | Stock: `SetStock`, `ClearStock`, `TryGetStock`, `Refill`, `RefillAll` |
| `71d4384` | `PreventRepeat`, `TryGetLastPicked`, `SetLastPicked`, `ClearLastPicked` |
| `170089c` | `SampleDistinct(rng, Span<T>)` |
| `d5e70fa` | `IEffect<TTarget>` and `EffectWheel<TEffect, TTarget>` in `Runtime/RNG/Distributions/EffectWheel.cs` |
| `2d297c9` | `WeightModifier`, `ModifierOperation`, `ModifierLoader` deleted; runner demo; all docs |
| `03d6664` | Review fixes: edge-draw and draw-count tests, `TieredTable.AddTier` no longer wipes a tier when it throws |
| `b89aab5` | Review minors: `OverflowException` on total overflow, setter validation order, stronger tests, changelog |

## Failed Attempts

Carried over, still true:

- **Bash heredocs containing apostrophes fail in this environment.** Use the Write/Edit tools for file content.
- **`unity command recompile` without `--focus true`** reports `completed` without importing new files.
- **`run_tests --filter_type assembly`** ignores the filter. `--filter_type testName --filter "ShadowLib.BurstTests"` runs every class in that namespace.
- **`npm pack --pack-destination artifacts/upm`** fails if the directory does not exist. `mkdir -p artifacts/upm` first.

New this session:

- **`unity command recompile` straight after `package_add`** failed with `Network error` because the editor was mid domain reload. `package_add` already triggers the reimport; just poll `editor_status` until `ready` / `compiling:false` / `domainReloadInProgress:false`, then run the tests.
- **A superpowers plan workspace (`.superpowers/`) is not git-ignored in this repo.** It was excluded through `.git/info/exclude` (local only) and deleted after the run.
- **Comparing two `dotnet run` outputs to check determinism** fails if the first run also builds, because build warnings land in stdout. Use `--no-build` for both.

## Next Step

Continue the `SparseGraph<T>` design conversation (roadmap items 9 and 10). The facts that conversation starts from:

- `Runtime/Spatial/SparseGraph.cs` (638 lines) and `SpatialGraph3D.cs` (283 lines) already exist, are public, ship in the package, and have **no tests and no users** anywhere in the repo.
- The existing `SparseGraph<TOccupant>` is keyed by a caller-chosen `int`, with alive/dead nodes and edges, occupancy, `Distance`, `GetNodesInRange`, `FindConnectedComponents` (all BFS, span-based). It has no `FindPath`.
- The roadmap sketches a different API: coordinate-keyed (`AddNode(0, 0, data)`), `Node<T>` handles, `List<>` returns, plus `FindPath` (A*).
- So the first decision is which shape wins: keep and finish the int-keyed scaffolding, or replace it with the roadmap's coordinate/handle API.

After `SparseGraph`, continue down the roadmap order table: `Polyomino.Generate`, `RandomWalk`, `SpacedPlacement` (its roadmap example uses `graph.Distance`), `CellularAutomata`.

## Notes

Durable facts worth carrying into any future sessions (see also `memory/MEMORY.md`):

- **Orchestrator decisions, do not reopen:** instance not static; no parameterless constructor; `CreateRNG` seeds unchanged (`Seeding.DeriveSeed64(root, context)`); `Derive`/`Next`/`Fork` XOR a per-kind constant into the root; save is a versioned `byte[]`; `Fork` children are not part of the parent's save.
- **Weight table decisions, do not reopen:** influence replaces the push-style API entirely (the user chose this over keeping both); the source is a `Func<float>`, not a provider interface; disabled / depleted / repeat-excluded items weigh zero instead of being picked and discarded; repeat prevention excludes rather than re-rolls; duplicate `Add` throws; stock and last pick are not serialized by the table. The model was taken from RNGNeeds' "Probability Influence" at the user's request. Features deliberately not copied: pick history, locked items, selection-method registry, pick-count curves.
- **Pinned values:** `OrchestratorTests.DerivedSeeds_ArePinned` and `DynamicWeightTableTests.Sample_PlainTable_SequenceIsPinned`. If either fails, saved worlds or existing seeds would change: fix the code, not the pins, unless the break is intended and goes in the changelog.
- **Weight table invariants:** exactly one `rng.NextFloat()` per picked item and none when nothing is pickable; no allocation per pick after warm-up; all influence sources are read before any mutation or draw. Each has a test.
- **Known limits of the weight table, accepted:** no getter for an item's max stock or influence range (a save must know them from design data); sources of disabled or depleted items are not called; a source that calls back into its own table recurses; single-precision sums mean a weight tiny next to the total can round away.
- **Thread-safety contract:** orchestrator stream/seed members take one lock; generators and all distributions are single-threaded; `Save`/`Load` must not overlap draws from a `CreateRNG` stream; `Next` only replays when called from one thread.
- **Burst:** identical to managed output in `FloatMode.Default` and `Deterministic`. `FloatMode.Fast` changes about 1 in 4 results of `Range(float)` / `Range(double)`. IL2CPP player builds are unchecked.
- **Unity compile coverage is Unity 6 only.** The package claims Unity 2021.3 (C# 9) but nothing has compiled it there; `LangVersion` is `latest` in the csproj, so `dotnet build` would not catch a C# 10+ feature either.
- **Re-running the Unity check:** `mkdir -p artifacts/upm && npm pack --pack-destination artifacts/upm`, then in ShadowLab `unity command --project-path . package_add --identifier "file:<tgz>" --confirm true --wait true`, poll `editor_status`, then `run_tests --mode editmode --filter_type testName --filter "ShadowLib.BurstTests"`. The editor must be open on the project. Use `recompile --focus true` only when test files changed without a package reinstall.
- **Every tracked `.cs` in the package needs a Unity `.meta`:** `bash scripts/generate-unity-meta.sh` after adding one; delete the `.meta` with any deleted `.cs` (and the folder's `.meta` if the folder goes).
- **New public surface needs:** a test in `Tests/Runtime/`, a runner demo in `ShadowLib.Runner/Program.cs`, roadmap row moved to Done, `CHANGELOG.md` `[Unreleased]`, and the "For LLMs / do not hallucinate" list in `USAGE_GUIDE.md` updated.
- **Library constraints:** `netstandard2.1`, no `UnityEngine` in `Runtime/`, no NuGet refs outside `Runtime/Optional/` (which is now NCalc only), all randomness through `IRandom`.
- **User's working rules:** commit every verified change, never push; subagents default to Sonnet with the model stated (the final whole-branch review ran on Fable); tabs/arrow-function style applies to TS/JS only, this repo's C# uses 4 spaces.
- **How this user works through a feature:** short design conversation with concrete options and a recommendation, written design doc, plan, inline execution on a feature branch, one independent review at the end, then fast-forward into local `main`. They asked for the review's minor findings to be fixed too, not deferred.
- **Deferred, not blocking:** `Seeding` seed-expansion means `Xoshiro128(S)` and `Xoshiro128(S + 0x9E3779B97F4A7C15)` share half their state (pre-existing; no derived seed produces such pairs). NuGet advisory NU1902 on `NCalcSync` 5.11.0 predates this work.
