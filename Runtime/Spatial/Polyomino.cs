namespace ShadowLib.Spatial;

using System;

/// <summary>
/// Represents an arbitrary polyomino shape as a set of cell offsets relative to an anchor at (0, 0).
/// Offsets are normalized so that the minimum column and row are always zero.
/// </summary>
public readonly struct Polyomino
{
    private readonly (int Col, int Row)[] _offsets;

    /// <summary>
    /// Gets a read-only span over the cell offsets that define this shape.
    /// Each offset is relative to the placement anchor (0, 0).
    /// </summary>
    public ReadOnlySpan<(int Col, int Row)> Offsets => _offsets != null ? _offsets.AsSpan() : ReadOnlySpan<(int Col, int Row)>.Empty;

    /// <summary>
    /// Gets the bounding-box width of this shape (max column + 1).
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the bounding-box height of this shape (max row + 1).
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the number of cells this shape occupies.
    /// </summary>
    public int CellCount => _offsets?.Length ?? 0;

    private Polyomino((int Col, int Row)[] offsets, int width, int height)
    {
        _offsets = offsets;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Creates a rectangular polyomino of the given dimensions.
    /// </summary>
    /// <param name="width">Number of columns. Must be greater than zero.</param>
    /// <param name="height">Number of rows. Must be greater than zero.</param>
    /// <returns>A <see cref="Polyomino"/> covering a full <paramref name="width"/> x <paramref name="height"/> rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="height"/> is less than or equal to zero.
    /// </exception>
    public static Polyomino FromRect(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

        var offsets = new (int Col, int Row)[width * height];
        int i = 0;
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                offsets[i++] = (col, row);
            }
        }

        return new Polyomino(offsets, width, height);
    }

    /// <summary>
    /// Creates a polyomino from explicit cell offsets. Offsets are normalized so that the
    /// minimum column and row become zero.
    /// </summary>
    /// <param name="offsets">
    /// One or more (Col, Row) pairs defining the shape. Duplicates are not checked.
    /// </param>
    /// <returns>A normalized <see cref="Polyomino"/> with the given cell layout.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="offsets"/> is empty.</exception>
    public static Polyomino FromOffsets(params (int Col, int Row)[] offsets)
    {
        if (offsets == null || offsets.Length == 0)
            throw new ArgumentException("At least one offset is required.", nameof(offsets));

        int minCol = offsets[0].Col;
        int minRow = offsets[0].Row;
        int maxCol = offsets[0].Col;
        int maxRow = offsets[0].Row;

        for (int i = 1; i < offsets.Length; i++)
        {
            if (offsets[i].Col < minCol) minCol = offsets[i].Col;
            if (offsets[i].Row < minRow) minRow = offsets[i].Row;
            if (offsets[i].Col > maxCol) maxCol = offsets[i].Col;
            if (offsets[i].Row > maxRow) maxRow = offsets[i].Row;
        }

        var normalized = new (int Col, int Row)[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            normalized[i] = (offsets[i].Col - minCol, offsets[i].Row - minRow);
        }

        int width = maxCol - minCol + 1;
        int height = maxRow - minRow + 1;

        return new Polyomino(normalized, width, height);
    }

    private static readonly Polyomino _single = FromRect(1, 1);

    /// <summary>
    /// Gets a cached single-cell (1x1) polyomino.
    /// </summary>
    public static ref readonly Polyomino Single => ref _single;
}
