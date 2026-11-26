# 3D degradable backpack (`SpatialGraph3D` + `Polycube`) — design

Date: 2026-11-26. Replaces roadmap items 9 (`SparseGraph<T>`) and 10 (connected component analysis).

## Intent

The user wants a Tarkov-style inventory backpack in three dimensions that wears out: a box of cells where only some cells exist (the pockets), items with real depth occupy cells, and damage kills cells. Flat inventories are already covered by `Grid2D`; this is specifically for items that take up depth.

`Runtime/Spatial/SparseGraph.cs` and `SpatialGraph3D.cs` were written for this a while ago. They are public and ship in the package, but have no tests and no users, only support flat (2D) items on a single layer, and have bugs. The roadmap meanwhile describes a generic coordinate graph with pathfinding, which the user has ruled out for this.

## Goals

- Items are 3D shapes (`Polycube`) placed all-or-nothing across layers.
- Cells can be killed and repaired. Killing a cell never moves or removes an item; it reports what was there and the game decides.
- A dead cell rejects new placements.
- A seeded way to kill a random living cell, replayable from the same seed.
- "Which cells still form one compartment" (pockets) after damage.

## Non-goals

- Pathfinding, distance, or within-N-steps queries on the backpack. A* is wanted on `Grid2D` and becomes its own roadmap line.
- Hand-made connections between non-adjacent cells, and killing a connection while both cells live.
- Shape rotation. `Polyomino` has none either; the caller builds the rotated shape (`FromBox(1, 1, 5)` instead of `FromBox(5, 1, 1)`).
- Auto-fitting items (`BinPacker` stays 2D) and implementing `ISpace` (it is defined around `Polyomino` and a width × height surface).
- Saving. The game owns its inventory data; the class exposes enough to read state and rebuild it.
- Thread safety.

## Anti-goals

- The library owning the damage loop or dropping items by itself. The loop (kill → inspect → roll to drop → bump chance → repeat) lives in the game.
- An event/callback system for cell death. The return value is the notification.
- Picking the random cell from hash-table order, which would not replay.

## Constraints

`netstandard2.1`; no `UnityEngine`; no NuGet refs; randomness only through `IRandom`; every new `.cs` needs a `.meta` (`bash scripts/generate-unity-meta.sh`) and every deleted `.cs` loses its `.meta`; C# uses 4 spaces; XML docs on public members; new public surface needs tests, a runner demo, roadmap, changelog and usage-guide updates.

## Approaches

1. **One class with built-in neighbours (picked by the user).** The box has fixed bounds, so a cell's neighbours are the six cells touching it and nothing stores connections. `SparseGraph` is deleted. Roughly a third of the current code; the scaffolding's bugs disappear with the code they live in. Gives up hand-made and killable connections.
2. **Fix and finish the two existing classes.** Keeps a generic numbered-node graph underneath with stored, killable connections. About three times the code, and the generic layer has no user.
3. **The roadmap's coordinate graph with `Node<T>` handles and list-returning searches.** Designed for pathfinding, with no notion of dead cells or items.

## Design

### `Polycube` (new, `Runtime/Spatial/Polycube.cs`)

A `readonly struct` mirroring `Polyomino` in three dimensions, normalized so the minimum of each axis is 0.

```csharp
public static Polycube FromBox(int width, int height, int depth);                 // each > 0
public static Polycube FromOffsets(params (int Col, int Row, int Layer)[] offsets); // at least one
public static Polycube FromPolyomino(Polyomino shape, int depth = 1);             // extrude a flat shape
public static ref readonly Polycube Single { get; }

public ReadOnlySpan<(int Col, int Row, int Layer)> Offsets { get; }   // empty for default(Polycube)
public int Width { get; }    // columns
public int Height { get; }   // rows
public int Depth { get; }    // layers
public int CellCount { get; }
```

Unlike `Polyomino.FromOffsets`, duplicate offsets throw `ArgumentException`: a duplicate would make an item occupy the same cell twice.

### `SpatialGraph3D<TOccupant> where TOccupant : notnull` (rewritten in place, name kept)

A `width × height × layers` box. Each position is in one of these states: no cell; living cell; dead cell. A cell (living or dead) may hold one occupant.

```csharp
public SpatialGraph3D(int width, int height, int layers = 1,
    IEqualityComparer<TOccupant>? occupantComparer = null);     // each dimension > 0; volume must fit in int

public int Width { get; }
public int Height { get; }
public int Layers { get; }
public int Volume { get; }        // Width * Height * Layers
public int ExistingCount { get; } // cells that exist, living or dead
public int AliveCount { get; }

public bool IsValidPosition(int x, int y, int layer = 0);       // inside the box
public int GetIndex(int x, int y, int layer = 0);               // layer * Width * Height + y * Width + x
public (int X, int Y, int Layer) GetPosition(int index);

// cells
public bool AddCell(int x, int y, int layer = 0);               // false if it already exists
public int AddPocket(Polycube shape, int x, int y, int layer = 0); // returns cells created
public bool Exists(int x, int y, int layer = 0);
public bool IsAlive(int x, int y, int layer = 0);

// items
public bool CanPlace(int x, int y, int layer, Polycube shape);
public bool Place(int x, int y, int layer, TOccupant occupant, Polycube shape);
public bool Free(TOccupant occupant);                           // false if it was not placed
public bool Contains(TOccupant occupant);
public bool IsOccupied(int x, int y, int layer = 0);
public bool TryGetOccupant(int x, int y, int layer, out TOccupant occupant);
public int GetCells(TOccupant occupant, Span<(int X, int Y, int Layer)> buffer); // returns count written

// degradation
public bool TryKill(int x, int y, int layer, out KilledCell<TOccupant> cell);
public bool TryKillRandom(IRandom rng, out KilledCell<TOccupant> cell);
public bool Repair(int x, int y, int layer = 0);                // false unless the cell exists and is dead

// pockets
public int FindPockets(Span<int> pocketIds);                    // returns pocket count
```

```csharp
public readonly struct KilledCell<TOccupant>
{
    public int X { get; }
    public int Y { get; }
    public int Layer { get; }
    public bool WasOccupied { get; }
    public TOccupant Occupant { get; }   // default when WasOccupied is false
}
```

Rules:

- **Queries never throw for positions.** `Exists`, `IsAlive`, `IsOccupied`, `TryGetOccupant`, `CanPlace` return false outside the box or where no cell exists.
- **`AddCell`** throws `ArgumentOutOfRangeException` outside the box. **`AddPocket`** is all-or-nothing about bounds: if any cell of the shape falls outside the box it throws `ArgumentOutOfRangeException` and creates nothing; cells that already exist are skipped and not counted. New cells are alive and empty.
- **`CanPlace`** is true only if every cell of the shape maps to a cell that exists, is alive and is unoccupied.
- **`Place`** returns false, changing nothing, when `CanPlace` is false. It throws `ArgumentException` if the occupant is already placed (an item is in the backpack once; move it with `Free` then `Place`).
- **`Free`** clears every cell the occupant holds, including dead ones.
- **`TryKill`** returns false, changing nothing, if the position has no living cell. Otherwise the cell becomes dead, `AliveCount` drops by one, and `cell` reports the position and its occupant. The occupant stays placed on all its cells.
- **`TryKillRandom`** returns false **without drawing** when `AliveCount` is 0. Otherwise it draws exactly once, `rng.Range(0, AliveCount)`, and kills the living cell with that rank in ascending `GetIndex` order.
- **`Repair`** makes a dead cell alive again and leaves any occupant on it untouched.
- **`FindPockets`** groups living cells that touch through their six face neighbours (±x, ±y, ±layer). `pocketIds` is indexed by `GetIndex` and must hold at least `Width * Height * Layers` entries, else `ArgumentException`. Each living cell gets its pocket number; everything else gets `-1`. Pockets are numbered from 0 in ascending order of their lowest `GetIndex`. Occupancy does not affect pockets.
- **Rebuilding saved state:** add cells, place items, then kill the dead cells. That order is the only one that can recreate an item sitting on a dead cell, and the XML docs and usage guide say so.

### Edge rules (designer's calls, from the builder check)

- **Occupant identity.** Items are matched with `occupantComparer`, defaulting to `EqualityComparer<TOccupant>.Default`, the same as `Grid2D`. Two items that compare equal are the same item to the backpack. An occupant must not change its hash while placed.
- **Naming.** `Volume` is the size of the box and `ExistingCount` the number of cells; the name `CellCount` is avoided because `Grid2D.CellCount` means the full grid. `CanPlace` / `Place` / `GetCells` / `IsValidPosition` / `GetPosition` deliberately keep the existing class's names rather than `Grid2D`'s.
- **Unlike `Grid2D.Occupy`,** which lets one occupant be added in several places, `Place` throws for an occupant that is already placed.
- **Dead cells still report their item.** `IsOccupied` and `TryGetOccupant` are true for a dead cell that holds one, and `GetCells` includes it.
- **`GetCells`** writes positions in ascending `GetIndex` order, at most `buffer.Length` of them (the lowest), and returns the number written. Unknown occupant: `0`.
- **Failed `Try…` calls** set their `out` value to `default`. With a struct `TOccupant`, `default` can look like a real item, so callers read the returned bool (and `KilledCell.WasOccupied`), which the XML docs say.
- **Killing the same cell twice:** the second `TryKill` returns false. `Repair` returns false for a living cell, a missing cell or a position outside the box.
- **`AddCell` / `AddPocket` never revive.** An existing dead cell is skipped like any existing cell; only `Repair` brings it back. There is no way to make a cell stop existing; shrinking a backpack means building a new one.
- **Empty shape.** For `default(Polycube)` (zero cells) `CanPlace` and `Place` return false and `AddPocket` throws `ArgumentException`.
- **Sizes.** The constructor computes the volume with overflow checking and throws `ArgumentOutOfRangeException` if it does not fit in `int`. Memory is proportional to the volume, which the docs state. `Polycube.FromBox` checks its cell count the same way; `FromPolyomino` throws for `depth <= 0` or an empty shape.
- **`GetPosition`** throws `ArgumentOutOfRangeException` for an index outside `0..Volume-1`, like `Grid2D.GetCoordinates`. `GetIndex` is the bare formula and is only meaningful for positions inside the box.
- **`TryKillRandom`** throws `ArgumentNullException` for a null `rng`, finds the cell with a linear scan and does not allocate. With one living cell it still draws once.
- **`FindPockets`** writes only the first `Volume` entries of a longer buffer. Dead and missing cells both get `-1`; tell them apart with `Exists`.
- **No version bump** in this work; the changelog entry is enough until a release is cut.

### Deleted

`Runtime/Spatial/SparseGraph.cs` and its `.meta`. Removed from `SpatialGraph3D`: the `Graph` property, `AddNode`, `NodeExists`, `ConnectAdjacent`, `ConnectLayers`, the `Polyomino` overloads of `AddPocket` / `CanPlace` / `Place` (use `Polycube.FromPolyomino`), and the `initialCapacity` constructor parameter.

## Tests (`Tests/Runtime/PolycubeTests.cs`, `SpatialGraph3DTests.cs`)

At least: `Polycube` normalization, bounds, extrusion, duplicate rejection; placing a `2×2×2` across layers and rejecting overlap, missing cells and dead cells; `Place` leaving nothing behind when it fails; an item staying put when a cell under it dies, and `KilledCell` reporting it; a freed item unable to return to a spot with a dead cell; `Repair`; `TryKillRandom` drawing once, never when empty, replaying for the same seed, and eventually killing every cell exactly once; `FindPockets` on one pocket, two separate pockets, a pocket split by a dead cell, and a too-small buffer; the save-rebuild order.

## Other surface

- Runner demo in `ShadowLib.Runner/Program.cs`: build a backpack with two pockets, place 3D items, run a short seeded damage loop that drops an item by chance, print pockets before and after.
- `SHADOWLIB_ROADMAP.md`: rows 9 and 10 become one Done row for `SpatialGraph3D` + `Polycube`; the "Not Yet Implemented" `SparseGraph<T>` section goes; a new pending line "A* pathfinding on `Grid2D`"; the `SpacedPlacement` example stops calling `graph.Distance` and uses a plain distance function.
- `CHANGELOG.md` `[Unreleased]`: added types; breaking removal of `SparseGraph<TOccupant>` and of the listed `SpatialGraph3D` members.
- `USAGE_GUIDE.md`: a section for the backpack; the "For LLMs" list drops the roadmap-only `SparseGraph` line and says `SparseGraph`, `FindPath`, `GetNodesInRange` and `Distance` do not exist.
- `README.md` and `CLAUDE.md`: remove the "scaffolding, not stable API" wording and describe the backpack.
- Before calling the docs done, grep the whole repo for `SparseGraph`, `ConnectAdjacent`, `ConnectLayers`, `NodeExists`, `FindConnectedComponents`, `GetNodesInRange` and `.Graph`.

## Calls made by the designer (not the user)

- Class keeps the name `SpatialGraph3D`; the new shape is named `Polycube`.
- Distance and within-N-steps are dropped along with pathfinding, since the user said nothing needs them.
- `TryKill` / `TryKillRandom` return a `KilledCell` struct instead of raising an event.
- The random helper kills a cell rather than only picking one.
- No rotation helper on `Polycube`.
- An occupant can be placed only once; a second `Place` throws.
- `AddPocket` throws when the shape leaves the box (the old code silently clipped it).
- Duplicate offsets in a `Polycube` throw.
- Coordinates keep the existing `(x, y, layer)` parameter names; shape offsets are `(Col, Row, Layer)` to match `Polyomino`.

## Left to the builder

Internal storage (dense flat arrays sized to the box are the obvious fit, since bounds are fixed and index order must be deterministic), how occupant → cells is tracked, scratch buffers for `FindPockets` (must not allocate per call after warm-up), exact exception messages.
