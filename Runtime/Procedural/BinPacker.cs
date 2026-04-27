namespace ShadowLib.Procedural;

using System;
using System.Collections.Generic;
using ShadowLib.Spatial;

/// <summary>
/// A bin packer for efficiently placing polyomino-shaped items into any <see cref="ISpace{TOccupant}"/>.
/// Provides multiple placement strategies: first-fit, best-fit, bottom-left, and automatic repacking.
/// </summary>
/// <typeparam name="TOccupant">The type of occupant stored in the space.</typeparam>
public class BinPacker<TOccupant>
    where TOccupant : notnull
{
    private readonly ISpace<TOccupant> _space;

    /// <summary>
    /// Initializes a new <see cref="BinPacker{TOccupant}"/> that operates on the given space.
    /// </summary>
    /// <param name="space">The spatial container to pack items into.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="space"/> is null.</exception>
    public BinPacker(ISpace<TOccupant> space)
    {
        _space = space ?? throw new ArgumentNullException(nameof(space));
    }

    /// <summary>
    /// Scans the space in row-major order and returns the first position where <paramref name="shape"/> fits.
    /// Fastest placement strategy — O(W*H) worst case, but exits early on first match.
    /// </summary>
    /// <param name="shape">The polyomino shape to place.</param>
    /// <returns>
    /// The (col, row) anchor position of the first valid placement, or <see langword="null"/> if the shape
    /// cannot fit anywhere in the space.
    /// </returns>
    public (int col, int row)? FirstFit(Polyomino shape)
    {
        int maxX = _space.Width - shape.Width + 1;
        int maxY = _space.Height - shape.Height + 1;

        for (int y = 0; y < maxY; y++)
        {
            for (int x = 0; x < maxX; x++)
            {
                if (_space.CanOccupy(x, y, shape))
                    return (x, y);
            }
        }

        return null;
    }

    /// <summary>
    /// Scans all valid positions and picks the one where the shape has the most occupied neighbors,
    /// producing tighter packing with fewer gaps. Falls back to top-left if all positions score equally.
    /// </summary>
    /// <param name="shape">The polyomino shape to place.</param>
    /// <returns>
    /// The (col, row) anchor of the best-scoring position, or <see langword="null"/> if the shape
    /// cannot fit anywhere.
    /// </returns>
    public (int col, int row)? BestFit(Polyomino shape)
    {
        int maxX = _space.Width - shape.Width + 1;
        int maxY = _space.Height - shape.Height + 1;

        int bestScore = -1;
        int bestX = -1;
        int bestY = -1;

        for (int y = 0; y < maxY; y++)
        {
            for (int x = 0; x < maxX; x++)
            {
                if (!_space.CanOccupy(x, y, shape))
                    continue;

                int score = ScorePosition(x, y, shape);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        if (bestScore < 0)
            return null;

        return (bestX, bestY);
    }

    /// <summary>
    /// Scans column-first (left-to-right, then top-to-bottom) to pack items as far left and as high as possible.
    /// Useful for gravity/shelf-style inventory layouts.
    /// </summary>
    /// <param name="shape">The polyomino shape to place.</param>
    /// <returns>
    /// The (col, row) anchor of the bottom-left-most valid position, or <see langword="null"/> if the shape
    /// cannot fit anywhere.
    /// </returns>
    public (int col, int row)? BottomLeft(Polyomino shape)
    {
        int maxX = _space.Width - shape.Width + 1;
        int maxY = _space.Height - shape.Height + 1;

        for (int x = 0; x < maxX; x++)
        {
            for (int y = maxY - 1; y >= 0; y--)
            {
                if (_space.CanOccupy(x, y, shape))
                    return (x, y);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a position using <paramref name="strategy"/> and immediately occupies the cells.
    /// Convenience method that combines search + placement in one call.
    /// </summary>
    /// <param name="occupant">The occupant to place.</param>
    /// <param name="shape">The polyomino shape to place.</param>
    /// <param name="strategy">The placement strategy to use. Defaults to <see cref="PackStrategy.FirstFit"/>.</param>
    /// <returns>
    /// The (col, row) anchor where the item was placed, or <see langword="null"/> if no valid position exists.
    /// </returns>
    public (int col, int row)? TryPlace(TOccupant occupant, Polyomino shape, PackStrategy strategy = PackStrategy.FirstFit)
    {
        var pos = FindPosition(shape, strategy);
        if (pos == null)
            return null;

        _space.Occupy(pos.Value.col, pos.Value.row, occupant, shape);
        return pos;
    }

    /// <summary>
    /// Checks whether the shape can fit anywhere in the space without placing it.
    /// </summary>
    /// <param name="shape">The polyomino shape to test.</param>
    /// <returns><see langword="true"/> if at least one valid placement exists; otherwise <see langword="false"/>.</returns>
    public bool CanFit(Polyomino shape) => FirstFit(shape) != null;

    /// <summary>
    /// Returns every valid anchor position where <paramref name="shape"/> can be placed.
    /// Useful for UI highlighting of valid drop zones.
    /// </summary>
    /// <param name="shape">The polyomino shape to test.</param>
    /// <param name="buffer">A span to receive valid positions.</param>
    /// <returns>The number of positions written to <paramref name="buffer"/>.</returns>
    public int AllFits(Polyomino shape, Span<(int col, int row)> buffer)
    {
        int maxX = _space.Width - shape.Width + 1;
        int maxY = _space.Height - shape.Height + 1;
        int count = 0;

        for (int y = 0; y < maxY; y++)
        {
            for (int x = 0; x < maxX; x++)
            {
                if (count >= buffer.Length)
                    return count;

                if (_space.CanOccupy(x, y, shape))
                    buffer[count++] = (x, y);
            }
        }

        return count;
    }

    /// <summary>
    /// Removes all provided occupants from the space, then repacks them using the specified strategy
    /// in decreasing cell-count order (larger shapes first).
    /// </summary>
    /// <param name="items">
    /// The occupants and their associated shapes to repack. Items that cannot fit after repacking are omitted
    /// from the result.
    /// </param>
    /// <param name="strategy">The placement strategy to use for each item. Defaults to <see cref="PackStrategy.FirstFit"/>.</param>
    /// <returns>
    /// A list of successfully placed items with their new anchor positions. Items that could not be placed
    /// are excluded.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    public List<(TOccupant item, int col, int row)> AutoSort(
        IReadOnlyList<(TOccupant item, Polyomino shape)> items,
        PackStrategy strategy = PackStrategy.FirstFit)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        for (int i = 0; i < items.Count; i++)
            _space.Free(items[i].item);

        var sorted = new (TOccupant item, Polyomino shape)[items.Count];
        for (int i = 0; i < items.Count; i++)
            sorted[i] = items[i];

        Array.Sort(sorted, (a, b) => b.shape.CellCount.CompareTo(a.shape.CellCount));

        var result = new List<(TOccupant item, int col, int row)>(sorted.Length);
        for (int i = 0; i < sorted.Length; i++)
        {
            var pos = FindPosition(sorted[i].shape, strategy);
            if (pos == null)
                continue;

            _space.Occupy(pos.Value.col, pos.Value.row, sorted[i].item, sorted[i].shape);
            result.Add((sorted[i].item, pos.Value.col, pos.Value.row));
        }

        return result;
    }

    private (int col, int row)? FindPosition(Polyomino shape, PackStrategy strategy) => strategy switch
    {
        PackStrategy.BestFit => BestFit(shape),
        PackStrategy.BottomLeft => BottomLeft(shape),
        _ => FirstFit(shape),
    };

    /// <summary>
    /// Scores a candidate position by counting how many of the shape's edge-adjacent cells are
    /// occupied or out of bounds (walls). Higher score = tighter fit with fewer gaps.
    /// </summary>
    private int ScorePosition(int anchorX, int anchorY, Polyomino shape)
    {
        int score = 0;
        var offsets = shape.Offsets;

        for (int i = 0; i < offsets.Length; i++)
        {
            int cx = anchorX + offsets[i].Col;
            int cy = anchorY + offsets[i].Row;

            // Check 4 cardinal neighbors; occupied or out-of-bounds both score
            if (IsBlockedOrWall(cx - 1, cy, offsets, anchorX, anchorY)) score++;
            if (IsBlockedOrWall(cx + 1, cy, offsets, anchorX, anchorY)) score++;
            if (IsBlockedOrWall(cx, cy - 1, offsets, anchorX, anchorY)) score++;
            if (IsBlockedOrWall(cx, cy + 1, offsets, anchorX, anchorY)) score++;
        }

        return score;
    }

    /// <summary>
    /// Returns true if (nx, ny) is occupied in the space, out of bounds, or NOT part of the shape itself.
    /// Neighbors that are part of the same shape don't count as contact.
    /// </summary>
    private bool IsBlockedOrWall(int nx, int ny, ReadOnlySpan<(int Col, int Row)> offsets, int anchorX, int anchorY)
    {
        // Out of bounds counts as wall contact
        if (nx < 0 || nx >= _space.Width || ny < 0 || ny >= _space.Height)
            return true;

        // If this neighbor is part of the shape being placed, it doesn't count
        for (int i = 0; i < offsets.Length; i++)
        {
            if (anchorX + offsets[i].Col == nx && anchorY + offsets[i].Row == ny)
                return false;
        }

        // Already occupied by another item counts as contact
        return _space.IsOccupied(nx, ny);
    }
}

/// <summary>
/// Specifies the placement strategy used by <see cref="BinPacker{TOccupant}"/>.
/// </summary>
public enum PackStrategy
{
    /// <summary>
    /// Row-major scan, returns the first valid position. Fastest option.
    /// </summary>
    FirstFit,

    /// <summary>
    /// Scans all positions and picks the one with the most occupied/wall neighbors.
    /// Produces tighter packing at the cost of a full scan.
    /// </summary>
    BestFit,

    /// <summary>
    /// Column-first scan, packing items as far left and as low as possible.
    /// Good for gravity/shelf-style layouts.
    /// </summary>
    BottomLeft
}
