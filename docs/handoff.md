# Handoff — 2026-11-26

## Goal

Finish the ShadowLib roadmap (`SHADOWLIB_ROADMAP.md`) in three projects, agreed with the user in this order:

1. **Orchestrator redesign + thread/Burst safety + `XorShift128` removal** — done and merged.
2. **The small roadmap items** — next: `IEffect<T>` + `EffectWheel<TEffect, TTarget>`, `SparseGraph<T>` with connected-component analysis, `Polyomino.Generate`, `RandomWalk`, `SpacedPlacement`, `CellularAutomata`.
3. **BSP, WFC, maze generation** — after project 2. The roadmap gives these one line and no API, so each needs its own short design conversation first.

## Current State

- Project 1 is complete on local `main` (fast-forwarded from `determinism-fixes-and-perf`, tip `5dc95aa`). **Nothing is pushed**; the user pushes.
- `dotnet test ShadowLib.sln -c Release`: 72/72 pass. `dotnet run --project ShadowLib.Runner` exits 0.
- Burst verified in the user's Unity project `L:\Projects\games\ShadowLab` (Unity 6000.6.10f1, Burst 2.0.0): 4/4 EditMode tests pass.
- Version is still `0.1.0-preview.5` in `package.json` and `Runtime/ShadowLib.csproj`. `CHANGELOG.md` `[Unreleased]` holds this work, including breaking changes. No release was cut.
- Projects 2 and 3 are not started. No design doc or plan exists for them.

## Files in Flight

None in this repo; the working tree is clean.

In `L:\Projects\games\ShadowLab` (a different repo, left uncommitted on purpose because its manifest already carried the user's own pending changes):

- `Packages/manifest.json` / `packages-lock.json` — `com.shadowlib.core` added, pointing at `file:L:/Projects/packages/shadowlib/artifacts/upm/com.shadowlib.core-0.1.0-preview.5.tgz` (absolute path to a git-ignored build output; this machine only).
- `Assets/_project/Tests/ShadowLibBurst/` — `ShadowLib.BurstTests.asmdef` and `XoshiroBurstTests.cs`.

## Changed

Commits on `main` from this session, oldest first:

| Commit | What |
|---|---|
| `7ca5625`, `b79604e` | Design doc: `docs/superpowers/specs/2026-11-25-orchestrator-redesign-design.md` |
| `6428e00` | Plan: `docs/superpowers/plans/2026-11-25-orchestrator-redesign.md` |
| `4e2332a` | Unmanaged `Xoshiro128` struct (in `Runtime/RNG/Sources/Xoshiro128StarStar.cs`), `Seeding.DeriveSeed64(ulong, ulong)`, `XorShift128` deleted |
| `2cc693a` | `Orchestrator` is an instance: `CreateRNG`, `Derive`, `Next`, `DeriveSeed`, `NextSeed`, `Fork`, `RootSeed`. Tests, runner and sample moved off the static API |
| `0089758` | `Orchestrator.Save()` → `byte[]`, `Load(ReadOnlySpan<byte>)` |
| `8a989aa` | README, USAGE_GUIDE, CONTRIBUTING, ROADMAP, CHANGELOG; `docs/` excluded from the UPM tarball |
| `837887c` | Review fixes: context names saved as UTF-16 code units, malformed name lengths rejected as `InvalidDataException`, derived seeds pinned in a test, thread-safety claim for `Save`/`Load` narrowed |
| `50c68eb` | Burst results in docs, wording fixes, `.claude/` and `scripts/` excluded from the tarball, `CLAUDE.md` updated |
| `5dc95aa` | The user's pending `release.yml` edit (NuGet feed push step), committed separately so it is easy to drop |

## Failed Attempts

- **Bash heredocs containing apostrophes fail in this environment** (`unexpected EOF while looking for matching '`), even with a quoted delimiter. Two file rewrites were lost to this. Use the Write/Edit tools for file content.
- **Saving context names as UTF-8 (`BinaryWriter.Write(string)`)**: a name with a lone surrogate was silently rewritten to U+FFFD, so its stream lost state on load, and a negative length prefix threw `IOException` instead of `InvalidDataException`. Replaced with raw UTF-16 code units, which is also what seeds are hashed from.
- **First Burst test computed float range bounds separately inside the job and in managed code** (`0.37f + index * 0.011f`). About 8% of `Range(float)` results differed. The generator was fine: the bounds themselves differed in the last bit between Burst and Mono. Fixed by precomputing bounds once and passing them in.
- **`unity command recompile` without `--focus true`** reported `completed` but never imported the new files (no `.meta` generated) while the editor was unfocused, and commands timed out at 30 s.
- **`run_tests --filter_type assembly --filter ShadowLib.BurstTests`** ignored the filter and ran the 55 CCF tests. `--filter_type testName --filter "ShadowLib.BurstTests.XoshiroBurstTests"` works.
- **`npm pack --pack-destination artifacts/upm`** fails with ENOENT if the directory does not exist. `mkdir -p artifacts/upm` first.

## Next Step

Start project 2 with a short design pass for `IEffect<TTarget>` + `EffectWheel<TEffect, TTarget>` (roadmap item 8, depends only on `DynamicWeightTable`), then continue down the roadmap's implementation-order table. Before designing `SparseGraph<T>`, read the existing `Runtime/Spatial/SparseGraph.cs` and `SpatialGraph3D.cs`: they are on disk as scaffolding but the roadmap treats them as not implemented.

## Notes

Durable facts worth carrying into any future sessions (see also `memory/MEMORY.md`):

- **Design decisions already made for the orchestrator, do not reopen:** instance not static; no parameterless constructor; `CreateRNG` seeds unchanged from before (`Seeding.DeriveSeed64(root, context)`); `Derive`/`Next`/`Fork` XOR a per-kind constant into the root; save is a versioned `byte[]`; `Fork` children are not part of the parent's save.
- **Pinned seed values** live in `OrchestratorTests.DerivedSeeds_ArePinned`. If that test fails, saved worlds would change: fix the code, not the pins, unless the break is intended and goes in the changelog.
- **Thread-safety contract:** stream/seed members take one lock; generators are single-threaded; `Save`/`Load` must not overlap draws from a `CreateRNG` stream; `Next` only replays when called from one thread.
- **Burst:** identical to managed output in `FloatMode.Default` and `Deterministic`. `FloatMode.Fast` changes about 1 in 4 results of `Range(float)` / `Range(double)`; integer draws, `NextFloat`, `NextDouble` stay exact. IL2CPP player builds are unchecked.
- **Re-running the Burst check:** `mkdir -p artifacts/upm && npm pack --pack-destination artifacts/upm`, then in ShadowLab `unity command --project-path . package_add --identifier "file:<tgz>" --confirm true --wait true`, `recompile --focus true`, poll `editor_status` / `recompile_status`, then `run_tests --mode editmode --filter_type testName --filter "ShadowLib.BurstTests.XoshiroBurstTests"`. The editor must be open on the project.
- **Every tracked `.cs` in the package needs a Unity `.meta`.** This session added no new `.cs` files for that reason (new types went into existing files). Project 2 will add files: generate metas with `scripts/generate-unity-meta.sh` or let Unity create them.
- **New public surface needs:** a test in `Tests/Runtime/`, a runner demo in `ShadowLib.Runner/Program.cs`, roadmap row moved to Done, `CHANGELOG.md` `[Unreleased]`, and the "For LLMs / do not hallucinate" list in `USAGE_GUIDE.md` updated (it currently lists the project 2 APIs as non-existent).
- **Library constraints:** `netstandard2.1`, no `UnityEngine` in `Runtime/`, no new NuGet refs outside `Runtime/Optional/`, all randomness through `IRandom`.
- **User's working rules:** commit every verified change, never push; subagents default to Sonnet with the model stated; tabs/arrow-function style applies to TS/JS only, this repo's C# uses 4 spaces.
- **Deferred, not blocking:** `Seeding` seed-expansion means `Xoshiro128(S)` and `Xoshiro128(S + 0x9E3779B97F4A7C15)` share half their state (pre-existing; no derived seed produces such pairs). `LangVersion` is `latest` in the csproj, so a C# 10+ feature unsupported by Unity 2021.3 would not be caught by `dotnet build`.
