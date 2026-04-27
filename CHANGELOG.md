# Changelog

All notable changes to ShadowLib are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Sub-1.0 minor bumps may break public API.

## [Unreleased]

## [0.1.0-preview.3] — 2026-04-27

### Fixed

- `WeightModifier.Stage`: removed the `= 999` auto-property initializer that required C# 10+ and broke compilation on Unity 2021.3 LTS (C# 9 — auto-property initializers in structs require C# 10).

### Changed

- **Behavior change:** `WeightModifier.Stage` now defaults to `0` instead of `999`. Modifiers constructed without an explicit `Stage` will now apply *first* rather than *last*. Set `Stage` explicitly on any modifier whose ordering matters.
- `.gitignore`: added `.claude/` so local Claude Code session state stays out of the repo.

## [0.1.0-preview.2] — 2026-04-27

### Added

- Unity `.meta` files for every shipped asset (root manifests, `Runtime/`, `Tests/`). Without these, Unity refused to import the package ("immutable folder, asset will be ignored").
- `scripts/generate-unity-meta.sh` — idempotent generator that creates missing meta files with deterministic, path-derived GUIDs. Documented in `CONTRIBUTING.md`.
- `scripts/generate-unity-meta.sh`: macOS `md5 -q` fallback when `md5sum` isn't on `PATH`, and optional support for `.uss`, `.uxml`, `.asset`, `.shader`, `.compute` assets.
- `ci.yml` `unity-meta-check` job: runs the meta generator on every push/PR and fails if it produces uncommitted `.meta` drift.

### Changed

- Converted all 31 `.cs` files from file-scoped to block-scoped namespaces so the library stays compilable on Unity 2021.3 LTS (C# 9, which doesn't support file-scoped namespaces). Public API is unchanged.

### Fixed

- CI/release workflows: bumped `actions/setup-node` to Node 22 LTS (silences the Node 20 deprecation warning) and made `npm pack --pack-destination` resilient to a missing destination directory.

## [0.1.0-preview.1] — 2026-04-27

Initial pre-release. Repository restructured for Unity UPM consumption alongside the existing .NET solution.

### Added

- Unity package layout at the repo root: `package.json`, `Runtime/`, `Tests/Runtime/`, `Samples~/BasicUsage/`.
- Unity asmdefs:
  - `ShadowLib.asmdef` (Runtime, `noEngineReferences: true`, no external dependencies).
  - `ShadowLib.Optional.asmdef` (under `Runtime/Optional/`, gated by the `SHADOWLIB_NCALC` scripting define) for NCalc condition strings + JSON modifier loader.
  - `ShadowLib.Tests.asmdef` (gated by `UNITY_INCLUDE_TESTS`).
  - `ShadowLib.Samples.BasicUsage.asmdef`.
- `.NET` test project at `Tests/Runtime/ShadowLib.Tests.csproj` (NUnit), with sanity tests for `Seeding`, `Orchestrator`, `ScalingCurve`, `Polyomino`, and `Dice`.
- GitHub Actions:
  - `ci.yml` — restore / build / test / dry-run pack on every push and PR.
  - `release.yml` — tag-driven build that attaches DLL, NuGet `.nupkg`, and UPM `.tgz` to the GitHub release.
- `.gitignore` covering .NET, Rider, Visual Studio, VS Code, and Unity-generated files.
- `.npmignore` so `npm pack` produces a clean UPM tarball without the .NET solution + tooling.
- `README.md`, `USAGE_GUIDE.md`, `CONTRIBUTING.md`, `LICENSE.md`.

### Changed

- Library source moved from `ShadowLib/` to `Runtime/` to match Unity package layout. Namespaces are unchanged.
- `Runtime/ShadowLib.csproj` gained NuGet packaging metadata (`PackageId`, `Version`, `Description`, license, README packaging).
- `ShadowLib.sln` updated to point at the new `Runtime/` and `Tests/Runtime/` paths; removed dead third-project GUID.
- `ShadowLib.Runner/ShadowLib.Runner.csproj` reference updated to `..\Runtime\ShadowLib.csproj`.
- `ConditionEvaluator.cs` and `ModifierLoader.cs` relocated to `Runtime/Optional/` so the main asmdef stays dependency-free.

### Known limitations

- Pre-1.0; public API may break before `1.0.0`.
- `XorShift128` does not fully implement `IRandom` (legacy).
- Roadmap items not yet implemented: `EffectWheel`, `SparseGraph<T>`, `Polyomino.Generate`, `RandomWalk`, `SpacedPlacement`, `CellularAutomata`, BSP / WFC / Maze gen.

[Unreleased]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.3...HEAD
[0.1.0-preview.3]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.2...v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/MrRyzen/ShadowLib/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/MrRyzen/ShadowLib/releases/tag/v0.1.0-preview.1
