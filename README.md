# ShadowLib

> **Pre-release — `0.1.0-preview.1`.** Public API may break before `1.0.0`. Pin a specific Git tag or version when consuming.

ShadowLib is a deterministic-RNG and procedural-generation toolkit for games. It is plain C# targeting **`netstandard2.1`** — usable in Unity (2021.3 LTS+) and in any non-Unity .NET project.

```
RNG        deterministic streams, seeding, weighted/tiered/alias tables, dice, simplex noise
Spatial    Grid2D, FieldGrid, Polyomino, ICell/ISpace contracts
Procedural BinPacker (FirstFit / BestFit / BottomLeft / AutoSort)
Optional   NCalc-driven weight modifiers and JSON modifier loading (opt-in)
```

The library has zero engine references in `Runtime/` — `noEngineReferences: true`. It can be dropped into a Unity project as a UPM package or referenced as a NuGet package from any .NET tool.

---

## Installation

### 1. Unity — Package Manager (Git URL)

`Window → Package Manager → + → Add package from git URL…`

```
https://github.com/MrRyzen/ShadowLib.git
```

To pin a release:

```
https://github.com/MrRyzen/ShadowLib.git#v0.1.0-preview.1
```

### 2. Unity — Local package path

```bash
git clone https://github.com/MrRyzen/ShadowLib.git
```

Then in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.shadowlib.core": "file:../path/to/ShadowLib"
  }
}
```

### 3. Unity — DLL drop

1. Download `ShadowLib.dll` from the [Releases](https://github.com/MrRyzen/ShadowLib/releases) page.
2. Place under `Assets/Plugins/ShadowLib/`.
3. (Optional) Place `ShadowLib.xml` next to it for IntelliSense.

### 4. .NET / NuGet

```bash
dotnet add package ShadowLib --prerelease
```

---

## Quick start

```csharp
using ShadowLib.RNG;
using ShadowLib.RNG.Distributions;
using ShadowLib.RNG.Utilities;

// 1. Seed once at boot. Same string = identical world every time.
Orchestrator.Initialize(Seeding.GenerateSeed64("my-world-seed"));

// 2. Ask the orchestrator for a per-feature stream.
var lootRng = Orchestrator.CreateRNG("loot");

// 3. Roll a tiered loot table.
var loot = new TieredTable<string>();
loot.AddTier(1, new (string, float)[] { ("Sword", 20f), ("Vest", 20f), ("Potion", 25f) });
loot.AddTier(2, new (string, float)[] { ("Cleaver", 5f), ("Plate", 10f) });

string[] drops = loot.Sample(lootRng, tier: 1, count: 3).ToArray();
```

For more, see [`USAGE_GUIDE.md`](./USAGE_GUIDE.md).

---

## Supported targets

| Target | Status |
|---|---|
| `.NET Standard 2.1`        | Library TFM (Unity baseline since 2021.3 LTS). |
| Unity `2021.3` LTS – `6000` | Verified UPM compatibility. |
| `.NET 6 / 7 / 8 / 9`        | Consume via NuGet or DLL. |
| C# 9.0+                     | Required by language features used in source. |

---

## Repository layout

```
.
├── package.json                  Unity UPM manifest
├── Runtime/                      Library source — UPM Runtime/
│   ├── ShadowLib.asmdef          Main asmdef (no engine refs, no external deps)
│   ├── ShadowLib.csproj          .NET project for build/test/pack
│   ├── RNG/ Spatial/ Procedural/ Source modules
│   └── Optional/                 Opt-in features (NCalc, System.Text.Json) — separate asmdef
├── Tests/Runtime/                NUnit tests, dual-purpose (dotnet test + Unity Test Runner)
├── Samples~/BasicUsage/          Importable Unity sample
├── ShadowLib.Runner/             Plain .NET console smoke-test (not part of UPM package)
├── ShadowLib.sln                 .NET solution
└── .github/workflows/            CI (build/test) + Release (pack & publish on tag)
```

---

## Development setup

Required:

- **.NET SDK 8.0+** (the library targets `netstandard2.1`; tests target `net8.0`).
- **Node 18+** (only for `npm pack` to build the UPM tarball locally).
- **Unity 2021.3 LTS+** (only if iterating on Unity-side asmdefs/samples).

Clone:

```bash
git clone https://github.com/MrRyzen/ShadowLib.git
cd ShadowLib
dotnet restore ShadowLib.sln
```

---

## Build & test

```bash
# Build the whole solution
dotnet build ShadowLib.sln -c Release

# Run all tests
dotnet test ShadowLib.sln -c Release

# Run a single test class
dotnet test Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~OrchestratorTests"

# Run the .NET console smoke test
dotnet run --project ShadowLib.Runner

# Pack a NuGet package locally
dotnet pack Runtime/ShadowLib.csproj -c Release -o artifacts/nuget

# Pack a UPM tarball locally (consumable by Unity Package Manager via "Install from tarball")
npm pack --pack-destination artifacts/upm
```

CI (`.github/workflows/ci.yml`) runs build+test on every push/PR. Release (`.github/workflows/release.yml`) triggers on tags matching `v*.*.*` (or `v*.*.*-*` for pre-releases).

---

## Versioning & release

- Semantic versioning. Pre-1.0 minor bumps may break public API; we'll call them out in `CHANGELOG.md`.
- Pre-release tags use `-preview.N` (e.g. `0.2.0-preview.1`).
- **The version is duplicated in two places:** `package.json` and `Runtime/ShadowLib.csproj`. The release workflow fails fast if the tag and these two values disagree.
- To cut a release:
  1. Update `package.json` `version` and `Runtime/ShadowLib.csproj` `<Version>`.
  2. Update `CHANGELOG.md`.
  3. Tag: `git tag v0.2.0-preview.1 && git push --tags`.
  4. The release workflow builds DLL, NuGet `.nupkg`, and UPM `.tgz`, attaches them to the GitHub release.

---

## Known limitations

- **Pre-1.0.** Names, signatures, namespaces may move. Sub-1.0 minor bumps can break.
- **Opt-in modules need extra setup in Unity.** `Runtime/Optional/` (NCalc condition strings + JSON modifier loader) requires:
  1. NCalc + System.Text.Json available in the project (NuGetForUnity is the easy path).
  2. The scripting define `SHADOWLIB_NCALC` set in `Project Settings → Player → Scripting Define Symbols`.
  Without these, the optional module is silently excluded from compilation. The core RNG/Spatial/Procedural modules work without any of this.
- **`XorShift128` does not fully implement `IRandom`.** It exists for legacy reasons; prefer `Xoshiro128StarStar` (the default produced by `Orchestrator.CreateRNG`).
- **Roadmap items not yet implemented**: see [`SHADOWLIB_ROADMAP.md`](./SHADOWLIB_ROADMAP.md). `SparseGraph<T>`, polyomino generator, random walk, spaced placement, cellular automata, BSP/WFC/Maze gen are scaffolded or pending.
- **`CanOccupyCell` is O(w·h)** for region checks — fine for typical inventory grids, slow for huge worlds with large footprints. A bitmask-based fast path is on the roadmap.

---

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md). The short version: keep `Runtime/` engine-free, keep `netstandard2.1` compatibility, add a test for any new public surface, bump the version in *both* manifests when shipping.

## License

MIT — see [`LICENSE.md`](./LICENSE.md).
