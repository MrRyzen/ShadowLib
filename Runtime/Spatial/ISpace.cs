namespace ShadowLib.Spatial;

/// <summary>
/// Defines a 2D spatial container that supports polyomino-shaped occupancy.
/// Implemented by <see cref="Grid2D{TCell, TOccupant}"/> and any future spatial structures.
/// </summary>
/// <typeparam name="TOccupant">The type of occupant data stored in cells.</typeparam>
public interface ISpace<TOccupant> where TOccupant : notnull
{
    /// <summary>
    /// Gets the number of columns in this space.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the number of rows in this space.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Determines whether the given <paramref name="shape"/> can be placed at the specified anchor position
    /// without overlapping existing occupants or exceeding bounds.
    /// </summary>
    /// <param name="x">The column of the placement anchor.</param>
    /// <param name="y">The row of the placement anchor.</param>
    /// <param name="shape">The polyomino shape to test.</param>
    /// <returns><see langword="true"/> if every cell of the shape is valid and unoccupied; otherwise <see langword="false"/>.</returns>
    bool CanOccupy(int x, int y, Polyomino shape);

    /// <summary>
    /// Places <paramref name="occupant"/> in all cells covered by <paramref name="shape"/> at the given anchor.
    /// This is an all-or-nothing operation: if any cell cannot be occupied, no cells are modified.
    /// </summary>
    /// <param name="x">The column of the placement anchor.</param>
    /// <param name="y">The row of the placement anchor.</param>
    /// <param name="occupant">The occupant to place.</param>
    /// <param name="shape">The polyomino shape defining which cells to occupy.</param>
    /// <returns><see langword="true"/> if all cells were successfully occupied; otherwise <see langword="false"/>.</returns>
    bool Occupy(int x, int y, TOccupant occupant, Polyomino shape);

    /// <summary>
    /// Removes the specified occupant from all cells it currently occupies.
    /// </summary>
    /// <param name="occupant">The occupant to remove.</param>
    void Free(TOccupant occupant);

    /// <summary>
    /// Checks whether the cell at the specified coordinates is currently occupied.
    /// Returns <see langword="false"/> for out-of-bounds coordinates rather than throwing.
    /// </summary>
    /// <param name="x">The column index.</param>
    /// <param name="y">The row index.</param>
    /// <returns><see langword="true"/> if the cell is valid and occupied; otherwise <see langword="false"/>.</returns>
    bool IsOccupied(int x, int y);
}
