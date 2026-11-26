# Weight Table Influence + EffectWheel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DynamicWeightTable<T>`'s push-style weight API with pull-based influence, add enabled flags, stock, repeat prevention, distinct multi-pick and a probability query, then add `IEffect<TTarget>` + `EffectWheel<TEffect, TTarget>` on top.

**Architecture:** One class, `DynamicWeightTable<T>`, rewritten in place. Each pick computes an effective weight per item (designed weight, bent by influence, zeroed by disabled / depleted / repeat exclusion), then does one `rng.NextFloat()` draw over the running sum. `EffectWheel` is a subclass adding one method. `TieredTable<T>` is untouched.

**Tech Stack:** C# on `netstandard2.1` (library), NUnit on `net8.0` (tests), `net9.0` console runner.

**Spec:** `docs/superpowers/specs/2026-11-26-weight-table-influence-design.md`. Read it before any task; the "Edge rules" section is binding.

## Global Constraints

- Library stays `netstandard2.1`; no `UnityEngine` / `UnityEditor` in `Runtime/`; no new NuGet references.
- All randomness through `IRandom`. Exactly one `rng.NextFloat()` per picked item; zero draws when nothing is pickable.
- No allocation per pick after warm-up (reuse scratch buffers).
- C# uses 4 spaces. XML docs on every public member (doc-XML is on). No comments that narrate code.
- Every new tracked `.cs` needs a Unity `.meta`: run `bash scripts/generate-unity-meta.sh` after creating one and commit the `.meta` with it. Every deleted `.cs` loses its `.meta` in the same commit.
- Write file content with the Write/Edit tools, never bash heredocs (heredocs containing apostrophes fail in this environment).
- Tests seed with `new Xoshiro128StarStar(123UL)` unless a step says otherwise. Test namespace is `ShadowLib.Tests`, fixtures are `[TestFixture] public class XTests`.
- Commit after every task. Never push. End every commit message with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- Test command for one fixture: `dotnet test Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~<FixtureName>"`.

## Review Focus

1. **An influence source returns `NaN` or `±Infinity`.** Expected: `NaN` acts as `0`, infinities clamp to `±1`; never a `NaN` total. Test in Task 3.
2. **An influence source throws during a pick.** Expected: the exception propagates and the table is exactly as before: no stock spent, last pick unchanged, no RNG draw. Test in Task 4 (needs stock to observe).
3. **The draw lands at the very top of the range while the last items are unpickable.** Expected: never returns a disabled / depleted item. Test in Task 2.
4. **An item is removed while its neighbours carry influence, stock or a disabled flag.** Expected: every other item keeps its own settings (the old class kept two parallel lists; an off-by-one here silently moves settings to the wrong item). Test in Task 4.
5. **`GetProbability` / `TotalWeight` are called every frame for UI.** Expected: no stock spent, no RNG draw, last pick unchanged. Test in Task 5.

---

### Task 1: Pin today's sampling sequence

**Files:**
- Create: `Tests/Runtime/DynamicWeightTableTests.cs` (+ `.meta`)

**Interfaces:**
- Produces: fixture `DynamicWeightTableTests` with a private helper `static DynamicWeightTable<string> PlainTable()` returning items `"A"`,`"B"`,`"C"`,`"D"` with weights `1f, 2f, 3f, 4f` in that order. Later tasks add tests to this fixture and reuse the helper.

- [ ] **Step 1: Write the pin test against the current, unmodified class**

```csharp
[Test]
public void Sample_PlainTable_SequenceIsPinned()
{
    var table = PlainTable();
    var rng = new Xoshiro128StarStar(123UL);
    var picks = new System.Text.StringBuilder();
    for (int i = 0; i < 48; i++) picks.Append(table.Sample(rng));
    Assert.AreEqual("", picks.ToString());
}
```

- [ ] **Step 2: Run it, read the actual 48-character string from the failure message, paste it in as the expected value**

Run: `dotnet test Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~DynamicWeightTableTests"`
Expected first: FAIL showing the actual string. After pasting: PASS.
The string must come from this run of the old implementation. Do not compute it by hand and do not change it in any later task.

- [ ] **Step 3: Generate the meta and commit**

```bash
bash scripts/generate-unity-meta.sh
git add Tests/Runtime/DynamicWeightTableTests.cs Tests/Runtime/DynamicWeightTableTests.cs.meta
git commit -m "Pin DynamicWeightTable sampling sequence before the revamp"
```

---

### Task 2: Rewrite the table core (items, designed weight, enabled, picking)

**Files:**
- Modify: `Runtime/RNG/Distributions/DynamicWeightTable.cs` (full rewrite)
- Test: `Tests/Runtime/DynamicWeightTableTests.cs`

**Interfaces:**
- Produces, on `public class DynamicWeightTable<T> where T : IEquatable<T>`:
  - `DynamicWeightTable(int initialCapacity = 16)`
  - `int Count { get; }`
  - `void Add(T item, float weight)`: `weight <= 0` or non-finite throws `ArgumentOutOfRangeException`; item already present throws `ArgumentException`
  - `bool TryRemove(T item)`, `void Clear()`, `bool Contains(T item)`
  - `float GetWeight(T item)` (`KeyNotFoundException` if missing), `bool TryGetWeight(T item, out float weight)`, `bool SetWeight(T item, float weight)` (returns found; `weight <= 0` throws `ArgumentOutOfRangeException`)
  - `bool SetEnabled(T item, bool enabled)`, `bool IsEnabled(T item)` (false if missing)
  - `float TotalWeight { get; }`: sum of effective weights now
  - `float GetProbability(T item)`: effective / total; `0` if missing or total is `0`
  - `T Sample(IRandom rng)`: `InvalidOperationException` when total is `0`; `ArgumentNullException` for null `rng`
  - `bool TrySample(IRandom rng, out T item)`: false, and no draw, when total is `0`
- Removed in this task: `AddWeight`, `MultiplyWeight`, `ResetWeights`, `UniformWeights`, `NormalizeWeights`, `GetBaseWeight`, `TryGetBaseWeight`, `SetBaseWeight`, the base/dynamic split, the cumulative-weights cache.
- Internal shape later tasks build on: one private method that fills a reusable `float[]` scratch buffer with effective weights and returns their sum, and one private method that picks an index from that buffer with a single `rng.NextFloat()`. Tasks 3 to 6 extend the first and reuse the second.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void Add_Duplicate_Throws()
{
    var t = PlainTable();
    Assert.Throws<ArgumentException>(() => t.Add("A", 1f));
}

[Test] public void SetWeight_ChangesDesignedWeight()
{
    var t = PlainTable();
    Assert.IsTrue(t.SetWeight("A", 10f));
    Assert.AreEqual(10f, t.GetWeight("A"));
    Assert.AreEqual(19f, t.TotalWeight);
    Assert.IsFalse(t.SetWeight("Z", 1f));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetWeight("A", 0f));
}

[Test] public void Disabled_ItemIsNeverPicked_AndWeighsZero()
{
    var t = PlainTable();
    t.SetEnabled("D", false);
    Assert.IsFalse(t.IsEnabled("D"));
    Assert.AreEqual(6f, t.TotalWeight);
    Assert.AreEqual(0f, t.GetProbability("D"));
    var rng = new Xoshiro128StarStar(123UL);
    for (int i = 0; i < 100_000; i++) Assert.AreNotEqual("D", t.Sample(rng));
}

[Test] public void GetProbability_SumsToOne()
{
    var t = PlainTable();
    float sum = t.GetProbability("A") + t.GetProbability("B") + t.GetProbability("C") + t.GetProbability("D");
    Assert.AreEqual(1f, sum, 1e-6f);
    Assert.AreEqual(0.4f, t.GetProbability("D"), 1e-6f);
    Assert.AreEqual(0f, t.GetProbability("Z"));
}

[Test] public void NothingPickable_SampleThrows_TrySampleFalse_NoDraw()
{
    var empty = new DynamicWeightTable<string>();
    var allOff = PlainTable();
    foreach (var s in new[] { "A", "B", "C", "D" }) allOff.SetEnabled(s, false);
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);

    Assert.Throws<InvalidOperationException>(() => empty.Sample(rng));
    Assert.Throws<InvalidOperationException>(() => allOff.Sample(rng));
    Assert.IsFalse(allOff.TrySample(rng, out _));
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
}

[Test] public void Sample_DrawsExactlyOncePerPick()
{
    var t = PlainTable();
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);
    for (int i = 0; i < 10; i++) { t.Sample(rng); reference.NextFloat(); }
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
}
```

- [ ] **Step 2: Run to verify they fail** (compile errors for `SetEnabled`, `IsEnabled`, `GetProbability`, `TrySample` count as failing)

- [ ] **Step 3: Rewrite `DynamicWeightTable.cs` to the interface above**

Picking rule, which the signature does not determine: `target = rng.NextFloat() * total`; walk items in insertion order keeping a running sum; return the first item with effective weight `> 0` whose running sum is `>= target`; if the walk ends without one, return the last item with effective weight `> 0`. Lookups are a linear `Equals` scan.

- [ ] **Step 4: Run the whole fixture**

Expected: all PASS, including `Sample_PlainTable_SequenceIsPinned` with its string unchanged. If the pin fails, the picking rule is wrong; fix the code, not the pin.

- [ ] **Step 5: Build everything, then commit**

Run: `dotnet build ShadowLib.sln -c Release`. Expected: 0 errors (`TieredTable` only uses `Add` and `Sample`).

```bash
git add Runtime/RNG/Distributions/DynamicWeightTable.cs Tests/Runtime/DynamicWeightTableTests.cs
git commit -m "Rewrite DynamicWeightTable core: designed weights, enabled flag, TrySample, GetProbability"
```

---

### Task 3: Influence

**Files:**
- Modify: `Runtime/RNG/Distributions/DynamicWeightTable.cs`
- Test: `Tests/Runtime/DynamicWeightTableTests.cs`

**Interfaces:**
- Consumes: the effective-weight method from Task 2.
- Produces:
  - `bool SetInfluence(T item, float min, float max, Func<float> source)`: returns found; null `source` throws `ArgumentNullException`; anything violating `0 <= min <= weight <= max` or non-finite throws `ArgumentOutOfRangeException`
  - `bool ClearInfluence(T item)`
  - `SetWeight` now throws `ArgumentOutOfRangeException` when the item is influenced and the new weight is outside `[min, max]`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestCase(-1f, 5f)]
[TestCase(-0.5f, 12.5f)]
[TestCase(0f, 20f)]
[TestCase(0.5f, 40f)]
[TestCase(1f, 60f)]
[TestCase(3f, 60f)]
[TestCase(-3f, 5f)]
[TestCase(float.NaN, 20f)]
[TestCase(float.PositiveInfinity, 60f)]
[TestCase(float.NegativeInfinity, 5f)]
public void Influence_MapsOntoRange(float influence, float expectedWeight)
{
    var t = new DynamicWeightTable<string>();
    t.Add("drain", 20f);
    t.SetInfluence("drain", 5f, 60f, () => influence);
    Assert.AreEqual(expectedWeight, t.TotalWeight);
    Assert.AreEqual(20f, t.GetWeight("drain"));
}

[Test] public void Influence_MinZero_SwitchesItemOff()
{
    var t = PlainTable();
    t.SetInfluence("D", 0f, 4f, () => -1f);
    Assert.AreEqual(0f, t.GetProbability("D"));
    var rng = new Xoshiro128StarStar(123UL);
    for (int i = 0; i < 10_000; i++) Assert.AreNotEqual("D", t.Sample(rng));
}

[Test] public void Influence_IsReadAtPickTime()
{
    float corruption = 0f;
    var t = PlainTable();
    t.SetInfluence("A", 1f, 91f, () => corruption);
    Assert.AreEqual(0.1f, t.GetProbability("A"), 1e-6f);
    corruption = 1f;
    Assert.AreEqual(0.91f, t.GetProbability("A"), 1e-6f);
    t.ClearInfluence("A");
    Assert.AreEqual(0.1f, t.GetProbability("A"), 1e-6f);
}

[Test] public void Influence_SourceCalledOncePerPick()
{
    int calls = 0;
    var t = PlainTable();
    t.SetInfluence("A", 1f, 2f, () => { calls++; return 0f; });
    t.Sample(new Xoshiro128StarStar(123UL));
    Assert.AreEqual(1, calls);
}

[Test] public void Influence_Validation()
{
    var t = PlainTable();                      // "B" weighs 2
    Assert.IsFalse(t.SetInfluence("Z", 0f, 1f, () => 0f));
    Assert.Throws<ArgumentNullException>(() => t.SetInfluence("B", 1f, 3f, null!));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", -1f, 3f, () => 0f));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", 2.5f, 3f, () => 0f));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetInfluence("B", 1f, 1.5f, () => 0f));
    t.SetInfluence("B", 1f, 3f, () => 0f);
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetWeight("B", 4f));
    Assert.IsTrue(t.SetWeight("B", 3f));
}
```

- [ ] **Step 2: Run to verify they fail**

- [ ] **Step 3: Implement**

Effective weight for an influenced item: `i = source()`; `NaN` becomes `0`; clamp to `[-1, 1]`; `i < 0 ? Lerp(weight, min, -i) : Lerp(weight, max, i)` where `Lerp(a, b, t)` is `a + (b - a) * t` but returns `a` exactly at `t == 0` and `b` exactly at `t == 1`. Disabled items do not call their source.

- [ ] **Step 4: Run the fixture.** Expected: all PASS, pin unchanged.

- [ ] **Step 5: Commit**

```bash
git add Runtime/RNG/Distributions/DynamicWeightTable.cs Tests/Runtime/DynamicWeightTableTests.cs
git commit -m "Add pull-based influence to DynamicWeightTable"
```

---

### Task 4: Stock

**Files:**
- Modify: `Runtime/RNG/Distributions/DynamicWeightTable.cs`
- Test: `Tests/Runtime/DynamicWeightTableTests.cs`

**Interfaces:**
- Produces:
  - `bool SetStock(T item, int units)`: same as `SetStock(item, units, units)`
  - `bool SetStock(T item, int units, int maxUnits)`: returns found; requires `maxUnits >= 1` and `0 <= units <= maxUnits`, else `ArgumentOutOfRangeException`
  - `bool ClearStock(T item)`: back to infinite
  - `bool TryGetStock(T item, out int units)`: false if missing or infinite
  - `bool Refill(T item)`: false if missing or infinite
  - `void RefillAll()`
  - A successful pick of a stocked item spends one unit; an item with `0` units weighs zero.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void Stock_RunsOut_ThenRefills()
{
    var t = new DynamicWeightTable<string>();
    t.Add("relic", 1f);
    t.SetStock("relic", 2);
    var rng = new Xoshiro128StarStar(123UL);
    Assert.AreEqual("relic", t.Sample(rng));
    Assert.AreEqual("relic", t.Sample(rng));
    Assert.IsTrue(t.TryGetStock("relic", out int left));
    Assert.AreEqual(0, left);
    Assert.IsFalse(t.TrySample(rng, out _));
    Assert.IsTrue(t.Refill("relic"));
    Assert.AreEqual("relic", t.Sample(rng));
}

[Test] public void Stock_DepletedItem_LeavesTheRestPickable()
{
    var t = PlainTable();
    t.SetStock("D", 1);
    var rng = new Xoshiro128StarStar(123UL);
    int dCount = 0;
    for (int i = 0; i < 1000; i++) if (t.Sample(rng) == "D") dCount++;
    Assert.AreEqual(1, dCount);
    Assert.AreEqual(0f, t.GetProbability("D"));
}

[Test] public void Stock_RestoreAfterLoad_AndValidation()
{
    var t = PlainTable();
    Assert.IsTrue(t.SetStock("A", 1, 5));
    Assert.IsTrue(t.TryGetStock("A", out int units));
    Assert.AreEqual(1, units);
    t.RefillAll();
    t.TryGetStock("A", out units);
    Assert.AreEqual(5, units);
    Assert.IsFalse(t.TryGetStock("B", out _));
    Assert.IsFalse(t.Refill("B"));
    Assert.IsFalse(t.SetStock("Z", 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", 6, 5));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", -1, 5));
    Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStock("A", 0, 0));
    Assert.IsTrue(t.ClearStock("A"));
    Assert.IsFalse(t.TryGetStock("A", out _));
}

[Test] public void ThrowingSource_LeavesTableUntouched()
{
    var t = PlainTable();
    foreach (var s in new[] { "A", "B", "C", "D" }) t.SetStock(s, 3);
    bool explode = true;
    t.SetInfluence("D", 1f, 8f, () => explode ? throw new InvalidOperationException("boom") : 0f);
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);

    Assert.Throws<InvalidOperationException>(() => t.Sample(rng));
    foreach (var s in new[] { "A", "B", "C", "D" })
    {
        t.TryGetStock(s, out int units);
        Assert.AreEqual(3, units);
    }
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
}

[Test] public void Remove_KeepsNeighbourSettingsOnTheRightItems()
{
    var t = PlainTable();
    t.SetEnabled("A", false);
    t.SetStock("C", 2, 7);
    t.SetInfluence("D", 0f, 40f, () => 1f);
    Assert.IsTrue(t.TryRemove("B"));

    Assert.IsFalse(t.IsEnabled("A"));
    Assert.IsTrue(t.IsEnabled("C"));
    Assert.IsTrue(t.TryGetStock("C", out int units));
    Assert.AreEqual(2, units);
    Assert.IsFalse(t.TryGetStock("D", out _));
    Assert.AreEqual(43f, t.TotalWeight);       // C = 3, D influenced to 40, A disabled
}
```

- [ ] **Step 2: Run to verify they fail**

- [ ] **Step 3: Implement.** Stock is spent only after a pick has been chosen; all sources are read before any mutation or draw.

- [ ] **Step 4: Run the fixture.** Expected: all PASS, pin unchanged.

- [ ] **Step 5: Commit**

```bash
git add Runtime/RNG/Distributions/DynamicWeightTable.cs Tests/Runtime/DynamicWeightTableTests.cs
git commit -m "Add depletable stock to DynamicWeightTable"
```

---

### Task 5: Last pick and repeat prevention

**Files:**
- Modify: `Runtime/RNG/Distributions/DynamicWeightTable.cs`
- Test: `Tests/Runtime/DynamicWeightTableTests.cs`

**Interfaces:**
- Produces:
  - `bool PreventRepeat { get; set; }` (default `false`)
  - `bool TryGetLastPicked(out T item)`
  - `bool SetLastPicked(T item)`: false if the item is not in the table
  - `void ClearLastPicked()`
  - The last pick is recorded on every successful pick whether or not `PreventRepeat` is on. `TryRemove` of the last-picked item and `Clear` clear it; `SetEnabled(item, false)` does not.
  - With `PreventRepeat` on, the last pick weighs zero for the next pick if any other item has positive effective weight; otherwise it stays pickable. `GetProbability` and `TotalWeight` reflect the same exclusion.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void PreventRepeat_NeverRepeats_WithTwoOrMorePickable()
{
    var t = PlainTable();
    t.PreventRepeat = true;
    var rng = new Xoshiro128StarStar(123UL);
    string previous = t.Sample(rng);
    for (int i = 0; i < 10_000; i++)
    {
        string next = t.Sample(rng);
        Assert.AreNotEqual(previous, next);
        previous = next;
    }
}

[Test] public void PreventRepeat_AllowsRepeat_WhenOnlyOnePickable()
{
    var t = PlainTable();
    t.PreventRepeat = true;
    foreach (var s in new[] { "B", "C", "D" }) t.SetEnabled(s, false);
    var rng = new Xoshiro128StarStar(123UL);
    Assert.AreEqual("A", t.Sample(rng));
    Assert.AreEqual(1f, t.GetProbability("A"));
    Assert.AreEqual("A", t.Sample(rng));
}

[Test] public void PreventRepeat_ExcludedItemReportsZeroProbability()
{
    var t = PlainTable();
    t.PreventRepeat = true;
    Assert.IsTrue(t.SetLastPicked("D"));
    Assert.AreEqual(0f, t.GetProbability("D"));
    Assert.AreEqual(6f, t.TotalWeight);
    Assert.AreEqual(0.5f, t.GetProbability("C"), 1e-6f);
}

[Test] public void LastPicked_Lifecycle()
{
    var t = PlainTable();
    Assert.IsFalse(t.TryGetLastPicked(out _));
    string picked = t.Sample(new Xoshiro128StarStar(123UL));
    Assert.IsTrue(t.TryGetLastPicked(out string last));
    Assert.AreEqual(picked, last);

    t.SetEnabled(picked, false);
    Assert.IsTrue(t.TryGetLastPicked(out _));
    t.TryRemove(picked);
    Assert.IsFalse(t.TryGetLastPicked(out _));

    Assert.IsFalse(t.SetLastPicked("Z"));
    string other = picked == "A" ? "B" : "A";
    Assert.IsTrue(t.SetLastPicked(other));
    t.ClearLastPicked();
    Assert.IsFalse(t.TryGetLastPicked(out _));
    t.SetLastPicked(other);
    t.Clear();
    Assert.IsFalse(t.TryGetLastPicked(out _));
}

[Test] public void Queries_DoNotChangeState()
{
    var t = PlainTable();
    t.PreventRepeat = true;
    t.SetStock("A", 2);
    t.SetLastPicked("B");
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);

    for (int i = 0; i < 100; i++) { _ = t.TotalWeight; _ = t.GetProbability("A"); }

    t.TryGetStock("A", out int units);
    Assert.AreEqual(2, units);
    t.TryGetLastPicked(out string last);
    Assert.AreEqual("B", last);
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
}
```

- [ ] **Step 2: Run to verify they fail**

- [ ] **Step 3: Implement.** Track the last pick by index internally so removal can clear or shift it.

- [ ] **Step 4: Run the fixture.** Expected: all PASS, pin unchanged (`PreventRepeat` defaults to off).

- [ ] **Step 5: Commit**

```bash
git add Runtime/RNG/Distributions/DynamicWeightTable.cs Tests/Runtime/DynamicWeightTableTests.cs
git commit -m "Add last-pick tracking and repeat prevention to DynamicWeightTable"
```

---

### Task 6: SampleDistinct

**Files:**
- Modify: `Runtime/RNG/Distributions/DynamicWeightTable.cs`
- Test: `Tests/Runtime/DynamicWeightTableTests.cs`

**Interfaces:**
- Produces: `int SampleDistinct(IRandom rng, Span<T> results)`: returns the number of items written.
  - Sources are read once per call, not once per slot.
  - One draw per item written. Each written item weighs zero for the remaining slots of this call.
  - Repeat prevention applies to the first slot only.
  - Each written item spends one stock unit and the last one written becomes the last pick.
  - Empty span: returns `0`, no draw. Nothing pickable: returns what was written so far; when that is `0` the last pick is unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void SampleDistinct_ReturnsDifferentItems()
{
    var t = PlainTable();
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);
    var results = new string[3];
    for (int round = 0; round < 1000; round++)
    {
        Assert.AreEqual(3, t.SampleDistinct(rng, results));
        Assert.AreEqual(3, new HashSet<string>(results).Count);
        for (int i = 0; i < 3; i++) reference.NextFloat();
    }
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
    t.TryGetLastPicked(out string last);
    Assert.AreEqual(results[2], last);
}

[Test] public void SampleDistinct_StopsEarly_WhenPoolRunsOut()
{
    var t = PlainTable();
    t.SetEnabled("A", false);
    var results = new string[10];
    int written = t.SampleDistinct(new Xoshiro128StarStar(123UL), results);
    Assert.AreEqual(3, written);
    CollectionAssert.AreEquivalent(new[] { "B", "C", "D" }, results[..3]);
}

[Test] public void SampleDistinct_EmptySpanOrEmptyPool_WritesNothing()
{
    var t = PlainTable();
    t.SetLastPicked("A");
    var rng = new Xoshiro128StarStar(123UL);
    var reference = new Xoshiro128StarStar(123UL);
    Assert.AreEqual(0, t.SampleDistinct(rng, Span<string>.Empty));
    foreach (var s in new[] { "A", "B", "C", "D" }) t.SetEnabled(s, false);
    Assert.AreEqual(0, t.SampleDistinct(rng, new string[2]));
    t.TryGetLastPicked(out string last);
    Assert.AreEqual("A", last);
    Assert.AreEqual(reference.NextFloat(), rng.NextFloat());
}

[Test] public void SampleDistinct_SpendsStock_AndReadsSourcesOnce()
{
    int calls = 0;
    var t = PlainTable();
    t.SetStock("A", 1);
    t.SetInfluence("B", 1f, 3f, () => { calls++; return 0f; });
    var results = new string[4];
    Assert.AreEqual(4, t.SampleDistinct(new Xoshiro128StarStar(123UL), results));
    Assert.AreEqual(1, calls);
    t.TryGetStock("A", out int units);
    Assert.AreEqual(0, units);
}

[Test] public void SampleDistinct_PreventRepeat_FirstSlotOnly()
{
    var t = PlainTable();
    t.PreventRepeat = true;
    var rng = new Xoshiro128StarStar(123UL);
    var results = new string[4];
    for (int round = 0; round < 500; round++)
    {
        t.SetLastPicked("D");
        Assert.AreEqual(4, t.SampleDistinct(rng, results));
        Assert.AreNotEqual("D", results[0]);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

- [ ] **Step 3: Implement**, reusing the effective-weight buffer: after each pick, zero that index in the buffer and recompute the sum from the buffer for the next slot (do not subtract from the previous sum; rounding can leave a phantom remainder).

- [ ] **Step 4: Run the fixture.** Expected: all PASS, pin unchanged.

- [ ] **Step 5: Commit**

```bash
git add Runtime/RNG/Distributions/DynamicWeightTable.cs Tests/Runtime/DynamicWeightTableTests.cs
git commit -m "Add SampleDistinct to DynamicWeightTable"
```

---

### Task 7: IEffect and EffectWheel

**Files:**
- Create: `Runtime/RNG/Distributions/EffectWheel.cs` (+ `.meta`), holding both types in namespace `ShadowLib.RNG.Distributions`
- Create: `Tests/Runtime/EffectWheelTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `DynamicWeightTable<T>.Sample(IRandom)`.
- Produces:
  - `public interface IEffect<TTarget> { void Apply(TTarget target); }`
  - `public class EffectWheel<TEffect, TTarget> : DynamicWeightTable<TEffect> where TEffect : IEffect<TTarget>, IEquatable<TEffect>` with `public TEffect SampleAndApply(IRandom rng, TTarget target)`: samples, applies, returns the sampled effect. The XML doc notes that a struct effect has `Apply` run on a copy.

- [ ] **Step 1: Write the failing tests**

```csharp
private sealed class Player { public int Health = 100; }

private sealed class Damage : IEffect<Player>, IEquatable<Damage>
{
    public readonly int Amount;
    public Damage(int amount) => Amount = amount;
    public void Apply(Player target) => target.Health -= Amount;
    public bool Equals(Damage? other) => other != null && other.Amount == Amount;
    public override bool Equals(object? obj) => Equals(obj as Damage);
    public override int GetHashCode() => Amount;
}

[Test] public void SampleAndApply_AppliesExactlyTheReturnedEffect()
{
    var wheel = new EffectWheel<Damage, Player>();
    wheel.Add(new Damage(25), 20f);
    wheel.Add(new Damage(1), 15f);
    var rng = new Xoshiro128StarStar(123UL);
    for (int i = 0; i < 100; i++)
    {
        var player = new Player();
        Damage applied = wheel.SampleAndApply(rng, player);
        Assert.AreEqual(100 - applied.Amount, player.Health);
    }
}

[Test] public void SampleAndApply_MatchesSample_ForTheSameSeed()
{
    var a = new EffectWheel<Damage, Player>();
    var b = new EffectWheel<Damage, Player>();
    foreach (var w in new[] { a, b }) { w.Add(new Damage(25), 20f); w.Add(new Damage(1), 15f); }
    var rngA = new Xoshiro128StarStar(123UL);
    var rngB = new Xoshiro128StarStar(123UL);
    for (int i = 0; i < 50; i++)
        Assert.AreEqual(a.Sample(rngA), b.SampleAndApply(rngB, new Player()));
}

[Test] public void SampleAndApply_EmptyWheel_Throws()
{
    var wheel = new EffectWheel<Damage, Player>();
    Assert.Throws<InvalidOperationException>(() => wheel.SampleAndApply(new Xoshiro128StarStar(123UL), new Player()));
}

[Test] public void Wheel_InheritsInfluence()
{
    var drain = new Damage(25);
    var wheel = new EffectWheel<Damage, Player>();
    wheel.Add(drain, 20f);
    wheel.Add(new Damage(1), 20f);
    wheel.SetInfluence(drain, 0f, 60f, () => 1f);
    Assert.AreEqual(0.75f, wheel.GetProbability(drain), 1e-6f);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Tests/Runtime/ShadowLib.Tests.csproj --filter "FullyQualifiedName~EffectWheelTests"`

- [ ] **Step 3: Implement `EffectWheel.cs`**

- [ ] **Step 4: Run to verify they pass**

- [ ] **Step 5: Generate metas and commit**

```bash
bash scripts/generate-unity-meta.sh
git add Runtime/RNG/Distributions/EffectWheel.cs Runtime/RNG/Distributions/EffectWheel.cs.meta Tests/Runtime/EffectWheelTests.cs Tests/Runtime/EffectWheelTests.cs.meta
git commit -m "Add IEffect and EffectWheel"
```

---

### Task 8: Remove the modifier types, runner demo, docs

**Files:**
- Delete: `Runtime/RNG/Modifiers/WeightModifier.cs`, `Runtime/RNG/Modifiers/WeightModifier.cs.meta`, `Runtime/RNG/Modifiers.meta`, `Runtime/Optional/ModifierLoader.cs`, `Runtime/Optional/ModifierLoader.cs.meta` (the `Modifiers` folder disappears with its last file)
- Modify: `ShadowLib.Runner/Program.cs`, `SHADOWLIB_ROADMAP.md`, `CHANGELOG.md`, `USAGE_GUIDE.md`, `README.md`, `CONTRIBUTING.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: everything produced by Tasks 2 to 7.

- [ ] **Step 1: Delete the five files with `git rm`, then build**

Run: `dotnet build ShadowLib.sln -c Release`
Expected: 0 errors. If something still references `WeightModifier`, `ModifierOperation` or `ModifierLoader`, remove that reference; do not restore the types.

- [ ] **Step 2: Extend `ShadowLib.Runner/Program.cs`**

After the existing `TieredTable` section, add a section that seeds its RNG from the runner's existing `Orchestrator` (follow how the neighbouring sections get theirs) and prints, in order: an `EffectWheel` with two effects where one has `SetInfluence(effect, min, max, () => corruption)`; `GetProbability` of that effect at `corruption = -1, 0, 1`; five `SampleAndApply` results with `PreventRepeat = true`; a `SampleDistinct` of 3 from a 5-item table; and a stocked item running out (`TrySample` returning false).

Run: `dotnet run --project ShadowLib.Runner`
Expected: exit code 0, the new section prints, and two consecutive runs print identical output.

- [ ] **Step 3: Update the docs**

- `SHADOWLIB_ROADMAP.md`: rewrite the `DynamicWeightTable<T>` bullet for the new feature set; adjust the `TieredTable<T>` bullet if it mentions dynamic weights; delete the `WeightModifier<T>` and `ModifierLoader` bullets (keep `ConditionEvaluator`, retitle the section if it is now a single bullet); move the `IEffect` / `EffectWheel` section from "Not Yet Implemented" to "Implemented"; set row 8 of the order table to **Done**.
- `CHANGELOG.md` `[Unreleased]`: Added (influence, enabled, stock, repeat prevention, `SampleDistinct`, `TrySample`, `GetProbability`, `IEffect`, `EffectWheel`); Changed, marked breaking (`Add` throws on a duplicate item, `SetWeight` now sets the designed weight); Removed, marked breaking (each removed member by name, `WeightModifier<T>`, `ModifierOperation`, `ModifierLoader`). Give the migration in one line each: `MultiplyWeight` / `AddWeight` / `ResetWeights` become `SetInfluence`; removing via `AddWeight` becomes `SetEnabled(false)` or `TryRemove`.
- `USAGE_GUIDE.md`: rewrite the weight-table section around the new API with one example covering influence, enabled, stock, repeat prevention, `SampleDistinct` and `GetProbability`; note that stock and the last pick are not saved by the table and show `TryGetStock` / `SetStock(item, units, maxUnits)` / `TryGetLastPicked` / `SetLastPicked` for save and load; fix the `GetTier(tier).SetWeight` example; add an `EffectWheel` section; remove the `ModifierLoader` example and show `ConditionEvaluator` driving an influence lambda instead; in the "For LLMs" list remove the `EffectWheel<,>` / `IEffect<>` roadmap-only line and add lines saying `AddWeight`, `MultiplyWeight`, `ResetWeights`, `UniformWeights`, `NormalizeWeights`, `GetBaseWeight`, `SetBaseWeight`, `WeightModifier`, `ModifierLoader` do not exist.
- `README.md`, `CONTRIBUTING.md`, `CLAUDE.md`: remove every mention of the removed members and of `System.Text.Json` / "JSON-dependent" for `Runtime/Optional/` (it is NCalc only now); in `CLAUDE.md` also drop `EffectWheel` from anything describing it as pending.

- [ ] **Step 4: Verify nothing stale is left**

Run: `git grep -n -E "AddWeight|MultiplyWeight|ResetWeights|UniformWeights|NormalizeWeights|GetBaseWeight|SetBaseWeight|WeightModifier|ModifierOperation|ModifierLoader|System\.Text\.Json" -- . ":!docs/superpowers" ":!docs/handoff.md"`
Expected: matches only in `CHANGELOG.md` (the removal notes and older release entries) and the `USAGE_GUIDE.md` "For LLMs" do-not-exist lines.

- [ ] **Step 5: Full verification**

Run: `dotnet test ShadowLib.sln -c Release` then `dotnet run --project ShadowLib.Runner` then `mkdir -p artifacts/upm && npm pack --pack-destination artifacts/upm`
Expected: every test passes (72 existing plus the new fixtures), runner exits 0, the tarball builds. Then `git status --short` must show no `.cs` without a sibling `.meta` under `Runtime/` or `Tests/`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Remove WeightModifier and ModifierLoader; document influence tables and EffectWheel"
```
