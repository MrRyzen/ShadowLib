# Contributing to ShadowLib

Thanks for considering a contribution. This guide is the rulebook for adding to or changing the library without breaking package consumers.

## Project ground rules

These exist to keep ShadowLib deterministic, Unity-compatible, and dependency-light. Violations will get blocked in review.

1. **`Runtime/` stays engine-free.** No `using UnityEngine`, no `using UnityEditor`. The asmdef is `noEngineReferences: true` and that must remain true. If you're tempted to call `Debug.Log`, you're in the wrong folder.
2. **`Runtime/` stays NuGet-dependency-free.** New external NuGet references go into `Runtime/Optional/` (under the `SHADOWLIB_NCALC` define) or behind a new opt-in asmdef. The base library is `netstandard2.1` + BCL only.
3. **Editor-only code lives in `Editor/`.** Anything that imports `UnityEditor` belongs there with its own asmdef. There is no `Editor/` folder yet — if you create one, mirror the pattern from `Runtime/`.
4. **`netstandard2.1` is the floor.** Don't use APIs only present in net6+ (`System.Random.Shared`, generic math, `static abstract` interface members, `DateOnly`, etc.).
5. **Determinism is non-negotiable.** All randomness must thread through `IRandom`. Never `System.Random`, `UnityEngine.Random`, `DateTime.Now`, or `Guid.NewGuid()` inside the library. Tests must seed explicitly.
6. **`Orchestrator` is global state.** Avoid taking direct dependencies on it from leaf classes. Take an `IRandom` parameter instead and let the caller decide the context.

## Adding a new module

The expected flow for adding (e.g.) a new RNG distribution:

1. **Read the roadmap.** `SHADOWLIB_ROADMAP.md` lists what's intended and the implementation order. Don't reorder dependencies.
2. **Place the file** under the correct sub-folder of `Runtime/` (`Runtime/RNG/Distributions/MyThing.cs`, `Runtime/Spatial/MyThing.cs`, etc.).
3. **Use the right namespace.** Namespace must match the path: `ShadowLib.RNG.Distributions`, `ShadowLib.Spatial`, etc.
4. **Document with XML comments.** `<GenerateDocumentationFile>` is on; missing docs become missing IntelliSense for users.
5. **Add tests.** Every new public type gets at least one test in `Tests/Runtime/`. Tests must:
   - Seed via `Seeding.GenerateSeed64("name-of-test")`.
   - Use `Orchestrator.Initialize` *only* if the test cares about orchestrator behavior; otherwise pass an `IRandom` directly.
   - Be deterministic: same test, same result, every run.
6. **Update the roadmap.** Move the row from "Pending" to "Done" in `SHADOWLIB_ROADMAP.md`.
7. **Update `CHANGELOG.md`** under `[Unreleased]`.

## Adding asmdefs

If you add a new opt-in module:

- Create a new sub-folder under `Runtime/` (e.g. `Runtime/MyOptional/`).
- Add an asmdef referencing `ShadowLib`, with `noEngineReferences: true` if pure C#.
- If it requires a NuGet/external DLL, set `defineConstraints: ["SHADOWLIB_MYFEATURE"]` so users opt in by define.
- Document the opt-in in `README.md` "Known limitations" and in `USAGE_GUIDE.md` "Optional module".

## Build & test locally

```bash
dotnet restore ShadowLib.sln
dotnet build   ShadowLib.sln -c Release
dotnet test    ShadowLib.sln -c Release
dotnet run --project ShadowLib.Runner       # eyeball-test smoke run
```

The console runner is your "did I just break something obvious" check before pushing — extend it when you add a new public surface so a `dotnet run` still demonstrates everything end-to-end.

## Pull request checklist

- [ ] `Runtime/` does not import `UnityEngine` or `UnityEditor`.
- [ ] `Runtime/` does not introduce a new NuGet dependency outside `Runtime/Optional/`.
- [ ] Public types have XML doc comments.
- [ ] At least one new test in `Tests/Runtime/` covers the new public surface.
- [ ] `dotnet build` and `dotnet test` pass locally.
- [ ] `CHANGELOG.md` updated under `[Unreleased]`.
- [ ] If a roadmap item is now done, `SHADOWLIB_ROADMAP.md` reflects it.

## Versioning & releasing

Semantic versioning. Pre-1.0 minor bumps are allowed to break public API; we'll call them out explicitly in the changelog.

The version is duplicated in two places — they MUST agree:

- `package.json` → `"version": "X.Y.Z"`
- `Runtime/ShadowLib.csproj` → `<Version>X.Y.Z</Version>`

The release workflow fails fast if these disagree with the git tag.

To cut a release:

1. Update both version strings.
2. Move the `[Unreleased]` block in `CHANGELOG.md` to a new `[X.Y.Z] — YYYY-MM-DD` section.
3. Commit on `main`.
4. Tag: `git tag vX.Y.Z && git push --tags`.
5. The release workflow builds DLL, NuGet, and UPM tarball, attaches them to the GitHub release.

Pre-release tags use `vX.Y.Z-preview.N`. The release will be flagged as a pre-release on GitHub automatically.

## Don't

- Don't add `Newtonsoft.Json`. We use `System.Text.Json` and even that is in the `Optional/` sub-package.
- Don't add a logging framework. The library is library-only — surfaces should throw on misuse, not log.
- Don't add LINQ to hot paths inside the library. Users can compose their own.
- Don't broaden `IRandom`. It's a load-bearing surface; new methods should go on a sibling utility class.
- Don't `git push --force` to `main`. Pre-release tags are immutable once cut.
