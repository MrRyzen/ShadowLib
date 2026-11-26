# Shape Generators Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Polyomino.Generate` and `Polycube.Generate`: seeded N-cell shapes with tunable compactness and an optional bounding-size limit.

**Architecture:** One internal 3D growth core (`ShapeGrowth`) returns grown cells in growth order. `Polycube.Generate` wraps it; `Polyomino.Generate` calls it with depth limit 1 and a neighbour maximum of 4, then drops the layer.

**Tech Stack:** C# on `netstandard2.1`, NUnit, `IRandom`.

**Spec:** `docs/superpowers/specs/2026-11-26-shape-generators-design.md`

## Global Constraints

- Float maths limited to add, subtract, multiply, divide, compare. No `MathF.Pow` / `Exp` / `Log`.
- No iteration over `Dictionary` or `HashSet`; frontier is walked in insertion order.
- Exactly `cellCount - 1` draws, each one `rng.NextFloat()`.
- No `UnityEngine`, no NuGet refs, 4-space indent, no explanatory comments.
- Every new `.cs` under the package gets a `.meta` via `bash scripts/generate-unity-meta.sh`.
- Check test output for `Passed!` before each commit; never chain commit after a piped test run.

## Review Focus

1. A limit of 1 on every axis with `cellCount: 1` returns the single cell and draws nothing.
2. `cellCount` equal to the limit volume fills the box (growth never stalls when the box is nearly full).
3. Huge limits (`int.MaxValue` on all axes) do not overflow the volume check or the extent check.
4. A pick of `NextFloat() * total` that lands at or past the last cumulative weight through rounding still selects an allowed cell (the last allowed one), never a blocked one or index out of range.
5. A frontier cell blocked by the limit earlier becomes irrelevant, not selectable: once an axis is at its maximum, cells outside it weigh zero for the rest of the run.

Tests for 1–3 and 5 are in Task 1. Item 4 is covered by a `FixedRandom`-style test in Task 1 using an `IRandom` that returns the largest float below 1.

---

### Task 1: Growth core and `Polycube.Generate`

**Files:**
- Create: `Runtime/Spatial/ShapeGrowth.cs` (+ `.meta`)
- Modify: `Runtime/Spatial/Polycube.cs`
- Test: `Tests/Runtime/PolycubeGenerateTests.cs` (+ `.meta` if the Tests folder tracks them)

**Interfaces:**
- Produces: `internal static (int Col, int Row, int Layer)[] ShapeGrowth.Grow(IRandom rng, int cellCount, float compactness, int maxWidth, int maxHeight, int maxDepth, int neighbourMax)`. Validates every argument per the spec's Errors section (parameter names as given). Returned cells are in growth order, not normalized.
- Produces: `public static Polycube Polycube.Generate(IRandom rng, int cellCount, float compactness, int maxWidth = int.MaxValue, int maxHeight = int.MaxValue, int maxDepth = int.MaxValue)` = `FromOffsets(ShapeGrowth.Grow(..., neighbourMax: 6))`.

- [ ] **Step 1: Write failing tests** (all except the pin):
  - `Generate_HasExactCellCount_Distinct_Connected_WithinLimits`: seeds 1..20 × compactness {0, 0.25, 0.5, 0.75, 1} × cellCount {1, 2, 7, 30}, limits (4, 4, 2) where volume allows; assert `CellCount`, distinct offsets, flood fill over six neighbours reaches every cell, `Width <= 4`, `Height <= 4`, `Depth <= 2`.
  - `Generate_DrawsOncePerAddedCell`: after `Generate(rng, 9, 0.5f)`, `rng.NextUInt()` equals the 9th `NextUInt()` of a fresh generator with the same seed after 8 `NextFloat()` calls; `cellCount: 1` leaves the generator untouched.
  - `Generate_CellCountEqualToLimitVolume_FillsTheBox`: `(rng, 24, c, 4, 3, 2)` for c in {0, 1} → 24 distinct cells, size exactly 4×3×2.
  - `Generate_UnitLimits_SingleCell`: `(rng, 1, 0.5f, 1, 1, 1)`.
  - `Generate_DefaultLimits_DoNotOverflow`: `(rng, 50, 0f)` succeeds.
  - `Generate_HighestDraw_PicksAnAllowedCell`: an `IRandom` stub whose `NextFloat()` returns `0.99999994f`; `(stub, 12, 0.3f, 3, 2, 2)` returns 12 distinct cells within limits.
  - `Generate_CompactnessOne_IsTighterThanZero`: over seeds 1..50 at 12 cells, mean `Width * Height * Depth` at compactness 1 is less than at 0.
  - Errors: null rng → `ArgumentNullException`; `cellCount` 0 and −1; compactness −0.01f, 1.01f, `float.NaN`; each limit 0; `(rng, 25, 0.5f, 4, 3, 2)` → `ArgumentOutOfRangeException`.
- [ ] **Step 2: Run** `dotnet test Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~PolycubeGenerateTests"` → compile failure (no `Generate`).
- [ ] **Step 3: Implement `ShapeGrowth.Grow` and `Polycube.Generate`.** Decisions the tests do not fix:
  - Weight tables start as `blocky[n] = 8^(n-1)`, `stringy[n] = 8^(neighbourMax-n)`, as `float`; weight = `(1 - c) * stringy[n] + c * blocky[n]`.
  - Frontier: `List<(int, int, int)>` in first-joined order plus `Dictionary<(int, int, int), int>` for neighbour counts (lookup only). A picked cell is removed with an order-preserving `RemoveAt`.
  - A frontier cell is allowed when adding it keeps `max - min + 1 <= limit` on every axis (compute in `long`). Disallowed cells contribute zero weight but stay in the list.
  - Pick: `target = rng.NextFloat() * total`; walk allowed cells accumulating weight; choose the first with `target < cumulative`; if none, the last allowed cell.
  - Seed cell is `(0, 0, 0)`; neighbour visiting order is +Col, −Col, +Row, −Row, +Layer, −Layer.
- [ ] **Step 4: Run the filter again** → `Passed!`.
- [ ] **Step 5: Tune the tables.** Temporarily print 2D-ish shapes (`maxDepth: 1`) at compactness 0, 0.5, 1 from a scratch test or the runner; adjust table bases until 0 is visibly stringy and 1 visibly blocky. Record the final tables in the spec's "Left to the builder" section.
- [ ] **Step 6: Add `Generate_Seed123_ShapeIsPinned`**: `Generate(new Xoshiro128StarStar(123UL), 8, 0.5f)` equals a literal offset array captured from the tuned implementation. Run → `Passed!`.
- [ ] **Step 7:** `bash scripts/generate-unity-meta.sh`, full `dotnet test ShadowLib.sln -c Release` → `Passed!`, commit.

### Task 2: `Polyomino.Generate`

**Files:**
- Modify: `Runtime/Spatial/Polyomino.cs`
- Test: `Tests/Runtime/PolyominoGenerateTests.cs`

**Interfaces:**
- Consumes: `ShapeGrowth.Grow` from Task 1.
- Produces: `public static Polyomino Polyomino.Generate(IRandom rng, int cellCount, float compactness, int maxWidth = int.MaxValue, int maxHeight = int.MaxValue)` = `FromOffsets` over `Grow(..., maxDepth: 1, neighbourMax: 4)` with the layer dropped.

- [ ] **Step 1: Write failing tests:** the 2D versions of the property test (four-neighbour flood fill, limits (4, 3)), draw count, fill-the-rect (`(rng, 12, c, 4, 3)`), compactness comparison on `Width * Height`, and the error cases (`maxWidth` / `maxHeight` 0, `cellCount` 13 in 4×3).
- [ ] **Step 2: Run the filter** `FullyQualifiedName~PolyominoGenerateTests` → compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → `Passed!`.
- [ ] **Step 5: Add `Generate_Seed123_ShapeIsPinned`** for `(new Xoshiro128StarStar(123UL), 8, 0.5f)`. Run → `Passed!`.
- [ ] **Step 6:** meta script, full test run → `Passed!`, commit.

### Task 3: Demo, docs, Unity check

**Files:**
- Modify: `ShadowLib.Runner/Program.cs`, `SHADOWLIB_ROADMAP.md`, `CHANGELOG.md`, `USAGE_GUIDE.md`, `README.md` (only if it lists shape constructors), `CLAUDE.md` (one line under Spatial on the pins and the no-`Pow` rule)
- Create (ShadowLab repo, uncommitted like its siblings): `Assets/_project/Tests/ShadowLibBurst/ShapeGenerateUnityTests.cs`

- [ ] **Step 1: Runner demo:** print one generated polyomino at compactness 0.1 and 0.9 as ASCII, and place a generated `Polycube` (limits matching the pocket) into the existing backpack demo. `dotnet run --project ShadowLib.Runner` exits 0; two `--no-build` runs print identical output.
- [ ] **Step 2: Docs:** roadmap section removed from "Not Yet Implemented", `Polyomino` / `Polycube` bullets mention `Generate`, row 11 → **Done**; changelog `[Unreleased]` Added; usage guide section plus "For LLMs" entries (returns the struct not a list, limits are extents, no rotation).
- [ ] **Step 3: Unity:** two EditMode tests asserting the same pinned arrays as Tasks 1 and 2. Repack, `package_add`, poll `editor_status`, run `ShadowLib.BurstTests` → all pass.
- [ ] **Step 4:** full `dotnet test` → `Passed!`, commit.
